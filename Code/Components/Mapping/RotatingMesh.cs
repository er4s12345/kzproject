using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Sandbox.Components.Mapping
{
	public enum RotatingMeshState
	{
		IDLE,
		ACCELERATING,
		DECCELERATING,
		FULLSPEED
	}

	public enum RotatingMeshUpdateMode
	{
		NetworkedHostState,
		NetworkedProxyState,
		DeterministicLocalMapFallback
	}


	public class RotatingMesh : Component, Component.IPressable
	{
		[Property, ReadOnly]
		[Sync(SyncFlags.FromHost), Change("OnStateChanged")] protected RotatingMeshState State { get; set; } = RotatingMeshState.IDLE;

		[Property, ReadOnly]
		[Sync( SyncFlags.FromHost )] protected RotatingMeshState PreviousState { get; set; } = RotatingMeshState.IDLE;

		[Property, ReadOnly]
		[Sync(SyncFlags.FromHost)] protected float CurrentRotationSpeed { get; set; } = 0f;

		[Property]
		[Description( "If true, can be enabled/disabled/toggled by player use" )]
		protected bool AllowPlayerUse { get; set; } = false;

		[Property]
		protected float RotationSpeed { get; set; } = 90f;
		[Property]
		protected Vector3 RotationAxis { get; set; } = Vector3.Up;
		[Property]
		protected bool StartEnabled { get; set; } = false;
		[Property]
		protected bool Accelerate { get; set; } = false;
		[Property]
		[ShowIf( nameof( Accelerate ), true )]
		protected float AccelerateSpeed { get; set; } = 25.0f;

		[Property]
		[ShowIf( nameof( Accelerate ), true )]
		protected float DeccelerateSpeed { get; set; } = 25.0f;

		[Property, Group( "Sounds" )]
		[Description( "RotatingSound is looped sound when mesh is rotating" )]
		protected SoundEvent StartMovingSound, StopMovingSound, RotatingSound;

		[Property, Group( "Sounds" )]
		protected bool UsePitchForRotationSound = false;

		[Property, Group( "Sounds" ), ShowIf( nameof( UsePitchForRotationSound ), true )]
		Vector2 PitchRange { get; set; } = new Vector2(0.75f, 1.25f);

		[Property, Group( "Networking" )]
		[Description( "Host drives the rotation for networked objects; clients apply replicated transform/state." )]
		protected bool HostAuthoritative { get; set; } = true;

		[Property, Group( "Networking" )]
		[Description( "Dynamic hazards should keep transmitting even when visibility culling is uncertain." )]
		protected bool AlwaysTransmitWhenNetworked { get; set; } = true;

		[Property, Group( "Networking" )]
		[Description( "Allow local deterministic motion if this component is loaded without an active network object." )]
		protected bool AllowClientFallbackWhenUnnetworked { get; set; } = true;

		[Property, Group( "Networking" )]
		protected bool LogNetworking { get; set; } = false;

		[Property, Group( "Scene Mirror" )]
		[Description( "Optional legacy cleanup for host-only Hammer mesh visuals/collision. Off by default because map HammerMesh can be unsafe to disable during RotatingMesh.Start." )]
		protected bool DisableHammerMeshWhenUnnetworked { get; set; } = false;

		protected SoundHandle _rotatingSoundHandle;


		[Property, Sync( SyncFlags.FromHost )] protected TimeSince _lastTimeStateChanged { get; set; } = 999.9f;
		[Sync( SyncFlags.FromHost )] protected Rotation StateStartLocalRotation { get; set; } = Rotation.Identity;
		[Sync( SyncFlags.FromHost )] protected float StateStartTime { get; set; }
		[Sync( SyncFlags.FromHost )] protected float StateStartSpeed { get; set; }
		[Sync( SyncFlags.FromHost )] protected bool HasAuthoritativeState { get; set; }

		private Rotation _initialLocalRotation;
		private bool _initialTransformCached;
		private bool _localDeterministicInitialized;
		private Rotation _localDeterministicStartRotation;
		private float _localDeterministicStartTime;
		private float _localDeterministicStartSpeed;
		private float _localDeterministicCurrentSpeed;
		private RotatingMeshState _localDeterministicState = RotatingMeshState.IDLE;
		private TimeSince _timeSinceNetworkLog;

		protected override void OnAwake()
		{
			if ( !IsValid || !GameObject.IsValid() )
				return;

			CacheInitialTransform();
			EnsureNetworkSettings();
		}

		private void EnsureNetworkSettings()
		{
			if ( !IsValid || !GameObject.IsValid() )
				return;

			GameObject.NetworkMode = NetworkMode.Object;
			GameObject.Network.AlwaysTransmit = AlwaysTransmitWhenNetworked;
			GameObject.Network.Interpolation = true;
			GameObject.Network.Flags &= ~NetworkFlags.NoTransformSync;
		}

		protected override void OnStart()
		{
			try
			{
				if ( !IsValid || !GameObject.IsValid() )
					return;

				CacheInitialTransform();
				EnsureNetworkSettings();

				var updateMode = GetUpdateMode();
				LogStartDiagnostics( updateMode );

				if ( updateMode == RotatingMeshUpdateMode.DeterministicLocalMapFallback )
				{
					DisableOriginalHammerMeshIfNeeded();
					InitializeLocalDeterministicState();
				}
				else if ( StartEnabled && State == RotatingMeshState.IDLE && updateMode == RotatingMeshUpdateMode.NetworkedHostState )
				{
					SetState( Accelerate ? RotatingMeshState.ACCELERATING : RotatingMeshState.FULLSPEED );
				}

				ApplyRotationForMode( updateMode );
				LogNetworkState( "started", true );
			}
			catch ( Exception exception )
			{
				Log.Warning( $"RotatingMesh '{GetSafeObjectName()}' failed during OnStart. Disabling this legacy map rotator; scene-side DeathrunRotatingObstacle mirrors should provide multiplayer-visible movement. Exception: {exception}" );
				Enabled = false;
			}
		}

		public bool Press( IPressable.Event e )
		{
			if(!AllowPlayerUse)
				return false;

			Activate ( e.Source.GameObject );
			return true;
		}


		[Rpc.Host]
		public void Activate( GameObject Activator )
		{
			if ( !IsValid || !GameObject.IsValid() )
				return;

			if ( HostAuthoritative && Networking.IsActive && IsNetworkActive() && GameObject.Network.IsProxy )
				return;

			Log.Info( $"RotatingMesh activated by {(Activator.IsValid() ? Activator.Name : "unknown")}" );

			if ( GetUpdateMode() == RotatingMeshUpdateMode.DeterministicLocalMapFallback )
			{
				ToggleLocalDeterministicState();
				Log.Warning( $"RotatingMesh '{GameObject.Name}' changed state locally while NetworkActive=false. This visual state change will not replicate; convert this map entity to a networked scene object if runtime toggles must be multiplayer-synced." );
				return;
			}

			switch ( State )
			{
				case RotatingMeshState.IDLE:
					SetState( Accelerate ? RotatingMeshState.ACCELERATING : RotatingMeshState.FULLSPEED );
					break;
				case RotatingMeshState.ACCELERATING:
					SetState( RotatingMeshState.DECCELERATING );
					break;
				case RotatingMeshState.DECCELERATING:
					SetState( RotatingMeshState.ACCELERATING );
					break;
				case RotatingMeshState.FULLSPEED:
					SetState( Accelerate ? RotatingMeshState.DECCELERATING : RotatingMeshState.IDLE );
					break;
			}
		}

		protected override void OnFixedUpdate()
		{
			if ( !IsValid || !GameObject.IsValid() )
				return;

			CacheInitialTransform();
			EnsureNetworkSettings();

			var updateMode = GetUpdateMode();

			if ( updateMode == RotatingMeshUpdateMode.NetworkedHostState )
				UpdateAuthoritativeState();

			ApplyRotationForMode( updateMode );

			if ( UsePitchForRotationSound )
				HandleSoundPitch();

			LogNetworkState( $"{updateMode} update" );
		}

		public void SetState( RotatingMeshState newState )
		{
			if ( !IsValid || !GameObject.IsValid() )
				return;

			if ( State == newState )
				return;

			PreviousState = State;
			StateStartLocalRotation = GameObject.LocalRotation;
			StateStartTime = Time.Now;
			StateStartSpeed = CurrentRotationSpeed;
			HasAuthoritativeState = true;
			State = newState;
			_lastTimeStateChanged = Time.Now;
		}

		private void CacheInitialTransform()
		{
			if ( !IsValid || !GameObject.IsValid() )
				return;

			if ( _initialTransformCached )
				return;

			_initialLocalRotation = GameObject.LocalRotation;
			StateStartLocalRotation = _initialLocalRotation;
			StateStartTime = Time.Now;
			_initialTransformCached = true;
		}

		private bool CanDriveTransform()
		{
			return GetUpdateMode() != RotatingMeshUpdateMode.NetworkedProxyState;
		}

		public RotatingMeshUpdateMode GetUpdateMode()
		{
			if ( !Networking.IsActive )
				return RotatingMeshUpdateMode.DeterministicLocalMapFallback;

			if ( !IsValid || !GameObject.IsValid() || !IsNetworkActive() )
				return RotatingMeshUpdateMode.DeterministicLocalMapFallback;

			if ( !HostAuthoritative )
				return RotatingMeshUpdateMode.NetworkedHostState;

			return GameObject.Network.IsProxy ? RotatingMeshUpdateMode.NetworkedProxyState : RotatingMeshUpdateMode.NetworkedHostState;
		}

		private void ApplyRotationForMode( RotatingMeshUpdateMode updateMode )
		{
			if ( !IsValid || !GameObject.IsValid() )
				return;

			if ( updateMode == RotatingMeshUpdateMode.DeterministicLocalMapFallback )
			{
				DisableOriginalHammerMeshIfNeeded();
				UpdateLocalDeterministicState();
				ApplyLocalDeterministicRotation();
				return;
			}

			ApplyNetworkedDeterministicRotation();
		}

		private void InitializeLocalDeterministicState()
		{
			if ( _localDeterministicInitialized )
				return;

			_localDeterministicInitialized = true;
			_localDeterministicState = StartEnabled ? (Accelerate ? RotatingMeshState.ACCELERATING : RotatingMeshState.FULLSPEED) : RotatingMeshState.IDLE;
			_localDeterministicStartRotation = _initialLocalRotation;
			_localDeterministicStartTime = 0.0f;
			_localDeterministicStartSpeed = 0.0f;
			_localDeterministicCurrentSpeed = _localDeterministicState == RotatingMeshState.FULLSPEED ? RotationSpeed : 0.0f;
		}

		private void SetLocalDeterministicState( RotatingMeshState newState )
		{
			if ( _localDeterministicState == newState )
				return;

			ApplyLocalDeterministicRotation();

			_localDeterministicStartRotation = GameObject.LocalRotation;
			_localDeterministicStartTime = Time.Now;
			_localDeterministicStartSpeed = _localDeterministicCurrentSpeed;
			_localDeterministicState = newState;
		}

		private void ToggleLocalDeterministicState()
		{
			switch ( _localDeterministicState )
			{
				case RotatingMeshState.IDLE:
					SetLocalDeterministicState( Accelerate ? RotatingMeshState.ACCELERATING : RotatingMeshState.FULLSPEED );
					break;
				case RotatingMeshState.ACCELERATING:
					SetLocalDeterministicState( RotatingMeshState.DECCELERATING );
					break;
				case RotatingMeshState.DECCELERATING:
					SetLocalDeterministicState( RotatingMeshState.ACCELERATING );
					break;
				case RotatingMeshState.FULLSPEED:
					SetLocalDeterministicState( Accelerate ? RotatingMeshState.DECCELERATING : RotatingMeshState.IDLE );
					break;
			}
		}

		private void UpdateLocalDeterministicState()
		{
			InitializeLocalDeterministicState();

			if ( _localDeterministicState == RotatingMeshState.IDLE )
				return;

			var elapsed = MathF.Max( 0.0f, Time.Now - _localDeterministicStartTime );

			switch ( _localDeterministicState )
			{
				case RotatingMeshState.FULLSPEED:
					_localDeterministicCurrentSpeed = RotationSpeed;
					break;
				case RotatingMeshState.ACCELERATING:
					_localDeterministicCurrentSpeed = Math.Clamp( _localDeterministicStartSpeed + AccelerateSpeed * elapsed, 0, RotationSpeed );

					if ( _localDeterministicCurrentSpeed >= RotationSpeed )
						SetLocalDeterministicState( RotatingMeshState.FULLSPEED );

					break;
				case RotatingMeshState.DECCELERATING:
					_localDeterministicCurrentSpeed = Math.Clamp( _localDeterministicStartSpeed - DeccelerateSpeed * elapsed, 0, RotationSpeed );

					if ( _localDeterministicCurrentSpeed <= 0 )
						SetLocalDeterministicState( RotatingMeshState.IDLE );

					break;
			}
		}

		private void ApplyLocalDeterministicRotation()
		{
			InitializeLocalDeterministicState();

			if ( _localDeterministicState == RotatingMeshState.IDLE )
				return;

			ApplyRotationFromState( _localDeterministicState, _localDeterministicStartRotation, _localDeterministicStartTime, _localDeterministicStartSpeed );
		}

		private void UpdateAuthoritativeState()
		{
			if ( State == RotatingMeshState.IDLE )
				return;

			if ( !HasAuthoritativeState )
			{
				StateStartLocalRotation = GameObject.LocalRotation;
				StateStartTime = Time.Now;
				StateStartSpeed = CurrentRotationSpeed;
				HasAuthoritativeState = true;
			}

			var elapsed = MathF.Max( 0.0f, Time.Now - StateStartTime );

			switch ( State )
			{
				case RotatingMeshState.FULLSPEED:
					CurrentRotationSpeed = RotationSpeed;
					break;
				case RotatingMeshState.ACCELERATING:
					CurrentRotationSpeed = Math.Clamp( StateStartSpeed + AccelerateSpeed * elapsed, 0, RotationSpeed );

					if ( CurrentRotationSpeed >= RotationSpeed )
						SetState( RotatingMeshState.FULLSPEED );

					break;
				case RotatingMeshState.DECCELERATING:
					CurrentRotationSpeed = Math.Clamp( StateStartSpeed - DeccelerateSpeed * elapsed, 0, RotationSpeed );

					if ( CurrentRotationSpeed <= 0 )
						SetState( RotatingMeshState.IDLE );

					break;
			}

			if ( Accelerate && State != RotatingMeshState.IDLE && CurrentRotationSpeed <= 0 )
				SetState( RotatingMeshState.IDLE );
		}

		private void ApplyNetworkedDeterministicRotation()
		{
			if ( !HasAuthoritativeState )
				return;

			ApplyRotationFromState( State, StateStartLocalRotation, StateStartTime, StateStartSpeed );
		}

		private void ApplyRotationFromState( RotatingMeshState state, Rotation startRotation, float startTime, float startSpeed )
		{
			if ( !IsValid || !GameObject.IsValid() )
				return;

			if ( state == RotatingMeshState.IDLE )
				return;

			var elapsed = MathF.Max( 0.0f, Time.Now - startTime );
			var angle = CalculateAngleDelta( state, elapsed, startSpeed ) % 360.0f;

			if ( RotationAxis.LengthSquared <= 0.0001f )
				return;

			GameObject.LocalRotation = startRotation * Rotation.FromAxis( RotationAxis.Normal, angle );
		}

		private void DisableOriginalHammerMeshIfNeeded()
		{
			if ( !IsValid || !GameObject.IsValid() )
				return;

			if ( !DisableHammerMeshWhenUnnetworked || IsNetworkActive() )
				return;

			var hammerMesh = FindHammerMeshComponent();

			if ( !hammerMesh.IsValid() )
			{
				if ( LogNetworking )
					Log.Warning( $"RotatingMesh '{GetSafeObjectName()}' requested legacy HammerMesh disabling, but no valid HammerMesh component was found." );

				return;
			}

			try
			{
				hammerMesh.Enabled = false;

				if ( LogNetworking )
					Log.Info( $"RotatingMesh '{GetSafeObjectName()}' disabled legacy HammerMesh visual/collision on inactive map rotator." );
			}
			catch ( Exception exception )
			{
				Log.Warning( $"RotatingMesh '{GetSafeObjectName()}' could not disable legacy HammerMesh safely. Leaving HammerMesh enabled and disabling RotatingMesh behavior instead. Exception: {exception}" );
				Enabled = false;
			}
		}

		private Component FindHammerMeshComponent()
		{
			if ( !IsValid || !GameObject.IsValid() )
				return null;

			try
			{
				foreach ( var component in Components.GetAll().ToArray() )
				{
					if ( !component.IsValid() )
						continue;

					if ( component.GetType().FullName == "Sandbox.HammerMesh" )
						return component;
				}
			}
			catch ( Exception exception )
			{
				if ( LogNetworking )
					Log.Warning( $"RotatingMesh '{GetSafeObjectName()}' could not inspect HammerMesh components safely. Exception: {exception}" );
			}

			return null;
		}

		private bool HasHammerMeshComponent()
		{
			return FindHammerMeshComponent().IsValid();
		}

		private bool IsNetworkActive()
		{
			return IsValid && GameObject.IsValid() && GameObject.Network.Active;
		}

		private bool IsNetworkProxy()
		{
			return IsNetworkActive() && GameObject.Network.IsProxy;
		}

		private bool GetSafeAlwaysTransmit()
		{
			return GameObject.IsValid() && GameObject.Network.AlwaysTransmit;
		}

		private bool GetSafeNetworkInterpolation()
		{
			return GameObject.IsValid() && GameObject.Network.Interpolation;
		}

		private NetworkFlags GetSafeNetworkFlags()
		{
			return GameObject.IsValid() ? GameObject.Network.Flags : default;
		}

		private Vector3 GetSafeLocalPosition()
		{
			return GameObject.IsValid() ? GameObject.LocalPosition : Vector3.Zero;
		}

		private Rotation GetSafeLocalRotation()
		{
			return GameObject.IsValid() ? GameObject.LocalRotation : Rotation.Identity;
		}

		private Vector3 GetSafeWorldPosition()
		{
			return GameObject.IsValid() ? WorldPosition : Vector3.Zero;
		}

		private Rotation GetSafeWorldRotation()
		{
			return GameObject.IsValid() ? WorldRotation : Rotation.Identity;
		}

		private string GetSafeObjectName()
		{
			return GameObject.IsValid() ? GameObject.Name : "invalid";
		}

		private void LogStartDiagnostics( RotatingMeshUpdateMode updateMode )
		{
			if ( !LogNetworking )
				return;

			Log.Info(
				$"RotatingMesh '{GetSafeObjectName()}' start diagnostics. NetworkMode={(GameObject.IsValid() ? GameObject.NetworkMode : default)}, " +
				$"NetworkActive={IsNetworkActive()}, IsHost={Networking.IsHost}, IsProxy={IsNetworkProxy()}, SourceType={GetSourceType()}, " +
				$"HammerMeshFound={HasHammerMeshComponent()}, DisableHammerMeshWhenUnnetworked={DisableHammerMeshWhenUnnetworked}, Mode={updateMode}." );
		}

		private float CalculateAngleDelta( RotatingMeshState state, float elapsed, float startSpeed )
		{
			return state switch
			{
				RotatingMeshState.FULLSPEED => RotationSpeed * elapsed,
				RotatingMeshState.ACCELERATING => CalculateAcceleratingAngle( elapsed, startSpeed ),
				RotatingMeshState.DECCELERATING => CalculateDeceleratingAngle( elapsed, startSpeed ),
				_ => 0.0f
			};
		}

		private float CalculateAcceleratingAngle( float elapsed, float startSpeed )
		{
			if ( AccelerateSpeed <= 0.0f )
				return RotationSpeed * elapsed;

			var timeToFullSpeed = MathF.Max( 0.0f, (RotationSpeed - startSpeed) / AccelerateSpeed );
			var acceleratingTime = MathF.Min( elapsed, timeToFullSpeed );
			var fullSpeedTime = MathF.Max( 0.0f, elapsed - acceleratingTime );

			return startSpeed * acceleratingTime + 0.5f * AccelerateSpeed * acceleratingTime * acceleratingTime + RotationSpeed * fullSpeedTime;
		}

		private float CalculateDeceleratingAngle( float elapsed, float startSpeed )
		{
			if ( DeccelerateSpeed <= 0.0f )
				return 0.0f;

			var timeToStop = MathF.Max( 0.0f, startSpeed / DeccelerateSpeed );
			var deceleratingTime = MathF.Min( elapsed, timeToStop );

			return startSpeed * deceleratingTime - 0.5f * DeccelerateSpeed * deceleratingTime * deceleratingTime;
		}

		private void LogNetworkState( string reason, bool force = false )
		{
			if ( !LogNetworking )
				return;

			if ( !force && _timeSinceNetworkLog < 1.0f )
				return;

			_timeSinceNetworkLog = 0.0f;
			var updateMode = GetUpdateMode();

			Log.Info(
				$"RotatingMesh '{GetSafeObjectName()}' {reason}. NetworkMode={(GameObject.IsValid() ? GameObject.NetworkMode : default)}, NetworkActive={IsNetworkActive()}, " +
				$"IsHost={Networking.IsHost}, IsProxy={IsNetworkProxy()}, Mode={updateMode}, CanDriveTransform={CanDriveTransform()}, " +
				$"AlwaysTransmit={GetSafeAlwaysTransmit()}, Interpolation={GetSafeNetworkInterpolation()}, State={GetEffectiveState()}, Speed={GetEffectiveRotationSpeed():0.##}, " +
				$"InitialLocalRotation={_initialLocalRotation}, Axis={RotationAxis}, RotationSpeed={RotationSpeed:0.##}, Elapsed={GetEffectiveElapsedTime():0.###}, " +
				$"LocalPosition={GetSafeLocalPosition()}, LocalRotation={GetSafeLocalRotation()}, HasAuthoritativeState={HasAuthoritativeState}, " +
				$"StateStartTime={StateStartTime:0.###}, LocalDeterministicInitialized={_localDeterministicInitialized}, SourceType={GetSourceType()}, Path={GetObjectPath()}." );
		}


		protected void DoAcceleration()
		{
			CurrentRotationSpeed = Math.Clamp( CurrentRotationSpeed + AccelerateSpeed * Time.Delta, 0, RotationSpeed );
		}

		protected void DoDecceleration()
		{
			CurrentRotationSpeed = Math.Clamp( CurrentRotationSpeed - DeccelerateSpeed * Time.Delta, 0, RotationSpeed );
		}


		protected void OnStateChanged( RotatingMeshState oldState, RotatingMeshState newState )
		{
			Log.Info( $"RotatingMesh state changed from {oldState} to {newState}" );
			PlayStateSound( newState );
		}

		protected void PlayStateSound( RotatingMeshState state )
		{
			switch ( state )
			{
				case RotatingMeshState.ACCELERATING:
					if ( StartMovingSound.IsValid() )
						GameObject.PlaySound( StartMovingSound );
						_rotatingSoundHandle = GameObject.PlaySound( RotatingSound );
					break;

				case RotatingMeshState.IDLE:
					if( PreviousState == RotatingMeshState.DECCELERATING || PreviousState == RotatingMeshState.FULLSPEED )
					{
						if ( StopMovingSound.IsValid() )
							GameObject.PlaySound( StopMovingSound );

						if ( _rotatingSoundHandle.IsValid() && _rotatingSoundHandle.IsPlaying )
							_rotatingSoundHandle.Stop();
						
					}
					break;

					case RotatingMeshState.FULLSPEED:
						if ( RotatingSound.IsValid() && !_rotatingSoundHandle.IsValid() )
						{
							_rotatingSoundHandle = GameObject.PlaySound( RotatingSound);
						}
						break ;
			}
		}

		protected void HandleSoundPitch()
		{
			if( !_rotatingSoundHandle.IsValid() || !_rotatingSoundHandle.IsPlaying )
				return;

			float t = RotationSpeed > 0 ? CurrentRotationSpeed / RotationSpeed : 0;
			float pitch = float.Lerp( PitchRange.x, PitchRange.y, t );
			_rotatingSoundHandle.Pitch = pitch;
			Log.Info( $"Setting rotating sound pitch to {pitch} based on rotation speed {CurrentRotationSpeed}" );
		}

		public string DescribeWorldObjectNetworking()
		{
			var updateMode = GetUpdateMode();

			return $"RotatingMesh object='{GetSafeObjectName()}', path='{GetObjectPath()}', sourceType={GetSourceType()}, " +
				$"NetworkMode={(GameObject.IsValid() ? GameObject.NetworkMode : default)}, NetworkActive={IsNetworkActive()}, Owner={DescribeOwner()}, IsProxy={IsNetworkProxy()}, IsHost={Networking.IsHost}, " +
				$"AlwaysTransmit={GetSafeAlwaysTransmit()}, Interpolation={GetSafeNetworkInterpolation()}, Flags={GetSafeNetworkFlags()}, Mode={updateMode}, " +
				$"CanDriveTransform={CanDriveTransform()}, State={GetEffectiveState()}, Speed={GetEffectiveRotationSpeed():0.##}, StartEnabled={StartEnabled}, Axis={RotationAxis}, RotationSpeed={RotationSpeed:0.##}, " +
				$"InitialLocalRotation={_initialLocalRotation}, Elapsed={GetEffectiveElapsedTime():0.###}, " +
				$"Position={GetSafeWorldPosition()}, LocalPosition={GetSafeLocalPosition()}, Rotation={GetSafeWorldRotation()}, LocalRotation={GetSafeLocalRotation()}, " +
				$"HasAuthoritativeState={HasAuthoritativeState}, LocalDeterministicInitialized={_localDeterministicInitialized}.";
		}

		private RotatingMeshState GetEffectiveState()
		{
			return GetUpdateMode() == RotatingMeshUpdateMode.DeterministicLocalMapFallback ? _localDeterministicState : State;
		}

		private float GetEffectiveRotationSpeed()
		{
			return GetUpdateMode() == RotatingMeshUpdateMode.DeterministicLocalMapFallback ? _localDeterministicCurrentSpeed : CurrentRotationSpeed;
		}

		private float GetEffectiveElapsedTime()
		{
			return GetUpdateMode() == RotatingMeshUpdateMode.DeterministicLocalMapFallback
				? MathF.Max( 0.0f, Time.Now - _localDeterministicStartTime )
				: MathF.Max( 0.0f, Time.Now - StateStartTime );
		}

		private string GetSourceType()
		{
			try
			{
				if ( IsValid && GameObject.IsValid() && Components.GetAll().Any( x => x.IsValid() && x.GetType().FullName?.Contains( "Hammer", StringComparison.OrdinalIgnoreCase ) == true ) )
					return "Hammer/map entity";

				if ( GameObject.IsValid() && HasAncestorComponentNamed( GameObject, "Sandbox.MapInstance" ) )
					return "map/map-instance hierarchy";
			}
			catch ( Exception exception )
			{
				if ( LogNetworking )
					Log.Warning( $"RotatingMesh '{GetSafeObjectName()}' could not determine source type safely. Exception: {exception}" );
			}

			return "scene GameObject";
		}

		private static bool HasAncestorComponentNamed( GameObject gameObject, string typeName )
		{
			var current = gameObject;

			while ( current.IsValid() )
			{
				try
				{
					if ( current.Components.GetAll().Any( x => x.IsValid() && x.GetType().FullName == typeName ) )
						return true;
				}
				catch
				{
					return false;
				}

				current = current.Parent;
			}

			return false;
		}

		private string DescribeOwner()
		{
			if ( !GameObject.IsValid() )
				return "invalid";

			var owner = GameObject.Network.Owner;
			return owner is null ? $"none ({GameObject.Network.OwnerId})" : $"{owner.DisplayName} ({owner.Id})";
		}

		private string GetObjectPath()
		{
			if ( !GameObject.IsValid() )
				return "invalid";

			var names = new List<string>();
			var current = GameObject;

			while ( current.IsValid() )
			{
				names.Add( current.Name );
				current = current.Parent;
			}

			names.Reverse();
			return string.Join( "/", names );
		}

	}
}
