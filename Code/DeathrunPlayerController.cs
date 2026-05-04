using System;
using System.Collections.Generic;
using System.Linq;

public enum DeathrunAirAccelerationMode
{
	Facepunch,
	Source
}

// Project-local fork/adaptation of Facepunch sbox-public PlayerController (MIT).
// Deathrun-specific behavior lives here as tuning and lifecycle hooks so the
// owning client has one movement controller, not a stack of competing scripts.
[Title( "Deathrun Player Controller" )]
[Category( "Deathrun" )]
[Icon( "directions_run" )]
public sealed class DeathrunPlayerController : Component, Component.INetworkSpawn, IScenePhysicsEvents
{
	private const float Skin = 0.095f;

	[Property, Group( "References" )] public Rigidbody Body { get; set; }
	[Property, Group( "References" )] public SkinnedModelRenderer Renderer { get; set; }
	[Property, Group( "References" )] public GameObject ColliderObject { get; set; }

	public CapsuleCollider BodyCollider { get; private set; }
	public BoxCollider FeetCollider { get; private set; }

	[Property, Group( "Input" )] public bool UseInputControls { get; set; } = true;
	[Property, Group( "Input" )] public bool UseLookControls { get; set; } = true;
	[Property, Group( "Input" )] public bool UseCameraControls { get; set; } = true;
	[Property, Group( "Input" )] public float WalkSpeed { get; set; } = 110.0f;
	[Property, Group( "Input" )] public float RunSpeed { get; set; } = 320.0f;
	[Property, Group( "Input" )] public float DuckedSpeed { get; set; } = 70.0f;
	[Property, Group( "Input" )] public float JumpSpeed { get; set; } = 300.0f;
	[Property, Group( "Input" )] public float DuckedHeight { get; set; } = 36.0f;
	// Deprecated for physics movement. Kept for saved inspector compatibility; GroundAcceleration now controls acceleration.
	[Property, Group( "Input" )] public float AccelerationTime { get; set; } = 0.0f;
	// Deprecated for physics movement. GroundFriction and StopSpeed now control braking.
	[Property, Group( "Input" )] public float DeaccelerationTime { get; set; } = 0.0f;
	[Property, Group( "Input" ), InputAction] public string JumpButton { get; set; } = "Jump";
	[Property, Group( "Input" ), InputAction] public string DuckButton { get; set; } = "Duck";
	[Property, Group( "Input" ), InputAction] public string AltMoveButton { get; set; } = "Run";
	[Property, Group( "Input" ), InputAction] public string UseButton { get; set; } = "Use";
	[Property, Group( "Input" )] public bool RunByDefault { get; set; } = false;
	[Property, Group( "Input" )] public bool EnablePressing { get; set; } = true;
	[Property, Group( "Input" )] public float ReachLength { get; set; } = 130.0f;
	[Property, Group( "Input" )] public float LookSensitivity { get; set; } = 1.0f;
	[Property, Group( "Input" )] public bool RotateWithGround { get; set; } = true;
	[Property, Group( "Input" ), Range( 0, 180 )] public float PitchClamp { get; set; } = 90.0f;
	[Property, Group( "Input" )] public float JumpCooldown { get; set; } = 0.1f;
	[Property, Group( "Input" )] public float CoyoteTime { get; set; } = 0.2f;

	[Property, Group( "Body" ), Range( 1, 64 )] public float BodyRadius { get; set; } = 16.0f;
	[Property, Group( "Body" ), Range( 1, 128 )] public float BodyHeight { get; set; } = 72.0f;
	[Property, Group( "Body" ), Range( 1, 1000 )] public float BodyMass { get; set; } = 500.0f;
	[Property, Group( "Body" )] public TagSet BodyCollisionTags { get; set; }
	[Property, Group( "Body" )] public bool FreezeBodyWhenMovementDisabled { get; set; } = true;

	[Property, Group( "Movement" )] public float GroundAngle { get; set; } = 45.0f;
	[Property, Group( "Movement" )] public float StepUpHeight { get; set; } = 18.0f;
	[Property, Group( "Movement" )] public float StepDownHeight { get; set; } = 18.0f;
	[Property, Group( "Movement" )] public float GroundFriction { get; set; } = 6.0f;
	[Property, Group( "Movement" )] public float StopSpeed { get; set; } = 100.0f;
	[Property, Group( "Movement" )] public float IdleVelocityEpsilon { get; set; } = 1.0f;
	[Property, Group( "Movement" ), Range( 0, 1 )] public float BrakePower { get; set; } = 1.0f;
	[Property, Group( "Movement" ), Range( 0, 1 )] public float AirFriction { get; set; } = 0.1f;
	// Legacy selector retained for saved inspector values; air movement now always uses acceleration-based integration.
	[Property, Group( "Movement" )] public DeathrunAirAccelerationMode AirAccelerationMode { get; set; } = DeathrunAirAccelerationMode.Source;
	[Property, Group( "Movement" )] public float GroundAcceleration { get; set; } = 10.0f;
	[Property, Group( "Movement" )] public float MaxGroundSpeed { get; set; } = 0.0f;
	[Property, Group( "Movement" )] public float AirAcceleration { get; set; } = 12.0f;
	[Property, Group( "Movement" )] public float AirControl { get; set; } = 0.35f;
	[Property, Group( "Movement" )] public float MaxAirWishSpeed { get; set; } = 0.0f;
	[Property, Group( "Movement" )] public float MaxAirVelocity { get; set; } = 900.0f;
	[Property, Group( "Movement" )] public float StrafeMultiplier { get; set; } = 1.0f;
	[Property, Group( "Movement" )] public bool PreserveAirMomentum { get; set; } = true;
	[Property, Group( "Movement" )] public bool EnableBunnyhop { get; set; } = false;
	[Property, Group( "Movement" )] public float AutoJumpWindow { get; set; } = 0.12f;
	[Property, Group( "Movement" )] public bool BunnyhopPreserveFriction { get; set; } = true;
	[Property, Group( "Movement" )] public bool EnableGroundTransformCarry { get; set; } = true;
	[Property, Group( "Movement" )] public bool InheritGroundVelocityOnJump { get; set; } = true;
	[Property, Group( "Movement" )] public float MaxGroundCarryDistance { get; set; } = 96.0f;

	[Property, Group( "Camera" )] public float EyeDistanceFromTop { get; set; } = 8.0f;
	[Property, Group( "Camera" )] public bool ThirdPerson { get; set; } = false;
	[Property, Group( "Camera" )] public bool HideBodyInFirstPerson { get; set; } = true;
	[Property, Group( "Camera" )] public bool UseFovFromPreferences { get; set; } = true;
	[Property, Group( "Camera" )] public Vector3 CameraOffset { get; set; } = new( 256.0f, 0.0f, 12.0f );
	[Property, Group( "Camera" ), InputAction] public string ToggleCameraModeButton { get; set; } = "View";

	[Property, Group( "Animation" )] public bool UseAnimatorControls { get; set; } = true;
	[Property, Group( "Animation" )] public float RotationAngleLimit { get; set; } = 45.0f;
	[Property, Group( "Animation" )] public float RotationSpeed { get; set; } = 1.0f;
	[Property, Group( "Debug" )] public bool LogControllerDebug { get; set; } = false;
	[Property, Group( "Debug" )] public bool LogMovementDebug { get; set; } = false;
	[Property, Group( "Debug" )] public bool LogAirControlDebug { get; set; } = false;
	[Property, Group( "Debug" )] public bool StepDebug { get; set; } = false;

	[Sync] public Vector3 WishVelocity { get; set; }
	[Sync( SyncFlags.Interpolate )] public Angles EyeAngles { get; set; }
	[Sync] public bool IsDucking { get; set; }

	public Vector3 Velocity { get; private set; }
	public Vector3 GroundVelocity { get; private set; }
	public Vector3 BaseVelocity => _baseVelocity;
	public bool IsOnGround => _isOnGround;
	public bool IsAirborne => !IsOnGround;
	public GameObject GroundObject { get; private set; }
	public Component GroundComponent { get; private set; }
	public Surface GroundSurface { get; private set; }
	public bool GroundIsDynamic { get; private set; }
	public float CurrentGroundFriction { get; private set; }
	public TimeSince TimeSinceGrounded { get; private set; } = 0.0f;
	public TimeSince TimeSinceUngrounded { get; private set; } = 0.0f;
	public Transform EyeTransform { get; private set; }
	public Vector3 EyePosition => EyeTransform.Position;
	public float CurrentHeight => IsDucking ? DuckedHeight : BodyHeight;
	public float Headroom { get; private set; }
	public Component Hovered { get; private set; }
	public Component Pressed { get; private set; }
	public List<IPressable.Tooltip> Tooltips { get; } = new();

	private TimeSince _timeSinceJump;
	private TimeSince _timeSinceDebugLog;
	private TimeSince _timeSinceMovementDebug = 999.0f;
	private TimeSince _timeSinceJumpPressed = 999.0f;
	private TimeUntil _timeUntilAllowedGround;
	private bool _isOnGround;
	private bool _wasFalling;
	private bool _jumpedSinceGrounded;
	private bool _jumpQueued;
	private bool _skipGroundFrictionThisTick;
	private float _fallDistance;
	private Vector3 _previousPosition;
	private bool _didStep;
	private Vector3 _stepPosition;
	private Vector3 _baseVelocity;
	private Vector3 _groundTransformVelocity;
	private Vector3 _bodyDuckOffset;
	private float _cameraDistance = 100.0f;
	private float _smoothedEyeZ;
	private Transform _localGroundTransform;
	private int _groundHash;
	private bool _hasStoredMovementState;
	private bool _storedUseInputControls;
	private bool _storedUseLookControls;
	private bool _storedUseCameraControls;
	private bool _storedBodyMotionEnabled;

	public interface IEvents : ISceneEvent<IEvents>
	{
		void OnEyeAngles( ref Angles angles ) { }
		void PostCameraSetup( CameraComponent camera ) { }
		void OnJumped() { }
		void OnLanded( float distance, Vector3 impactVelocity ) { }
		Component GetUsableComponent( GameObject gameObject ) { return default; }
		void StartPressing( Component target ) { }
		void StopPressing( Component target ) { }
		void FailPressing() { }
		void PreInput() { }
	}

	protected override void OnAwake()
	{
		RemoveLegacyRootColliders();
		CacheReferences();
		DisableLegacyPlayerController();
		UpdateBodySetup();
		EyeAngles = WorldRotation.Angles() with { pitch = 0.0f, roll = 0.0f };
		WorldRotation = Rotation.Identity;
		PreviousPositionReset();
		UpdateEyeTransform();
	}

	protected override void OnEnabled()
	{
		CacheReferences();
		DisableLegacyPlayerController();
		UpdateBodySetup();
		CategorizeGround();
		PreviousPositionReset();
		UpdateEyeTransform();
		LogState( "enabled" );
	}

	protected override void OnDisabled()
	{
		StopPressing();
		SwitchHovered( null );
	}

	public void OnNetworkSpawn( Connection owner )
	{
		CacheReferences();
		DisableLegacyPlayerController();
		ResetForRespawn( WorldTransform, "network spawn" );
	}

	protected override void OnUpdate()
	{
		CacheReferences();
		UpdateVelocity();

		if ( CanProcessLocalInput() )
		{
			IEvents.PostToGameObject( GameObject, x => x.PreInput() );

			if ( UseLookControls )
			{
				UpdateEyeAngles();
				UpdateLookAt();
			}

			UpdateEyeTransform();

			if ( UseCameraControls )
				UpdateCameraPosition();
		}

		UpdateBodyVisibility();
		UpdateAnimation();
		LogPeriodicDebug();
	}

	protected override void OnFixedUpdate()
	{
		CacheReferences();

		if ( Scene.IsEditor )
			return;

		UpdateHeadroom();
		UpdateFalling();
		_previousPosition = WorldPosition;

		if ( !CanProcessLocalInput() || !UseInputControls || !Body.IsValid() || !Body.MotionEnabled )
		{
			WishVelocity = Vector3.Zero;
			return;
		}

		ApplyGroundTransformDelta();
		WishVelocity = BuildWishVelocity();
		UpdateJumpQueue();
		UpdateDucking( Input.Down( DuckButton ) );
		TryJump();
		UpdateEyeTransform();
	}

	void IScenePhysicsEvents.PrePhysicsStep()
	{
		CacheReferences();
		UpdateBodySetup();

		if ( !CanProcessLocalInput() || !UseInputControls || !Body.IsValid() || !Body.MotionEnabled )
			return;

		AddMovementVelocity();
		TryStep( StepUpHeight );
	}

	void IScenePhysicsEvents.PostPhysicsStep()
	{
		CacheReferences();

		if ( !Body.IsValid() )
			return;

		UpdateVelocity();
		UpdateGroundVelocity();
		RestoreStep();
		Reground( StepDownHeight );
		CategorizeGround();
		StoreGroundTransform();
		UpdateBodySetup();
		UpdateVelocity();
	}

	public bool CanProcessLocalInput()
	{
		if ( !GameObject.IsValid() )
			return false;

		if ( !Networking.IsActive )
			return true;

		if ( !GameObject.Network.Active )
			return false;

		return !GameObject.Network.IsProxy;
	}

	public bool ShouldProcessLocalInput()
	{
		return CanProcessLocalInput();
	}

	public void InitializeSpawnState( string reason = "spawn initialized" )
	{
		ResetForRespawn( WorldTransform, reason );
	}

	public void ResetForRespawn( Transform respawnTransform )
	{
		ResetForRespawn( respawnTransform, "respawn reset" );
	}

	public void ResetForRespawn( Transform respawnTransform, string reason )
	{
		CacheReferences();
		Enabled = true;
		GameObject.Enabled = true;
		WorldTransform = respawnTransform.WithScale( 1.0f );
		IsDucking = false;
		WishVelocity = Vector3.Zero;
		Velocity = Vector3.Zero;
		ClearBaseVelocity();
		ClearGround();
		_groundTransformVelocity = Vector3.Zero;
		_jumpedSinceGrounded = false;
		_jumpQueued = false;
		_skipGroundFrictionThisTick = false;
		_timeUntilAllowedGround = 0.0f;
		_timeSinceJump = JumpCooldown;
		_timeSinceJumpPressed = 999.0f;
		_bodyDuckOffset = Vector3.Zero;
		_hasStoredMovementState = false;
		UseInputControls = true;
		UseLookControls = true;
		UseCameraControls = true;
		PreviousPositionReset();

		if ( Body.IsValid() )
		{
			Body.Enabled = true;
			Body.MotionEnabled = true;
			Body.Velocity = Vector3.Zero;
			Body.Sleeping = false;
		}

		if ( GameObject.Network.Active )
			GameObject.Network.ClearInterpolation();

		UpdateBodySetup();
		UpdateHeadroom();
		CategorizeGround();
		UpdateEyeTransform();
		LogState( reason );
	}

	public void SetMovementEnabled( bool enabled )
	{
		CacheReferences();
		DisableLegacyPlayerController();

		if ( enabled )
		{
			Enabled = true;
			UseInputControls = _hasStoredMovementState ? _storedUseInputControls : true;
			UseLookControls = _hasStoredMovementState ? _storedUseLookControls : true;
			UseCameraControls = _hasStoredMovementState ? _storedUseCameraControls : true;

			if ( Body.IsValid() )
			{
				Body.Enabled = true;
				Body.MotionEnabled = _hasStoredMovementState ? _storedBodyMotionEnabled : true;
				Body.Sleeping = false;
			}

			_hasStoredMovementState = false;
			LogState( "movement enabled" );
			return;
		}

		if ( !_hasStoredMovementState )
		{
			_storedUseInputControls = UseInputControls;
			_storedUseLookControls = UseLookControls;
			_storedUseCameraControls = UseCameraControls;
			_storedBodyMotionEnabled = Body.IsValid() && Body.MotionEnabled;
			_hasStoredMovementState = true;
		}

		UseInputControls = false;
		UseLookControls = false;
		UseCameraControls = false;
		WishVelocity = Vector3.Zero;
		_jumpQueued = false;
		_skipGroundFrictionThisTick = false;
		ClearBaseVelocity();
		ClearGround();
		StopPressing();

		if ( Body.IsValid() )
		{
			Body.Velocity = Vector3.Zero;

			if ( FreezeBodyWhenMovementDisabled )
				Body.MotionEnabled = false;
		}

		LogState( "movement disabled" );
	}

	public void AddBaseVelocity( Vector3 velocity )
	{
		_baseVelocity += velocity;
	}

	public void ClearBaseVelocity()
	{
		_baseVelocity = Vector3.Zero;
	}

	public void ClearVelocity()
	{
		WishVelocity = Vector3.Zero;
		Velocity = Vector3.Zero;
		GroundVelocity = Vector3.Zero;
		_jumpQueued = false;
		_skipGroundFrictionThisTick = false;
		ClearBaseVelocity();

		if ( Body.IsValid() )
		{
			Body.Velocity = Vector3.Zero;
			Body.Sleeping = false;
		}
	}

	public void TeleportTo( Vector3 position, bool resetVelocity, string reason = "teleport" )
	{
		CacheReferences();
		WorldPosition = position;
		Transform.ClearInterpolation();

		if ( resetVelocity )
			ClearVelocity();
		else
			UpdateVelocity();

		ClearBaseVelocity();
		ClearGround();
		_timeUntilAllowedGround = 0.0f;
		_jumpedSinceGrounded = false;
		_groundTransformVelocity = Vector3.Zero;
		_jumpQueued = false;
		_skipGroundFrictionThisTick = false;
		StopPressing();
		SwitchHovered( null );
		PreviousPositionReset();

		if ( Body.IsValid() )
			Body.Sleeping = false;

		if ( GameObject.Network.Active )
			GameObject.Network.ClearInterpolation();

		UpdateBodySetup();
		UpdateHeadroom();
		CategorizeGround();
		StoreGroundTransform();
		UpdateEyeTransform();
		UpdateVelocity();
		LogState( reason );
	}

	public void Jump( Vector3 velocity )
	{
		if ( !Body.IsValid() )
			return;

		PreventGrounding( 0.2f );

		var currentVelocity = Body.Velocity;

		if ( InheritGroundVelocityOnJump && !_groundTransformVelocity.IsNearlyZero( 0.01f ) )
			currentVelocity += _groundTransformVelocity;

		var direction = velocity.Normal;
		var oppositeSpeed = Vector3.Dot( currentVelocity, direction );

		if ( oppositeSpeed < 0.0f )
			currentVelocity -= direction * oppositeSpeed;

		Body.Velocity = AddClampedDirection( currentVelocity, velocity, velocity.Length );
		Body.Sleeping = false;
		LogState( "jump applied" );
	}

	public GameObject CreateRagdoll( string name = "Ragdoll" )
	{
		var ragdoll = new GameObject( true, name );
		ragdoll.Tags.Add( "ragdoll" );
		ragdoll.WorldTransform = WorldTransform;

		if ( !Renderer.IsValid() )
			return ragdoll;

		var mainBody = ragdoll.Components.Create<SkinnedModelRenderer>();
		mainBody.CopyFrom( Renderer );
		mainBody.UseAnimGraph = false;

		foreach ( var clothing in Renderer.GameObject.Children.SelectMany( x => x.Components.GetAll<SkinnedModelRenderer>() ) )
		{
			if ( !clothing.IsValid() )
				continue;

			var clothingObject = new GameObject( true, clothing.GameObject.Name );
			clothingObject.Parent = ragdoll;

			var clothingRenderer = clothingObject.Components.Create<SkinnedModelRenderer>();
			clothingRenderer.CopyFrom( clothing );
			clothingRenderer.BoneMergeTarget = mainBody;
		}

		var physics = ragdoll.Components.Create<ModelPhysics>();
		physics.Model = mainBody.Model;
		physics.Renderer = mainBody;
		physics.CopyBonesFrom( Renderer, true );

		return ragdoll;
	}

	private void CacheReferences()
	{
		if ( !Body.IsValid() )
			Body = Components.GetOrCreate<Rigidbody>();

		if ( !Renderer.IsValid() )
			Renderer = GameObject.Children
				.SelectMany( x => x.Components.GetAll<SkinnedModelRenderer>() )
				.FirstOrDefault( x => x.IsValid() );

		if ( !ColliderObject.IsValid() )
			ColliderObject = GameObject.Children.FirstOrDefault( x => x.IsValid() && x.Name == "Colliders" );

		if ( !ColliderObject.IsValid() )
			ColliderObject = new GameObject( GameObject, true, "Colliders" );

		ColliderObject.LocalTransform = global::Transform.Zero;

		if ( BodyCollisionTags is not null )
			ColliderObject.Tags.SetFrom( BodyCollisionTags );

		BodyCollider = ColliderObject.Components.GetOrCreate<CapsuleCollider>();
		FeetCollider = ColliderObject.Components.GetOrCreate<BoxCollider>();
	}

	private void RemoveLegacyRootColliders()
	{
		var boxCollider = Components.Get<BoxCollider>();
		var capsuleCollider = Components.Get<CapsuleCollider>();

		if ( boxCollider.IsValid() && capsuleCollider.IsValid() && !boxCollider.IsTrigger && !capsuleCollider.IsTrigger )
		{
			boxCollider.Destroy();
			capsuleCollider.Destroy();
		}
	}

	private void DisableLegacyPlayerController()
	{
		var legacy = Components.Get<PlayerController>();

		if ( !legacy.IsValid() )
			return;

		legacy.UseInputControls = false;
		legacy.UseLookControls = false;
		legacy.UseCameraControls = false;
		legacy.Enabled = false;
	}

	private void UpdateBodySetup()
	{
		if ( !Body.IsValid() )
			return;

		Body.CollisionEventsEnabled = true;
		Body.CollisionUpdateEventsEnabled = true;
		Body.RigidbodyFlags = RigidbodyFlags.DisableCollisionSounds;
		Body.MassOverride = BodyMass;
		Body.OverrideMassCenter = true;
		Body.MassCenterOverride = new Vector3( 0.0f, 0.0f, GetMassCenterHeight() );
		Body.Gravity = WantsGravity();
		Body.LinearDamping = WantsBodyBrakes() ? 10.0f * BrakePower : AirFriction;
		Body.AngularDamping = 1.0f;

		var locking = Body.Locking;
		locking.Pitch = true;
		locking.Yaw = true;
		locking.Roll = true;
		Body.Locking = locking;

		if ( !ColliderObject.IsValid() )
			return;

		ColliderObject.LocalTransform = global::Transform.Zero;

		var feetHeight = CurrentHeight * 0.5f;
		var radius = (BodyRadius * MathF.Sqrt( 2.0f )) / 2.0f;
		var feetFriction = 0.0f;

		if ( IsOnGround && WantsFeetBrakes() )
			feetFriction = 1.0f + 100.0f * BrakePower * GetEffectiveGroundFriction();

		if ( BodyCollider.IsValid() )
		{
			BodyCollider.Radius = radius;
			BodyCollider.Start = Vector3.Up * (CurrentHeight - radius);
			BodyCollider.End = Vector3.Up * MathF.Max( BodyCollider.Start.z - (feetHeight - radius), radius + 1.0f );
			BodyCollider.Friction = 0.0f;
			BodyCollider.Enabled = BodyCollider.End.z < BodyCollider.Start.z;
		}

		if ( FeetCollider.IsValid() )
		{
			FeetCollider.Scale = new Vector3( BodyRadius, BodyRadius, BodyCollider.IsValid() && BodyCollider.Enabled ? feetHeight : CurrentHeight );
			FeetCollider.Center = new Vector3( 0.0f, 0.0f, FeetCollider.Scale.z * 0.5f );
			FeetCollider.Friction = feetFriction;
			FeetCollider.Enabled = true;
		}
	}

	private bool WantsGravity()
	{
		if ( !IsOnGround )
			return true;

		if ( Velocity.Length > 1.0f )
			return true;

		if ( GroundVelocity.Length > 1.0f )
			return true;

		if ( GroundIsDynamic )
			return true;

		return false;
	}

	private bool WantsBodyBrakes()
	{
		return IsOnGround
			&& WishVelocity.Length < 1.0f
			&& Velocity.WithZ( 0.0f ).Length < IdleVelocityEpsilon
			&& GroundVelocity.Length < 1.0f;
	}

	private bool WantsFeetBrakes()
	{
		if ( WishVelocity.Length < 5.0f )
			return Velocity.WithZ( 0.0f ).Length < IdleVelocityEpsilon;

		return WishVelocity.Length < Velocity.Length * 0.9f;
	}

	private float GetMassCenterHeight()
	{
		if ( !IsOnGround )
			return CurrentHeight * 0.5f;

		return WishVelocity.Length.Clamp( 0.0f, CurrentHeight * 0.5f );
	}

	private float GetEffectiveGroundFriction()
	{
		return MathF.Max( CurrentGroundFriction, 0.0f );
	}

	private void UpdateVelocity()
	{
		if ( !Body.IsValid() )
		{
			Velocity = Vector3.Zero;
			return;
		}

		Velocity = Body.Velocity - GroundVelocity - _baseVelocity;
	}

	private void UpdateGroundVelocity()
	{
		if ( !IsOnGround )
		{
			GroundVelocity = Vector3.Zero;
			return;
		}

		if ( GroundComponent is Collider collider )
		{
			GroundVelocity = collider.GetVelocityAtPoint( WorldPosition );
			return;
		}

		if ( GroundComponent is Rigidbody rigidbody )
		{
			var massFactor = rigidbody.Mass / (BodyMass + rigidbody.Mass);
			GroundVelocity = rigidbody.GetVelocityAtPoint( WorldPosition ) * massFactor;
			return;
		}

		GroundVelocity = Vector3.Zero;
	}

	private void UpdateEyeAngles()
	{
		var input = Input.AnalogLook * LookSensitivity;
		IEvents.PostToGameObject( GameObject, x => x.OnEyeAngles( ref input ) );

		var eyeAngles = EyeAngles + input;
		eyeAngles.roll = 0.0f;

		if ( PitchClamp > 0.0f )
			eyeAngles.pitch = eyeAngles.pitch.Clamp( -PitchClamp, PitchClamp );

		EyeAngles = eyeAngles;
	}

	private void UpdateEyeTransform()
	{
		var height = MathF.Max( 8.0f, CurrentHeight - EyeDistanceFromTop );
		EyeTransform = new Transform( WorldPosition + Vector3.Up * height, EyeAngles.ToRotation(), 1.0f );
	}

	private void UpdateCameraPosition()
	{
		if ( Scene.Camera is not CameraComponent camera )
			return;

		if ( !string.IsNullOrWhiteSpace( ToggleCameraModeButton ) && Input.Pressed( ToggleCameraModeButton ) )
		{
			ThirdPerson = !ThirdPerson;
			_cameraDistance = 20.0f;
		}

		UpdateEyeTransform();

		var eyePosition = EyeTransform.Position;

		if ( !IsAirborne && _smoothedEyeZ != 0.0f )
			eyePosition.z = _smoothedEyeZ.LerpTo( eyePosition.z, Time.Delta * 50.0f );

		_smoothedEyeZ = eyePosition.z;
		camera.WorldRotation = EyeTransform.Rotation;

		if ( !camera.RenderExcludeTags.Contains( "viewer" ) )
			camera.RenderExcludeTags.Add( "viewer" );

		if ( ThirdPerson )
		{
			var cameraDelta = EyeTransform.Rotation.Forward * -CameraOffset.x
				+ EyeTransform.Rotation.Up * CameraOffset.z
				+ EyeTransform.Rotation.Right * CameraOffset.y;

			var trace = Scene.Trace.FromTo( eyePosition, eyePosition + cameraDelta )
				.IgnoreGameObjectHierarchy( GameObject.Root )
				.Radius( 8.0f )
				.Run();

			if ( trace.StartedSolid )
				_cameraDistance = _cameraDistance.LerpTo( cameraDelta.Length, Time.Delta * 100.0f );
			else if ( trace.Distance < _cameraDistance )
				_cameraDistance = _cameraDistance.LerpTo( trace.Distance, Time.Delta * 200.0f );
			else
				_cameraDistance = _cameraDistance.LerpTo( trace.Distance, Time.Delta * 2.0f );

			eyePosition += cameraDelta.Normal * _cameraDistance;
		}

		camera.WorldPosition = eyePosition;

		if ( UseFovFromPreferences )
			camera.FieldOfView = Preferences.FieldOfView;

		IEvents.PostToGameObject( GameObject, x => x.PostCameraSetup( camera ) );
	}

	private void UpdateBodyVisibility()
	{
		if ( !Renderer.IsValid() )
			return;

		var viewer = CanProcessLocalInput() && UseCameraControls && HideBodyInFirstPerson && !ThirdPerson;
		Renderer.GameObject.Tags.Set( "viewer", viewer );
	}

	private Vector3 BuildWishVelocity()
	{
		var input = Input.AnalogMove.ClampLength( 1.0f );
		var eyes = Rotation.FromYaw( EyeAngles.yaw );
		var direction = (eyes * input).WithZ( 0.0f );

		if ( direction.IsNearlyZero( 0.1f ) )
			direction = Vector3.Zero;

		var run = Input.Down( AltMoveButton );

		if ( RunByDefault )
			run = !run;

		var speed = run ? RunSpeed : WalkSpeed;

		if ( IsDucking )
			speed = DuckedSpeed;

		if ( IsOnGround && MaxGroundSpeed > 0.0f )
			speed = MathF.Min( speed, MaxGroundSpeed );

		if ( direction.IsNearlyZero( 0.01f ) )
			return Vector3.Zero;

		return direction.Normal * speed * direction.Length.Clamp( 0.0f, 1.0f );
	}

	private void AddMovementVelocity()
	{
		if ( !Body.IsValid() )
			return;

		var wish = WishVelocity.WithZ( 0.0f );
		var baseVelocity = _baseVelocity;
		var supportVelocity = GroundVelocity + baseVelocity;
		var originalZ = Body.Velocity.z;
		var relativeVelocity = Body.Velocity - supportVelocity;
		var horizontalBefore = relativeVelocity.WithZ( 0.0f );
		var horizontal = horizontalBefore;
		var wishDirection = wish.IsNearlyZero( 0.01f ) ? Vector3.Zero : wish.Normal;
		var wishSpeed = IsOnGround ? GetGroundWishSpeed( wish ) : GetAirWishSpeed( wish );
		var currentSpeed = 0.0f;
		var addSpeed = 0.0f;
		var accelSpeed = 0.0f;
		var frictionDrop = 0.0f;
		var airControlAmount = 0.0f;
		var skippedFriction = false;

		if ( IsOnGround )
		{
			skippedFriction = _skipGroundFrictionThisTick && EnableBunnyhop && BunnyhopPreserveFriction;
			horizontal = ApplyGroundFriction( horizontal, Time.Delta, skippedFriction, out frictionDrop );
			horizontal = Accelerate( horizontal, wishDirection, wishSpeed, GroundAcceleration, Time.Delta, out currentSpeed, out addSpeed, out accelSpeed );
			relativeVelocity = horizontal.WithZ( originalZ - supportVelocity.z );
			LogMovementStep( "ground", horizontalBefore, horizontal, wishDirection, wishSpeed, currentSpeed, addSpeed, accelSpeed, frictionDrop, airControlAmount, skippedFriction );
		}
		else
		{
			horizontal = Accelerate( horizontal, wishDirection, wishSpeed, AirAcceleration, Time.Delta, out currentSpeed, out addSpeed, out accelSpeed );
			horizontal = ApplyAirControl( horizontal, wishDirection, Time.Delta, out airControlAmount );

			if ( MaxAirVelocity > 0.0f && horizontal.Length > MaxAirVelocity )
				horizontal = horizontal.Normal * MaxAirVelocity;

			relativeVelocity = horizontal.WithZ( relativeVelocity.z );
			LogMovementStep( "air", horizontalBefore, horizontal, wishDirection, wishSpeed, currentSpeed, addSpeed, accelSpeed, frictionDrop, airControlAmount, _skipGroundFrictionThisTick );
		}

		Body.Velocity = relativeVelocity + supportVelocity;
		_baseVelocity = Vector3.Zero;
		_skipGroundFrictionThisTick = false;
		Body.Sleeping = false;
	}

	private float GetGroundWishSpeed( Vector3 wish )
	{
		var wishSpeed = wish.Length;

		if ( MaxGroundSpeed > 0.0f )
			wishSpeed = MathF.Min( wishSpeed, MaxGroundSpeed );

		return wishSpeed;
	}

	private Vector3 ApplyGroundFriction( Vector3 velocity, float deltaTime, bool skipFriction, out float frictionDrop )
	{
		frictionDrop = 0.0f;

		if ( skipFriction || deltaTime <= 0.0f )
			return velocity;

		var speed = velocity.Length;

		if ( speed < IdleVelocityEpsilon )
			return Vector3.Zero;

		var control = MathF.Max( speed, MathF.Max( StopSpeed, 0.0f ) );
		frictionDrop = control * GetEffectiveGroundFriction() * deltaTime;
		var newSpeed = MathF.Max( speed - frictionDrop, 0.0f );

		if ( newSpeed < IdleVelocityEpsilon )
			return Vector3.Zero;

		return velocity * (newSpeed / speed);
	}

	private float GetAirWishSpeed( Vector3 wish )
	{
		var wishSpeed = wish.Length * MathF.Max( 0.0f, StrafeMultiplier );

		if ( MaxAirWishSpeed > 0.0f )
			wishSpeed = MathF.Min( wishSpeed, MaxAirWishSpeed );

		return wishSpeed;
	}

	private Vector3 ApplyAirControl( Vector3 velocity, Vector3 wishDirection, float deltaTime, out float controlAmount )
	{
		controlAmount = 0.0f;

		if ( AirControl <= 0.0f || deltaTime <= 0.0f || wishDirection.LengthSquared <= 0.0001f )
			return velocity;

		var speed = velocity.Length;

		if ( speed <= 0.01f )
			return velocity;

		var dot = Vector3.Dot( velocity.Normal, wishDirection );

		if ( dot <= 0.0f )
			return velocity;

		controlAmount = MathF.Min( AirControl * dot * dot * deltaTime, 1.0f );

		if ( controlAmount <= 0.0f )
			return velocity;

		return PreserveAirMomentum
			? (velocity.Normal * (1.0f - controlAmount) + wishDirection * controlAmount).Normal * speed
			: velocity + (wishDirection * speed - velocity) * controlAmount;
	}

	private static Vector3 Accelerate(
		Vector3 currentVelocity,
		Vector3 wishDirection,
		float wishSpeed,
		float acceleration,
		float deltaTime,
		out float currentSpeed,
		out float addSpeed,
		out float accelSpeed )
	{
		currentSpeed = 0.0f;
		addSpeed = 0.0f;
		accelSpeed = 0.0f;

		if ( wishSpeed <= 0.0f || acceleration <= 0.0f || deltaTime <= 0.0f || wishDirection.LengthSquared <= 0.0001f )
			return currentVelocity;

		currentSpeed = Vector3.Dot( currentVelocity, wishDirection );
		addSpeed = wishSpeed - currentSpeed;

		if ( addSpeed <= 0.0f )
			return currentVelocity;

		accelSpeed = MathF.Min( acceleration * wishSpeed * deltaTime, addSpeed );
		return currentVelocity + wishDirection * accelSpeed;
	}

	private static Vector3 AddClampedDirection( Vector3 currentVelocity, Vector3 addVelocity, float maxAddSpeed )
	{
		if ( addVelocity.IsNearlyZero( 0.0001f ) || maxAddSpeed <= 0.0f )
			return currentVelocity;

		var direction = addVelocity.Normal;
		var currentSpeed = Vector3.Dot( currentVelocity, direction );
		var addSpeed = MathF.Min( addVelocity.Length, maxAddSpeed - currentSpeed );

		if ( addSpeed <= 0.0f )
			return currentVelocity;

		return currentVelocity + direction * addSpeed;
	}

	private void UpdateJumpQueue()
	{
		if ( string.IsNullOrWhiteSpace( JumpButton ) )
		{
			_jumpQueued = false;
			return;
		}

		if ( Input.Pressed( JumpButton ) )
		{
			_timeSinceJumpPressed = 0.0f;
			_jumpQueued = EnableBunnyhop;
		}

		if ( EnableBunnyhop && Input.Down( JumpButton ) )
		{
			_jumpQueued = true;
			return;
		}

		if ( _timeSinceJumpPressed > MathF.Max( AutoJumpWindow, 0.0f ) )
			_jumpQueued = false;
	}

	private void TryJump()
	{
		if ( JumpSpeed <= 0.0f )
			return;

		if ( string.IsNullOrWhiteSpace( JumpButton ) )
			return;

		var jumpPressed = Input.Pressed( JumpButton );
		var jumpHeld = Input.Down( JumpButton );
		var jumpQueued = EnableBunnyhop && (_jumpQueued || jumpHeld || _timeSinceJumpPressed <= MathF.Max( AutoJumpWindow, 0.0f ));

		if ( !jumpPressed && !jumpQueued )
			return;

		if ( _timeSinceJump < JumpCooldown )
			return;

		if ( !IsOnGround && _jumpedSinceGrounded )
			return;

		if ( TimeSinceGrounded > CoyoteTime )
			return;

		_skipGroundFrictionThisTick = EnableBunnyhop && BunnyhopPreserveFriction && IsOnGround;
		_timeSinceJump = 0.0f;
		_timeSinceJumpPressed = 999.0f;
		_jumpQueued = false;
		Jump( Vector3.Up * JumpSpeed );
		_jumpedSinceGrounded = true;
		Renderer?.Set( "b_jump", true );
		IEvents.PostToGameObject( GameObject, x => x.OnJumped() );
	}

	private void LogMovementStep(
		string phase,
		Vector3 before,
		Vector3 after,
		Vector3 wishDirection,
		float wishSpeed,
		float currentSpeed,
		float addSpeed,
		float accelSpeed,
		float frictionDrop,
		float airControlAmount,
		bool skippedFriction )
	{
		if ( !LogMovementDebug && !(phase == "air" && LogAirControlDebug) )
			return;

		if ( _timeSinceMovementDebug < 0.2f )
			return;

		_timeSinceMovementDebug = 0.0f;
		var hasJumpButton = !string.IsNullOrWhiteSpace( JumpButton );

		Log.Info(
			$"DeathrunMovement '{GameObject.Name}' phase={phase}, grounded={IsOnGround}, before={before}, finalHorizontal={after}, " +
			$"wishDir={wishDirection}, wishSpeed={wishSpeed:0.###}, currentAlongWish={currentSpeed:0.###}, addSpeed={addSpeed:0.###}, accelSpeed={accelSpeed:0.###}, " +
			$"frictionDrop={frictionDrop:0.###}, airControl={airControlAmount:0.###}, jumpPressed={hasJumpButton && Input.Pressed( JumpButton )}, jumpHeld={hasJumpButton && Input.Down( JumpButton )}, " +
			$"jumpQueued={_jumpQueued}, skipFriction={skippedFriction}" );
	}

	public void UpdateDucking( bool wantsDuck )
	{
		if ( wantsDuck == IsDucking )
			return;

		var unduckDelta = BodyHeight - DuckedHeight;

		if ( !wantsDuck )
		{
			if ( IsAirborne )
				return;

			if ( Headroom < unduckDelta )
				return;
		}

		IsDucking = wantsDuck;

		if ( wantsDuck && IsAirborne )
		{
			WorldPosition += Vector3.Up * unduckDelta;
			Transform.ClearInterpolation();
			_bodyDuckOffset = Vector3.Up * -unduckDelta;
		}

		UpdateBodySetup();
	}

	private void UpdateHeadroom()
	{
		var trace = TraceBody(
			WorldPosition + Vector3.Up * CurrentHeight * 0.5f,
			WorldPosition + Vector3.Up * (100.0f + CurrentHeight * 0.5f),
			0.75f,
			0.5f );

		Headroom = trace.Distance;
	}

	private void UpdateFalling()
	{
		if ( !IsOnGround || _wasFalling )
		{
			var fallDelta = WorldPosition - _previousPosition;

			if ( fallDelta.z < 0.0f )
			{
				_wasFalling = true;
				_fallDistance -= fallDelta.z;
			}
		}

		if ( !IsOnGround )
			return;

		if ( _wasFalling && _fallDistance > 1.0f )
			IEvents.PostToGameObject( GameObject, x => x.OnLanded( _fallDistance, Velocity ) );

		_wasFalling = false;
		_fallDistance = 0.0f;
	}

	public void PreventGrounding( float seconds )
	{
		_timeUntilAllowedGround = MathF.Max( _timeUntilAllowedGround, seconds );
		ClearGround();
	}

	private void CategorizeGround()
	{
		var wasGrounded = IsOnGround;
		var groundZ = GroundVelocity.z;

		if ( groundZ > 250.0f )
		{
			PreventGrounding( 0.3f );
			return;
		}

		if ( _timeUntilAllowedGround > 0.0f || groundZ > 300.0f )
		{
			ClearGround();
			return;
		}

		var from = WorldPosition + Vector3.Up * 4.0f;
		var to = WorldPosition + Vector3.Down * 2.0f;
		var radiusScale = 1.0f;
		var trace = TraceBody( from, to, radiusScale, 0.5f );

		while ( trace.StartedSolid || (trace.Hit && !IsStandableSurface( trace )) )
		{
			radiusScale -= 0.1f;

			if ( radiusScale < 0.7f )
			{
				ClearGround();
				return;
			}

			trace = TraceBody( from, to, radiusScale, 0.5f );
		}

		if ( !trace.StartedSolid && trace.Hit && IsStandableSurface( trace ) )
			SetGround( trace );
		else
			ClearGround();

		if ( wasGrounded != IsOnGround )
			UpdateBodySetup();
	}

	private void Reground( float stepSize )
	{
		if ( !IsOnGround || !Body.IsValid() || Body.Sleeping || stepSize <= 0.0f )
			return;

		var currentPosition = WorldPosition;
		var radiusScale = 1.0f;
		var trace = TraceBody( currentPosition + Vector3.Up, currentPosition + Vector3.Down * stepSize, radiusScale, 0.5f );

		while ( trace.StartedSolid )
		{
			radiusScale -= 0.1f;

			if ( radiusScale < 0.7f )
				return;

			trace = TraceBody( currentPosition + Vector3.Up, currentPosition + Vector3.Down * stepSize, radiusScale, 0.5f );
		}

		if ( !trace.Hit || !IsStandableSurface( trace ) )
			return;

		var targetPosition = trace.EndPosition + Vector3.Up * 0.01f;
		var delta = currentPosition - targetPosition;

		if ( delta == Vector3.Zero )
			return;

		WorldPosition = targetPosition;

		if ( delta.z > 0.01f )
			Body.Velocity = Body.Velocity.WithZ( 0.0f );
	}

	private void TryStep( float maxDistance )
	{
		_didStep = false;

		if ( maxDistance <= 0.0f || !Body.IsValid() || _timeUntilAllowedGround > 0.0f )
			return;

		var velocity = Body.Velocity.WithZ( 0.0f );

		if ( velocity.IsNearlyZero( 0.01f ) )
			return;

		var from = WorldPosition;
		var stepDelta = velocity * Time.Delta;
		var radiusScale = 1.0f;
		SceneTraceResult trace;

		{
			var start = from - stepDelta.Normal * Skin;
			var end = from + stepDelta;
			trace = TraceBody( start, end, radiusScale );

			while ( trace.StartedSolid )
			{
				radiusScale -= 0.1f;

				if ( radiusScale < 0.6f )
					return;

				trace = TraceBody( start, end, radiusScale );
			}

			if ( !trace.Hit )
				return;

			if ( StepDebug )
				DebugOverlay.Line( start, end, duration: 10.0f, color: Color.Green );

			stepDelta = stepDelta.Normal * (stepDelta.Length - trace.Distance);

			if ( stepDelta.Length <= 0.0f )
				return;
		}

		{
			from = trace.EndPosition;
			var upPoint = from + Vector3.Up * maxDistance;
			trace = TraceBody( from, upPoint, radiusScale );

			if ( trace.StartedSolid || trace.Distance < 2.0f )
				return;

			if ( StepDebug )
				DebugOverlay.Line( from, trace.EndPosition, duration: 10.0f, color: Color.Green );
		}

		{
			var start = trace.EndPosition;
			var end = start + stepDelta;
			trace = TraceBody( start, end, radiusScale );

			if ( trace.StartedSolid )
				return;

			if ( StepDebug )
				DebugOverlay.Line( start, end, duration: 10.0f, color: Color.Green );
		}

		{
			var top = trace.EndPosition;
			var bottom = trace.EndPosition + Vector3.Down * maxDistance;
			trace = TraceBody( top, bottom, radiusScale );

			if ( !trace.Hit || !IsStandableSurface( trace ) )
				return;

			if ( trace.EndPosition.z.AlmostEqual( Body.WorldPosition.z, 0.015f ) )
				return;

			_didStep = true;
			_stepPosition = trace.EndPosition + Vector3.Up * Skin;
			Body.WorldPosition = _stepPosition;
			Body.Velocity = Body.Velocity.WithZ( 0.0f ) * 0.9f;

			if ( StepDebug )
				DebugOverlay.Line( top, _stepPosition, duration: 10.0f, color: Color.Green );
		}
	}

	private void RestoreStep()
	{
		if ( !_didStep || !Body.IsValid() )
			return;

		_didStep = false;
		Body.WorldPosition = _stepPosition;
	}

	private void SetGround( SceneTraceResult trace )
	{
		var body = trace.Body;
		GroundObject = trace.GameObject;
		GroundComponent = trace.Component ?? trace.Collider ?? body?.Component;
		GroundSurface = trace.Surface;
		GroundIsDynamic = false;
		CurrentGroundFriction = GroundFriction;
		_isOnGround = true;
		_jumpedSinceGrounded = false;
		TimeSinceGrounded = 0.0f;

		if ( !GroundObject.IsValid() && trace.Collider.IsValid() )
			GroundObject = trace.Collider.GameObject;
		else if ( !GroundObject.IsValid() && GroundComponent.IsValid() )
			GroundObject = GroundComponent.GameObject;
		else if ( !GroundObject.IsValid() )
			GroundObject = body?.GameObject;

		if ( GroundSurface is not null )
			CurrentGroundFriction = GroundFriction * MathF.Max( GroundSurface.Friction, 0.0f );

		if ( trace.Collider.IsValid() )
		{
			GroundComponent = trace.Collider;
			GroundVelocity = trace.Collider.GetVelocityAtPoint( WorldPosition );
			GroundIsDynamic = trace.Collider.IsDynamic || IsKnownMovingGround( GroundObject );

			if ( trace.Collider.Friction.HasValue )
				CurrentGroundFriction = GroundFriction * MathF.Max( trace.Collider.Friction.Value, 0.0f );
		}
		else if ( GroundComponent is Collider collider )
		{
			GroundVelocity = collider.GetVelocityAtPoint( WorldPosition );
			GroundIsDynamic = collider.IsDynamic || IsKnownMovingGround( GroundObject );
		}
		else if ( GroundComponent is Rigidbody rigidbody )
		{
			GroundVelocity = rigidbody.GetVelocityAtPoint( WorldPosition );
			GroundIsDynamic = true;
		}
		else
		{
			GroundVelocity = Vector3.Zero;
			GroundIsDynamic = IsKnownMovingGround( GroundObject );
		}
	}

	private static bool IsKnownMovingGround( GameObject groundObject )
	{
		return groundObject.IsValid()
			&& (groundObject.Components.Get<DeathrunRotatingObstacle>().IsValid()
				|| groundObject.Components.Get<DeathrunNetworkedTransformDriver>().IsValid());
	}

	private void ClearGround()
	{
		if ( IsOnGround )
			TimeSinceUngrounded = 0.0f;

		_isOnGround = false;
		GroundObject = null;
		GroundComponent = null;
		GroundSurface = null;
		GroundVelocity = Vector3.Zero;
		GroundIsDynamic = false;
		CurrentGroundFriction = GroundFriction;
		_groundHash = default;
		_localGroundTransform = default;
		_groundTransformVelocity = Vector3.Zero;
	}

	private bool IsStandableSurface( SceneTraceResult trace )
	{
		return Vector3.GetAngle( Vector3.Up, trace.Normal ) <= GroundAngle;
	}

	public BBox BodyBox( float scale = 1.0f, float heightScale = 1.0f )
	{
		return new BBox(
			new Vector3( -BodyRadius * 0.5f * scale, -BodyRadius * 0.5f * scale, 0.0f ),
			new Vector3( BodyRadius * 0.5f * scale, BodyRadius * 0.5f * scale, CurrentHeight * heightScale ) );
	}

	public SceneTraceResult TraceBody( Vector3 from, Vector3 to, float scale = 1.0f, float heightScale = 1.0f )
	{
		return Scene.Trace.Box( BodyBox( scale, heightScale ), from, to )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithCollisionRules( Tags )
			.Run();
	}

	private void ApplyGroundTransformDelta()
	{
		if ( !CanUseGroundTransformCarry() )
		{
			ResetGroundTransformTracking();
			return;
		}

		var hash = HashCode.Combine( GroundObject );

		if ( hash != _groundHash )
		{
			StoreGroundTransform();
			return;
		}

		var carriedTransform = GroundObject.WorldTransform.ToWorld( _localGroundTransform );
		var positionDelta = carriedTransform.Position - WorldPosition;
		var maxCarryDistance = MathF.Max( 1.0f, MaxGroundCarryDistance );

		if ( positionDelta.Length > maxCarryDistance )
		{
			ResetGroundTransformTracking();
			return;
		}

		_groundTransformVelocity = Time.Delta > 0.0f ? positionDelta / Time.Delta : Vector3.Zero;

		if ( !positionDelta.IsNearlyZero( 0.001f ) )
		{
			WorldPosition += positionDelta;
			Body.Sleeping = false;
		}

		if ( RotateWithGround )
		{
			var rotationDelta = carriedTransform.Rotation * WorldRotation.Inverse;
			var deltaYaw = rotationDelta.Angles().yaw;

			if ( MathF.Abs( deltaYaw ) > 0.001f )
			{
				EyeAngles = EyeAngles.WithYaw( EyeAngles.yaw + deltaYaw );

				if ( UseAnimatorControls && Renderer.IsValid() )
					Renderer.WorldRotation *= new Angles( 0.0f, deltaYaw, 0.0f );
			}
		}

		StoreGroundTransform();
	}

	private bool CanUseGroundTransformCarry()
	{
		return EnableGroundTransformCarry
			&& CanProcessLocalInput()
			&& UseInputControls
			&& IsOnGround
			&& GroundObject.IsValid()
			&& Body.IsValid()
			&& Body.MotionEnabled;
	}

	private void StoreGroundTransform()
	{
		if ( !CanUseGroundTransformCarry() )
		{
			ResetGroundTransformTracking();
			return;
		}

		_groundHash = HashCode.Combine( GroundObject );
		_localGroundTransform = GroundObject.WorldTransform.ToLocal( WorldTransform );
	}

	private void ResetGroundTransformTracking()
	{
		_groundHash = default;
		_localGroundTransform = default;
		_groundTransformVelocity = Vector3.Zero;
	}

	public void UpdateLookAt()
	{
		Tooltips.Clear();

		if ( !EnablePressing )
			return;

		if ( Pressed.IsValid() )
		{
			UpdatePressed();
			return;
		}

		UpdateHovered();
	}

	private void UpdatePressed()
	{
		if ( string.IsNullOrWhiteSpace( UseButton ) )
			return;

		var keepPressing = Input.Down( UseButton );

		if ( keepPressing && Pressed is IPressable pressable )
		{
			var pressEvent = new IPressable.Event { Ray = EyeTransform.ForwardRay, Source = this };

			if ( pressable.GetTooltip( pressEvent ) is { } tooltip )
			{
				tooltip.Pressable = pressable;
				Tooltips.Add( tooltip );
			}

			keepPressing = pressable.Pressing( pressEvent );
		}

		if ( GetDistanceFromGameObject( Pressed.GameObject, EyePosition ) > ReachLength )
			keepPressing = false;

		if ( !keepPressing )
			StopPressing();
	}

	private float GetDistanceFromGameObject( GameObject target, Vector3 point )
	{
		var distance = Vector3.DistanceBetween( target.WorldPosition, point );

		foreach ( var collider in Pressed.GetComponentsInChildren<Collider>() )
		{
			var closestPoint = collider.FindClosestPoint( point );
			var colliderDistance = Vector3.DistanceBetween( closestPoint, point );

			if ( colliderDistance < distance )
				distance = colliderDistance;
		}

		return distance;
	}

	private void UpdateHovered()
	{
		SwitchHovered( TryGetLookedAt() );

		if ( Hovered is IPressable pressable )
		{
			var pressEvent = new IPressable.Event { Ray = EyeTransform.ForwardRay, Source = this };
			pressable.Look( pressEvent );
		}

		if ( !string.IsNullOrWhiteSpace( UseButton ) && Input.Pressed( UseButton ) )
			StartPressing( Hovered );
	}

	public void StopPressing()
	{
		if ( !Pressed.IsValid() )
			return;

		IEvents.PostToGameObject( GameObject, x => x.StopPressing( Pressed ) );

		if ( Pressed is IPressable pressable )
			pressable.Release( new IPressable.Event { Ray = EyeTransform.ForwardRay, Source = this } );

		Pressed = default;
	}

	public void StartPressing( Component target )
	{
		StopPressing();

		if ( !target.IsValid() )
		{
			IEvents.PostToGameObject( GameObject, x => x.FailPressing() );
			return;
		}

		var pressable = target.GetComponent<IPressable>();

		if ( pressable is not null )
		{
			var pressEvent = new IPressable.Event { Ray = EyeTransform.ForwardRay, Source = this };

			if ( !pressable.CanPress( pressEvent ) )
			{
				IEvents.PostToGameObject( GameObject, x => x.FailPressing() );
				return;
			}

			pressable.Press( pressEvent );
		}

		Pressed = target;
		IEvents.PostToGameObject( GameObject, x => x.StartPressing( target ) );
	}

	private void SwitchHovered( Component target )
	{
		var pressEvent = new IPressable.Event { Ray = EyeTransform.ForwardRay, Source = this };

		if ( Hovered == target )
		{
			if ( Hovered is IPressable stillHovering )
				stillHovering.Look( pressEvent );

			return;
		}

		if ( Hovered is IPressable stoppedHovering )
			stoppedHovering.Blur( pressEvent );

		Hovered = target;

		if ( Hovered is IPressable startedHovering )
		{
			startedHovering.Hover( pressEvent );
			startedHovering.Look( pressEvent );
		}
	}

	private Component TryGetLookedAt()
	{
		for ( var radius = 0.0f; radius <= 4.0f; radius += 2.0f )
		{
			var hits = Scene.Trace
				.Ray( EyePosition, EyePosition + EyeTransform.Rotation.Forward * (ReachLength - radius) )
				.IgnoreGameObjectHierarchy( GameObject )
				.Radius( radius )
				.HitTriggers()
				.RunAll();

			foreach ( var hit in hits )
			{
				var hitObject = hit.Collider?.GameObject ?? hit.GameObject;

				if ( !hitObject.IsValid() )
					continue;

				Component foundComponent = default;
				IEvents.PostToGameObject( GameObject, x => foundComponent = x.GetUsableComponent( hitObject ) ?? foundComponent );

				if ( foundComponent.IsValid() )
					return foundComponent;

				foreach ( var component in hitObject.GetComponents<IPressable>() )
				{
					var pressEvent = new IPressable.Event { Ray = EyeTransform.ForwardRay, Source = this };
					var canPress = component.CanPress( pressEvent );

					if ( component.GetTooltip( pressEvent ) is { } tooltip )
					{
						tooltip.Enabled = tooltip.Enabled && canPress;
						tooltip.Pressable = component;
						Tooltips.Add( tooltip );
					}

					if ( canPress )
						return component as Component;
				}

				if ( hit.Collider is null || !hit.Collider.IsTrigger )
					break;
			}
		}

		return default;
	}

	private void UpdateAnimation()
	{
		if ( !UseAnimatorControls || !Renderer.IsValid() )
			return;

		Renderer.LocalPosition = _bodyDuckOffset;
		_bodyDuckOffset = Vector3.Lerp( _bodyDuckOffset, Vector3.Zero, MathF.Min( Time.Delta * 5.0f, 1.0f ) );

		Renderer.Set( "b_grounded", IsOnGround );
		Renderer.Set( "b_swim", false );
		Renderer.Set( "b_climbing", false );
		Renderer.Set( "duck", IsDucking ? 1.0f : Headroom.Remap( 25.0f, 0.0f, 0.0f, 0.5f, true ) );
		Renderer.Set( "wish_speed", WishVelocity.Length );
		Renderer.Set( "move_speed", Velocity.WithZ( 0.0f ).Length );

		var targetRotation = Rotation.FromYaw( EyeAngles.yaw );
		var rotateDifference = Renderer.WorldRotation.Distance( targetRotation );

		if ( rotateDifference > RotationAngleLimit )
		{
			var delta = 0.999f - RotationAngleLimit / rotateDifference;
			Renderer.WorldRotation = Rotation.Lerp( Renderer.WorldRotation, targetRotation, delta );
		}

		if ( WishVelocity.WithZ( 0.0f ).Length > 10.0f )
			Renderer.WorldRotation = Rotation.Slerp( Renderer.WorldRotation, targetRotation, Time.Delta * 2.0f * RotationSpeed * WishVelocity.Length.Remap( 0.0f, 100.0f ) );
	}

	private void LogPeriodicDebug()
	{
		if ( !LogControllerDebug || _timeSinceDebugLog < 1.0f )
			return;

		_timeSinceDebugLog = 0.0f;
		LogState( "state" );
	}

	private void LogState( string reason )
	{
		if ( !LogControllerDebug )
			return;

		var network = GameObject.Network;
		Log.Info(
			$"DeathrunPlayerController '{GameObject.Name}' {reason}. NetworkActive={network.Active}, IsOwner={network.IsOwner}, IsProxy={network.IsProxy}, CanProcessLocalInput={CanProcessLocalInput()}, " +
			$"Enabled={Enabled}, Input={UseInputControls}, Look={UseLookControls}, Camera={UseCameraControls}, IsOnGround={IsOnGround}, Ground='{(GroundObject.IsValid() ? GroundObject.Name : "none")}', " +
			$"JumpSpeed={JumpSpeed:0.##}, Velocity={Velocity}, GroundVelocity={GroundVelocity}, BaseVelocity={BaseVelocity}, WishVelocity={WishVelocity}, " +
			$"BodyValid={Body.IsValid()}, MotionEnabled={Body.IsValid() && Body.MotionEnabled}, BodyVelocity={(Body.IsValid() ? Body.Velocity : Vector3.Zero)}." );
	}

	private void PreviousPositionReset()
	{
		_previousPosition = WorldPosition;
		_wasFalling = false;
		_fallDistance = 0.0f;
	}
}
