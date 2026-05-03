using System;

[Title( "Deathrun Fall Damage" )]
[Category( "Deathrun" )]
[Icon( "vertical_align_bottom" )]
public sealed class DeathrunFallDamage : Component
{
	[Property] public bool EnableFallDamage { get; set; } = true;
	[Property] public float MinFallSpeed { get; set; } = 650.0f;
	[Property] public float FatalFallSpeed { get; set; } = 1400.0f;
	[Property] public float DamageMultiplier { get; set; } = 0.15f;
	[Property] public float MinDamage { get; set; } = 5.0f;
	[Property] public float MaxDamage { get; set; } = 100.0f;
	[Property] public float GraceTimeAfterSpawn { get; set; } = 1.0f;
	[Property] public bool IgnoreFallDamageWhenDead { get; set; } = true;
	[Property] public bool LogFallDamage { get; set; } = false;

	private DeathrunHealth _health;
	private DeathrunPlayerController _playerController;
	private Rigidbody _body;
	private bool _wasGrounded;
	private float _worstFallSpeed;
	private TimeSince _timeSinceStarted;

	protected override void OnStart()
	{
		_health = Components.Get<DeathrunHealth>();
		_playerController = Components.Get<DeathrunPlayerController>();
		_body = Components.Get<Rigidbody>();
		ResetFallTracking();
	}

	public void ResetFallTracking()
	{
		_wasGrounded = IsGrounded();
		_worstFallSpeed = 0.0f;
		_timeSinceStarted = 0.0f;

		if ( LogFallDamage )
			Log.Info( $"'{GameObject.Name}' fall damage tracking reset." );
	}

	protected override void OnFixedUpdate()
	{
		if ( !EnableFallDamage || !Networking.IsHost )
			return;

		if ( !_health.IsValid() )
			_health = Components.Get<DeathrunHealth>();

		if ( !_health.IsValid() )
			return;

		if ( IgnoreFallDamageWhenDead && _health.IsDead )
			return;

		if ( _timeSinceStarted < GraceTimeAfterSpawn )
			return;

		var grounded = IsGrounded();
		var verticalVelocity = GetVelocity().z;

		if ( !grounded )
			_worstFallSpeed = MathF.Max( _worstFallSpeed, MathF.Max( 0.0f, -verticalVelocity ) );

		if ( grounded && !_wasGrounded )
			ApplyLandingDamage( _worstFallSpeed );

		if ( grounded )
			_worstFallSpeed = 0.0f;

		_wasGrounded = grounded;
	}

	private void ApplyLandingDamage( float fallSpeed )
	{
		if ( fallSpeed < MinFallSpeed )
		{
			if ( LogFallDamage && fallSpeed > 0.0f )
				Log.Info( $"'{GameObject.Name}' landed safely. FallSpeed={fallSpeed:0.##}, MinFallSpeed={MinFallSpeed:0.##}." );

			return;
		}

		var lethal = fallSpeed >= FatalFallSpeed;
		var damage = lethal
			? MaxDamage
			: Clamp( (fallSpeed - MinFallSpeed) * DamageMultiplier, MinDamage, MaxDamage );

		if ( LogFallDamage )
			Log.Info( $"'{GameObject.Name}' landed with fall speed {fallSpeed:0.##}. Damage={damage:0.##}, Lethal={lethal}." );

		_health.TakeDamage( new DeathrunDamageInfo
		{
			Amount = damage,
			DamageType = DeathrunDamageType.Fall,
			Source = GameObject,
			SourcePosition = WorldPosition,
			HitPosition = WorldPosition,
			Force = Vector3.Down * fallSpeed,
			Reason = $"Fall speed {fallSpeed:0.##}",
			IsLethal = lethal,
			InvalidatesRun = true
		} );
	}

	private bool IsGrounded()
	{
		_playerController ??= Components.Get<DeathrunPlayerController>();

		if ( _playerController.IsValid() )
			return _playerController.IsOnGround;

		return false;
	}

	private Vector3 GetVelocity()
	{
		_playerController ??= Components.Get<DeathrunPlayerController>();

		if ( _playerController.IsValid() )
			return _playerController.Velocity;

		_body ??= Components.Get<Rigidbody>();
		return _body.IsValid() ? _body.Velocity : Vector3.Zero;
	}

	private static float Clamp( float value, float min, float max )
	{
		return MathF.Min( MathF.Max( value, min ), max );
	}
}
