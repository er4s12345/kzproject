using System;
using System.Collections.Generic;
using System.Linq;

[Title( "Deathrun Scene Visibility Debug" )]
[Category( "Deathrun" )]
[Icon( "visibility" )]
public sealed class DeathrunSceneVisibilityDebug : Component
{
	private const string TestCubeName = "CLIENT_VISIBILITY_TEST_CUBE";

	[Property] public bool LogVisibilityDebug { get; set; } = true;
	[Property] public float LogInterval { get; set; } = 2.0f;

	private TimeSince _timeSinceLastLog;

	protected override void OnStart()
	{
		_timeSinceLastLog = MathF.Max( 0.25f, LogInterval );
		LogSnapshot( "start" );
	}

	protected override void OnUpdate()
	{
		if ( !LogVisibilityDebug )
			return;

		var interval = MathF.Max( 0.25f, LogInterval );

		if ( _timeSinceLastLog < interval )
			return;

		_timeSinceLastLog = 0.0f;
		LogSnapshot( "periodic" );
	}

	private void LogSnapshot( string reason )
	{
		if ( !LogVisibilityDebug )
			return;

		var testCube = FindTestCube();
		var mapInstances = Scene.GetAllComponents<MapInstance>()
			.Where( x => x.IsValid() )
			.ToArray();

		Log.Info(
			$"SceneVisibilityDebug {reason}: Role={(Networking.IsHost ? "Host" : "Client")}, IsHost={Networking.IsHost}, NetworkingActive={Networking.IsActive}, SceneName='{Scene.Name}', " +
			$"TestCubeExists={testCube.IsValid()}, {DescribeTestCube( testCube )}, MapInstances={mapInstances.Length}, MapInfo='{DescribeMapInstances( mapInstances )}'." );

		if ( !testCube.IsValid() )
			Log.Warning( $"SceneVisibilityDebug: '{TestCubeName}' is missing on this peer. If this is the joined client, it is not loading the updated MapLoaderTestScene scene/package." );
	}

	private GameObject FindTestCube()
	{
		return Scene.Directory.FindByName( TestCubeName ).FirstOrDefault();
	}

	private static string DescribeTestCube( GameObject testCube )
	{
		if ( !testCube.IsValid() )
			return "Path=invalid, Enabled=false, ActiveInHierarchy=false, WorldPosition=invalid, ModelRenderer=false, RendererEnabled=false, NetworkMode=invalid, NetworkActive=false";

		var renderer = testCube.Components.Get<ModelRenderer>();

		return $"Path='{GetObjectPath( testCube )}', Enabled={testCube.Enabled}, ActiveInHierarchy={IsEnabledInHierarchy( testCube )}, WorldPosition={testCube.WorldPosition}, LocalPosition={testCube.LocalPosition}, " +
			$"ModelRenderer={renderer.IsValid()}, RendererEnabled={renderer.IsValid() && renderer.Enabled}, " +
			$"NetworkMode={testCube.NetworkMode}, NetworkActive={testCube.Network.Active}, NetworkTransmit={testCube.Network.AlwaysTransmit}, Tags='{testCube.Tags}'";
	}

	private static bool IsEnabledInHierarchy( GameObject go )
	{
		var current = go;

		while ( current.IsValid() )
		{
			if ( !current.Enabled )
				return false;

			current = current.Parent;
		}

		return true;
	}

	private static string GetObjectPath( GameObject go )
	{
		var names = new List<string>();
		var current = go;

		while ( current.IsValid() )
		{
			names.Add( current.Name );
			current = current.Parent;
		}

		names.Reverse();
		return string.Join( "/", names );
	}

	private static string DescribeMapInstances( MapInstance[] mapInstances )
	{
		if ( mapInstances.Length == 0 )
			return "none";

		return string.Join( "; ", mapInstances.Select( x => x.GameObject.IsValid()
			? $"{x.GameObject.Name}: enabled={x.Enabled}, map={x.MapName}, position={x.GameObject.WorldPosition}"
			: "invalid" ) );
	}
}
