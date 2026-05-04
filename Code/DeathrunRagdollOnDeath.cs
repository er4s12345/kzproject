using System;
using System.Collections.Generic;
using System.Threading.Tasks;

[Title( "Deathrun Ragdoll On Death" )]
[Category( "Deathrun" )]
[Icon( "accessibility_new" )]
public sealed class DeathrunRagdollOnDeath : Component
{
	[Property] public bool EnableRagdollOnDeath { get; set; } = true;
	[Property] public bool HideLiveBodyOnDeath { get; set; } = true;
	[Property] public bool DestroyRagdollOnRespawn { get; set; } = true;
	[Property] public float RagdollLifetime { get; set; } = 5.0f;
	[Property] public bool CopyPlayerTransform { get; set; } = true;
	[Property] public bool ApplyDeathForce { get; set; } = true;
	[Property] public float DeathForceMultiplier { get; set; } = 1.0f;
	[Property] public bool LogRagdoll { get; set; } = false;

	public GameObject CurrentRagdoll => _currentRagdoll;

	private readonly List<RendererState> _hiddenRenderers = new();
	private DeathrunPlayerController _playerController;
	private GameObject _currentRagdoll;
	private ModelPhysics _currentRagdollPhysics;
	private bool _liveBodyHidden;
	private int _ragdollGeneration;

	protected override void OnStart()
	{
		CacheComponents();
		LogBuiltInRagdollStatus();
	}

	public GameObject CreateDeathRagdoll( DeathrunDamageInfo damageInfo, Vector3 deathPosition, Rotation deathRotation )
	{
		if ( !EnableRagdollOnDeath )
			return null;

		if ( Game.IsPlaying && !Networking.IsHost )
		{
			if ( LogRagdoll )
				Log.Info( $"DeathrunRagdollOnDeath ignored death ragdoll creation for '{GameObject.Name}' on a client. Ragdolls are host-created." );

			return null;
		}

		CacheComponents();

		if ( !_playerController.IsValid() )
		{
			Log.Warning( $"DeathrunRagdollOnDeath on '{GameObject.Name}' needs a DeathrunPlayerController to create a project-local ragdoll." );
			return null;
		}

		if ( !_playerController.Renderer.IsValid() )
		{
			Log.Warning( $"DeathrunRagdollOnDeath on '{GameObject.Name}' needs DeathrunPlayerController.Renderer assigned to create a project-local ragdoll." );
			return null;
		}

		DestroyCurrentRagdoll( "replacement" );
		var ragdoll = _playerController.CreateRagdoll( $"Ragdoll - {GameObject.Name}" );

		if ( !ragdoll.IsValid() )
		{
			Log.Warning( $"DeathrunPlayerController.CreateRagdoll returned no ragdoll for '{GameObject.Name}'." );
			return null;
		}

		_currentRagdoll = ragdoll;
		_currentRagdollPhysics = ragdoll.Components.Get<ModelPhysics>();
		_ragdollGeneration++;

		if ( CopyPlayerTransform )
		{
			ragdoll.WorldPosition = deathPosition;
			ragdoll.WorldRotation = deathRotation;
		}

		if ( _currentRagdollPhysics.IsValid() )
			_currentRagdollPhysics.MotionEnabled = true;

		NetworkSpawnRagdoll( ragdoll );
		ApplyForceToRagdoll( damageInfo );
		HideLiveRenderers();
		QueueLifetimeCleanup( ragdoll, _ragdollGeneration );

		if ( LogRagdoll )
		{
			var bodyCount = _currentRagdollPhysics.IsValid() && _currentRagdollPhysics.Bodies is not null ? _currentRagdollPhysics.Bodies.Count : 0;
			Log.Info( $"DeathrunRagdollOnDeath created project-local ragdoll '{ragdoll.Name}' for '{GameObject.Name}' at {ragdoll.WorldPosition}. PhysicsBodies={bodyCount}, NetworkActive={ragdoll.Network.Active}." );
		}

		return ragdoll;
	}

	public void CleanupAfterRespawn()
	{
		if ( DestroyRagdollOnRespawn )
		{
			_ragdollGeneration++;
			DestroyCurrentRagdoll( "respawn" );
		}

		RestoreLiveRenderers();
	}

	public void HideLiveBodyVisuals()
	{
		HideLiveRenderers();
	}

	public void RestoreLiveBodyVisuals()
	{
		RestoreLiveRenderers();
	}

	private void CacheComponents()
	{
		if ( !_playerController.IsValid() )
			_playerController = Components.Get<DeathrunPlayerController>();
	}

	private void LogBuiltInRagdollStatus()
	{
		if ( !LogRagdoll )
			return;

		if ( !_playerController.IsValid() )
		{
			Log.Warning( $"DeathrunRagdollOnDeath on '{GameObject.Name}' did not find a DeathrunPlayerController. Project-local ragdoll creation is unavailable until one exists." );
			return;
		}

		var renderer = _playerController.Renderer;

		if ( !renderer.IsValid() )
		{
			Log.Warning( $"DeathrunRagdollOnDeath on '{GameObject.Name}' found DeathrunPlayerController but no Renderer. Assign the live SkinnedModelRenderer for project-local ragdolls." );
			return;
		}

		Log.Info( $"DeathrunRagdollOnDeath on '{GameObject.Name}' using DeathrunPlayerController.CreateRagdoll. Renderer='{renderer.GameObject.Name}', Model='{renderer.Model?.Name ?? "none"}'." );
	}

	private void NetworkSpawnRagdoll( GameObject ragdoll )
	{
		if ( !ragdoll.IsValid() || !Networking.IsHost || !Networking.IsActive )
			return;

		ragdoll.NetworkMode = NetworkMode.Object;
		var spawned = ragdoll.NetworkSpawn();

		if ( LogRagdoll )
			Log.Info( $"DeathrunRagdollOnDeath network spawned ragdoll '{ragdoll.Name}'. Success={spawned}, NetworkActive={ragdoll.Network.Active}." );
	}

	private void ApplyForceToRagdoll( DeathrunDamageInfo damageInfo )
	{
		if ( !ApplyDeathForce || !_currentRagdollPhysics.IsValid() )
			return;

		var force = damageInfo.Force * DeathForceMultiplier;

		if ( force.Length <= 0.01f )
			return;

		_currentRagdollPhysics.PhysicsGroup?.ApplyImpulse( force, false );

		if ( LogRagdoll )
			Log.Info( $"DeathrunRagdollOnDeath applied death impulse {force} to ragdoll '{_currentRagdoll.Name}'." );
	}

	private void HideLiveRenderers()
	{
		if ( !HideLiveBodyOnDeath || _liveBodyHidden )
			return;

		_hiddenRenderers.Clear();

		foreach ( var renderer in EnumerateSkinnedRenderers( GameObject ) )
		{
			if ( !renderer.IsValid() )
				continue;

			_hiddenRenderers.Add( new RendererState( renderer, renderer.Enabled ) );
			renderer.Enabled = false;
		}

		_liveBodyHidden = true;

		if ( LogRagdoll )
			Log.Info( $"DeathrunRagdollOnDeath hid {_hiddenRenderers.Count} live SkinnedModelRenderer component(s) for '{GameObject.Name}'." );
	}

	private void RestoreLiveRenderers()
	{
		if ( !_liveBodyHidden && _hiddenRenderers.Count == 0 )
			return;

		var restored = 0;

		foreach ( var state in _hiddenRenderers )
		{
			if ( !state.Renderer.IsValid() )
				continue;

			state.Renderer.Enabled = state.WasEnabled;
			restored++;
		}

		_hiddenRenderers.Clear();
		_liveBodyHidden = false;

		if ( LogRagdoll )
			Log.Info( $"DeathrunRagdollOnDeath restored {restored} live SkinnedModelRenderer component(s) for '{GameObject.Name}'." );
	}

	private async void QueueLifetimeCleanup( GameObject ragdoll, int generation )
	{
		if ( RagdollLifetime <= 0.0f || !ragdoll.IsValid() )
			return;

		try
		{
			await GameTask.DelaySeconds( RagdollLifetime, ragdoll.EnabledToken );
		}
		catch ( TaskCanceledException )
		{
			return;
		}

		if ( !IsValid || !GameObject.IsValid() || generation != _ragdollGeneration || ragdoll != _currentRagdoll )
			return;

		DestroyCurrentRagdoll( "lifetime" );
	}

	private void DestroyCurrentRagdoll( string reason )
	{
		if ( !_currentRagdoll.IsValid() )
		{
			_currentRagdoll = null;
			_currentRagdollPhysics = null;
			return;
		}

		var ragdollName = _currentRagdoll.Name;
		_currentRagdoll.Destroy();
		_currentRagdoll = null;
		_currentRagdollPhysics = null;

		if ( LogRagdoll )
			Log.Info( $"DeathrunRagdollOnDeath destroyed ragdoll '{ragdollName}' for '{GameObject.Name}'. Reason={reason}." );
	}

	private static IEnumerable<SkinnedModelRenderer> EnumerateSkinnedRenderers( GameObject root )
	{
		if ( !root.IsValid() )
			yield break;

		var renderer = root.Components.Get<SkinnedModelRenderer>();

		if ( renderer.IsValid() )
			yield return renderer;

		foreach ( var child in root.Children )
		{
			foreach ( var childRenderer in EnumerateSkinnedRenderers( child ) )
				yield return childRenderer;
		}
	}

	private readonly struct RendererState
	{
		public RendererState( SkinnedModelRenderer renderer, bool wasEnabled )
		{
			Renderer = renderer;
			WasEnabled = wasEnabled;
		}

		public SkinnedModelRenderer Renderer { get; }
		public bool WasEnabled { get; }
	}
}
