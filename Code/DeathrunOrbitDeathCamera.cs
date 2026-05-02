using System;

[Title( "Deathrun Orbit Death Camera" )]
[Category( "Deathrun" )]
[Icon( "videocam" )]
public sealed class DeathrunOrbitDeathCamera : Component
{
	[Property] public float Distance { get; set; } = 180.0f;
	[Property] public float Height { get; set; } = 80.0f;
	[Property] public float OrbitSpeed { get; set; } = 30.0f;
	[Property] public float LookAtHeight { get; set; } = 48.0f;
	[Property] public bool EnableMouseOrbit { get; set; } = true;
	[Property] public bool AutoRotate { get; set; } = true;
	[Property] public bool LogCamera { get; set; } = false;

	public bool IsOrbiting => _isOrbiting;

	private GameObject _cameraObject;
	private CameraComponent _camera;
	private GameObject _target;
	private Vector3 _deathPosition;
	private float _yaw;
	private float _heightOffset;
	private bool _isOrbiting;

	public void StartDeathCamera( GameObject target, Vector3 deathPosition )
	{
		if ( !Enabled )
		{
			if ( LogCamera )
				Log.Info( $"DeathrunOrbitDeathCamera is disabled on '{GameObject.Name}', so no death camera was started." );

			return;
		}

		if ( GameObject.Network.Active && !GameObject.Network.IsOwner )
		{
			if ( LogCamera )
				Log.Info( $"DeathrunOrbitDeathCamera ignored start for proxy player '{GameObject.Name}'." );

			return;
		}

		if ( _isOrbiting || _cameraObject.IsValid() )
			StopDeathCamera();

		_target = target;
		_deathPosition = deathPosition;
		_heightOffset = 0.0f;

		EnsureCamera();

		if ( !_camera.IsValid() )
		{
			Log.Warning( $"DeathrunOrbitDeathCamera could not create a CameraComponent for '{GameObject.Name}'." );
			return;
		}

		_camera.Enabled = true;
		_isOrbiting = true;
		UpdateOrbitCamera();

		if ( LogCamera )
			Log.Info( $"Owner death camera started for '{GameObject.Name}' around {deathPosition}." );
	}

	public void StopDeathCamera()
	{
		if ( !_isOrbiting && !_cameraObject.IsValid() )
		{
			if ( LogCamera )
				Log.Info( $"Owner death camera stop requested for '{GameObject.Name}', but no death camera was active." );

			return;
		}

		_isOrbiting = false;

		if ( _camera.IsValid() )
		{
			_camera.Enabled = false;
			_camera.IsMainCamera = false;
		}

		if ( _cameraObject.IsValid() )
		{
			_cameraObject.Enabled = false;
			_cameraObject.Destroy();
		}

		_cameraObject = null;
		_camera = null;
		_target = null;

		if ( LogCamera )
			Log.Info( $"Owner death camera stopped for '{GameObject.Name}'." );
	}

	public void BeginOrbit( GameObject target, Vector3 deathPosition )
	{
		StartDeathCamera( target, deathPosition );
	}

	public void EndOrbit()
	{
		StopDeathCamera();
	}

	protected override void OnUpdate()
	{
		if ( !_isOrbiting )
			return;

		UpdateOrbitInput();
		UpdateOrbitCamera();
	}

	protected override void OnDestroy()
	{
		StopDeathCamera();
	}

	private void EnsureCamera()
	{
		if ( _cameraObject.IsValid() && _camera.IsValid() )
			return;

		_cameraObject = Scene.CreateObject( true );
		_cameraObject.Name = $"Death Camera - {GameObject.Name}";
		_cameraObject.NetworkMode = NetworkMode.Never;

		_camera = _cameraObject.Components.Create<CameraComponent>();
		_camera.IsMainCamera = true;
		_camera.Priority = 100;
		_camera.FieldOfView = 70.0f;
		_camera.ZNear = 5.0f;
		_camera.ZFar = 100000.0f;
		_camera.EnablePostProcessing = true;
	}

	private void UpdateOrbitInput()
	{
		if ( AutoRotate )
			_yaw += OrbitSpeed * Time.Delta;

		if ( !EnableMouseOrbit )
			return;

		var mouseDelta = Input.MouseDelta;

		_yaw -= mouseDelta.x * 0.15f;
		_heightOffset = Clamp( _heightOffset - mouseDelta.y * 0.35f, -Height * 0.75f, Height * 1.5f );
	}

	private void UpdateOrbitCamera()
	{
		if ( !_cameraObject.IsValid() || !_camera.IsValid() )
			return;

		var lookAt = GetLookAtPosition();
		var orbitDirection = Rotation.FromYaw( _yaw ) * Vector3.Forward;
		var cameraPosition = lookAt - orbitDirection * MathF.Max( 16.0f, Distance ) + Vector3.Up * (Height + _heightOffset);
		var lookDirection = (lookAt - cameraPosition).Normal;

		_cameraObject.WorldPosition = cameraPosition;
		_cameraObject.WorldRotation = Rotation.LookAt( lookDirection, Vector3.Up );
	}

	private Vector3 GetLookAtPosition()
	{
		if ( _target.IsValid() )
		{
			var ragdollPhysics = _target.Components.Get<ModelPhysics>();

			if ( ragdollPhysics.IsValid() && ragdollPhysics.Bodies is not null && ragdollPhysics.Bodies.Count > 0 )
				return ragdollPhysics.MassCenter;

			return _target.WorldPosition + Vector3.Up * LookAtHeight;
		}

		return _deathPosition + Vector3.Up * LookAtHeight;
	}

	private static float Clamp( float value, float min, float max )
	{
		return MathF.Min( MathF.Max( value, min ), max );
	}
}
