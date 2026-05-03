using System.Threading.Tasks;

[Title( "Deathrun Network Manager" )]
[Category( "Networking" )]
[Icon( "groups" )]
public sealed class DeathrunNetworkManager : Component, Component.INetworkListener
{
	[Property] public bool StartServer { get; set; } = true;
	[Property] public GameObject PlayerTemplate { get; set; }
	[Property] public bool AutoFindPlayerTemplate { get; set; } = false;
	[Property] public bool DisableTemplateOnStart { get; set; } = true;
	[Property] public Vector3 FallbackSpawnOffset { get; set; } = new( 0.0f, 0.0f, 80.0f );
	[Property] public bool LogNetworking { get; set; } = false;

	private bool _templateDisabled;
	private int _spawnIndex;

	protected override async Task OnLoad()
	{
		if ( Scene.IsEditor )
			return;

		LogDebug( $"DeathrunNetworkManager loading. Networking active: {Networking.IsActive}. IsHost: {Networking.IsHost}. StartServer: {StartServer}." );

		if ( StartServer && !Networking.IsActive )
		{
			LoadingScreen.Title = "Creating Lobby";
			LogDebug( "DeathrunNetworkManager creating multiplayer lobby." );

			await GameTask.DelayRealtimeSeconds( 0.1f );
			Networking.CreateLobby( new() );
		}
	}

	protected override void OnStart()
	{
		ResolvePlayerTemplate();

		if ( !PlayerTemplate.IsValid() )
		{
			Log.Warning( "DeathrunNetworkManager has no PlayerTemplate. Assign PlayerTemplate manually in the Inspector." );
			return;
		}

		LogDebug( $"DeathrunNetworkManager resolved PlayerTemplate '{PlayerTemplate.Name}' at {PlayerTemplate.WorldPosition}." );

		if ( DisableTemplateOnStart )
			DisableSceneTemplate();
	}

	public void OnConnected( Connection connection )
	{
		LogDebug( $"DeathrunNetworkManager connection connected: {DescribeConnection( connection )}." );
	}

	public void OnActive( Connection connection )
	{
		LogDebug( $"DeathrunNetworkManager connection active: {DescribeConnection( connection )}. IsHost={Networking.IsHost}." );

		if ( !Networking.IsHost )
		{
			LogDebug( $"DeathrunNetworkManager is running as a client, so it will not spawn a player for {DescribeConnection( connection )}." );
			return;
		}

		SpawnPlayerForConnection( connection );
	}

	public void OnDisconnected( Connection connection )
	{
		LogDebug( $"DeathrunNetworkManager connection disconnected: {DescribeConnection( connection )}." );

		if ( !Networking.IsHost )
			return;

		var removed = 0;

		foreach ( var player in Scene.GetAllComponents<DeathrunPlayer>().ToArray() )
		{
			if ( !player.IsValid() || player.GameObject == PlayerTemplate )
				continue;

			if ( !player.IsOwnedBy( connection ) )
				continue;

			LogDebug( $"Destroying DeathrunPlayer '{player.GameObject.Name}' owned by {DescribeConnection( connection )}." );
			player.GameObject.Destroy();
			removed++;
		}

		LogDebug( $"DeathrunNetworkManager disconnect cleanup removed {removed} player object(s) for {DescribeConnection( connection )}." );
	}

	private void SpawnPlayerForConnection( Connection connection )
	{
		if ( connection is null )
		{
			Log.Warning( "DeathrunNetworkManager cannot spawn a player for a null connection." );
			return;
		}

		if ( !PlayerTemplate.IsValid() && !ResolvePlayerTemplate() )
		{
			Log.Warning( $"Cannot spawn player for {DescribeConnection( connection )}: PlayerTemplate is missing." );
			return;
		}

		if ( HasPlayerForConnection( connection ) )
		{
			Log.Warning( $"Prevented duplicate player spawn for {DescribeConnection( connection )}; an existing DeathrunPlayer already has this owner." );
			return;
		}

		var spawnTransform = GetNextSpawnTransform();
		var playerObject = PlayerTemplate.Clone( spawnTransform, name: $"Player - {connection.DisplayName}" );

		if ( !playerObject.IsValid() )
		{
			Log.Warning( $"Failed to clone PlayerTemplate for {DescribeConnection( connection )}." );
			return;
		}

		playerObject.Enabled = true;
		playerObject.NetworkMode = NetworkMode.Object;

		var controller = playerObject.Components.GetOrCreate<DeathrunPlayerController>();
		DisableLegacyMovement( playerObject );
		var deathrunPlayer = playerObject.Components.GetOrCreate<DeathrunPlayer>();
		var health = playerObject.Components.GetOrCreate<DeathrunHealth>();
		playerObject.Components.GetOrCreate<DeathrunOrbitDeathCamera>();
		playerObject.Components.GetOrCreate<DeathrunRagdollOnDeath>();
		deathrunPlayer.Initialize( connection );
		controller.InitializeSpawnState( "pre-network spawn" );

		if ( health.IsValid() )
			health.InitializeSpawnState();
		else
			Log.Warning( $"Spawned player '{playerObject.Name}' has no DeathrunHealth component. Damage sources will ignore it." );

		LogSpawnState( playerObject, connection, "before NetworkSpawn" );

		var spawnSucceeded = playerObject.NetworkSpawn( connection );

		if ( spawnSucceeded && playerObject.Network.Active && playerObject.Network.OwnerId != connection.Id )
		{
			Log.Warning( $"Player '{playerObject.Name}' spawned with ownerId={playerObject.Network.OwnerId}, expected {connection.Id}. Reassigning ownership to {DescribeConnection( connection )}." );
			playerObject.Network.AssignOwnership( connection );
		}

		LogSpawnState( playerObject, connection, "after NetworkSpawn" );

		if ( health.IsValid() )
			health.InitializeSpawnOwnerRpc();

		if ( deathrunPlayer.IsValid() )
			deathrunPlayer.EnableLocalInputDebugOwnerRpc();

		if ( !controller.IsValid() )
			Log.Warning( $"Spawned player '{playerObject.Name}' has no DeathrunPlayerController. It cannot receive project-local movement input." );

		if ( !spawnSucceeded || !playerObject.Network.Active )
		{
			Log.Warning( $"Player '{playerObject.Name}' did not become an active network object. Make sure spawning is host-side and the template/root NetworkMode is not Never." );
		}
	}

	public bool TryMovePlayerToRespawn( DeathrunPlayer player )
	{
		if ( !Networking.IsHost )
		{
			Log.Warning( "DeathrunNetworkManager ignored a respawn move on a client. Respawning is host-authoritative." );
			return false;
		}

		if ( !player.IsValid() || !player.GameObject.IsValid() )
		{
			Log.Warning( "DeathrunNetworkManager cannot respawn an invalid DeathrunPlayer." );
			return false;
		}

		if ( player.GameObject == PlayerTemplate )
		{
			Log.Warning( "DeathrunNetworkManager will not respawn the scene PlayerTemplate. Runtime player clones only." );
			return false;
		}

		var respawnTransform = GetNextSpawnTransform();
		var playerObject = player.GameObject;

		playerObject.Enabled = true;
		playerObject.WorldTransform = respawnTransform;
		ClearPlayerVelocity( playerObject );

		if ( playerObject.Network.Active )
			playerObject.Network.ClearInterpolation();

		LogDebug( $"DeathrunNetworkManager moved '{playerObject.Name}' owned by {player.OwnerName} ({player.OwnerId}) to respawn position {playerObject.WorldPosition}." );
		return true;
	}

	private bool ResolvePlayerTemplate()
	{
		if ( PlayerTemplate.IsValid() )
		{
			LogDebug( $"DeathrunNetworkManager using assigned PlayerTemplate '{PlayerTemplate.Name}'." );
			EnsureTemplateHasDeathrunPlayer();
			return true;
		}

		if ( !AutoFindPlayerTemplate )
			return false;

		var templates = Scene.GetAllComponents<DeathrunPlayer>()
			.Where( x => x.IsValid() && x.GameObject != GameObject )
			.Select( x => x.GameObject )
			.Distinct()
			.ToArray();

		if ( templates.Length == 0 )
		{
			Log.Warning( "AutoFindPlayerTemplate is enabled, but no GameObject with DeathrunPlayer was found." );
			return false;
		}

		if ( templates.Length > 1 )
		{
			Log.Warning( $"AutoFindPlayerTemplate found {templates.Length} possible DeathrunPlayer templates. Assign PlayerTemplate manually in the Inspector; using '{templates[0].Name}' for now." );
		}

		PlayerTemplate = templates[0];
		LogDebug( $"AutoFindPlayerTemplate selected '{PlayerTemplate.Name}'." );
		EnsureTemplateHasDeathrunController();
		EnsureTemplateHasDeathCamera();
		EnsureTemplateHasDeathRagdoll();
		return true;
	}

	private void EnsureTemplateHasDeathrunPlayer()
	{
		if ( !PlayerTemplate.IsValid() )
			return;

		if ( PlayerTemplate.Components.Get<DeathrunPlayer>().IsValid() )
		{
			EnsureTemplateHasDeathrunController();
			EnsureTemplateHasDeathCamera();
			EnsureTemplateHasDeathRagdoll();
			return;
		}

		PlayerTemplate.Components.Create<DeathrunPlayer>();
		Log.Warning( $"PlayerTemplate '{PlayerTemplate.Name}' did not have DeathrunPlayer, so one was added. Add it in the scene/prefab for clearer setup." );
		EnsureTemplateHasDeathrunController();
		EnsureTemplateHasDeathCamera();
		EnsureTemplateHasDeathRagdoll();
	}

	private void EnsureTemplateHasDeathrunController()
	{
		if ( !PlayerTemplate.IsValid() )
			return;

		if ( PlayerTemplate.Components.Get<DeathrunPlayerController>().IsValid() )
		{
			DisableLegacyMovement( PlayerTemplate );
			return;
		}

		PlayerTemplate.Components.Create<DeathrunPlayerController>();
		DisableLegacyMovement( PlayerTemplate );
		Log.Warning( $"PlayerTemplate '{PlayerTemplate.Name}' did not have DeathrunPlayerController, so one was added. Add it in the scene/prefab for clearer setup." );
	}

	private void EnsureTemplateHasDeathCamera()
	{
		if ( !PlayerTemplate.IsValid() )
			return;

		if ( PlayerTemplate.Components.Get<DeathrunOrbitDeathCamera>().IsValid() )
			return;

		PlayerTemplate.Components.Create<DeathrunOrbitDeathCamera>();
		Log.Warning( $"PlayerTemplate '{PlayerTemplate.Name}' did not have DeathrunOrbitDeathCamera, so one was added for owner-local death camera visuals." );
	}

	private void EnsureTemplateHasDeathRagdoll()
	{
		if ( !PlayerTemplate.IsValid() )
			return;

		if ( PlayerTemplate.Components.Get<DeathrunRagdollOnDeath>().IsValid() )
			return;

		PlayerTemplate.Components.Create<DeathrunRagdollOnDeath>();
		Log.Warning( $"PlayerTemplate '{PlayerTemplate.Name}' did not have DeathrunRagdollOnDeath, so one was added for death corpse visuals." );
	}

	private void DisableSceneTemplate()
	{
		if ( !PlayerTemplate.IsValid() )
			return;

		if ( PlayerTemplate == GameObject )
		{
			Log.Warning( "DeathrunNetworkManager will not disable PlayerTemplate because it is on the same GameObject as the manager. Put the manager on a separate NetworkManager object." );
			return;
		}

		PlayerTemplate.Enabled = false;
		_templateDisabled = true;
		LogDebug( $"DeathrunNetworkManager disabled scene PlayerTemplate '{PlayerTemplate.Name}'. Runtime clones will be spawned per connection." );
	}

	private bool HasPlayerForConnection( Connection connection )
	{
		foreach ( var player in Scene.GetAllComponents<DeathrunPlayer>() )
		{
			if ( !player.IsValid() || player.GameObject == PlayerTemplate )
				continue;

			if ( player.IsOwnedBy( connection ) )
				return true;
		}

		return false;
	}

	private Transform GetNextSpawnTransform()
	{
		var spawnPoints = GetSpawnPoints();

		if ( spawnPoints.Length > 0 )
		{
			var spawnPoint = spawnPoints[_spawnIndex++ % spawnPoints.Length];
			LogDebug( $"Selected spawn point '{spawnPoint.GameObject.Name}' with priority {spawnPoint.Priority} at {spawnPoint.WorldPosition}." );
			return spawnPoint.WorldTransform.WithScale( 1.0f );
		}

		var fallback = GetFallbackSpawnTransform( _spawnIndex++ );
		Log.Warning( $"No enabled DeathrunSpawnPoint found. Using fallback spawn at {fallback.Position}." );
		return fallback;
	}

	private DeathrunSpawnPoint[] GetSpawnPoints()
	{
		return Scene.GetAllComponents<DeathrunSpawnPoint>()
			.Where( x => x.IsValid() && x.IsValidSpawnPoint )
			.OrderBy( x => x.Priority )
			.ThenBy( x => x.GameObject.Name )
			.ToArray();
	}

	private Transform GetFallbackSpawnTransform( int spawnIndex )
	{
		var offset = FallbackSpawnOffset + new Vector3( spawnIndex * 64.0f, 0.0f, 0.0f );

		if ( PlayerTemplate.IsValid() )
		{
			var templateTransform = PlayerTemplate.WorldTransform.WithScale( 1.0f );
			return templateTransform.WithPosition( templateTransform.Position + offset );
		}

		return WorldTransform
			.WithPosition( WorldPosition + offset )
			.WithRotation( WorldRotation )
			.WithScale( 1.0f );
	}

	private static void ClearPlayerVelocity( GameObject playerObject )
	{
		if ( !playerObject.IsValid() )
			return;

		var controller = playerObject.Components.Get<DeathrunPlayerController>();

		if ( controller.IsValid() && controller.Body.IsValid() )
		{
			controller.InitializeSpawnState( "clear player velocity" );
			return;
		}

		var body = playerObject.Components.Get<Rigidbody>();

		if ( body.IsValid() )
		{
			body.MotionEnabled = true;
			body.Velocity = Vector3.Zero;
		}
	}

	private static void DisableLegacyMovement( GameObject playerObject )
	{
		if ( !playerObject.IsValid() )
			return;

		var legacy = playerObject.Components.Get<PlayerController>();

		if ( !legacy.IsValid() )
			return;

		legacy.UseInputControls = false;
		legacy.UseLookControls = false;
		legacy.UseCameraControls = false;
		legacy.Enabled = false;
	}

	private void LogDebug( string message )
	{
		if ( LogNetworking )
			Log.Info( message );
	}

	private void LogSpawnState( GameObject playerObject, Connection intendedOwner, string phase )
	{
		if ( !LogNetworking )
			return;

		if ( !playerObject.IsValid() )
		{
			Log.Info( $"DeathrunNetworkManager spawn {phase}: invalid player object for intended owner {DescribeConnection( intendedOwner )}." );
			return;
		}

		var network = playerObject.Network;
		var controller = playerObject.Components.Get<DeathrunPlayerController>();
		var legacyController = playerObject.Components.Get<PlayerController>();
		var health = playerObject.Components.Get<DeathrunHealth>();
		var body = controller.IsValid() && controller.Body.IsValid()
			? controller.Body
			: playerObject.Components.Get<Rigidbody>();

		Log.Info(
			$"DeathrunNetworkManager spawn {phase}: player='{playerObject.Name}', intendedOwner={DescribeConnection( intendedOwner )}, " +
			$"networkOwner={DescribeConnection( network.Owner )}, ownerId={network.OwnerId}, active={network.Active}, isOwnerHere={network.IsOwner}, isProxyHere={network.IsProxy}, " +
			$"networkMode={playerObject.NetworkMode}, enabled={playerObject.Enabled}, position={playerObject.WorldPosition}, " +
			$"hasPlayer={playerObject.Components.Get<DeathrunPlayer>().IsValid()}, hasHealth={health.IsValid()}, hasController={controller.IsValid()}, controllerEnabled={controller.IsValid() && controller.Enabled}, " +
			$"input={controller.IsValid() && controller.UseInputControls}, look={controller.IsValid() && controller.UseLookControls}, camera={controller.IsValid() && controller.UseCameraControls}, " +
			$"jumpSpeed={(controller.IsValid() ? controller.JumpSpeed : 0.0f):0.##}, grounded={controller.IsValid() && controller.IsOnGround}, shouldProcessLocalInput={controller.IsValid() && controller.ShouldProcessLocalInput()}, " +
			$"legacyPresent={legacyController.IsValid()}, legacyEnabled={legacyController.IsValid() && legacyController.Enabled}, isDead={health.IsValid() && health.IsDead}, bodyValid={body.IsValid()}, motionEnabled={body.IsValid() && body.MotionEnabled}." );
	}

	private static string DescribeConnection( Connection connection )
	{
		if ( connection is null )
			return "none";

		return $"{connection.DisplayName} ({connection.Id})";
	}
}
