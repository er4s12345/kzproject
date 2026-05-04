[Title( "Deathrun Player" )]
[Category( "Deathrun" )]
[Icon( "person" )]
public sealed class DeathrunPlayer : Component, Component.INetworkSpawn
{
	[Property] public bool LogPlayerLifecycle { get; set; } = false;
	[Property] public bool LogLocalInputDebug { get; set; } = false;
	[Property] public float SpawnDebugDelay { get; set; } = 0.35f;
	[Property] public float LocalInputSnapshotInterval { get; set; } = 1.0f;

	private Connection _owner;
	private TimeSince _timeSinceInputStateLog;
	private TimeSince _timeSinceLocalInputSnapshotLog;
	private TimeSince _timeSinceStarted;
	private TimeSince _timeSinceNetworkSpawn;
	private TimeSince _timeSinceOwnerDebugEnabled;
	private TimeSince _timeSinceJumpDownLog;
	private bool _ownerInputDebugEnabled;
	private bool _networkSpawnSeen;
	private bool _spawnDebugLogged;

	/// <summary>
	/// The connection that owns this networked player object.
	/// Falls back to the GameObject network owner so proxy copies can still report a readable owner.
	/// </summary>
	public Connection Owner => _owner ?? GameObject?.Network.Owner;

	public string OwnerName => Owner?.DisplayName ?? "Unowned";
	public string OwnerId => GetOwnerId();
	public bool HasOwner => Owner is not null;
	public bool IsLocalOwner => ShouldProcessLocalInput();

	protected override void OnStart()
	{
		_timeSinceStarted = 0.0f;
		_timeSinceInputStateLog = 1.0f;
		_timeSinceLocalInputSnapshotLog = 0.0f;
		_timeSinceJumpDownLog = 999.0f;
		LogLifecycle( "started" );
	}

	protected override void OnUpdate()
	{
		UpdateLocalInputDebug();

		if ( !LogPlayerLifecycle )
			return;

		var shouldProcessInput = ShouldProcessLocalInput();

		if ( shouldProcessInput && Input.Pressed( "Jump" ) )
			LogLifecycle( $"local Jump pressed; {DescribeControllerState( true )}" );

		if ( _timeSinceInputStateLog < 1.0f )
			return;

		_timeSinceInputStateLog = 0.0f;
		LogLifecycle( $"input state; {DescribeControllerState( shouldProcessInput )}" );
	}

	public void Initialize( Connection owner )
	{
		_owner = owner;

		if ( owner is null )
		{
			Log.Warning( $"DeathrunPlayer '{GameObject.Name}' initialized without an owner connection." );
			return;
		}

		if ( LogPlayerLifecycle )
			LogLifecycle( $"initialized for {OwnerName} ({OwnerId})" );
	}

	public void OnNetworkSpawn( Connection owner )
	{
		_owner = owner;
		_networkSpawnSeen = true;
		_spawnDebugLogged = false;
		_timeSinceNetworkSpawn = 0.0f;
		LogLifecycle( $"network spawned with owner {OwnerName} ({OwnerId})" );
	}

	[Rpc.Owner]
	public void EnableLocalInputDebugOwnerRpc()
	{
		if ( Rpc.Calling && !Rpc.Caller.IsHost )
			return;

		_ownerInputDebugEnabled = true;
		_spawnDebugLogged = false;
		_timeSinceOwnerDebugEnabled = 0.0f;
		LogInputDebugState( "owner debug rpc received", true, true );
	}

	public bool IsOwnedBy( Connection connection )
	{
		if ( connection is null )
			return false;

		var owner = Owner;

		if ( owner is null )
			return GameObject.Network.OwnerId == connection.Id;

		return owner == connection || owner.Id == connection.Id;
	}

	public bool ShouldProcessLocalInput()
	{
		if ( !GameObject.IsValid() )
			return false;

		var controller = Components.Get<DeathrunPlayerController>();

		if ( controller.IsValid() )
			return controller.ShouldProcessLocalInput();

		if ( !GameObject.Network.Active )
			return !Networking.IsActive;

		return !GameObject.Network.IsProxy;
	}

	public string DescribeOwnership()
	{
		var network = GameObject.Network;
		var owner = network.Owner;
		var ownerName = owner?.DisplayName ?? "none";
		var ownerId = owner?.Id.ToString() ?? network.OwnerId.ToString();

		return $"Owner={ownerName} ({ownerId}), NetworkActive={network.Active}, IsOwner={network.IsOwner}, IsProxy={network.IsProxy}, ShouldProcessLocalInput={ShouldProcessLocalInput()}, NetworkMode={GameObject.NetworkMode}";
	}

	public string DescribeControllerState( bool includeLocalInput )
	{
		var controller = Components.Get<DeathrunPlayerController>();
		var legacyController = Components.Get<PlayerController>();
		var health = Components.Get<DeathrunHealth>();
		var body = GetBestBody( controller );
		var jumpPressed = includeLocalInput && Input.Pressed( "Jump" );
		var jumpDown = includeLocalInput && Input.Down( "Jump" );

		return $"DeathrunControllerValid={controller.IsValid()}, ControllerEnabled={controller.IsValid() && controller.Enabled}, " +
			$"Input={controller.IsValid() && controller.UseInputControls}, Look={controller.IsValid() && controller.UseLookControls}, Camera={controller.IsValid() && controller.UseCameraControls}, " +
			$"IsOnGround={controller.IsValid() && controller.IsOnGround}, JumpSpeed={(controller.IsValid() ? controller.JumpSpeed : 0.0f):0.##}, Velocity={(controller.IsValid() ? controller.Velocity : Vector3.Zero)}, " +
			$"LegacyPlayerControllerPresent={legacyController.IsValid()}, LegacyEnabled={legacyController.IsValid() && legacyController.Enabled}, " +
			$"IsDead={health.IsValid() && health.IsDead}, Health={(health.IsValid() ? health.CurrentHealth : 0.0f):0.##}, BodyValid={body.IsValid()}, BodyEnabled={body.IsValid() && body.Enabled}, MotionEnabled={body.IsValid() && body.MotionEnabled}, BodyVelocity={(body.IsValid() ? body.Velocity : Vector3.Zero)}, " +
			$"JumpPressed={jumpPressed}, JumpDown={jumpDown}";
	}

	public void LogLifecycle( string reason )
	{
		if ( !LogPlayerLifecycle )
			return;

		Log.Info( $"DeathrunPlayer '{GameObject.Name}' {reason}. {DescribeOwnership()}." );
	}

	private string GetOwnerId()
	{
		var owner = Owner;

		if ( owner is not null )
			return owner.Id.ToString();

		if ( GameObject.IsValid() )
			return GameObject.Network.OwnerId.ToString();

		return "none";
	}

	private void UpdateLocalInputDebug()
	{
		if ( !ShouldLogLocalInputDebug() )
			return;

		if ( !_spawnDebugLogged && IsSpawnDebugReady() )
		{
			_spawnDebugLogged = true;
			LogInputDebugState( "post-spawn snapshot", true );
			_timeSinceLocalInputSnapshotLog = 0.0f;
		}

		if ( Input.Pressed( "Jump" ) )
		{
			_timeSinceJumpDownLog = 0.0f;
			_timeSinceLocalInputSnapshotLog = 0.0f;
			LogInputDebugState( "Jump pressed", true );
			return;
		}

		if ( Input.Down( "Jump" ) )
		{
			if ( _timeSinceJumpDownLog >= 0.25f )
			{
				_timeSinceJumpDownLog = 0.0f;
				_timeSinceLocalInputSnapshotLog = 0.0f;
				LogInputDebugState( "Jump down", true );
			}

			return;
		}

		var snapshotInterval = LocalInputSnapshotInterval < 0.1f ? 0.1f : LocalInputSnapshotInterval;

		if ( _timeSinceLocalInputSnapshotLog >= snapshotInterval )
		{
			_timeSinceLocalInputSnapshotLog = 0.0f;
			LogInputDebugState( "local input snapshot", true );
		}

		_timeSinceJumpDownLog = 999.0f;
	}

	private bool ShouldLogLocalInputDebug()
	{
		if ( !LogLocalInputDebug )
			return false;

		if ( LogPlayerLifecycle )
			return true;

		if ( !GameObject.IsValid() )
			return false;

		if ( !GameObject.Network.Active )
			return !Networking.IsActive;

		return _ownerInputDebugEnabled || GameObject.Network.IsOwner;
	}

	private bool IsSpawnDebugReady()
	{
		var delay = SpawnDebugDelay < 0.0f ? 0.0f : SpawnDebugDelay;

		if ( _ownerInputDebugEnabled )
			return _timeSinceOwnerDebugEnabled >= delay;

		if ( _networkSpawnSeen )
			return _timeSinceNetworkSpawn >= delay;

		return _timeSinceStarted >= delay;
	}

	private void LogInputDebugState( string reason, bool includeLocalInput, bool force = false )
	{
		if ( !LogLocalInputDebug && !LogPlayerLifecycle )
			return;

		if ( !force && !ShouldLogLocalInputDebug() )
			return;

		var controller = Components.Get<DeathrunPlayerController>();
		var legacyController = Components.Get<PlayerController>();
		var health = Components.Get<DeathrunHealth>();
		var network = GameObject.Network;
		var owner = network.Owner;
		var ownerName = owner?.DisplayName ?? "none";
		var ownerId = owner?.Id.ToString() ?? network.OwnerId.ToString();
		var localDebugTarget = ShouldLogLocalInputDebug();
		var canReadInput = includeLocalInput && localDebugTarget;
		var body = GetBestBody( controller );
		var jumpPressed = canReadInput && Input.Pressed( "Jump" );
		var jumpDown = canReadInput && Input.Down( "Jump" );

		Log.Info(
			$"DeathrunPlayerInputDebug reason='{reason}', player='{GameObject.Name}', " +
			$"NetworkActive={network.Active}, NetworkOwner={ownerName} ({ownerId}), OwnerId={network.OwnerId}, IsOwner={network.IsOwner}, IsProxy={network.IsProxy}, " +
			$"ShouldProcessLocalInput={ShouldProcessLocalInput()}, OwnerDebugRpc={_ownerInputDebugEnabled}, LocalDebugTarget={localDebugTarget}, " +
			$"DiagnosisCase='{ClassifyJumpDiagnostic( jumpPressed, jumpDown, controller, health, body )}', " +
			$"InputJumpAction='Jump', JumpPressed={jumpPressed}, JumpDown={jumpDown}, AnalogMove={(canReadInput ? Input.AnalogMove : Vector3.Zero)}, " +
			$"{DescribeControllerDebug( controller )}, {DescribeBodyDebug( controller )}, {DescribeHealthDebug( health )}, {DescribeCameraDebug()}, " +
			$"ControllerOnNetworkRoot={controller.IsValid() && controller.GameObject == GameObject}, LegacyPlayerControllerPresent={legacyController.IsValid()}, LegacyEnabled={legacyController.IsValid() && legacyController.Enabled}, RuntimeHierarchy={DescribeRuntimeHierarchy()}." );
	}

	private string DescribeControllerDebug( DeathrunPlayerController controller )
	{
		if ( !controller.IsValid() )
			return "DeathrunPlayerControllerExists=False";

		return $"DeathrunPlayerControllerExists=True, DeathrunPlayerControllerEnabled={controller.Enabled}, ControllerGameObjectActive={controller.GameObject.Active}, ShouldProcessLocalInput={controller.ShouldProcessLocalInput()}, " +
			$"UseInputControls={controller.UseInputControls}, UseLookControls={controller.UseLookControls}, UseCameraControls={controller.UseCameraControls}, " +
			$"JumpSpeed={controller.JumpSpeed:0.##}, IsOnGround={controller.IsOnGround}, ControllerVelocity={controller.Velocity}, EyeAngles={controller.EyeAngles}";
	}

	private string DescribeBodyDebug( DeathrunPlayerController controller )
	{
		var rootBody = Components.Get<Rigidbody>();
		Rigidbody controllerBody = null;

		if ( controller.IsValid() )
			controllerBody = controller.Body;

		var body = GetBestBody( controller );
		var colliderCount = CountKnownColliders( GameObject );

		return $"RootRigidbodyExists={rootBody.IsValid()}, ControllerBodyExists={controllerBody.IsValid()}, BodyExists={body.IsValid()}, " +
			$"BodyObject='{(body.IsValid() ? body.GameObject.Name : "none")}', BodyEnabled={body.IsValid() && body.Enabled}, MotionEnabled={body.IsValid() && body.MotionEnabled}, BodyVelocity={(body.IsValid() ? body.Velocity : Vector3.Zero)}, " +
			$"ColliderExists={colliderCount > 0}, ColliderCount={colliderCount}";
	}

	private Rigidbody GetBestBody( DeathrunPlayerController controller )
	{
		if ( controller.IsValid() && controller.Body.IsValid() )
			return controller.Body;

		return Components.Get<Rigidbody>();
	}

	private static string ClassifyJumpDiagnostic( bool jumpPressed, bool jumpDown, DeathrunPlayerController controller, DeathrunHealth health, Rigidbody body )
	{
		var hasJumpInput = jumpPressed || jumpDown;

		if ( !hasJumpInput )
			return "CaseA_NoJumpInput";

		if ( health.IsValid() && health.IsDead )
			return "CaseD_IsDead";

		if ( controller.IsValid() && !controller.UseInputControls )
			return "CaseD_UseInputControlsFalse";

		if ( body.IsValid() && !body.MotionEnabled )
			return "CaseD_MotionEnabledFalse";

		if ( controller.IsValid() && !controller.IsOnGround )
			return "CaseB_JumpInputButNotGrounded";

		if ( controller.IsValid() && controller.IsOnGround && controller.UseInputControls && (!health.IsValid() || !health.IsDead) && (!body.IsValid() || body.MotionEnabled) )
			return "CaseC_InputGroundedEnabledButStillNoJumpIfVelocityStaysFlat";

		return "Unclassified";
	}

	private string DescribeHealthDebug( DeathrunHealth health )
	{
		if ( !health.IsValid() )
			return "DeathrunHealthExists=False";

		return $"DeathrunHealthExists=True, IsDead={health.IsDead}, CurrentHealth={health.CurrentHealth:0.##}, MaxHealth={health.MaxHealth:0.##}, " +
			$"DisableInputOnDeath={health.DisableInputOnDeath}, FreezeBodyOnDeath={health.FreezeBodyOnDeath}";
	}

	private string DescribeCameraDebug()
	{
		var playerCamera = FindCamera( GameObject );
		var activeMainCamera = Scene.GetAllComponents<CameraComponent>()
			.FirstOrDefault( x => x.IsValid() && x.Enabled && x.IsMainCamera && x.GameObject.Active );
		var sceneMainCamera = Scene.GetAllComponents<CameraComponent>()
			.FirstOrDefault( x => x.IsValid() && x.GameObject.IsValid() && x.GameObject.Name == "Main Camera" );
		var activeMainName = activeMainCamera.IsValid() && activeMainCamera.GameObject.IsValid()
			? activeMainCamera.GameObject.Name
			: "none";

		return $"PlayerCameraExists={playerCamera.IsValid()}, PlayerCameraEnabled={playerCamera.IsValid() && playerCamera.Enabled}, PlayerCameraIsMain={playerCamera.IsValid() && playerCamera.IsMainCamera}, " +
			$"SceneMainCameraExists={sceneMainCamera.IsValid()}, SceneMainCameraActive={sceneMainCamera.IsValid() && sceneMainCamera.Enabled && sceneMainCamera.GameObject.Active}, SceneMainCameraIsMain={sceneMainCamera.IsValid() && sceneMainCamera.IsMainCamera}, " +
			$"ActiveMainCamera='{activeMainName}', ActiveMainCameraUnderThisPlayer={activeMainCamera.IsValid() && ContainsObject( GameObject, activeMainCamera.GameObject )}";
	}

	private string DescribeRuntimeHierarchy()
	{
		var hasBodyChild = GameObject.Children.Any( x => x.IsValid() && x.Name == "Body" );
		var hasColliderChild = GameObject.Children.Any( x => x.IsValid() && x.Name == "Colliders" );
		var hasRenderer = FindSkinnedRenderer( GameObject ).IsValid();

		return $"Root='{GameObject.Name}', HasBodyChild={hasBodyChild}, HasCollidersChild={hasColliderChild}, HasSkinnedRenderer={hasRenderer}";
	}

	private static CameraComponent FindCamera( GameObject root )
	{
		if ( !root.IsValid() )
			return null;

		var camera = root.Components.Get<CameraComponent>();

		if ( camera.IsValid() )
			return camera;

		foreach ( var child in root.Children )
		{
			camera = FindCamera( child );

			if ( camera.IsValid() )
				return camera;
		}

		return null;
	}

	private static SkinnedModelRenderer FindSkinnedRenderer( GameObject root )
	{
		if ( !root.IsValid() )
			return null;

		var renderer = root.Components.Get<SkinnedModelRenderer>();

		if ( renderer.IsValid() )
			return renderer;

		foreach ( var child in root.Children )
		{
			renderer = FindSkinnedRenderer( child );

			if ( renderer.IsValid() )
				return renderer;
		}

		return null;
	}

	private static int CountKnownColliders( GameObject root )
	{
		if ( !root.IsValid() )
			return 0;

		var count = 0;

		if ( root.Components.Get<CapsuleCollider>().IsValid() )
			count++;

		if ( root.Components.Get<BoxCollider>().IsValid() )
			count++;

		foreach ( var child in root.Children )
			count += CountKnownColliders( child );

		return count;
	}

	private static bool ContainsObject( GameObject root, GameObject candidate )
	{
		if ( !root.IsValid() || !candidate.IsValid() )
			return false;

		if ( root == candidate )
			return true;

		foreach ( var child in root.Children )
		{
			if ( ContainsObject( child, candidate ) )
				return true;
		}

		return false;
	}
}
