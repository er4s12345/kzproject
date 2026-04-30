[Title( "Deathrun Movement Stats" )]
[Category( "Player" )]
[Icon( "directions_run" )]
public sealed class DeathrunMovementStats : Component
{
	private const string SprintInput = "Run";
	private const string JumpInput = "Jump";

	private PlayerController _playerController;
	private int _fixedUpdateCount;
	private bool _hasLoggedMissingController;
	private bool _hasLoggedAmbiguousParentSearch;
	private bool _hasLoggedWishVelocityInputWarning;
	private bool _hasLoggedProxySkip;
	private MovementDriver _lastLoggedDriver = (MovementDriver)(-1);
	private float _lastLoggedBaseMoveSpeed = -1.0f;
	private float _lastLoggedSprintSpeed = -1.0f;
	private float _lastLoggedJumpForce = -1.0f;
	private bool _lastLoggedUseSprint;
	private bool _lastLoggedUseInputControls;

	[Property] public float BaseMoveSpeed { get; set; } = 180.0f;
	[Property] public float SprintSpeed { get; set; } = 320.0f;
	[Property] public float JumpForce { get; set; } = 300.0f;
	[Property] public bool UseSprint { get; set; } = true;

	[Property] public MovementDriver Driver { get; set; } = MovementDriver.PlayerControllerSpeeds;

	public enum MovementDriver
	{
		PlayerControllerSpeeds,
		WishVelocity
	}

	protected override void OnAwake()
	{
		Log.Info( $"DeathrunMovementStats awake on '{GameObject.Name}'." );
		FindPlayerController();
	}

	protected override void OnEnabled()
	{
		Log.Info( $"DeathrunMovementStats enabled on '{GameObject.Name}'." );
		FindPlayerController();
		ApplyMovementStats();
	}

	protected override void OnDisabled()
	{
		Log.Info( $"DeathrunMovementStats disabled on '{GameObject.Name}'." );
	}

	protected override void OnFixedUpdate()
	{
		_fixedUpdateCount++;

		if ( !FindPlayerController() )
			return;

		ApplyMovementStats();
	}

	private bool FindPlayerController()
	{
		if ( _playerController.IsValid() )
			return true;

		_playerController = Components.Get<PlayerController>();

		if ( _playerController.IsValid() )
		{
			LogFoundPlayerController( "same GameObject" );
			return true;
		}

		_playerController = GetComponentInParent<PlayerController>( true, false );

		if ( _playerController.IsValid() )
		{
			LogFoundPlayerController( "parent/ancestor GameObject" );
			return true;
		}

		_playerController = GetComponentInChildren<PlayerController>( true, false );

		if ( _playerController.IsValid() )
		{
			LogFoundPlayerController( "child/descendant GameObject" );
			return true;
		}

		_playerController = FindPlayerControllerInImmediateParentChildren();

		if ( _playerController.IsValid() )
		{
			LogFoundPlayerController( "sibling GameObject under the same parent" );
			return true;
		}

		if ( !_hasLoggedMissingController || _fixedUpdateCount % 100 == 0 )
		{
			Log.Warning( $"DeathrunMovementStats on '{GameObject.Name}' could not find a PlayerController on self, parents, or children." );
			_hasLoggedMissingController = true;
		}

		return false;
	}

	private PlayerController FindPlayerControllerInImmediateParentChildren()
	{
		var parent = GameObject.Parent;

		if ( parent is null || !parent.IsValid )
			return null;

		var controllers = parent.GetComponentsInChildren<PlayerController>( true, true ).ToArray();

		if ( controllers.Length == 1 )
			return controllers[0];

		if ( controllers.Length > 1 && !_hasLoggedAmbiguousParentSearch )
		{
			Log.Warning( $"DeathrunMovementStats found {controllers.Length} PlayerControllers under parent '{parent.Name}', so it will not guess which sibling to use. Put this component on the same GameObject as the intended PlayerController, or on its parent/child." );
			_hasLoggedAmbiguousParentSearch = true;
		}

		return null;
	}

	private void LogFoundPlayerController( string location )
	{
		_hasLoggedMissingController = false;
		Log.Info( $"DeathrunMovementStats found PlayerController on {location}: '{_playerController.GameObject.Name}'." );
		Log.Info( $"PlayerController initial state: UseInputControls={_playerController.UseInputControls}, WalkSpeed={_playerController.WalkSpeed}, RunSpeed={_playerController.RunSpeed}, JumpSpeed={_playerController.JumpSpeed}, Mode={GetMoveModeName()}." );
	}

	private void ApplyMovementStats()
	{
		if ( !_playerController.IsValid() )
			return;

		var wantsSprint = UseSprint && Input.Down( SprintInput, false );
		var moveSpeed = wantsSprint ? SprintSpeed : BaseMoveSpeed;
		var jumpForce = JumpForce;

		_playerController.JumpSpeed = jumpForce;

		if ( Driver == MovementDriver.WishVelocity )
		{
			ApplyWishVelocityMovement( moveSpeed, jumpForce );
		}
		else
		{
			ApplyPlayerControllerSpeedMovement( moveSpeed );
		}

		LogRuntimeStateIfChanged( moveSpeed, jumpForce, wantsSprint );
	}

	private void ApplyPlayerControllerSpeedMovement( float moveSpeed )
	{
		_playerController.WalkSpeed = moveSpeed;
		_playerController.RunSpeed = moveSpeed;

		_playerController.AltMoveButton = SprintInput;
		_playerController.RunByDefault = false;

		if ( !_playerController.UseInputControls )
		{
			Log.Warning( "DeathrunMovementStats is using PlayerControllerSpeeds, but PlayerController.UseInputControls is disabled. Enable Use Input Controls on the Player Controller, or switch this component's Driver to WishVelocity." );
		}
	}

	private void ApplyWishVelocityMovement( float moveSpeed, float jumpForce )
	{
		if ( _playerController.IsProxy )
		{
			if ( !_hasLoggedProxySkip )
			{
				Log.Info( $"DeathrunMovementStats is on proxy player '{_playerController.GameObject.Name}', so it will not read local input or drive WishVelocity here." );
				_hasLoggedProxySkip = true;
			}

			return;
		}

		if ( _playerController.UseInputControls )
		{
			_playerController.UseInputControls = false;

			if ( !_hasLoggedWishVelocityInputWarning )
			{
				Log.Warning( "DeathrunMovementStats Driver is WishVelocity, so it disabled PlayerController.UseInputControls. In the Player Controller inspector, turn off 'Use Input Controls' to avoid duplicate movement/jump input." );
				_hasLoggedWishVelocityInputWarning = true;
			}
		}

		var input = Input.AnalogMove;
		var eyeAngles = _playerController.EyeAngles;
		eyeAngles.pitch = 0.0f;
		eyeAngles.roll = 0.0f;

		var wishDirection = eyeAngles.ToRotation() * input;
		wishDirection = wishDirection.WithZ( 0.0f );

		if ( wishDirection.Length > 1.0f )
			wishDirection = wishDirection.Normal;

		_playerController.WishVelocity = wishDirection * moveSpeed;

		if ( Input.Pressed( JumpInput ) && _playerController.IsOnGround )
		{
			_playerController.Jump( Vector3.Up * jumpForce );
			Log.Info( $"DeathrunMovementStats applied jump: JumpForce={jumpForce}." );
		}
	}

	private void LogRuntimeStateIfChanged( float activeMoveSpeed, float activeJumpForce, bool wantsSprint )
	{
		var shouldLog =
			Driver != _lastLoggedDriver ||
			BaseMoveSpeed != _lastLoggedBaseMoveSpeed ||
			SprintSpeed != _lastLoggedSprintSpeed ||
			JumpForce != _lastLoggedJumpForce ||
			UseSprint != _lastLoggedUseSprint ||
			_playerController.UseInputControls != _lastLoggedUseInputControls;

		if ( !shouldLog && _fixedUpdateCount % 250 != 0 )
			return;

		Log.Info( $"DeathrunMovementStats tick: Driver={Driver}, ActiveMoveSpeed={activeMoveSpeed}, JumpForce={activeJumpForce}, WantsSprint={wantsSprint}, UseInputControls={_playerController.UseInputControls}, WishVelocity={_playerController.WishVelocity}, Mode={GetMoveModeName()}." );

		_lastLoggedDriver = Driver;
		_lastLoggedBaseMoveSpeed = BaseMoveSpeed;
		_lastLoggedSprintSpeed = SprintSpeed;
		_lastLoggedJumpForce = JumpForce;
		_lastLoggedUseSprint = UseSprint;
		_lastLoggedUseInputControls = _playerController.UseInputControls;
	}

	private string GetMoveModeName()
	{
		if ( !_playerController.IsValid() )
			return "None";

		return _playerController.Mode?.GetType().Name ?? "None";
	}
}
