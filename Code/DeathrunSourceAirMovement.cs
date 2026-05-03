using System;

[Title( "Deathrun Source Air Movement" )]
[Category( "Deathrun" )]
[Icon( "air" )]
public sealed class DeathrunSourceAirMovement : Component
{
	public enum DriverMode
	{
		AdditiveAirControl,
		FullWishVelocity
	}

	[Property] public bool EnableSourceAirMovement { get; set; } = true;
	[Property] public DriverMode Driver { get; set; } = DriverMode.AdditiveAirControl;
	[Property] public float AirAcceleration { get; set; } = 12.0f;
	[Property] public float AirControl { get; set; } = 0.35f;
	[Property] public float MaxAirWishSpeed { get; set; } = 420.0f;
	[Property] public float MaxAirVelocity { get; set; } = 900.0f;
	[Property] public float StrafeMultiplier { get; set; } = 1.0f;
	[Property] public bool PreserveMomentum { get; set; } = true;
	[Property] public bool EnableBunnyhop { get; set; } = false;
	[Property] public string JumpButton { get; set; } = "Jump";
	[Property] public float AutoJumpWindow { get; set; } = 0.1f;
	[Property] public float GroundFriction { get; set; } = 6.0f;
	[Property] public float GroundAcceleration { get; set; } = 10.0f;
	[Property] public float MaxGroundSpeed { get; set; } = 320.0f;
	[Property] public bool LogMovementDebug { get; set; } = false;

	private PlayerController _playerController;
	private DeathrunHealth _health;
	private bool _storedUseInputControls;
	private bool _hasStoredUseInputControls;
	private bool _fullWishVelocityActive;
	private bool _jumpPressedQueued;
	private bool _wasDead;
	private bool _skipMovementAfterRespawn;
	private string _lastBlockedReason;
	private TimeSince _timeSinceLastStateLog;
	private TimeSince _timeSinceJumpPressed;

	protected override void OnStart()
	{
		CacheComponents();
		_timeSinceJumpPressed = AutoJumpWindow + 1.0f;
		_wasDead = IsDead();
		LogMovementState( "started" );
	}

	protected override void OnDisabled()
	{
		RestorePlayerControllerInput();
	}

	protected override void OnDestroy()
	{
		RestorePlayerControllerInput();
	}

	protected override void OnUpdate()
	{
		UpdateDeathState();

		if ( !CanRunMovement( out var blockedReason ) )
		{
			ClearBufferedInput();
			LogBlockedState( blockedReason );
			return;
		}

		if ( IsJumpPressed() )
		{
			_jumpPressedQueued = true;
			_timeSinceJumpPressed = 0.0f;
			LogMovementState( "jump input pressed" );
		}
	}

	protected override void OnFixedUpdate()
	{
		CacheComponents();
		UpdateDeathState();

		if ( !_playerController.IsValid() )
		{
			ClearBufferedInput();
			LogBlockedState( "missing PlayerController" );
			return;
		}

		if ( !HasLocalMovementControl() )
		{
			ClearBufferedInput();
			LogBlockedState( "not local owner" );

			if ( !IsDead() )
				RestorePlayerControllerInput();

			return;
		}

		if ( IsDead() )
		{
			ClearBufferedInput();
			LogBlockedState( "dead" );
			return;
		}

		if ( !EnableSourceAirMovement )
		{
			ClearBufferedInput();
			RestorePlayerControllerInput();
			LogBlockedState( "disabled" );
			return;
		}

		if ( _skipMovementAfterRespawn )
		{
			ClearBufferedInput();
			_skipMovementAfterRespawn = false;

			if ( Driver == DriverMode.FullWishVelocity )
				UpdateDriverInputState();
			else
				RestorePlayerControllerInput();

			LogMovementState( "respawn movement resume deferred" );
			return;
		}

		UpdateDriverInputState();

		var grounded = _playerController.IsOnGround;
		var wishDirection = GetWishDirection( out var wishSpeed );

		if ( grounded )
		{
			if ( Driver == DriverMode.FullWishVelocity )
				ApplyFullGroundMove( wishDirection, wishSpeed );

			if ( EnableBunnyhop || Driver == DriverMode.FullWishVelocity )
				TryBufferedJump();

			return;
		}

		if ( Driver == DriverMode.FullWishVelocity )
			_playerController.WishVelocity = wishDirection * wishSpeed;

		var velocity = _playerController.Velocity;

		if ( wishSpeed > 0.0f )
		{
			velocity = AirAccelerate( velocity, wishDirection, wishSpeed, AirAcceleration, Time.Delta );
			velocity = ApplyAirControl( velocity, wishDirection, Time.Delta );
		}

		velocity = ClampHorizontalVelocity( velocity, MaxAirVelocity );

		SetVelocity( velocity );

		if ( LogMovementDebug )
			Log.Info( $"SourceAir '{GameObject.Name}' air velocity={velocity}, wishSpeed={wishSpeed:0.##}, driver={Driver}." );
	}

	public static Vector3 AirAccelerate( Vector3 currentVelocity, Vector3 wishDirection, float wishSpeed, float acceleration, float deltaTime )
	{
		if ( wishSpeed <= 0.0f || acceleration <= 0.0f || deltaTime <= 0.0f || wishDirection.LengthSquared <= 0.0001f )
			return currentVelocity;

		var currentSpeed = Vector3.Dot( currentVelocity, wishDirection );
		var addSpeed = wishSpeed - currentSpeed;

		if ( addSpeed <= 0.0f )
			return currentVelocity;

		var accelSpeed = acceleration * wishSpeed * deltaTime;
		accelSpeed = MathF.Min( accelSpeed, addSpeed );

		return currentVelocity + wishDirection * accelSpeed;
	}

	private void ApplyFullGroundMove( Vector3 wishDirection, float wishSpeed )
	{
		wishSpeed = MathF.Min( wishSpeed, MathF.Max( 0.0f, MaxGroundSpeed ) );
		_playerController.WishVelocity = wishDirection * wishSpeed;

		var velocity = _playerController.Velocity;
		velocity = ApplyGroundFriction( velocity, Time.Delta );
		velocity = AirAccelerate( velocity, wishDirection, wishSpeed, GroundAcceleration, Time.Delta );
		velocity = ClampHorizontalVelocity( velocity, MaxGroundSpeed );

		SetVelocity( velocity );
	}

	private Vector3 ApplyGroundFriction( Vector3 velocity, float deltaTime )
	{
		if ( GroundFriction <= 0.0f || deltaTime <= 0.0f )
			return velocity;

		var horizontal = velocity.WithZ( 0.0f );
		var speed = horizontal.Length;

		if ( speed <= 0.01f )
			return velocity.WithX( 0.0f ).WithY( 0.0f );

		var drop = speed * GroundFriction * deltaTime;
		var newSpeed = MathF.Max( speed - drop, 0.0f );
		horizontal *= newSpeed / speed;

		return horizontal.WithZ( velocity.z );
	}

	private Vector3 ApplyAirControl( Vector3 velocity, Vector3 wishDirection, float deltaTime )
	{
		if ( AirControl <= 0.0f || deltaTime <= 0.0f || wishDirection.LengthSquared <= 0.0001f )
			return velocity;

		var horizontal = velocity.WithZ( 0.0f );
		var speed = horizontal.Length;

		if ( speed <= 0.01f )
			return velocity;

		var control = MathF.Min( MathF.Max( AirControl, 0.0f ) * deltaTime * 8.0f, 1.0f );
		var controlled = PreserveMomentum
			? (horizontal.Normal + wishDirection * control).Normal * speed
			: horizontal + (wishDirection * speed - horizontal) * control;

		return controlled.WithZ( velocity.z );
	}

	private Vector3 GetWishDirection( out float wishSpeed )
	{
		wishSpeed = 0.0f;

		if ( !HasLocalMovementControl() || IsDead() )
			return Vector3.Zero;

		var input = Input.AnalogMove.WithZ( 0.0f );
		var inputLength = input.Length;

		if ( inputLength <= 0.01f )
			return Vector3.Zero;

		input = input.ClampLength( 1.0f );

		var eyeYaw = Rotation.FromYaw( _playerController.EyeAngles.yaw );
		var wishDirection = (eyeYaw * input).WithZ( 0.0f );

		if ( wishDirection.LengthSquared <= 0.0001f )
			return Vector3.Zero;

		wishSpeed = MathF.Min( inputLength, 1.0f ) * MathF.Max( 0.0f, MaxAirWishSpeed ) * MathF.Max( 0.0f, StrafeMultiplier );
		return wishDirection.Normal;
	}

	private void TryBufferedJump()
	{
		if ( !HasLocalMovementControl() || IsDead() )
			return;

		if ( !_playerController.IsOnGround )
			return;

		var wantsJump = EnableBunnyhop
			? IsJumpDown() || _timeSinceJumpPressed <= MathF.Max( 0.0f, AutoJumpWindow )
			: _jumpPressedQueued;

		if ( !wantsJump )
			return;

		_jumpPressedQueued = false;
		_timeSinceJumpPressed = AutoJumpWindow + 1.0f;
		_playerController.Jump( Vector3.Up * _playerController.JumpSpeed );

		if ( LogMovementDebug )
			Log.Info( $"SourceAir '{GameObject.Name}' jump applied. Bunnyhop={EnableBunnyhop}, button='{JumpButton}', driver={Driver}." );
	}

	private Vector3 ClampHorizontalVelocity( Vector3 velocity, float maxSpeed )
	{
		if ( maxSpeed <= 0.0f )
			return velocity;

		var horizontal = velocity.WithZ( 0.0f );
		var speed = horizontal.Length;

		if ( speed <= maxSpeed )
			return velocity;

		horizontal = horizontal.Normal * maxSpeed;
		return horizontal.WithZ( velocity.z );
	}

	private void SetVelocity( Vector3 velocity )
	{
		// PlayerController.Velocity is readable in game code, but this S&box build does not expose
		// its setter to project code. The controller body is the supported fallback used elsewhere
		// in this project for respawn, death freeze, and teleports.
		if ( _playerController.Body.IsValid() )
		{
			_playerController.Body.Velocity = velocity + _playerController.GroundVelocity;
			return;
		}

		_playerController.WishVelocity = velocity.WithZ( 0.0f );
	}

	private void UpdateDriverInputState()
	{
		if ( Driver == DriverMode.FullWishVelocity )
		{
			if ( !_fullWishVelocityActive )
			{
				_storedUseInputControls = _playerController.UseInputControls;
				_hasStoredUseInputControls = true;
				_fullWishVelocityActive = true;
				LogMovementState( "FullWishVelocity taking input control" );
			}

			_playerController.UseInputControls = false;
			return;
		}

		RestorePlayerControllerInput();
	}

	private void RestorePlayerControllerInput()
	{
		if ( !_fullWishVelocityActive || !_hasStoredUseInputControls )
			return;

		CacheComponents();

		if ( !_playerController.IsValid() )
			return;

		if ( IsDead() )
			return;

		_playerController.UseInputControls = _storedUseInputControls;
		_fullWishVelocityActive = false;
		_hasStoredUseInputControls = false;
		LogMovementState( "restored PlayerController input" );
	}

	private bool CanRunMovement()
	{
		return CanRunMovement( out _ );
	}

	private bool CanRunMovement( out string blockedReason )
	{
		CacheComponents();

		if ( !EnableSourceAirMovement )
		{
			blockedReason = "disabled";
			return false;
		}

		if ( !_playerController.IsValid() )
		{
			blockedReason = "missing PlayerController";
			return false;
		}

		if ( IsDead() )
		{
			blockedReason = "dead";
			return false;
		}

		if ( !HasLocalMovementControl() )
		{
			blockedReason = "not local owner";
			return false;
		}

		if ( _skipMovementAfterRespawn )
		{
			blockedReason = "respawn defer";
			return false;
		}

		blockedReason = null;
		return true;
	}

	private void UpdateDeathState()
	{
		var isDead = IsDead();

		if ( isDead )
		{
			ClearBufferedInput();
			_skipMovementAfterRespawn = false;
		}
		else if ( _wasDead )
		{
			ClearBufferedInput();
			_skipMovementAfterRespawn = true;
			LogMovementState( "alive after death state sync" );
		}

		_wasDead = isDead;
	}

	private void ClearBufferedInput()
	{
		_jumpPressedQueued = false;
		_timeSinceJumpPressed = AutoJumpWindow + 1.0f;
	}

	private bool IsJumpPressed()
	{
		return !string.IsNullOrWhiteSpace( JumpButton ) && Input.Pressed( JumpButton );
	}

	private bool IsJumpDown()
	{
		return !string.IsNullOrWhiteSpace( JumpButton ) && Input.Down( JumpButton );
	}

	private void LogBlockedState( string reason )
	{
		if ( !LogMovementDebug )
			return;

		if ( _lastBlockedReason == reason && _timeSinceLastStateLog < 1.0f )
			return;

		_lastBlockedReason = reason;
		LogMovementState( $"blocked: {reason}" );
	}

	private void LogMovementState( string reason )
	{
		if ( !LogMovementDebug )
			return;

		_timeSinceLastStateLog = 0.0f;
		var owner = GameObject.Network.Owner;
		var ownerName = owner?.DisplayName ?? "none";
		var ownerId = owner?.Id.ToString() ?? GameObject.Network.OwnerId.ToString();
		var networkActive = GameObject.Network.Active;
		var isOwner = networkActive && GameObject.Network.IsOwner;
		var hasLocalControl = HasLocalMovementControl();
		var jumpPressed = IsJumpPressed();
		var jumpDown = IsJumpDown();

		Log.Info(
			$"SourceAir '{GameObject.Name}' {reason}. Owner={ownerName} ({ownerId}), NetworkActive={networkActive}, IsOwner={isOwner}, HasLocalControl={hasLocalControl}, " +
			$"Input={_playerController.IsValid() && _playerController.UseInputControls}, Look={_playerController.IsValid() && _playerController.UseLookControls}, Camera={_playerController.IsValid() && _playerController.UseCameraControls}, " +
			$"IsDead={IsDead()}, IsOnGround={_playerController.IsValid() && _playerController.IsOnGround}, JumpSpeed={(_playerController.IsValid() ? _playerController.JumpSpeed : 0.0f):0.##}, " +
			$"Driver={Driver}, JumpButton='{JumpButton}', JumpPressed={jumpPressed}, JumpDown={jumpDown}." );
	}

	private bool IsDead()
	{
		_health ??= Components.Get<DeathrunHealth>();
		return _health.IsValid() && _health.IsDead;
	}

	private bool HasLocalMovementControl()
	{
		if ( !GameObject.Network.Active )
			return !Networking.IsActive;

		return GameObject.Network.IsOwner;
	}

	private void CacheComponents()
	{
		if ( !_playerController.IsValid() )
			_playerController = Components.Get<PlayerController>();

		if ( !_health.IsValid() )
			_health = Components.Get<DeathrunHealth>();
	}
}
