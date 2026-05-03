using System;
using System.Collections.Generic;

public enum DeathrunRotatingObstacleBaseRotationMode
{
	InitialSceneRotation,
	ExplicitRotation
}

[Title( "Deathrun Rotating Obstacle" )]
[Category( "Deathrun" )]
[Icon( "rotate_right" )]
public sealed class DeathrunRotatingObstacle : Component
{
	[Property] public bool StartEnabled { get; set; } = true;
	[Property] public Vector3 RotationAxis { get; set; } = Vector3.Up;
	[Property] public float RotationSpeed { get; set; } = 90.0f;
	[Property] public bool UseLocalSpace { get; set; } = true;
	[Property] public bool DeterministicLocal { get; set; } = true;
	[Property] public bool UseAbsoluteTime { get; set; } = true;
	[Property] public float StartTimeOffset { get; set; }
	[Property] public float StartPhaseDegrees { get; set; }
	[Property] public DeathrunRotatingObstacleBaseRotationMode BaseRotationMode { get; set; } = DeathrunRotatingObstacleBaseRotationMode.InitialSceneRotation;
	[Property] public Rotation BaseRotationOverride { get; set; } = Rotation.Identity;
	[Property] public bool NetworkObject { get; set; } = false;
	[Property] public bool AlwaysTransmitWhenNetworked { get; set; } = false;
	[Property] public bool LogNetworking { get; set; } = false;

	public int FixedUpdateCount { get; private set; }
	public int RotationApplyCount { get; private set; }
	public bool LastUpdateWasHost { get; private set; }
	public bool LastUpdateWasClient { get; private set; }
	public Rotation BaseLocalRotation { get; private set; }
	public Rotation BaseWorldRotation { get; private set; }
	public double LastTimeUsed { get; private set; }
	public double LastLocalTimeUsed { get; private set; }
	public float LastCalculatedAngle { get; private set; }

	private double _localStartTime;
	private bool _initialized;
	private TimeSince _timeSinceLog;

	protected override void OnAwake()
	{
		Initialize();
		EnsureNetworkSettings();
	}

	protected override void OnStart()
	{
		Initialize();
		EnsureNetworkSettings();

		if ( StartEnabled && DeterministicLocal )
			ApplyDeterministicRotation();

		LogState( "started", true );
	}

	protected override void OnFixedUpdate()
	{
		Initialize();

		FixedUpdateCount++;
		LastUpdateWasHost = Networking.IsHost;
		LastUpdateWasClient = !Networking.IsHost;

		if ( StartEnabled && DeterministicLocal )
			ApplyDeterministicRotation();

		LogState( "deterministic local update" );
	}

	private void Initialize()
	{
		if ( _initialized )
			return;

		BaseLocalRotation = BaseRotationMode == DeathrunRotatingObstacleBaseRotationMode.ExplicitRotation
			? BaseRotationOverride
			: GameObject.LocalRotation;

		BaseWorldRotation = BaseRotationMode == DeathrunRotatingObstacleBaseRotationMode.ExplicitRotation
			? BaseRotationOverride
			: WorldRotation;

		_localStartTime = Time.NowDouble;
		_initialized = true;
	}

	private void EnsureNetworkSettings()
	{
		if ( !NetworkObject )
			return;

		GameObject.NetworkMode = NetworkMode.Object;
		GameObject.Network.AlwaysTransmit = AlwaysTransmitWhenNetworked;
		GameObject.Network.Interpolation = true;
		GameObject.Network.Flags &= ~NetworkFlags.NoTransformSync;
	}

	private void ApplyDeterministicRotation()
	{
		if ( RotationAxis.LengthSquared <= 0.0001f || RotationSpeed == 0.0f )
			return;

		LastLocalTimeUsed = Math.Max( 0.0, Time.NowDouble - _localStartTime );
		LastTimeUsed = UseAbsoluteTime ? RealTime.GlobalNow : LastLocalTimeUsed;
		LastCalculatedAngle = NormalizeDegrees( RotationSpeed * (LastTimeUsed + StartTimeOffset) + StartPhaseDegrees );

		var rotation = Rotation.FromAxis( RotationAxis.Normal, LastCalculatedAngle );

		if ( UseLocalSpace )
			GameObject.LocalRotation = BaseLocalRotation * rotation;
		else
			WorldRotation = BaseWorldRotation * rotation;

		RotationApplyCount++;
	}

	private static float NormalizeDegrees( double degrees )
	{
		var normalized = degrees % 360.0;

		if ( normalized < 0.0 )
			normalized += 360.0;

		return (float)normalized;
	}

	private void LogState( string reason, bool force = false )
	{
		if ( !LogNetworking )
			return;

		if ( !force && _timeSinceLog < 1.0f )
			return;

		_timeSinceLog = 0.0f;

		Log.Info( DescribeWorldObjectNetworking( reason ) );
	}

	public string DescribeWorldObjectNetworking( string reason = "snapshot" )
	{
		return $"DeathrunRotatingObstacle '{GameObject.Name}' {reason}. Path={GetObjectPath( GameObject )}, NetworkMode={GameObject.NetworkMode}, " +
			$"NetworkActive={GameObject.Network.Active}, IsHost={Networking.IsHost}, IsProxy={GameObject.Network.Active && GameObject.Network.IsProxy}, ComponentEnabled={Enabled}, " +
			$"AlwaysTransmit={GameObject.Network.AlwaysTransmit}, StartEnabled={StartEnabled}, DeterministicLocal={DeterministicLocal}, UseAbsoluteTime={UseAbsoluteTime}, " +
			$"StartTimeOffset={StartTimeOffset:0.###}, StartPhaseDegrees={StartPhaseDegrees:0.###}, BaseRotationMode={BaseRotationMode}, BaseRotationOverride={BaseRotationOverride}, " +
			$"UseLocalSpace={UseLocalSpace}, Axis={RotationAxis}, Speed={RotationSpeed:0.##}, TimeUsed={LastTimeUsed:0.###}, LocalTimeUsed={LastLocalTimeUsed:0.###}, " +
			$"CalculatedAngle={LastCalculatedAngle:0.###}, FixedUpdateCount={FixedUpdateCount}, RotationApplyCount={RotationApplyCount}, " +
			$"LastUpdateWasHost={LastUpdateWasHost}, LastUpdateWasClient={LastUpdateWasClient}, BaseLocalRotation={BaseLocalRotation}, " +
			$"BaseWorldRotation={BaseWorldRotation}, CurrentLocalRotation={GameObject.LocalRotation}, CurrentWorldRotation={WorldRotation}.";
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
