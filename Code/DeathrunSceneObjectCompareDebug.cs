using System;
using System.Collections.Generic;
using System.Linq;

[Title( "Deathrun Scene Object Compare Debug" )]
[Category( "Deathrun" )]
[Icon( "compare_arrows" )]
public sealed class DeathrunSceneObjectCompareDebug : Component
{
	private static readonly string[] ObjectNames =
	{
		"CLIENT_VISIBILITY_TEST_CUBE",
		"Scene Rotating Obstacle 0",
		"Scene Rotating Obstacle 1",
		"Scene Rotating Obstacle 2"
	};

	[Property] public bool LogCompareDebug { get; set; } = true;
	[Property] public float LogInterval { get; set; } = 2.0f;

	private TimeSince _timeSinceLastLog;
	private readonly Dictionary<string, ObjectSnapshot> _lastSnapshots = new();

	protected override void OnStart()
	{
		_timeSinceLastLog = MathF.Max( 0.25f, LogInterval );
		LogSnapshot( "start" );
	}

	protected override void OnUpdate()
	{
		if ( !LogCompareDebug )
			return;

		var interval = MathF.Max( 0.25f, LogInterval );

		if ( _timeSinceLastLog < interval )
			return;

		_timeSinceLastLog = 0.0f;
		LogSnapshot( "periodic" );
	}

	private void LogSnapshot( string reason )
	{
		if ( !LogCompareDebug )
			return;

		Log.Info( $"SceneObjectCompare {reason}: Role={(Networking.IsHost ? "Host" : "Client")}, IsHost={Networking.IsHost}, NetworkingActive={Networking.IsActive}, SceneName='{Scene.Name}'." );

		foreach ( var objectName in ObjectNames )
		{
			var go = Scene.Directory.FindByName( objectName ).FirstOrDefault();
			var snapshot = ObjectSnapshot.FromGameObject( go );
			var previous = _lastSnapshots.TryGetValue( objectName, out var lastSnapshot ) ? lastSnapshot : null;

			Log.Info( snapshot.Describe( previous ) );

			_lastSnapshots[objectName] = snapshot;
		}
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

	private sealed class ObjectSnapshot
	{
		public string Name { get; private init; }
		public bool Exists { get; private init; }
		public string Path { get; private init; }
		public bool Enabled { get; private init; }
		public bool ActiveInHierarchy { get; private init; }
		public bool ParentExists { get; private init; }
		public string ParentName { get; private init; }
		public bool ParentEnabled { get; private init; }
		public Vector3 WorldPosition { get; private init; }
		public Vector3 LocalPosition { get; private init; }
		public Rotation WorldRotation { get; private init; }
		public Rotation LocalRotation { get; private init; }
		public Vector3 LocalScale { get; private init; }
		public bool ModelRendererExists { get; private init; }
		public bool ModelRendererEnabled { get; private init; }
		public string ModelName { get; private init; }
		public Color Tint { get; private init; }
		public bool BoxColliderExists { get; private init; }
		public bool BoxColliderEnabled { get; private init; }
		public Vector3 BoxColliderCenter { get; private init; }
		public Vector3 BoxColliderScale { get; private init; }
		public NetworkMode NetworkMode { get; private init; }
		public bool NetworkActive { get; private init; }
		public bool NetworkAlwaysTransmit { get; private init; }
		public bool DeathrunRotatingObstacleExists { get; private init; }
		public bool DeathrunRotatingObstacleEnabled { get; private init; }
		public bool ObstacleStartEnabled { get; private init; }
		public bool ObstacleDeterministicLocal { get; private init; }
		public bool ObstacleUseAbsoluteTime { get; private init; }
		public float ObstacleStartTimeOffset { get; private init; }
		public float ObstacleStartPhaseDegrees { get; private init; }
		public DeathrunRotatingObstacleBaseRotationMode ObstacleBaseRotationMode { get; private init; }
		public Rotation ObstacleBaseRotationOverride { get; private init; }
		public Rotation ObstacleBaseLocalRotation { get; private init; }
		public double ObstacleTimeUsed { get; private init; }
		public double ObstacleLocalTimeUsed { get; private init; }
		public float ObstacleCalculatedAngle { get; private init; }
		public Vector3 ObstacleRotationAxis { get; private init; }
		public float ObstacleRotationSpeed { get; private init; }
		public bool ObstacleNetworkObject { get; private init; }
		public bool ObstacleAlwaysTransmitWhenNetworked { get; private init; }
		public int ObstacleFixedUpdateCount { get; private init; }
		public int ObstacleRotationApplyCount { get; private init; }
		public bool ObstacleLastUpdateWasHost { get; private init; }
		public bool ObstacleLastUpdateWasClient { get; private init; }

		public static ObjectSnapshot FromGameObject( GameObject go )
		{
			if ( !go.IsValid() )
			{
				return new ObjectSnapshot
				{
					Name = "missing",
					Exists = false,
					Path = "invalid",
					ModelName = "invalid",
					ParentName = "none"
				};
			}

			var parent = go.Parent;
			var renderer = go.Components.Get<ModelRenderer>();
			var collider = go.Components.Get<BoxCollider>();
			var obstacle = go.Components.Get<DeathrunRotatingObstacle>();

			return new ObjectSnapshot
			{
				Name = go.Name,
				Exists = true,
				Path = GetObjectPath( go ),
				Enabled = go.Enabled,
				ActiveInHierarchy = IsEnabledInHierarchy( go ),
				ParentExists = parent.IsValid(),
				ParentName = parent.IsValid() ? parent.Name : "none",
				ParentEnabled = parent.IsValid() && parent.Enabled,
				WorldPosition = go.WorldPosition,
				LocalPosition = go.LocalPosition,
				WorldRotation = go.WorldRotation,
				LocalRotation = go.LocalRotation,
				LocalScale = go.LocalScale,
				ModelRendererExists = renderer.IsValid(),
				ModelRendererEnabled = renderer.IsValid() && renderer.Enabled,
				ModelName = renderer.IsValid() && renderer.Model is not null ? renderer.Model.Name : "none",
				Tint = renderer.IsValid() ? renderer.Tint : Color.Transparent,
				BoxColliderExists = collider.IsValid(),
				BoxColliderEnabled = collider.IsValid() && collider.Enabled,
				BoxColliderCenter = collider.IsValid() ? collider.Center : Vector3.Zero,
				BoxColliderScale = collider.IsValid() ? collider.Scale : Vector3.Zero,
				NetworkMode = go.NetworkMode,
				NetworkActive = go.Network.Active,
				NetworkAlwaysTransmit = go.Network.AlwaysTransmit,
				DeathrunRotatingObstacleExists = obstacle.IsValid(),
				DeathrunRotatingObstacleEnabled = obstacle.IsValid() && obstacle.Enabled,
				ObstacleStartEnabled = obstacle.IsValid() && obstacle.StartEnabled,
				ObstacleDeterministicLocal = obstacle.IsValid() && obstacle.DeterministicLocal,
				ObstacleUseAbsoluteTime = obstacle.IsValid() && obstacle.UseAbsoluteTime,
				ObstacleStartTimeOffset = obstacle.IsValid() ? obstacle.StartTimeOffset : 0.0f,
				ObstacleStartPhaseDegrees = obstacle.IsValid() ? obstacle.StartPhaseDegrees : 0.0f,
				ObstacleBaseRotationMode = obstacle.IsValid() ? obstacle.BaseRotationMode : DeathrunRotatingObstacleBaseRotationMode.InitialSceneRotation,
				ObstacleBaseRotationOverride = obstacle.IsValid() ? obstacle.BaseRotationOverride : Rotation.Identity,
				ObstacleBaseLocalRotation = obstacle.IsValid() ? obstacle.BaseLocalRotation : Rotation.Identity,
				ObstacleTimeUsed = obstacle.IsValid() ? obstacle.LastTimeUsed : 0.0,
				ObstacleLocalTimeUsed = obstacle.IsValid() ? obstacle.LastLocalTimeUsed : 0.0,
				ObstacleCalculatedAngle = obstacle.IsValid() ? obstacle.LastCalculatedAngle : 0.0f,
				ObstacleRotationAxis = obstacle.IsValid() ? obstacle.RotationAxis : Vector3.Zero,
				ObstacleRotationSpeed = obstacle.IsValid() ? obstacle.RotationSpeed : 0.0f,
				ObstacleNetworkObject = obstacle.IsValid() && obstacle.NetworkObject,
				ObstacleAlwaysTransmitWhenNetworked = obstacle.IsValid() && obstacle.AlwaysTransmitWhenNetworked,
				ObstacleFixedUpdateCount = obstacle.IsValid() ? obstacle.FixedUpdateCount : 0,
				ObstacleRotationApplyCount = obstacle.IsValid() ? obstacle.RotationApplyCount : 0,
				ObstacleLastUpdateWasHost = obstacle.IsValid() && obstacle.LastUpdateWasHost,
				ObstacleLastUpdateWasClient = obstacle.IsValid() && obstacle.LastUpdateWasClient
			};
		}

		public string Describe( ObjectSnapshot previous )
		{
			if ( !Exists )
				return $"SceneObjectCompare Object: Exists=false, Name='{Name}', Path={Path}.";

			return $"SceneObjectCompare Object: Name='{Name}', Exists={Exists}, Path='{Path}', Enabled={Enabled}, ActiveInHierarchy={ActiveInHierarchy}, " +
				$"ParentExists={ParentExists}, ParentName='{ParentName}', ParentEnabled={ParentEnabled}, " +
				$"WorldPosition={WorldPosition}, LocalPosition={LocalPosition}, LocalScale={LocalScale}, WorldRotation={WorldRotation}, LocalRotation={LocalRotation}, " +
				$"PositionChanged={Changed( previous?.WorldPosition, WorldPosition )}, RotationChanged={Changed( previous?.LocalRotation, LocalRotation )}, ScaleChanged={Changed( previous?.LocalScale, LocalScale )}, " +
				$"ModelRendererExists={ModelRendererExists}, ModelRendererEnabled={ModelRendererEnabled}, ModelName='{ModelName}', Tint={Tint}, " +
				$"BoxColliderExists={BoxColliderExists}, BoxColliderEnabled={BoxColliderEnabled}, BoxColliderCenter={BoxColliderCenter}, BoxColliderScale={BoxColliderScale}, " +
				$"NetworkMode={NetworkMode}, NetworkActive={NetworkActive}, NetworkAlwaysTransmit={NetworkAlwaysTransmit}, " +
				$"DeathrunRotatingObstacleExists={DeathrunRotatingObstacleExists}, DeathrunRotatingObstacleEnabled={DeathrunRotatingObstacleEnabled}, " +
				$"ObstacleStartEnabled={ObstacleStartEnabled}, ObstacleDeterministicLocal={ObstacleDeterministicLocal}, ObstacleUseAbsoluteTime={ObstacleUseAbsoluteTime}, " +
				$"ObstacleTimeUsed={ObstacleTimeUsed:0.###}, ObstacleLocalTimeUsed={ObstacleLocalTimeUsed:0.###}, ObstacleStartTimeOffset={ObstacleStartTimeOffset:0.###}, " +
				$"ObstacleStartPhaseDegrees={ObstacleStartPhaseDegrees:0.###}, ObstacleCalculatedAngle={ObstacleCalculatedAngle:0.###}, " +
				$"ObstacleBaseRotationMode={ObstacleBaseRotationMode}, ObstacleBaseRotationOverride={ObstacleBaseRotationOverride}, ObstacleBaseLocalRotation={ObstacleBaseLocalRotation}, ObstacleRotationAxis={ObstacleRotationAxis}, " +
				$"ObstacleRotationSpeed={ObstacleRotationSpeed:0.##}, ObstacleNetworkObject={ObstacleNetworkObject}, " +
				$"ObstacleAlwaysTransmitWhenNetworked={ObstacleAlwaysTransmitWhenNetworked}, ObstacleFixedUpdateCount={ObstacleFixedUpdateCount}, ObstacleRotationApplyCount={ObstacleRotationApplyCount}, " +
				$"ObstacleLastUpdateWasHost={ObstacleLastUpdateWasHost}, ObstacleLastUpdateWasClient={ObstacleLastUpdateWasClient}.";
		}

		private static bool Changed( Vector3? previous, Vector3 current )
		{
			return previous.HasValue && previous.Value != current;
		}

		private static bool Changed( Rotation? previous, Rotation current )
		{
			return previous.HasValue && previous.Value != current;
		}
	}
}
