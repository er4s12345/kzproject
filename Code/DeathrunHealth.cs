using System;
using System.Threading.Tasks;

[Title( "Deathrun Health" )]
[Category( "Deathrun" )]
[Icon( "favorite" )]
public sealed class DeathrunHealth : Component
{
	[Property] public float MaxHealth { get; set; } = 100.0f;

	// The player object is client-owned for movement, but health/death is decided by the host.
	// SyncFlags.FromHost makes these values readable on clients without letting the owner drive death state.
	[Sync( SyncFlags.FromHost ), Property] public float CurrentHealth { get; set; }
	[Sync( SyncFlags.FromHost ), Property] public bool IsDead { get; set; }

	[Property] public bool Invulnerable { get; set; } = false;
	[Property] public bool CanTakeDamage { get; set; } = true;
	[Property] public bool DieOnAnyDamage { get; set; } = false;
	[Property] public bool RespawnOnDeath { get; set; } = true;
	[Property] public float RespawnDelay { get; set; } = 5.0f;
	[Property] public bool LogDamage { get; set; } = false;
	[Property] public bool LogRespawnDebug { get; set; } = false;
	[Property] public bool DisableInputOnDeath { get; set; } = true;
	[Property] public bool FreezeBodyOnDeath { get; set; } = true;

	public bool IsAlive => !IsDead;

	private PlayerController _playerController;
	private Rigidbody _body;
	private DeathrunRagdollOnDeath _ragdollOnDeath;
	private bool _storedUseInputControls;
	private bool _storedUseLookControls;
	private bool _storedUseCameraControls;
	private bool _storedBodyMotionEnabled;
	private bool _hasStoredInputState;
	private bool _hasStoredBodyState;
	private bool _respawnQueued;
	private bool _respawnInProgress;
	private int _respawnGeneration;
	private Vector3 _initialSpawnPosition;
	private Rotation _initialSpawnRotation;
	private Vector3 _lastDeathPosition;

	protected override void OnStart()
	{
		_playerController = Components.Get<PlayerController>();
		_body = GetBody();
		_ragdollOnDeath = Components.Get<DeathrunRagdollOnDeath>();
		_initialSpawnPosition = WorldPosition;
		_initialSpawnRotation = WorldRotation;

		if ( Networking.IsHost || !Game.IsPlaying )
			ResetHealth();

		LogRespawnState( "started" );
	}

	public bool TakeDamage( DeathrunDamageInfo damageInfo )
	{
		if ( !Networking.IsHost )
		{
			if ( LogDamage )
				Log.Info( $"Ignoring {damageInfo.DamageType} damage on client for '{GameObject.Name}'. Damage is host-authoritative." );

			return false;
		}

		if ( !CanTakeDamage || Invulnerable || IsDead )
		{
			if ( LogDamage && IsDead )
				Log.Info( $"Ignoring {damageInfo.DamageType} damage for '{GameObject.Name}' because the player is already dead." );

			return false;
		}

		var amount = MathF.Max( 0.0f, damageInfo.Amount );

		if ( amount <= 0.0f && !damageInfo.IsLethal && !DieOnAnyDamage )
			return false;

		CurrentHealth = MathF.Max( CurrentHealth - amount, 0.0f );

		if ( LogDamage )
		{
			var sourceName = damageInfo.Source.IsValid() ? damageInfo.Source.Name : "none";
			Log.Info( $"'{GameObject.Name}' took {amount:0.##} {damageInfo.DamageType} damage from '{sourceName}'. Health={CurrentHealth:0.##}/{MaxHealth:0.##}. Reason='{damageInfo.Reason ?? "none"}'. InvalidatesRun={damageInfo.InvalidatesRun}." );
		}

		if ( CurrentHealth <= 0.0f || damageInfo.IsLethal || DieOnAnyDamage )
			Die( damageInfo );

		return true;
	}

	public void Heal( float amount )
	{
		if ( !Networking.IsHost )
			return;

		if ( amount <= 0.0f || IsDead )
			return;

		CurrentHealth = MathF.Min( CurrentHealth + amount, MaxHealth );

		if ( LogDamage )
			Log.Info( $"'{GameObject.Name}' healed {amount:0.##}. Health={CurrentHealth:0.##}/{MaxHealth:0.##}." );
	}

	public void ResetHealth()
	{
		if ( !Networking.IsHost && Game.IsPlaying )
			return;

		CurrentHealth = MaxHealth;
		IsDead = false;
		_respawnQueued = false;
		_respawnInProgress = false;
		SetMovementEnabled( true );
		CleanupDeathRagdoll();
		SetLiveBodyHiddenForClients( false );
		ResetRespawnSensitiveComponents();
		LogRespawnState( "health reset" );

		if ( LogDamage )
			Log.Info( $"'{GameObject.Name}' health reset to {CurrentHealth:0.##}/{MaxHealth:0.##}." );
	}

	public void Kill( string reason )
	{
		TakeDamage( new DeathrunDamageInfo
		{
			Amount = MaxHealth,
			DamageType = DeathrunDamageType.Generic,
			Source = GameObject,
			SourcePosition = WorldPosition,
			HitPosition = WorldPosition,
			Reason = reason,
			IsLethal = true,
			InvalidatesRun = true
		} );
	}

	private void Die( DeathrunDamageInfo damageInfo )
	{
		if ( IsDead )
		{
			if ( LogDamage )
				Log.Info( $"Die ignored for '{GameObject.Name}' because the player is already dead." );

			return;
		}

		if ( _respawnQueued || _respawnInProgress )
		{
			if ( LogDamage )
				Log.Info( $"Die ignored for '{GameObject.Name}' because a respawn is already queued or running." );

			return;
		}

		IsDead = true;
		CurrentHealth = 0.0f;
		_lastDeathPosition = WorldPosition;
		var deathCameraTarget = CreateDeathRagdoll( damageInfo );

		if ( deathCameraTarget.IsValid() && deathCameraTarget != GameObject )
			SetLiveBodyHiddenForClients( true );

		LogRespawnState( "before death movement disable" );
		SetMovementEnabled( false );
		LogRespawnState( "after death movement disable" );
		StartDeathVisualsRpc( _lastDeathPosition, deathCameraTarget );

		if ( LogDamage )
			Log.Info( $"Die called for '{GameObject.Name}'. Type={damageInfo.DamageType}, Reason='{damageInfo.Reason ?? "none"}', InvalidatesRun={damageInfo.InvalidatesRun}." );

		// TODO: Notify run tracking / checkpoint / leaderboard systems here when they exist.
		// damageInfo.InvalidatesRun is the hook to mark a competitive run invalid.

		if ( RespawnOnDeath && !_respawnQueued )
			QueueRespawn();
		else if ( !RespawnOnDeath )
		{
			if ( LogDamage )
				Log.Info( $"'{GameObject.Name}' will stay dead because RespawnOnDeath is disabled." );
		}
	}

	private async void QueueRespawn()
	{
		_respawnQueued = true;
		var generation = ++_respawnGeneration;
		var delay = MathF.Max( 0.0f, RespawnDelay );

		if ( LogDamage )
			Log.Info( $"'{GameObject.Name}' respawn delay started: {delay:0.##} second(s)." );

		try
		{
			await GameTask.DelaySeconds( delay, GameObject.EnabledToken );
		}
		catch ( TaskCanceledException )
		{
			if ( LogDamage )
				Log.Info( $"'{GameObject.Name}' respawn delay cancelled because the player object was disabled or destroyed." );

			return;
		}

		if ( !IsValid || !GameObject.IsValid() || !Networking.IsHost )
			return;

		if ( generation != _respawnGeneration || !IsDead )
		{
			if ( LogDamage )
				Log.Info( $"'{GameObject.Name}' respawn delay completed but respawn was skipped. Generation={generation}, CurrentGeneration={_respawnGeneration}, IsDead={IsDead}." );

			return;
		}

		if ( LogDamage )
			Log.Info( $"'{GameObject.Name}' respawn delay completed after {delay:0.##} second(s)." );

		RespawnNow();
	}

	private void RespawnNow()
	{
		if ( !Networking.IsHost )
		{
			Log.Warning( $"Ignoring RespawnNow for '{GameObject.Name}' on a client. Respawning is host-authoritative." );
			return;
		}

		if ( !IsDead )
		{
			if ( LogDamage )
				Log.Info( $"Respawn skipped for '{GameObject.Name}' because the player is no longer dead." );

			return;
		}

		if ( _respawnInProgress )
		{
			if ( LogDamage )
				Log.Info( $"Respawn skipped for '{GameObject.Name}' because a respawn is already in progress." );

			return;
		}

		_respawnInProgress = true;

		if ( LogDamage )
			Log.Info( $"Respawn started for '{GameObject.Name}'." );

		if ( !TryMoveToRespawnPoint() )
			FallbackRespawnMove();

		ResetHealth();
		FinishRespawnVisualsRpc( WorldPosition, WorldRotation );

		if ( LogDamage )
			Log.Info( $"Respawn completed for '{GameObject.Name}' at {WorldPosition}." );
	}

	private bool TryMoveToRespawnPoint()
	{
		var player = Components.Get<DeathrunPlayer>();

		if ( !player.IsValid() )
		{
			Log.Warning( $"'{GameObject.Name}' has no DeathrunPlayer marker, so DeathrunHealth will use fallback respawn movement." );
			return false;
		}

		var manager = Scene.GetAllComponents<DeathrunNetworkManager>()
			.FirstOrDefault( x => x.IsValid() );

		if ( !manager.IsValid() )
		{
			Log.Warning( $"No DeathrunNetworkManager found for '{GameObject.Name}', so DeathrunHealth will use fallback respawn movement." );
			return false;
		}

		return manager.TryMovePlayerToRespawn( player );
	}

	private void FallbackRespawnMove()
	{
		WorldPosition = _initialSpawnPosition;
		WorldRotation = _initialSpawnRotation;
		ClearVelocity();

		if ( GameObject.Network.Active )
			GameObject.Network.ClearInterpolation();

		Log.Warning( $"Fallback respawn moved '{GameObject.Name}' to its initial spawn position {_initialSpawnPosition}. TODO: replace this with checkpoint-aware respawn when checkpoints exist." );
	}

	private void SetMovementEnabled( bool enabled )
	{
		if ( !DisableInputOnDeath )
		{
			LogRespawnState( $"movement {(enabled ? "restore" : "disable")} skipped because DisableInputOnDeath=false" );
			return;
		}

		_playerController ??= Components.Get<PlayerController>();
		LogRespawnState( $"before movement {(enabled ? "restore" : "disable")}" );

		if ( _playerController.IsValid() )
		{
			if ( !enabled )
			{
				if ( !_hasStoredInputState )
				{
					_storedUseInputControls = _playerController.UseInputControls;
					_storedUseLookControls = _playerController.UseLookControls;
					_storedUseCameraControls = _playerController.UseCameraControls;
					_hasStoredInputState = true;
				}

				_playerController.UseInputControls = false;
				_playerController.UseLookControls = false;
				_playerController.UseCameraControls = false;
			}
			else if ( _hasStoredInputState )
			{
				_playerController.UseInputControls = _storedUseInputControls;
				_playerController.UseLookControls = _storedUseLookControls;
				_playerController.UseCameraControls = _storedUseCameraControls;
				_hasStoredInputState = false;

				if ( LogDamage )
					Log.Info( $"'{GameObject.Name}' PlayerController input/camera restored. Input={_playerController.UseInputControls}, Look={_playerController.UseLookControls}, Camera={_playerController.UseCameraControls}." );
			}
		}
		else if ( !enabled )
		{
			Log.Warning( $"'{GameObject.Name}' has no PlayerController to disable on death." );
		}

		_body = GetBody();

		if ( _body.IsValid() )
		{
			if ( !enabled )
			{
				if ( !_hasStoredBodyState )
				{
					_storedBodyMotionEnabled = _body.MotionEnabled;
					_hasStoredBodyState = true;
				}

				_body.Velocity = Vector3.Zero;

				if ( FreezeBodyOnDeath )
					_body.MotionEnabled = false;
			}
			else if ( _hasStoredBodyState )
			{
				_body.MotionEnabled = _storedBodyMotionEnabled;
				_body.Velocity = Vector3.Zero;
				_hasStoredBodyState = false;

				if ( LogDamage )
					Log.Info( $"'{GameObject.Name}' Rigidbody motion restored. MotionEnabled={_body.MotionEnabled}." );
			}
		}

		if ( LogDamage )
			Log.Info( $"'{GameObject.Name}' movement/input {(enabled ? "restored" : "disabled")}." );

		LogRespawnState( $"after movement {(enabled ? "restore" : "disable")}" );
	}

	private Rigidbody GetBody()
	{
		_playerController ??= Components.Get<PlayerController>();

		if ( _playerController.IsValid() && _playerController.Body.IsValid() )
			return _playerController.Body;

		return Components.Get<Rigidbody>();
	}

	private void ClearVelocity()
	{
		_body = GetBody();

		if ( _body.IsValid() )
			_body.Velocity = Vector3.Zero;
	}

	private void ResetRespawnSensitiveComponents()
	{
		var fallDamage = Components.Get<DeathrunFallDamage>();

		if ( fallDamage.IsValid() )
			fallDamage.ResetFallTracking();
	}

	private GameObject CreateDeathRagdoll( DeathrunDamageInfo damageInfo )
	{
		_ragdollOnDeath = GetRagdollOnDeath();

		if ( !_ragdollOnDeath.IsValid() )
			return GameObject;

		var ragdoll = _ragdollOnDeath.CreateDeathRagdoll( damageInfo, _lastDeathPosition, WorldRotation );

		if ( ragdoll.IsValid() )
			return ragdoll;

		return GameObject;
	}

	private void CleanupDeathRagdoll()
	{
		_ragdollOnDeath = GetRagdollOnDeath();

		if ( _ragdollOnDeath.IsValid() )
			_ragdollOnDeath.CleanupAfterRespawn();
	}

	private DeathrunRagdollOnDeath GetRagdollOnDeath()
	{
		if ( !_ragdollOnDeath.IsValid() )
			_ragdollOnDeath = Components.Get<DeathrunRagdollOnDeath>();

		return _ragdollOnDeath;
	}

	private void SetLiveBodyHiddenForClients( bool hidden )
	{
		if ( !Game.IsPlaying || !GameObject.Network.Active )
			return;

		SetLiveBodyHiddenRpc( hidden );
	}

	[Rpc.Broadcast]
	public void SetLiveBodyHiddenRpc( bool hidden )
	{
		if ( Rpc.Calling && !Rpc.Caller.IsHost )
			return;

		var ragdollOnDeath = GetRagdollOnDeath();

		if ( !ragdollOnDeath.IsValid() )
			return;

		if ( hidden )
			ragdollOnDeath.HideLiveBodyVisuals();
		else
			ragdollOnDeath.RestoreLiveBodyVisuals();
	}

	[Rpc.Owner]
	public void StartDeathVisualsRpc( Vector3 deathPosition, GameObject cameraTarget )
	{
		if ( Rpc.Calling && !Rpc.Caller.IsHost )
			return;

		if ( !ShouldRunOwnerVisuals() )
			return;

		LogRespawnState( "owner death visuals before movement disable" );
		SetMovementEnabled( false );
		LogRespawnState( "owner death visuals after movement disable" );

		var deathCamera = Components.GetOrCreate<DeathrunOrbitDeathCamera>();
		var target = cameraTarget.IsValid() ? cameraTarget : GameObject;
		deathCamera.StartDeathCamera( target, deathPosition );

		if ( LogDamage )
			Log.Info( $"Owner death camera started for local player '{GameObject.Name}' at {deathPosition}. Target='{target.Name}'." );
	}

	[Rpc.Owner]
	public void FinishRespawnVisualsRpc( Vector3 respawnPosition, Rotation respawnRotation )
	{
		if ( Rpc.Calling && !Rpc.Caller.IsHost )
			return;

		if ( !ShouldRunOwnerVisuals() )
			return;

		WorldPosition = respawnPosition;
		WorldRotation = respawnRotation;
		ClearVelocity();

		if ( GameObject.Network.Active )
			GameObject.Network.ClearInterpolation();

		var deathCamera = Components.Get<DeathrunOrbitDeathCamera>();

		if ( deathCamera.IsValid() )
			deathCamera.StopDeathCamera();

		LogRespawnState( "owner respawn visuals before movement restore" );
		SetMovementEnabled( true );
		LogRespawnState( "owner respawn visuals after movement restore" );

		if ( LogDamage )
			Log.Info( $"Owner death camera stopped and controls restored for local player '{GameObject.Name}'." );
	}

	private bool ShouldRunOwnerVisuals()
	{
		return !GameObject.Network.Active || GameObject.Network.IsOwner;
	}

	private void LogRespawnState( string reason )
	{
		if ( !LogRespawnDebug )
			return;

		_playerController ??= Components.Get<PlayerController>();
		_body = GetBody();

		var owner = GameObject.Network.Owner;
		var ownerName = owner?.DisplayName ?? "none";
		var ownerId = owner?.Id.ToString() ?? GameObject.Network.OwnerId.ToString();
		var networkActive = GameObject.Network.Active;
		var isOwner = networkActive && GameObject.Network.IsOwner;

		Log.Info(
			$"DeathrunHealth '{GameObject.Name}' {reason}. Owner={ownerName} ({ownerId}), NetworkActive={networkActive}, IsOwner={isOwner}, IsHost={Networking.IsHost}, " +
			$"IsDead={IsDead}, Health={CurrentHealth:0.##}/{MaxHealth:0.##}, Input={_playerController.IsValid() && _playerController.UseInputControls}, " +
			$"Look={_playerController.IsValid() && _playerController.UseLookControls}, Camera={_playerController.IsValid() && _playerController.UseCameraControls}, " +
			$"BodyValid={_body.IsValid()}, MotionEnabled={_body.IsValid() && _body.MotionEnabled}, Velocity={(_body.IsValid() ? _body.Velocity : Vector3.Zero)}." );
	}
}
