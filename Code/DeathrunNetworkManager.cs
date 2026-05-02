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

		var deathrunPlayer = playerObject.Components.GetOrCreate<DeathrunPlayer>();
		playerObject.Components.GetOrCreate<DeathrunOrbitDeathCamera>();
		playerObject.Components.GetOrCreate<DeathrunRagdollOnDeath>();

		var spawnSucceeded = playerObject.NetworkSpawn( connection );
		deathrunPlayer.Initialize( connection );

		var health = playerObject.Components.Get<DeathrunHealth>();

		if ( health.IsValid() )
			health.ResetHealth();
		else
			Log.Warning( $"Spawned player '{playerObject.Name}' has no DeathrunHealth component. Damage sources will ignore it." );

		LogDebug( $"NetworkSpawn result for '{playerObject.Name}': success={spawnSucceeded}, networkActive={playerObject.Network.Active}, owner={DescribeConnection( playerObject.Network.Owner )}, position={playerObject.WorldPosition}, networkMode={playerObject.NetworkMode}." );

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
			EnsureTemplateHasDeathCamera();
			EnsureTemplateHasDeathRagdoll();
			return;
		}

		PlayerTemplate.Components.Create<DeathrunPlayer>();
		Log.Warning( $"PlayerTemplate '{PlayerTemplate.Name}' did not have DeathrunPlayer, so one was added. Add it in the scene/prefab for clearer setup." );
		EnsureTemplateHasDeathCamera();
		EnsureTemplateHasDeathRagdoll();
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

		var controller = playerObject.Components.Get<PlayerController>();

		if ( controller.IsValid() && controller.Body.IsValid() )
		{
			controller.Body.MotionEnabled = true;
			controller.Body.Velocity = Vector3.Zero;
			return;
		}

		var body = playerObject.Components.Get<Rigidbody>();

		if ( body.IsValid() )
		{
			body.MotionEnabled = true;
			body.Velocity = Vector3.Zero;
		}
	}

	private void LogDebug( string message )
	{
		if ( LogNetworking )
			Log.Info( message );
	}

	private static string DescribeConnection( Connection connection )
	{
		if ( connection is null )
			return "none";

		return $"{connection.DisplayName} ({connection.Id})";
	}
}
