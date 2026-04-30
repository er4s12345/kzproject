using System.Threading.Tasks;

[Title( "Deathrun Network Manager" )]
[Category( "Networking" )]
[Icon( "groups" )]
public sealed class DeathrunNetworkManager : Component, Component.INetworkListener
{
	[Property] public bool StartServer { get; set; } = true;
	[Property] public GameObject PlayerTemplate { get; set; }
	[Property] public bool AutoFindPlayerTemplate { get; set; } = true;
	[Property] public bool DisableTemplateOnStart { get; set; } = true;
	[Property] public List<GameObject> SpawnPoints { get; set; } = new();
	[Property] public Vector3 FallbackSpawnOffset { get; set; } = new( 0.0f, 0.0f, 80.0f );

	private bool _templateDisabled;
	private int _spawnIndex;

	protected override async Task OnLoad()
	{
		if ( Scene.IsEditor )
			return;

		Log.Info( $"DeathrunNetworkManager loading. Networking active: {Networking.IsActive}. StartServer: {StartServer}." );

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
			Log.Warning( "DeathrunNetworkManager has no PlayerTemplate. Assign your Player Controller prefab/template in the Inspector." );
			return;
		}

		Log.Info( $"DeathrunNetworkManager using player template '{PlayerTemplate.Name}' at {PlayerTemplate.WorldPosition}." );

		if ( DisableTemplateOnStart )
		{
			if ( PlayerTemplate == GameObject )
			{
				Log.Warning( "DeathrunNetworkManager is on the same GameObject as PlayerTemplate, so it will not disable the template. Put this component on a separate empty GameObject." );
				return;
			}

			PlayerTemplate.Enabled = false;
			_templateDisabled = true;
			Log.Info( $"DeathrunNetworkManager disabled scene template '{PlayerTemplate.Name}'. Networked player clones will be spawned per connection." );
		}
	}

	public void OnConnected( Connection connection )
	{
		Log.Info( $"Client connected: {DescribeConnection( connection )}." );
	}

	public void OnActive( Connection connection )
	{
		Log.Info( $"Client active: {DescribeConnection( connection )}. Preparing player spawn." );

		if ( !PlayerTemplate.IsValid() && !ResolvePlayerTemplate() )
		{
			Log.Warning( $"Cannot spawn player for {DescribeConnection( connection )}: no PlayerTemplate assigned or found." );
			return;
		}

		if ( HasPlayerForConnection( connection ) )
		{
			Log.Warning( $"Skipping duplicate player spawn for {DescribeConnection( connection )}; a networked PlayerController already exists for this owner." );
			return;
		}

		var spawnTransform = FindSpawnTransform( _spawnIndex++ );
		var player = PlayerTemplate.Clone( spawnTransform, name: $"Player - {connection.DisplayName}" );
		player.Enabled = true;
		player.NetworkMode = NetworkMode.Object;

		var spawnSucceeded = player.NetworkSpawn( connection );
		Log.Info( $"Spawned '{player.Name}' for {DescribeConnection( connection )}. NetworkSpawn success: {spawnSucceeded}. Network active: {player.Network.Active}. Owner: {DescribeConnection( player.Network.Owner )}. Position: {player.WorldPosition}. NetworkMode: {player.NetworkMode}." );

		LogPlayerSetup( player );

		if ( !spawnSucceeded || !player.Network.Active )
		{
			Log.Warning( $"Player '{player.Name}' did not become an active network object. Check that spawning is happening on the host and that the object is not set to NetworkMode.Never." );
		}
	}

	public void OnDisconnected( Connection connection )
	{
		Log.Info( $"Client disconnected: {DescribeConnection( connection )}." );

		foreach ( var controller in Scene.GetAllComponents<PlayerController>().ToArray() )
		{
			var player = controller.GameObject;

			if ( player == PlayerTemplate )
				continue;

			if ( player.Network.Owner != connection )
				continue;

			Log.Info( $"Destroying player '{player.Name}' for disconnected client {DescribeConnection( connection )}." );
			player.Destroy();
		}
	}

	private bool ResolvePlayerTemplate()
	{
		if ( PlayerTemplate.IsValid() )
			return true;

		if ( !AutoFindPlayerTemplate )
			return false;

		var controllers = Scene.GetAllComponents<PlayerController>()
			.Where( x => x.GameObject != GameObject )
			.ToArray();

		if ( controllers.Length == 0 )
			return false;

		if ( controllers.Length > 1 )
		{
			Log.Warning( $"DeathrunNetworkManager found {controllers.Length} PlayerControllers in the scene. Assign PlayerTemplate manually so it does not pick the wrong one." );
		}

		PlayerTemplate = controllers[0].GameObject;
		return PlayerTemplate.IsValid();
	}

	private bool HasPlayerForConnection( Connection connection )
	{
		foreach ( var controller in Scene.GetAllComponents<PlayerController>() )
		{
			var player = controller.GameObject;

			if ( player == PlayerTemplate )
				continue;

			if ( player.Network.Active && player.Network.Owner == connection )
				return true;
		}

		return false;
	}

	private Transform FindSpawnTransform( int spawnIndex )
	{
		var spawnPoints = SpawnPoints?.Where( x => x.IsValid() ).ToArray();

		if ( spawnPoints is { Length: > 0 } )
			return spawnPoints[spawnIndex % spawnPoints.Length].WorldTransform.WithScale( 1.0f );

		if ( _templateDisabled && PlayerTemplate.IsValid() )
		{
			var templateTransform = PlayerTemplate.WorldTransform.WithScale( 1.0f );
			return templateTransform.WithPosition( templateTransform.Position + new Vector3( spawnIndex * 64.0f, 0.0f, 0.0f ) );
		}

		return WorldTransform
			.WithPosition( WorldPosition + FallbackSpawnOffset + new Vector3( spawnIndex * 64.0f, 0.0f, 0.0f ) )
			.WithRotation( WorldRotation )
			.WithScale( 1.0f );
	}

	private void LogPlayerSetup( GameObject player )
	{
		var controller = player.GetComponent<PlayerController>();
		var renderers = player.GetComponentsInChildren<SkinnedModelRenderer>( true, true ).ToArray();

		if ( controller is null )
		{
			Log.Warning( $"Spawned player '{player.Name}' has no PlayerController on the root GameObject." );
		}
		else
		{
			Log.Info( $"PlayerController for '{player.Name}': UseInputControls={controller.UseInputControls}, UseLookControls={controller.UseLookControls}, HideBodyInFirstPerson={controller.HideBodyInFirstPerson}, WalkSpeed={controller.WalkSpeed}, RunSpeed={controller.RunSpeed}, JumpSpeed={controller.JumpSpeed}." );
		}

		if ( renderers.Length == 0 )
		{
			Log.Warning( $"Spawned player '{player.Name}' has no SkinnedModelRenderer in its children, so remote clients will not have a visible body." );
			return;
		}

		foreach ( var renderer in renderers )
		{
			Log.Info( $"Renderer for '{player.Name}': GameObject='{renderer.GameObject.Name}', Enabled={renderer.Enabled}, Active={renderer.Active}, RenderType={renderer.RenderType}, Model={renderer.Model}." );
		}
	}

	private static string DescribeConnection( Connection connection )
	{
		if ( connection is null )
			return "none";

		return $"{connection.DisplayName} ({connection.Id})";
	}
}
