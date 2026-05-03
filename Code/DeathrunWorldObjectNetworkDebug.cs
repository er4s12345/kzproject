using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Components.Mapping;

[Title( "Deathrun World Object Network Debug" )]
[Category( "Deathrun" )]
[Icon( "travel_explore" )]
public sealed class DeathrunWorldObjectNetworkDebug : Component
{
	[Property] public bool LogWorldObjectNetworking { get; set; } = false;
	[Property] public bool LogTransformSync { get; set; } = false;
	[Property] public bool LogAsHost { get; set; } = true;
	[Property] public bool LogAsClient { get; set; } = true;
	[Property] public bool IncludePhysicsProps { get; set; } = true;
	[Property] public float LogInterval { get; set; } = 2.0f;

	private TimeSince _timeSinceLastLog;
	private readonly Dictionary<RotatingMesh, string> _lastRotatorRotations = new();
	private readonly Dictionary<DeathrunRotatingObstacle, string> _lastSceneObstacleRotations = new();

	protected override void OnStart()
	{
		_timeSinceLastLog = MathF.Max( 0.25f, LogInterval );
		LogSnapshot( "start" );
	}

	protected override void OnUpdate()
	{
		if ( !ShouldLog() )
			return;

		var interval = MathF.Max( 0.25f, LogInterval );

		if ( _timeSinceLastLog < interval )
			return;

		_timeSinceLastLog = 0.0f;
		LogSnapshot( "periodic" );
	}

	private bool ShouldLog()
	{
		if ( !LogWorldObjectNetworking && !LogTransformSync )
			return false;

		if ( Networking.IsHost && !LogAsHost )
			return false;

		if ( !Networking.IsHost && !LogAsClient )
			return false;

		return true;
	}

	private void LogSnapshot( string reason )
	{
		if ( !ShouldLog() )
			return;

		var rotators = Scene.GetAllComponents<RotatingMesh>()
			.Where( x => x.IsValid() )
			.ToArray();
		var sceneObstacles = Scene.GetAllComponents<DeathrunRotatingObstacle>()
			.Where( x => x.IsValid() )
			.ToArray();
		var drivers = Scene.GetAllComponents<DeathrunNetworkedTransformDriver>()
			.Where( x => x.IsValid() )
			.ToArray();
		var bodies = IncludePhysicsProps
			? Scene.GetAllComponents<Rigidbody>()
				.Where( x => x.IsValid() && !x.Components.Get<DeathrunPlayer>().IsValid() && !x.Components.Get<DeathrunHealth>().IsValid() )
				.ToArray()
			: Array.Empty<Rigidbody>();

		Log.Info( $"WorldObjectDebug {reason}: Rotators={rotators.Length}, DeathrunRotatingObstacles={sceneObstacles.Length}, TransformDrivers={drivers.Length}, PhysicsBodies={bodies.Length}, IsHost={Networking.IsHost}, NetworkingActive={Networking.IsActive}." );

		if ( !Networking.IsHost && rotators.Length == 0 )
			Log.Warning( "WorldObjectDebug: Rotators=0 on this client. Hammer/map RotatingMesh components are not present here; use scene DeathrunRotatingObstacle objects for multiplayer-visible rotating gameplay obstacles." );

		foreach ( var rotator in rotators )
			Log.Info( $"WorldObjectDebug Rotator: LocalRotationChanged={HasLocalRotationChanged( rotator )}, {rotator.DescribeWorldObjectNetworking()}" );

		foreach ( var obstacle in sceneObstacles )
			Log.Info( $"WorldObjectDebug DeathrunRotatingObstacle: LocalRotationChanged={HasLocalRotationChanged( obstacle )}, {obstacle.DescribeWorldObjectNetworking()}" );

		foreach ( var driver in drivers )
			Log.Info( $"WorldObjectDebug TransformDriver: {driver.DescribeWorldObjectNetworking()}" );

		foreach ( var body in bodies )
			Log.Info( $"WorldObjectDebug Rigidbody: {DescribeRigidbody( body )}" );
	}

	private string DescribeRigidbody( Rigidbody body )
	{
		var go = body.GameObject;
		return $"object='{go.Name}', path='{GetObjectPath( go )}', sourceType={GetSourceType( go)}, NetworkMode={go.NetworkMode}, NetworkActive={go.Network.Active}, " +
			$"Owner={DescribeOwner( go )}, IsProxy={go.Network.Active && go.Network.IsProxy}, IsHost={Networking.IsHost}, AlwaysTransmit={go.Network.AlwaysTransmit}, " +
			$"Interpolation={go.Network.Interpolation}, Flags={go.Network.Flags}, MotionEnabled={body.MotionEnabled}, Sleeping={body.Sleeping}, " +
			$"Position={go.WorldPosition}, Rotation={go.WorldRotation}, Velocity={body.Velocity}, AngularVelocity={body.AngularVelocity}.";
	}

	private bool HasLocalRotationChanged( RotatingMesh rotator )
	{
		var currentRotation = rotator.GameObject.LocalRotation.ToString();

		if ( !_lastRotatorRotations.TryGetValue( rotator, out var previousRotation ) )
		{
			_lastRotatorRotations[rotator] = currentRotation;
			return false;
		}

		_lastRotatorRotations[rotator] = currentRotation;
		return previousRotation != currentRotation;
	}

	private bool HasLocalRotationChanged( DeathrunRotatingObstacle obstacle )
	{
		var currentRotation = obstacle.GameObject.LocalRotation.ToString();

		if ( !_lastSceneObstacleRotations.TryGetValue( obstacle, out var previousRotation ) )
		{
			_lastSceneObstacleRotations[obstacle] = currentRotation;
			return false;
		}

		_lastSceneObstacleRotations[obstacle] = currentRotation;
		return previousRotation != currentRotation;
	}

	private static string GetSourceType( GameObject go )
	{
		if ( go.Components.GetAll().Any( x => x.IsValid() && x.GetType().FullName?.Contains( "Hammer", StringComparison.OrdinalIgnoreCase ) == true ) )
			return "Hammer/map entity";

		if ( HasAncestorComponentNamed( go, "Sandbox.MapInstance" ) )
			return "map/map-instance hierarchy";

		return "scene GameObject";
	}

	private static bool HasAncestorComponentNamed( GameObject go, string typeName )
	{
		var current = go;

		while ( current.IsValid() )
		{
			if ( current.Components.GetAll().Any( x => x.IsValid() && x.GetType().FullName == typeName ) )
				return true;

			current = current.Parent;
		}

		return false;
	}

	private static string DescribeOwner( GameObject go )
	{
		var owner = go.Network.Owner;
		return owner is null ? $"none ({go.Network.OwnerId})" : $"{owner.DisplayName} ({owner.Id})";
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
}
