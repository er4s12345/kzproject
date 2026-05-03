using System;
using System.Collections.Generic;
using System.Linq;

[Title( "Deathrun Networked Transform Driver" )]
[Category( "Deathrun" )]
[Icon( "sync" )]
public sealed class DeathrunNetworkedTransformDriver : Component
{
	[Property] public bool DrivePosition { get; set; } = false;
	[Property] public bool DriveRotation { get; set; } = true;
	[Property] public Vector3 RotateAxis { get; set; } = Vector3.Up;
	[Property] public float RotationSpeed { get; set; } = 90.0f;
	[Property] public Vector3 MoveOffset { get; set; } = Vector3.Zero;
	[Property] public float MoveSpeed { get; set; } = 1.0f;
	[Property] public bool UseLocalSpace { get; set; } = true;
	[Property] public float StartTimeOffset { get; set; } = 0.0f;
	[Property] public bool AlwaysTransmitWhenNetworked { get; set; } = true;
	[Property] public bool AllowClientFallbackWhenUnnetworked { get; set; } = true;
	[Property] public bool LogNetworking { get; set; } = false;

	[Sync( SyncFlags.FromHost )] private Vector3 NetworkedPosition { get; set; }
	[Sync( SyncFlags.FromHost )] private Rotation NetworkedRotation { get; set; } = Rotation.Identity;
	[Sync( SyncFlags.FromHost )] private bool HasNetworkedTransform { get; set; }

	private Vector3 _baseLocalPosition;
	private Rotation _baseLocalRotation;
	private Vector3 _baseWorldPosition;
	private Rotation _baseWorldRotation;
	private float _startTime;
	private bool _initialized;
	private TimeSince _timeSinceNetworkLog;

	protected override void OnAwake()
	{
		Initialize();
		EnsureNetworkSettings();
	}

	protected override void OnStart()
	{
		Initialize();
		EnsureNetworkSettings();
		PublishNetworkedTransform();
		LogNetworkState( "started", true );
	}

	protected override void OnFixedUpdate()
	{
		Initialize();
		EnsureNetworkSettings();

		if ( !CanDriveTransform() )
		{
			ApplyNetworkedTransform();
			LogNetworkState( "proxy/client applying replicated transform" );
			return;
		}

		ApplyDrivenTransform();
		PublishNetworkedTransform();
		LogNetworkState( "authoritative update" );
	}

	private void Initialize()
	{
		if ( _initialized )
			return;

		_baseLocalPosition = GameObject.LocalPosition;
		_baseLocalRotation = GameObject.LocalRotation;
		_baseWorldPosition = WorldPosition;
		_baseWorldRotation = WorldRotation;
		_startTime = Time.Now + StartTimeOffset;
		_initialized = true;
	}

	private void EnsureNetworkSettings()
	{
		GameObject.NetworkMode = NetworkMode.Object;
		GameObject.Network.AlwaysTransmit = AlwaysTransmitWhenNetworked;
		GameObject.Network.Interpolation = true;
		GameObject.Network.Flags &= ~NetworkFlags.NoTransformSync;
	}

	private bool CanDriveTransform()
	{
		if ( !Networking.IsActive )
			return true;

		if ( !GameObject.Network.Active )
			return Networking.IsHost || AllowClientFallbackWhenUnnetworked;

		return !GameObject.Network.IsProxy;
	}

	private void ApplyDrivenTransform()
	{
		var elapsed = MathF.Max( 0.0f, Time.Now - _startTime );
		var positionDelta = GetPositionDelta( elapsed );
		var rotationDelta = GetRotationDelta( elapsed );

		if ( UseLocalSpace )
		{
			if ( DrivePosition )
				GameObject.LocalPosition = _baseLocalPosition + positionDelta;

			if ( DriveRotation )
				GameObject.LocalRotation = _baseLocalRotation * rotationDelta;

			return;
		}

		if ( DrivePosition )
			WorldPosition = _baseWorldPosition + positionDelta;

		if ( DriveRotation )
			WorldRotation = _baseWorldRotation * rotationDelta;
	}

	private Vector3 GetPositionDelta( float elapsed )
	{
		if ( !DrivePosition || MoveOffset.LengthSquared <= 0.0001f || MoveSpeed <= 0.0f )
			return Vector3.Zero;

		var phase = MathF.Sin( elapsed * MoveSpeed * MathF.PI * 2.0f ) * 0.5f + 0.5f;
		return MoveOffset * phase;
	}

	private Rotation GetRotationDelta( float elapsed )
	{
		if ( !DriveRotation || RotateAxis.LengthSquared <= 0.0001f || RotationSpeed == 0.0f )
			return Rotation.Identity;

		return Rotation.FromAxis( RotateAxis.Normal, RotationSpeed * elapsed );
	}

	private void PublishNetworkedTransform()
	{
		if ( Networking.IsActive && GameObject.Network.Active && GameObject.Network.IsProxy )
			return;

		NetworkedPosition = UseLocalSpace ? GameObject.LocalPosition : WorldPosition;
		NetworkedRotation = UseLocalSpace ? GameObject.LocalRotation : WorldRotation;
		HasNetworkedTransform = true;
	}

	private void ApplyNetworkedTransform()
	{
		if ( !HasNetworkedTransform )
		{
			if ( AllowClientFallbackWhenUnnetworked )
				ApplyDrivenTransform();

			return;
		}

		if ( UseLocalSpace )
		{
			if ( DrivePosition )
				GameObject.LocalPosition = NetworkedPosition;

			if ( DriveRotation )
				GameObject.LocalRotation = NetworkedRotation;

			return;
		}

		if ( DrivePosition )
			WorldPosition = NetworkedPosition;

		if ( DriveRotation )
			WorldRotation = NetworkedRotation;
	}

	private void LogNetworkState( string reason, bool force = false )
	{
		if ( !LogNetworking )
			return;

		if ( !force && _timeSinceNetworkLog < 1.0f )
			return;

		_timeSinceNetworkLog = 0.0f;

		Log.Info(
			$"NetworkedTransformDriver '{GameObject.Name}' {reason}. NetworkMode={GameObject.NetworkMode}, NetworkActive={GameObject.Network.Active}, " +
			$"IsHost={Networking.IsHost}, IsProxy={GameObject.Network.Active && GameObject.Network.IsProxy}, CanDriveTransform={CanDriveTransform()}, " +
			$"AlwaysTransmit={GameObject.Network.AlwaysTransmit}, Interpolation={GameObject.Network.Interpolation}, Position={(UseLocalSpace ? GameObject.LocalPosition : WorldPosition)}, " +
			$"Rotation={(UseLocalSpace ? GameObject.LocalRotation : WorldRotation)}, HasNetworkedTransform={HasNetworkedTransform}, UnsyncedFallback={!HasNetworkedTransform && AllowClientFallbackWhenUnnetworked}." );
	}

	public string DescribeWorldObjectNetworking()
	{
		return $"NetworkedTransformDriver object='{GameObject.Name}', path='{GetObjectPath( GameObject )}', sourceType={GetSourceType( GameObject )}, " +
			$"NetworkMode={GameObject.NetworkMode}, NetworkActive={GameObject.Network.Active}, Owner={DescribeOwner( GameObject )}, IsProxy={GameObject.Network.Active && GameObject.Network.IsProxy}, IsHost={Networking.IsHost}, " +
			$"AlwaysTransmit={GameObject.Network.AlwaysTransmit}, Interpolation={GameObject.Network.Interpolation}, Flags={GameObject.Network.Flags}, CanDriveTransform={CanDriveTransform()}, " +
			$"DrivePosition={DrivePosition}, DriveRotation={DriveRotation}, UseLocalSpace={UseLocalSpace}, MoveOffset={MoveOffset}, MoveSpeed={MoveSpeed:0.##}, RotateAxis={RotateAxis}, RotationSpeed={RotationSpeed:0.##}, " +
			$"Position={(UseLocalSpace ? GameObject.LocalPosition : WorldPosition)}, Rotation={(UseLocalSpace ? GameObject.LocalRotation : WorldRotation)}, " +
			$"HasNetworkedTransform={HasNetworkedTransform}, UnsyncedFallback={!HasNetworkedTransform && AllowClientFallbackWhenUnnetworked}.";
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
