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

	private bool _templateDisabled;
	private int _spawnIndex;

	protected override async Task OnLoad()
	{
		if ( Scene.IsEditor )
			return;

		Log.Info( $"DeathrunNetworkManager loading. Networking active: {Networking.IsActive}. IsHost: {Networking.IsHost}. StartServer: {StartServer}." );

		if ( StartServer && !Networking.IsActive )
		{
			LoadingScreen.Title = "Creating Lobby";
			Log.Info( "DeathrunNetworkManager creating multiplayer lobby." );

			await Task.DelayRealtimeSeconds( 0.1f );
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

		Log.Info( $"DeathrunNetworkManager resolved PlayerTemplate '{PlayerTemplate.Name}' at {PlayerTemplate.WorldPosition}." );

		if ( DisableTemplateOnStart )
			DisableSceneTemplate();
	}

	public void OnConnected( Connection connection )
	{
		Log.Info( $"DeathrunNetworkManager connection connected: {DescribeConnection( connection )}." );
	}

	public void OnActive( Connection connection )
	{
		Log.Info( $"DeathrunNetworkManager connection active: {DescribeConnection( connection )}. IsHost={Networking.IsHost}." );

		if ( !Networking.IsHost )
		{
			Log.Info( $"DeathrunNetworkManager is running as a client, so it will not spawn a player for {DescribeConnection( connection )}." );
			return;
		}

		SpawnPlayerForConnection( connection );
	}

	public void OnDisconnected( Connection connection )
	{
		Log.Info( $"DeathrunNetworkManager connection disconnected: {DescribeConnection( connection )}." );

		if ( !Networking.IsHost )
			return;

		var removed = 0;

		foreach ( var player in Scene.GetAllComponents<DeathrunPlayer>().ToArray() )
		{
			if ( !player.IsValid() || player.GameObject == PlayerTemplate )
				continue;

			if ( !player.IsOwnedBy( connection ) )
				continue;

			Log.Info( $"Destroying DeathrunPlayer '{player.GameObject.Name}' owned by {DescribeConnection( connection )}." );
			player.GameObject.Destroy();
			removed++;
		}

		Log.Info( $"DeathrunNetworkManager disconnect cleanup removed {removed} player object(s) for {DescribeConnection( connection )}." );
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
		var spawnSucceeded = playerObject.NetworkSpawn( connection );
		deathrunPlayer.Initialize( connection );

		Log.Info( $"NetworkSpawn result for '{playerObject.Name}': success={spawnSucceeded}, networkActive={playerObject.Network.Active}, owner={DescribeConnection( playerObject.Network.Owner )}, position={playerObject.WorldPosition}, networkMode={playerObject.NetworkMode}." );

		if ( !spawnSucceeded || !playerObject.Network.Active )
		{
			Log.Warning( $"Player '{playerObject.Name}' did not become an active network object. Make sure spawning is host-side and the template/root NetworkMode is not Never." );
		}
	}

	private bool ResolvePlayerTemplate()
	{
		if ( PlayerTemplate.IsValid() )
		{
			Log.Info( $"DeathrunNetworkManager using assigned PlayerTemplate '{PlayerTemplate.Name}'." );
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
		Log.Info( $"AutoFindPlayerTemplate selected '{PlayerTemplate.Name}'." );
		return true;
	}

	private void EnsureTemplateHasDeathrunPlayer()
	{
		if ( !PlayerTemplate.IsValid() )
			return;

		if ( PlayerTemplate.Components.Get<DeathrunPlayer>().IsValid() )
			return;

		PlayerTemplate.Components.Create<DeathrunPlayer>();
		Log.Warning( $"PlayerTemplate '{PlayerTemplate.Name}' did not have DeathrunPlayer, so one was added. Add it in the scene/prefab for clearer setup." );
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
		Log.Info( $"DeathrunNetworkManager disabled scene PlayerTemplate '{PlayerTemplate.Name}'. Runtime clones will be spawned per connection." );
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
			Log.Info( $"Selected spawn point '{spawnPoint.GameObject.Name}' with priority {spawnPoint.Priority} at {spawnPoint.WorldPosition}." );
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

	private static string DescribeConnection( Connection connection )
	{
		if ( connection is null )
			return "none";

		return $"{connection.DisplayName} ({connection.Id})";
	}
}
