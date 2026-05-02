using System;

[Title( "Deathrun Physics Damage" )]
[Category( "Deathrun" )]
[Icon( "speed" )]
public sealed class DeathrunPhysicsDamage : Component, Component.ICollisionListener
{
	[Property] public bool EnablePhysicsDamage { get; set; } = true;
	[Property] public float MinImpactSpeed { get; set; } = 500.0f;
	[Property] public float FatalImpactSpeed { get; set; } = 1800.0f;
	[Property] public float DamageMultiplier { get; set; } = 0.10f;
	[Property] public float MinDamage { get; set; } = 5.0f;
	[Property] public float MaxDamage { get; set; } = 100.0f;
	[Property] public float Cooldown { get; set; } = 0.25f;
	[Property] public bool IgnoreSelfOwnedObjects { get; set; } = true;
	[Property] public bool LogPhysicsDamage { get; set; } = false;

	private float _nextDamageTime;

	void Component.ICollisionListener.OnCollisionStart( Collision collision )
	{
		TryApplyImpactDamage( collision );
	}

	void Component.ICollisionListener.OnCollisionUpdate( Collision collision )
	{
	}

	void Component.ICollisionListener.OnCollisionStop( CollisionStop collision )
	{
	}

	private void TryApplyImpactDamage( Collision collision )
	{
		if ( !EnablePhysicsDamage || !Networking.IsHost || Time.Now < _nextDamageTime )
			return;

		var otherObject = collision.Other.GameObject;
		var targetHealth = FindTargetHealth( otherObject );

		if ( !targetHealth.IsValid() || targetHealth.IsDead )
			return;

		var source = targetHealth.GameObject == GameObject ? otherObject : GameObject;

		if ( IgnoreSelfOwnedObjects && HasSameOwner( targetHealth.GameObject, source ) )
			return;

		var impactSpeed = GetImpactSpeed( collision );

		if ( impactSpeed < MinImpactSpeed )
			return;

		var lethal = impactSpeed >= FatalImpactSpeed;
		var damage = lethal
			? MaxDamage
			: Clamp( (impactSpeed - MinImpactSpeed) * DamageMultiplier, MinDamage, MaxDamage );

		_nextDamageTime = Time.Now + MathF.Max( 0.01f, Cooldown );

		if ( LogPhysicsDamage )
			Log.Info( $"Physics impact damage: target='{targetHealth.GameObject.Name}', source='{(source.IsValid() ? source.Name : "none")}', speed={impactSpeed:0.##}, damage={damage:0.##}, lethal={lethal}." );

		targetHealth.TakeDamage( new DeathrunDamageInfo
		{
			Amount = damage,
			DamageType = DeathrunDamageType.PhysicsImpact,
			Source = source,
			SourcePosition = source.IsValid() ? source.WorldPosition : collision.Contact.Point,
			HitPosition = collision.Contact.Point,
			Force = collision.Contact.Speed,
			Reason = $"Physics impact speed {impactSpeed:0.##}",
			IsLethal = lethal,
			InvalidatesRun = true
		} );
	}

	private DeathrunHealth FindTargetHealth( GameObject otherObject )
	{
		var selfHealth = Components.Get<DeathrunHealth>();

		if ( selfHealth.IsValid() )
			return selfHealth;

		if ( otherObject.IsValid() )
			return otherObject.Components.GetInAncestorsOrSelf<DeathrunHealth>();

		return null;
	}

	private static float GetImpactSpeed( Collision collision )
	{
		var normalSpeed = MathF.Abs( collision.Contact.NormalSpeed );

		if ( normalSpeed > 0.0f )
			return normalSpeed;

		return collision.Contact.Speed.Length;
	}

	private static bool HasSameOwner( GameObject target, GameObject source )
	{
		if ( !target.IsValid() || !source.IsValid() )
			return false;

		var targetPlayer = target.Components.GetInAncestorsOrSelf<DeathrunPlayer>();
		var sourcePlayer = source.Components.GetInAncestorsOrSelf<DeathrunPlayer>();

		if ( !targetPlayer.IsValid() || !sourcePlayer.IsValid() )
			return false;

		var targetOwner = targetPlayer.Owner;
		var sourceOwner = sourcePlayer.Owner;

		return targetOwner is not null && sourceOwner is not null && targetOwner.Id == sourceOwner.Id;
	}

	private static float Clamp( float value, float min, float max )
	{
		return MathF.Min( MathF.Max( value, min ), max );
	}
}
