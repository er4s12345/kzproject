using System;

[Title( "Deathrun Damage Volume" )]
[Category( "Deathrun" )]
[Icon( "blur_on" )]
public sealed class DeathrunDamageVolume : Component, Component.ITriggerListener
{
	[Property] public float DamagePerSecond { get; set; } = 10.0f;
	[Property] public float DamageInterval { get; set; } = 0.5f;
	[Property] public DeathrunDamageType DamageType { get; set; } = DeathrunDamageType.Fire;
	[Property] public bool RequireStayInside { get; set; } = true;
	[Property] public bool InvalidatesRun { get; set; } = true;
	[Property] public bool LogDamageVolume { get; set; } = true;

	private readonly HashSet<DeathrunHealth> _inside = new();
	private Collider _triggerCollider;
	private TimeUntil _timeUntilNextDamage;

	protected override void OnAwake()
	{
		_triggerCollider = Components.Get<Collider>() ?? Components.Create<BoxCollider>();

		if ( _triggerCollider.IsValid() )
			_triggerCollider.IsTrigger = true;
	}

	protected override void OnFixedUpdate()
	{
		// Use the built-in Component.Enabled checkbox as the volume on/off toggle.
		if ( !Enabled || !Networking.IsHost || !RequireStayInside )
			return;

		if ( _timeUntilNextDamage > 0.0f )
			return;

		var interval = MathF.Max( 0.05f, DamageInterval );
		var amount = DamagePerSecond * interval;

		foreach ( var health in _inside.ToArray() )
		{
			if ( !health.IsValid() || health.IsDead )
			{
				_inside.Remove( health );
				continue;
			}

			ApplyDamage( health, amount );
		}

		_timeUntilNextDamage = interval;
	}

	void Component.ITriggerListener.OnTriggerEnter( GameObject other )
	{
		if ( !Enabled || !Networking.IsHost )
			return;

		var health = FindHealth( other );

		if ( !health.IsValid() )
			return;

		if ( RequireStayInside )
		{
			_inside.Add( health );
			return;
		}

		ApplyDamage( health, DamagePerSecond );
	}

	void Component.ITriggerListener.OnTriggerExit( GameObject other )
	{
		var health = FindHealth( other );

		if ( health.IsValid() )
			_inside.Remove( health );
	}

	private void ApplyDamage( DeathrunHealth health, float amount )
	{
		if ( LogDamageVolume )
			Log.Info( $"Damage volume '{GameObject.Name}' applying {amount:0.##} {DamageType} damage to '{health.GameObject.Name}'." );

		health.TakeDamage( new DeathrunDamageInfo
		{
			Amount = amount,
			DamageType = DamageType,
			Source = GameObject,
			SourcePosition = WorldPosition,
			HitPosition = health.WorldPosition,
			Reason = $"Inside damage volume '{GameObject.Name}'",
			InvalidatesRun = InvalidatesRun
		} );
	}

	private static DeathrunHealth FindHealth( GameObject other )
	{
		return other.IsValid() ? other.Components.GetInAncestorsOrSelf<DeathrunHealth>() : null;
	}
}
