using System;
using System.Linq;

// Adapted from Facepunch sbox-public PlayerController source (MIT).
// Project-local controller: owns local input explicitly so multiplayer movement
// is not hidden inside Sandbox.PlayerController internals.
[Title( "Deathrun Player Controller" )]
[Category( "Deathrun" )]
[Icon( "directions_run" )]
public sealed class DeathrunPlayerController : Component, Component.INetworkSpawn
{
	private const float Skin = 0.095f;

	[Property, Group( "References" )] public Rigidbody Body { get; set; }
	[Property, Group( "References" )] public SkinnedModelRenderer Renderer { get; set; }
	[Property, Group( "References" )] public GameObject ColliderObject { get; set; }

	[Property, Group( "Input" )] public bool UseInputControls { get; set; } = true;
	[Property, Group( "Input" )] public bool UseLookControls { get; set; } = true;
	[Property, Group( "Input" )] public bool UseCameraControls { get; set; } = true;
	[Property, Group( "Input" )] public float WalkSpeed { get; set; } = 110.0f;
	[Property, Group( "Input" )] public float RunSpeed { get; set; } = 320.0f;
	[Property, Group( "Input" )] public float DuckedSpeed { get; set; } = 70.0f;
	[Property, Group( "Input" )] public float JumpSpeed { get; set; } = 300.0f;
	[Property, Group( "Input" )] public float DuckedHeight { get; set; } = 36.0f;
	[Property, Group( "Input" ), InputAction] public string JumpButton { get; set; } = "Jump";
	[Property, Group( "Input" ), InputAction] public string DuckButton { get; set; } = "Duck";
	[Property, Group( "Input" ), InputAction] public string AltMoveButton { get; set; } = "Run";
	[Property, Group( "Input" )] public bool RunByDefault { get; set; } = false;
	[Property, Group( "Input" )] public float LookSensitivity { get; set; } = 1.0f;
	[Property, Group( "Input" ), Range( 0, 180 )] public float PitchClamp { get; set; } = 90.0f;
	[Property, Group( "Input" )] public float JumpCooldown { get; set; } = 0.1f;
	[Property, Group( "Input" )] public float CoyoteTime { get; set; } = 0.2f;

	[Property, Group( "Body" ), Range( 1, 64 )] public float BodyRadius { get; set; } = 16.0f;
	[Property, Group( "Body" ), Range( 1, 128 )] public float BodyHeight { get; set; } = 72.0f;
	[Property, Group( "Body" ), Range( 1, 1000 )] public float BodyMass { get; set; } = 500.0f;
	[Property, Group( "Body" )] public TagSet BodyCollisionTags { get; set; }

	[Property, Group( "Movement" )] public float GroundAngle { get; set; } = 45.0f;
	[Property, Group( "Movement" )] public float StepUpHeight { get; set; } = 18.0f;
	[Property, Group( "Movement" )] public float StepDownHeight { get; set; } = 18.0f;
	[Property, Group( "Movement" )] public float GroundFriction { get; set; } = 6.0f;
	[Property, Group( "Movement" )] public float GroundAcceleration { get; set; } = 10.0f;
	[Property, Group( "Movement" )] public float AirAcceleration { get; set; } = 12.0f;
	[Property, Group( "Movement" )] public float AirControl { get; set; } = 0.35f;
	[Property, Group( "Movement" )] public float MaxAirVelocity { get; set; } = 900.0f;
	[Property, Group( "Movement" )] public bool PreserveAirMomentum { get; set; } = true;

	[Property, Group( "Camera" )] public float EyeDistanceFromTop { get; set; } = 8.0f;
	[Property, Group( "Camera" )] public bool ThirdPerson { get; set; } = false;
	[Property, Group( "Camera" )] public bool HideBodyInFirstPerson { get; set; } = true;
	[Property, Group( "Camera" )] public bool UseFovFromPreferences { get; set; } = true;
	[Property, Group( "Camera" )] public Vector3 CameraOffset { get; set; } = new( 256.0f, 0.0f, 12.0f );
	[Property, Group( "Camera" ), InputAction] public string ToggleCameraModeButton { get; set; } = "View";

	[Property, Group( "Animation" )] public bool UseAnimatorControls { get; set; } = true;
	[Property, Group( "Debug" )] public bool LogControllerDebug { get; set; } = false;

	[Sync] public Vector3 WishVelocity { get; set; }
	[Sync( SyncFlags.Interpolate )] public Angles EyeAngles { get; set; }
	[Sync] public bool IsDucking { get; set; }

	public Vector3 Velocity { get; private set; }
	public Vector3 GroundVelocity { get; private set; }
	public bool IsOnGround => _isOnGround;
	public bool IsAirborne => !IsOnGround;
	public GameObject GroundObject { get; private set; }
	public Component GroundComponent { get; private set; }
	public Surface GroundSurface { get; private set; }
	public Transform EyeTransform { get; private set; }
	public Vector3 EyePosition => EyeTransform.Position;
	public float CurrentHeight => IsDucking ? DuckedHeight : BodyHeight;

	private CapsuleCollider _bodyCollider;
	private BoxCollider _feetCollider;
	private TimeSince _timeSinceGrounded;
	private TimeSince _timeSinceUngrounded;
	private TimeSince _timeSinceJump;
	private TimeSince _timeSinceDebugLog;
	private TimeUntil _preventGrounding;
	private bool _isOnGround;
	private float _cameraDistance = 100.0f;
	private float _smoothedEyeZ;

	protected override void OnAwake()
	{
		CacheReferences();
		DisableLegacyPlayerController();
		UpdateBodySetup();
		EyeAngles = WorldRotation.Angles() with { pitch = 0.0f, roll = 0.0f };
		WorldRotation = Rotation.Identity;
		UpdateEyeTransform();
	}

	protected override void OnEnabled()
	{
		CacheReferences();
		DisableLegacyPlayerController();
		UpdateBodySetup();
		UpdateEyeTransform();
		LogState( "enabled" );
	}

	public void OnNetworkSpawn( Connection owner )
	{
		CacheReferences();
		DisableLegacyPlayerController();
		InitializeSpawnState( "network spawn" );
	}

	protected override void OnUpdate()
	{
		CacheReferences();
		UpdateVelocity();

		if ( ShouldProcessLocalInput() )
		{
			if ( UseLookControls )
				UpdateEyeAngles();

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
		UpdateBodySetup();
		UpdateVelocity();
		CategorizeGround();

		if ( !ShouldProcessLocalInput() || !UseInputControls || !Body.IsValid() || !Body.MotionEnabled )
		{
			WishVelocity = Vector3.Zero;
			return;
		}

		UpdateDucking( Input.Down( DuckButton ) );
		WishVelocity = BuildWishVelocity();
		ApplyMovement();
		TryJump();
		UpdateVelocity();
		StickToGround();
		UpdateEyeTransform();
	}

	public bool ShouldProcessLocalInput()
	{
		if ( !GameObject.IsValid() )
			return false;

		if ( !Networking.IsActive )
			return true;

		if ( !GameObject.Network.Active )
			return false;

		return !GameObject.Network.IsProxy;
	}

	public void InitializeSpawnState( string reason = "spawn initialized" )
	{
		CacheReferences();
		Enabled = true;
		UseInputControls = true;
		UseLookControls = true;
		UseCameraControls = true;
		IsDucking = false;
		WishVelocity = Vector3.Zero;
		Velocity = Vector3.Zero;
		GroundVelocity = Vector3.Zero;
		_preventGrounding = 0.0f;

		if ( Body.IsValid() )
		{
			Body.Enabled = true;
			Body.MotionEnabled = true;
			Body.Velocity = Vector3.Zero;
			Body.Sleeping = false;
		}

		UpdateBodySetup();
		CategorizeGround();
		UpdateEyeTransform();
		LogState( reason );
	}

	public void ClearVelocity()
	{
		WishVelocity = Vector3.Zero;
		Velocity = Vector3.Zero;
		GroundVelocity = Vector3.Zero;

		if ( Body.IsValid() )
		{
			Body.Velocity = Vector3.Zero;
			Body.Sleeping = false;
		}
	}

	public void Jump( Vector3 velocity )
	{
		if ( !Body.IsValid() )
			return;

		_preventGrounding = 0.2f;
		ClearGround();

		var currentVelocity = Body.Velocity;

		if ( currentVelocity.z < 0.0f )
			currentVelocity = currentVelocity.WithZ( 0.0f );

		currentVelocity = currentVelocity.WithZ( MathF.Max( currentVelocity.z, velocity.z ) );
		Body.Velocity = currentVelocity;
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
			Body = Components.Get<Rigidbody>();

		if ( !Renderer.IsValid() )
			Renderer = GameObject.Children
				.SelectMany( x => x.Components.GetAll<SkinnedModelRenderer>() )
				.FirstOrDefault( x => x.IsValid() );

		if ( !ColliderObject.IsValid() )
			ColliderObject = GameObject.Children.FirstOrDefault( x => x.IsValid() && x.Name == "Colliders" );

		if ( ColliderObject.IsValid() )
		{
			if ( !_bodyCollider.IsValid() )
				_bodyCollider = ColliderObject.Components.GetOrCreate<CapsuleCollider>();

			if ( !_feetCollider.IsValid() )
				_feetCollider = ColliderObject.Components.GetOrCreate<BoxCollider>();
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
		Body.Gravity = true;
		Body.MassOverride = BodyMass;
		Body.OverrideMassCenter = true;
		Body.MassCenterOverride = new Vector3( 0.0f, 0.0f, CurrentHeight * 0.5f );

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
		var groundedFriction = IsOnGround ? 1.0f + GroundFriction : 0.0f;

		if ( _bodyCollider.IsValid() )
		{
			_bodyCollider.Radius = radius;
			_bodyCollider.Start = Vector3.Up * (CurrentHeight - radius);
			_bodyCollider.End = Vector3.Up * MathF.Max( _bodyCollider.Start.z - (feetHeight - radius), radius + 1.0f );
			_bodyCollider.Friction = 0.0f;
			_bodyCollider.Enabled = _bodyCollider.End.z < _bodyCollider.Start.z;
		}

		if ( _feetCollider.IsValid() )
		{
			_feetCollider.Scale = new Vector3( BodyRadius, BodyRadius, _bodyCollider.IsValid() && _bodyCollider.Enabled ? feetHeight : CurrentHeight );
			_feetCollider.Center = new Vector3( 0.0f, 0.0f, _feetCollider.Scale.z * 0.5f );
			_feetCollider.Friction = groundedFriction;
			_feetCollider.Enabled = true;
		}
	}

	private void UpdateVelocity()
	{
		if ( !Body.IsValid() )
		{
			Velocity = Vector3.Zero;
			return;
		}

		Velocity = Body.Velocity - GroundVelocity;
	}

	private void UpdateEyeAngles()
	{
		var input = Input.AnalogLook * LookSensitivity;
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
	}

	private void UpdateBodyVisibility()
	{
		if ( !Renderer.IsValid() )
			return;

		var viewer = ShouldProcessLocalInput() && UseCameraControls && HideBodyInFirstPerson && !ThirdPerson;
		Renderer.GameObject.Tags.Set( "viewer", viewer );
	}

	private Vector3 BuildWishVelocity()
	{
		var input = Input.AnalogMove.ClampLength( 1.0f );

		if ( input.LengthSquared <= 0.0001f )
			return Vector3.Zero;

		var eyes = Rotation.FromYaw( EyeAngles.yaw );
		var direction = (eyes * input).WithZ( 0.0f );

		if ( direction.LengthSquared <= 0.0001f )
			return Vector3.Zero;

		var run = Input.Down( AltMoveButton );

		if ( RunByDefault )
			run = !run;

		var speed = run ? RunSpeed : WalkSpeed;

		if ( IsDucking )
			speed = DuckedSpeed;

		return direction.Normal * speed * MathF.Min( input.Length, 1.0f );
	}

	private void ApplyMovement()
	{
		if ( !Body.IsValid() )
			return;

		var velocity = Body.Velocity;
		var horizontal = velocity.WithZ( 0.0f );
		var wish = WishVelocity.WithZ( 0.0f );

		if ( IsOnGround )
		{
			horizontal = ApplyGroundFriction( horizontal, Time.Delta );

			if ( wish.LengthSquared > 0.0001f )
				horizontal = Accelerate( horizontal, wish.Normal, wish.Length, GroundAcceleration, Time.Delta );

			velocity = horizontal.WithZ( MathF.Min( velocity.z, 0.0f ) );
		}
		else if ( wish.LengthSquared > 0.0001f )
		{
			horizontal = Accelerate( horizontal, wish.Normal, wish.Length, AirAcceleration, Time.Delta );
			horizontal = ApplyAirControl( horizontal, wish.Normal, Time.Delta );

			if ( MaxAirVelocity > 0.0f && horizontal.Length > MaxAirVelocity )
				horizontal = horizontal.Normal * MaxAirVelocity;

			velocity = horizontal.WithZ( velocity.z );
		}

		Body.Velocity = velocity + GroundVelocity.WithZ( 0.0f );
		Body.Sleeping = false;
	}

	private Vector3 ApplyGroundFriction( Vector3 velocity, float deltaTime )
	{
		if ( GroundFriction <= 0.0f || deltaTime <= 0.0f )
			return velocity;

		var speed = velocity.Length;

		if ( speed <= 0.01f )
			return Vector3.Zero;

		var drop = speed * GroundFriction * deltaTime;
		var newSpeed = MathF.Max( speed - drop, 0.0f );
		return velocity * (newSpeed / speed);
	}

	private Vector3 ApplyAirControl( Vector3 velocity, Vector3 wishDirection, float deltaTime )
	{
		if ( AirControl <= 0.0f || deltaTime <= 0.0f || wishDirection.LengthSquared <= 0.0001f )
			return velocity;

		var speed = velocity.Length;

		if ( speed <= 0.01f )
			return velocity;

		var control = MathF.Min( AirControl * deltaTime * 8.0f, 1.0f );

		return PreserveAirMomentum
			? (velocity.Normal + wishDirection * control).Normal * speed
			: velocity + (wishDirection * speed - velocity) * control;
	}

	private static Vector3 Accelerate( Vector3 currentVelocity, Vector3 wishDirection, float wishSpeed, float acceleration, float deltaTime )
	{
		if ( wishSpeed <= 0.0f || acceleration <= 0.0f || deltaTime <= 0.0f || wishDirection.LengthSquared <= 0.0001f )
			return currentVelocity;

		var currentSpeed = Vector3.Dot( currentVelocity, wishDirection );
		var addSpeed = wishSpeed - currentSpeed;

		if ( addSpeed <= 0.0f )
			return currentVelocity;

		var accelSpeed = MathF.Min( acceleration * wishSpeed * deltaTime, addSpeed );
		return currentVelocity + wishDirection * accelSpeed;
	}

	private void TryJump()
	{
		if ( JumpSpeed <= 0.0f )
			return;

		if ( !Input.Pressed( JumpButton ) )
			return;

		if ( _timeSinceJump < JumpCooldown )
			return;

		if ( !IsOnGround && _timeSinceGrounded > CoyoteTime )
			return;

		_timeSinceJump = 0.0f;
		Jump( Vector3.Up * JumpSpeed );
	}

	private void UpdateDucking( bool wantsDuck )
	{
		if ( wantsDuck == IsDucking )
			return;

		if ( !wantsDuck && !HasHeadroomToStand() )
			return;

		IsDucking = wantsDuck;
		UpdateBodySetup();
	}

	private bool HasHeadroomToStand()
	{
		if ( !IsDucking )
			return true;

		var extraHeight = BodyHeight - DuckedHeight;

		if ( extraHeight <= 0.0f )
			return true;

		var from = WorldPosition + Vector3.Up * CurrentHeight;
		var to = from + Vector3.Up * extraHeight;
		var trace = Scene.Trace.Box( BodyBox( 0.8f, 0.5f ), from, to )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithCollisionRules( Tags )
			.Run();

		return !trace.Hit && !trace.StartedSolid;
	}

	private void CategorizeGround()
	{
		var wasGrounded = IsOnGround;

		if ( _preventGrounding > 0.0f )
		{
			ClearGround();
			return;
		}

		var from = WorldPosition + Vector3.Up * 4.0f;
		var to = WorldPosition + Vector3.Down * MathF.Max( 4.0f, StepDownHeight * 0.5f );
		var trace = TraceBody( from, to, 1.0f, 0.5f );

		if ( trace.Hit && !trace.StartedSolid && IsStandableSurface( trace ) )
			SetGround( trace );
		else
			ClearGround();

		if ( wasGrounded != IsOnGround )
			UpdateBodySetup();
	}

	private void StickToGround()
	{
		if ( !IsOnGround || !Body.IsValid() || StepDownHeight <= 0.0f )
			return;

		var trace = TraceBody( WorldPosition + Vector3.Up, WorldPosition + Vector3.Down * StepDownHeight, 1.0f, 0.5f );

		if ( !trace.Hit || trace.StartedSolid || !IsStandableSurface( trace ) )
			return;

		var targetPosition = trace.EndPosition + Vector3.Up * Skin;
		var delta = WorldPosition - targetPosition;

		if ( delta == Vector3.Zero )
			return;

		WorldPosition = targetPosition;

		if ( delta.z > 0.01f )
			Body.Velocity = Body.Velocity.WithZ( 0.0f );
	}

	private void SetGround( SceneTraceResult trace )
	{
		var body = trace.Body;
		GroundObject = trace.GameObject;
		GroundComponent = trace.Component ?? trace.Collider ?? body?.Component;
		GroundSurface = trace.Surface;
		_isOnGround = true;
		_timeSinceGrounded = 0.0f;

		if ( !GroundObject.IsValid() && GroundComponent.IsValid() )
			GroundObject = GroundComponent.GameObject;
		else if ( !GroundObject.IsValid() )
			GroundObject = body?.GameObject;

		if ( trace.Collider.IsValid() )
			GroundVelocity = trace.Collider.GetVelocityAtPoint( WorldPosition );
		else if ( GroundComponent is Collider collider )
			GroundVelocity = collider.GetVelocityAtPoint( WorldPosition );
		else if ( GroundComponent is Rigidbody rigidbody )
			GroundVelocity = rigidbody.GetVelocityAtPoint( WorldPosition );
		else
			GroundVelocity = Vector3.Zero;
	}

	private void ClearGround()
	{
		if ( IsOnGround )
			_timeSinceUngrounded = 0.0f;

		_isOnGround = false;
		GroundObject = null;
		GroundComponent = null;
		GroundSurface = null;
		GroundVelocity = Vector3.Zero;
	}

	private bool IsStandableSurface( SceneTraceResult trace )
	{
		return Vector3.GetAngle( Vector3.Up, trace.Normal ) <= GroundAngle;
	}

	private BBox BodyBox( float scale = 1.0f, float heightScale = 1.0f )
	{
		return new BBox(
			new Vector3( -BodyRadius * 0.5f * scale, -BodyRadius * 0.5f * scale, 0.0f ),
			new Vector3( BodyRadius * 0.5f * scale, BodyRadius * 0.5f * scale, CurrentHeight * heightScale ) );
	}

	private SceneTraceResult TraceBody( Vector3 from, Vector3 to, float scale = 1.0f, float heightScale = 1.0f )
	{
		return Scene.Trace.Box( BodyBox( scale, heightScale ), from, to )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithCollisionRules( Tags )
			.Run();
	}

	private void UpdateAnimation()
	{
		if ( !UseAnimatorControls || !Renderer.IsValid() )
			return;

		Renderer.Set( "b_grounded", IsOnGround );
		Renderer.Set( "b_swim", false );
		Renderer.Set( "b_climbing", false );
		Renderer.Set( "duck", IsDucking ? 1.0f : 0.0f );
		Renderer.Set( "wish_speed", WishVelocity.Length );
		Renderer.Set( "move_speed", Velocity.WithZ( 0.0f ).Length );

		var targetRotation = Rotation.FromYaw( EyeAngles.yaw );

		if ( WishVelocity.WithZ( 0.0f ).Length > 10.0f )
			Renderer.WorldRotation = Rotation.Slerp( Renderer.WorldRotation, targetRotation, Time.Delta * 8.0f );
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
			$"DeathrunPlayerController '{GameObject.Name}' {reason}. NetworkActive={network.Active}, IsOwner={network.IsOwner}, IsProxy={network.IsProxy}, ShouldProcessLocalInput={ShouldProcessLocalInput()}, " +
			$"Enabled={Enabled}, Input={UseInputControls}, Look={UseLookControls}, Camera={UseCameraControls}, IsOnGround={IsOnGround}, JumpSpeed={JumpSpeed:0.##}, " +
			$"Velocity={Velocity}, WishVelocity={WishVelocity}, BodyValid={Body.IsValid()}, MotionEnabled={Body.IsValid() && Body.MotionEnabled}, BodyVelocity={(Body.IsValid() ? Body.Velocity : Vector3.Zero)}." );
	}
}
