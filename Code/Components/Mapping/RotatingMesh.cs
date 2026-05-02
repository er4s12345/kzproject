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


		protected SoundHandle _rotatingSoundHandle;


		[Property, Sync( SyncFlags.FromHost )] protected TimeSince _lastTimeStateChanged { get; set; } = 999.9f;

		protected override void OnAwake()
		{
			// Change network state.
			GameObject.NetworkMode = NetworkMode.Object;
			GameObject.Network.AlwaysTransmit = false;
			GameObject.Network.Interpolation = true;
		}

		protected override void OnStart()
		{
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
		public void Activate( GameObject Activator )
		{
			Log.Info( $"RotatingMesh activated by {Activator.Name}" );

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
			if ( State == newState )
				return;

			PreviousState = State;
			State = newState;
			_lastTimeStateChanged = Time.Now;
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

	}
}
