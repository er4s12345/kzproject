using System;
using System.Threading.Tasks;

[Title( "Deathrun Health" )]
[Category( "Deathrun" )]
[Icon( "favorite" )]
public sealed class DeathrunHealth : Component
{
	[Property] public float MaxHealth { get; set; } = 100.0f;

	// Sync keeps this readable on clients for debugging/UI. If your S&box build rejects host writes
	// to sync properties on client-owned players, move health state to a host-owned child or mirror it with RPC later.
	[Sync, Property] public float CurrentHealth { get; set; }
	[Sync, Property] public bool IsDead { get; set; }

	[Property] public bool Invulnerable { get; set; } = false;
	[Property] public bool CanTakeDamage { get; set; } = true;
	[Property] public bool DieOnAnyDamage { get; set; } = false;
	[Property] public bool RespawnOnDeath { get; set; } = true;
	[Property] public float RespawnDelay { get; set; } = 2.0f;
	[Property] public bool LogDamage { get; set; } = true;
	[Property] public bool DisableInputOnDeath { get; set; } = true;

	public bool IsAlive => !IsDead;

	private PlayerController _playerController;
	private bool _storedUseInputControls;
	private bool _hasStoredInputState;
	private bool _respawnQueued;

	protected override void OnStart()
	{
		_playerController = Components.Get<PlayerController>();
		ResetHealth();
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
			return false;

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
		RestoreInput();

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
			return;

		IsDead = true;
		CurrentHealth = 0.0f;
		DisableInput();

		Log.Info( $"'{GameObject.Name}' died. Type={damageInfo.DamageType}, Reason='{damageInfo.Reason ?? "none"}', InvalidatesRun={damageInfo.InvalidatesRun}." );

		// TODO: Notify run tracking / checkpoint / leaderboard systems here when they exist.
		// damageInfo.InvalidatesRun is the hook to mark a competitive run invalid.

		if ( RespawnOnDeath && !_respawnQueued )
			QueueRespawn();
	}

	private async void QueueRespawn()
	{
		_respawnQueued = true;
		await Task.DelayRealtimeSeconds( MathF.Max( 0.0f, RespawnDelay ) );

		if ( !IsValid || !GameObject.IsValid() || !Networking.IsHost )
			return;

		// TODO: Integrate with DeathrunNetworkManager/checkpoints for a project-specific respawn point.
		// For now this restores health/input in place, which keeps the MVP damage flow safe.
		ResetHealth();
	}

	private void DisableInput()
	{
		if ( !DisableInputOnDeath )
			return;

		_playerController ??= Components.Get<PlayerController>();

		if ( !_playerController.IsValid() )
			return;

		if ( !_hasStoredInputState )
		{
			_storedUseInputControls = _playerController.UseInputControls;
			_hasStoredInputState = true;
		}

		_playerController.UseInputControls = false;

		if ( _playerController.Body.IsValid() )
			_playerController.Body.Velocity = Vector3.Zero;
	}

	private void RestoreInput()
	{
		_playerController ??= Components.Get<PlayerController>();

		if ( !_playerController.IsValid() || !_hasStoredInputState )
			return;

		_playerController.UseInputControls = _storedUseInputControls;
	}
}
