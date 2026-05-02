[Title( "Deathrun Kill Zone" )]
[Category( "Deathrun" )]
[Icon( "dangerous" )]
public sealed class DeathrunKillZone : Component, Component.ITriggerListener
{
	[Property] public float DamageAmount { get; set; } = 9999.0f;
	[Property] public bool KillInstantly { get; set; } = true;
	[Property] public DeathrunDamageType DamageType { get; set; } = DeathrunDamageType.KillZone;
	[Property] public bool InvalidatesRun { get; set; } = true;
	[Property] public bool LogKillZone { get; set; } = true;

	private Collider _triggerCollider;

	protected override void OnAwake()
	{
		_triggerCollider = Components.Get<Collider>() ?? Components.Create<BoxCollider>();

		if ( _triggerCollider.IsValid() )
			_triggerCollider.IsTrigger = true;
	}

	void Component.ITriggerListener.OnTriggerEnter( GameObject other )
	{
		// Use the built-in Component.Enabled checkbox as the kill-zone on/off toggle.
		if ( !Enabled || !Networking.IsHost )
			return;

		var health = FindHealth( other );

		if ( !health.IsValid() || health.IsDead )
			return;

		if ( LogKillZone )
			Log.Info( $"Kill zone '{GameObject.Name}' hit '{health.GameObject.Name}'." );

		health.TakeDamage( new DeathrunDamageInfo
		{
			Amount = DamageAmount,
			DamageType = DamageType,
			Source = GameObject,
			SourcePosition = WorldPosition,
			HitPosition = health.WorldPosition,
			Reason = $"Entered kill zone '{GameObject.Name}'",
			IsLethal = KillInstantly,
			InvalidatesRun = InvalidatesRun
		} );
	}

	void Component.ITriggerListener.OnTriggerExit( GameObject other )
	{
	}

	private static DeathrunHealth FindHealth( GameObject other )
	{
		if ( !other.IsValid() )
			return null;

		var health = other.Components.GetInAncestorsOrSelf<DeathrunHealth>();

		if ( health.IsValid() )
			return health;

		return other.Components.GetInDescendantsOrSelf<DeathrunHealth>( true );
	}
}
