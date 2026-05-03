using System;
using System.Collections.Generic;
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

	/**
	 * Component for rotating meshes, with optional acceleration and player interaction. Should be used for gameplay thing, not cosmetics ones.
	 * NOTE: You have to MANUALLY SWITCH the network mode of the GameObject to NetworkMode.Object for this component to work properly. IF NOT it will be not visible in game for CLIENTS.
	 */
	public class RotatingMesh : Component, Component.IPressable
	{
		

		[Property, ReadOnly]
		[Category("Debug")]
		[Sync(SyncFlags.FromHost), Change("OnStateChanged")] protected RotatingMeshState State { get; set; } = RotatingMeshState.IDLE;

		[Property, ReadOnly]
		[Category( "Debug" )]
		[Sync( SyncFlags.FromHost )] protected RotatingMeshState PreviousState { get; set; } = RotatingMeshState.IDLE;

		[Property, ReadOnly]
		[Category( "Debug" )]
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


		// Debug only-editor properties
		[Property, Group( "Debug" )]
		private bool DebugLog { get; set; } = false;


		protected SoundHandle _rotatingSoundHandle;


		[Property] protected TimeSince _lastTimeStateChanged { get; set; } = 999.9f;

		protected override void OnValidate()
		{
			base.OnValidate();

			GameObject.NetworkMode = NetworkMode.Object;
		}

		protected override void OnStart()
		{
			if( Networking.IsActive && !Networking.IsHost )
				return;

			if( StartEnabled )
				Activate( GameObject );			
		}

		public bool Press( IPressable.Event e )
		{
			if(!AllowPlayerUse)
				return false;

			Activate ( e.Source.GameObject );
			return true;
		}


		[Rpc.Host]
		[Description("Works like a toggle")]
		public void Activate( GameObject Activator )
		{
			if(Activator.IsValid() && DebugLog) // Only check it here, Activator is not mandatory arg
				Log.Info( $"RotatingMesh({GameObject.Name}) activated by {Activator.Name}" );

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
			switch ( State )
			{
				case RotatingMeshState.FULLSPEED:
					Rotate( RotationSpeed );
					break;

				case RotatingMeshState.ACCELERATING:
					DoAcceleration();
					Rotate( CurrentRotationSpeed );
					if ( CurrentRotationSpeed >= RotationSpeed )
						SetState( RotatingMeshState.FULLSPEED );
					break;

				case RotatingMeshState.DECCELERATING:
					DoDecceleration();
					Rotate( CurrentRotationSpeed );
					if ( CurrentRotationSpeed <= 0 )
						SetState( RotatingMeshState.IDLE );
					break;
			}

			if ( Accelerate )
			{
				if ( State != RotatingMeshState.IDLE && CurrentRotationSpeed <= 0 )
					SetState( RotatingMeshState.IDLE );
			}

			if(UsePitchForRotationSound)
				HandleSoundPitch();
		}

		public void SetState( RotatingMeshState newState )
		{
			if ( Networking.IsActive && !Networking.IsHost )
			{
				if(DebugLog)
					Log.Warning( $"RotatingMesh ({GameObject.Name}): SetState called not on SERVER. Ignoring." );
				return;
			}

			if ( State == newState )
				return;

			PreviousState = State;
			State = newState;
			_lastTimeStateChanged = 0;

			if(DebugLog)
				Log.Info( $"[SERVER/HOST] RotatingMesh ({GameObject.Name}) state changed to {newState}" );
		}

		protected void Rotate( float speed )
		{
			GameObject.LocalRotation *= Rotation.FromAxis( RotationAxis, speed * Time.Delta );
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
			if(DebugLog)
				Log.Info( $"RotatingMesh ({GameObject.Name}) NETWORKED state changed from {oldState} to {newState}" );

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
		}

	}
}
