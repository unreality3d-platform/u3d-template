using Fusion;
using System.Collections.Generic;
using U3D;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController), typeof(PlayerInput))]
public class U3DPlayerController : NetworkBehaviour
{
    [Header("Basic Movement")]
    [SerializeField] private bool enableMovement = true;
    [SerializeField] private float walkSpeed = 4f;
    [SerializeField] private float runSpeed = 8f;
    [SerializeField] private float gravity = -20f;

    [HideInInspector][SerializeField] private float groundCheckDistance = 0.1f;

    public enum PerspectiveMode { FirstPersonOnly, ThirdPersonOnly, SmoothScroll }

    [Header("Perspective Control")]
    [SerializeField] private PerspectiveMode perspectiveMode = PerspectiveMode.SmoothScroll;

    [HideInInspector][SerializeField] private float thirdPersonDistance = 5f;
    [HideInInspector][SerializeField] private float perspectiveTransitionSpeed = 8f;
    [HideInInspector][SerializeField] private bool enableCameraCollision = true;
    [HideInInspector][SerializeField] private bool enableSmoothTransitions = true;

    [Header("Mouse Sensitivity Settings")]
    [SerializeField] private float baseMouseSensitivity = 1.0f;
    [SerializeField] private float webglSensitivityMultiplier = 0.25f;
    [SerializeField] private float mobileSensitivityMultiplier = 0.8f;
    [SerializeField] private float userSensitivityMultiplier = 1.0f;
    [SerializeField] private bool enableMouseSmoothing = true;
    [SerializeField] private float mouseSmoothingAmount = 0.1f;

    [HideInInspector] private float mouseSensitivity;
    [HideInInspector] private float cameraOrbitSensitivity;

    [HideInInspector][SerializeField] private float lookUpLimit = 80f;
    [HideInInspector][SerializeField] private float lookDownLimit = -80f;
    [HideInInspector][SerializeField] private float cameraCollisionRadius = 0.2f;
    [HideInInspector][SerializeField] private float cameraCollisionBuffer = 0.1f;

    [Header("AAA Camera System")]
    [SerializeField] private bool enableAdvancedCamera = true;
    [SerializeField] private float characterTurnSpeed = 90f;

    [Header("Mouse Look Behavior")]
    [SerializeField] private bool enableAlwaysFreeLook = true;

    [Header("Smooth Camera Transition")]
    [SerializeField]
    private AnimationCurve cameraDistanceCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(1f, 5f)
    );
    [SerializeField]
    private AnimationCurve cameraHeightCurve = new AnimationCurve(
        new Keyframe(0f, 1.5f),
        new Keyframe(1f, 1.5f)
    );
    [SerializeField] private float transitionTime = 1.5f;

    private U3DInteractionManager _interactionManager;

    private float _runtimeMouseSensitivity;
    private float _runtimeOrbitSensitivity;
    private RuntimePlatform _currentPlatform;

    private float currentTransitionValue = 0f;
    private float targetTransitionValue = 0f;
    private bool isTransitioning = false;
    private Vector3 originalFirstPersonPosition;

    private Transform cameraPivot;
    private float cameraYaw = 0f;
    private float cameraPitchAdvanced = 0f;
    private bool isLeftMouseDragging = false;
    private bool isRightMouseDragging = false;
    private bool isBothMouseForward = false;

    private bool advancedModeActive = false;

    [Header("Advanced Movement")]
    [SerializeField] private bool enableSprintToggle = true;
    [SerializeField] private KeyCode sprintKey = KeyCode.LeftShift;
    [SerializeField] private bool enableAutoRun = true;
    [SerializeField] private KeyCode autoRunKey = KeyCode.Numlock;
    [SerializeField] private bool enableFlying = true;
    [SerializeField] private KeyCode flyKey = KeyCode.F;
    [SerializeField] private bool enableCrouchToggle = true;
    [SerializeField] private KeyCode crouchKey = KeyCode.C;
    [SerializeField] private bool enableTeleport = true;
    [SerializeField] private bool enableViewZoom = true;
    [SerializeField] private bool enableFOVAdjustment = true;

    [Header("Jump Settings")]
    [SerializeField] private bool enableJumping = true;
    [SerializeField] private KeyCode jumpKey = KeyCode.Space;
    [SerializeField] private float jumpHeight = 2f;
    [SerializeField] private float[] additionalJumps = new float[] { 4f };

    [Header("Interaction")]
    [SerializeField] private KeyCode interactKey = KeyCode.R;

    [Header("Network Synchronization")]
    [SerializeField] private float networkSendRate = 20f;
    [SerializeField] private float positionThreshold = 0.1f;
    [SerializeField] private float rotationThreshold = 1f;

    [HideInInspector][SerializeField] private float zoomFOV = 30f;
    [HideInInspector][SerializeField] private float defaultFOV = 60f;
    [HideInInspector][SerializeField] private float zoomSpeed = 5f;

    [HideInInspector][Networked] public Vector3 NetworkPosition { get; set; }
    [HideInInspector][Networked] public Quaternion NetworkRotation { get; set; }
    [HideInInspector][Networked] public bool NetworkIsMoving { get; set; }
    [HideInInspector][Networked] public bool NetworkIsSprinting { get; set; }
    [HideInInspector][Networked] public bool NetworkIsCrouching { get; set; }
    [HideInInspector][Networked] public bool NetworkIsFlying { get; set; }
    [HideInInspector][Networked] public float NetworkCameraPitch { get; set; }
    [HideInInspector][Networked] public bool NetworkIsInteracting { get; set; }
    [HideInInspector][Networked] public bool NetworkIsJumping { get; set; }

    [HideInInspector][Networked] public bool NetworkIsSwimming { get; set; }
    [HideInInspector][Networked] public bool NetworkIsClimbing { get; set; }

    [HideInInspector][Networked] public bool NetworkIsInVR { get; set; }
    [HideInInspector][Networked] public Vector3 NetworkHeadPosition { get; set; }
    [HideInInspector][Networked] public Quaternion NetworkHeadRotation { get; set; }
    [HideInInspector][Networked] public Vector3 NetworkLeftHandPos { get; set; }
    [HideInInspector][Networked] public Quaternion NetworkLeftHandRot { get; set; }
    [HideInInspector][Networked] public Vector3 NetworkRightHandPos { get; set; }
    [HideInInspector][Networked] public Quaternion NetworkRightHandRot { get; set; }

    private Queue<Vector2> _mouseInputBuffer = new Queue<Vector2>();
    private Queue<float> _mouseTimeBuffer = new Queue<float>();
    private const float MOUSE_SMOOTHING_WINDOW = 0.015f;
    private Vector2 _smoothedMouseInput = Vector2.zero;

    private CharacterController characterController;
    private PlayerInput playerInput;
    private Camera playerCamera;

    private Vector3 velocity;
    private Vector2 moveInput;
    private Vector2 lookInput;
    private bool isGrounded;
    private int jumpCount;
    private bool isSprinting;
    private bool isCrouching;
    private bool isFlying;
    private bool isAutoRunning;
    private bool isZooming;

    private float cameraPitch;
    private bool isFirstPerson = true;
    private Vector3 firstPersonPosition;
    private Vector3 thirdPersonPosition;
    private float currentCameraDistance;
    private float targetFOV;
    private bool lookInverted;
    private float originalCameraHeight;
    private float crouchCameraOffset = -0.5f;
    private int _spawnFrameCount = 0;
    private const int SPAWN_PROTECTION_FRAMES = 5;

    private bool _isLocalPlayer;
    private bool _jumpPressedThisFrame;
    private bool _jumpPressedPending;
    private float _lastNetworkSendTime;
    private Vector3 _lastSentPosition;
    private Quaternion _lastSentRotation;
    private bool _justTeleported = false;

    private U3DWebGLCursorManager _cursorManager;
    private NetworkButtons _buttonsPrevious;
    private U3D.Networking.U3DFusionNetworkManager _networkManager;

    private bool _isInVRMode = false;
    private Transform _leftHandVisual;
    private Transform _rightHandVisual;
    private U3D.XR.U3DWebXRManager _webXRManager;

    private const float VR_SNAP_TURN_ANGLE = 45f;
    private const float VR_SNAP_TURN_COOLDOWN = 0.3f;
    private float _lastSnapTurnTime = 0f;
    private const float VR_MOVEMENT_SPEED_MULTIPLIER = 1.0f;

    // Rideable support
    private U3D.U3DRideableController _currentRideable;

    void CalculateRuntimeSensitivity()
    {
        _currentPlatform = Application.platform;

        float platformMultiplier = 1.0f;

        switch (_currentPlatform)
        {
            case RuntimePlatform.WebGLPlayer:
                platformMultiplier = webglSensitivityMultiplier;
                break;
            case RuntimePlatform.IPhonePlayer:
            case RuntimePlatform.Android:
                platformMultiplier = mobileSensitivityMultiplier;
                break;
            default:
                platformMultiplier = 1.0f;
                break;
        }

        _runtimeMouseSensitivity = baseMouseSensitivity * platformMultiplier * userSensitivityMultiplier;
        _runtimeOrbitSensitivity = baseMouseSensitivity * platformMultiplier * userSensitivityMultiplier;

        mouseSensitivity = _runtimeMouseSensitivity;
        cameraOrbitSensitivity = _runtimeOrbitSensitivity;
    }

    public void SetUserSensitivity(float sensitivity)
    {
        userSensitivityMultiplier = Mathf.Clamp(sensitivity, 0.1f, 3.0f);
        CalculateRuntimeSensitivity();
        SaveSensitivitySettings();
    }

    public float GetUserSensitivity() => userSensitivityMultiplier;
    public float GetEffectiveSensitivity() => _runtimeMouseSensitivity;

    void LoadSensitivitySettings()
    {
        userSensitivityMultiplier = PlayerPrefs.GetFloat("U3D_MouseSensitivity", 1.0f);
    }

    void SaveSensitivitySettings()
    {
        PlayerPrefs.SetFloat("U3D_MouseSensitivity", userSensitivityMultiplier);
        PlayerPrefs.Save();
    }

    public override void Spawned()
    {
        _isLocalPlayer = Object.HasStateAuthority;

        if (_isLocalPlayer)
        {
            NetworkPosition = transform.position;
            NetworkRotation = transform.rotation;
        }

        InitializeComponents();
        LoadSensitivitySettings();
        CalculateRuntimeSensitivity();
        ConfigurePlayerForNetworking();

        _spawnFrameCount = 0;

        if (_isLocalPlayer && enableAdvancedCamera && cameraPivot != null)
            cameraYaw = transform.eulerAngles.y;

        if (_isLocalPlayer)
        {
            _webXRManager = U3D.XR.U3DWebXRManager.Instance;
            if (_webXRManager != null)
                _webXRManager.RegisterLocalPlayer(this);
        }

        if (_isLocalPlayer)
        {
            switch (perspectiveMode)
            {
                case PerspectiveMode.FirstPersonOnly: SetFirstPerson(); break;
                case PerspectiveMode.ThirdPersonOnly: SetThirdPerson(); break;
                case PerspectiveMode.SmoothScroll: SetFirstPerson(); break;
            }
        }
        else
        {
            CreateNametag();
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        base.Despawned(runner, hasState);

        if (_isLocalPlayer && _webXRManager != null)
            _webXRManager.UnregisterLocalPlayer(this);

        if (_leftHandVisual != null) Destroy(_leftHandVisual.gameObject);
        if (_rightHandVisual != null) Destroy(_rightHandVisual.gameObject);
    }

    void InitializeCameraPivot()
    {
        if (!enableAdvancedCamera) return;

        originalFirstPersonPosition = firstPersonPosition;

        GameObject pivotGO = new GameObject("CameraPivot");
        cameraPivot = pivotGO.transform;
        cameraPivot.SetParent(transform);
        cameraPivot.localPosition = firstPersonPosition;
        cameraPivot.localRotation = Quaternion.identity;

        if (playerCamera != null)
        {
            playerCamera.transform.SetParent(cameraPivot);
            UpdateCameraTransitionPosition();
            cameraYaw = transform.eulerAngles.y;
            cameraPitchAdvanced = 0f;
        }
    }

    void UpdateCameraTransitionPosition()
    {
        if (cameraPivot == null || playerCamera == null) return;

        float distance = cameraDistanceCurve.Evaluate(currentTransitionValue);
        float heightOffset = cameraHeightCurve.Evaluate(currentTransitionValue);

        Vector3 targetPosition;

        if (currentTransitionValue <= 0.01f)
        {
            targetPosition = Vector3.zero;
        }
        else
        {
            float relativeHeight = heightOffset - firstPersonPosition.y;
            targetPosition = new Vector3(0f, relativeHeight, -distance);
        }

        if (isCrouching)
            targetPosition.y += crouchCameraOffset;

        if (currentTransitionValue > 0.01f && enableCameraCollision)
            targetPosition = GetCollisionSafeCameraPosition(targetPosition);

        playerCamera.transform.localPosition = targetPosition;
    }

    void Awake()
    {
        characterController = GetComponent<CharacterController>();
        playerInput = GetComponent<PlayerInput>();
        playerCamera = GetComponentInChildren<Camera>();

        if (playerCamera == null)
        {
            Debug.LogError("U3DPlayerController: No Camera found in children. Please add a Camera as a child object.");
            enabled = false;
            return;
        }

        firstPersonPosition = playerCamera.transform.localPosition;
        thirdPersonPosition = firstPersonPosition + Vector3.back * thirdPersonDistance;
        currentCameraDistance = 0f;
        targetFOV = defaultFOV;
        playerCamera.fieldOfView = defaultFOV;

        InitializeCameraPivot();
        LoadPlayerPreferences();
    }

    void InitializeComponents()
    {
        if (!_isLocalPlayer) return;
        _cursorManager = FindAnyObjectByType<U3DWebGLCursorManager>();
        if (_cursorManager == null)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void ConfigurePlayerForNetworking()
    {
        if (_isLocalPlayer)
        {
            if (playerInput != null)
                playerInput.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;

            if (playerCamera != null)
            {
                playerCamera.enabled = true;
                playerCamera.tag = "MainCamera";
            }

            InitializeInteractionManager();
        }
        else
        {
            if (playerInput != null)
                playerInput.enabled = false;

            if (playerCamera != null)
            {
                playerCamera.enabled = false;
                playerCamera.tag = "Untagged";
            }

            if (characterController != null)
                characterController.enabled = false;
        }
    }

    void InitializeInteractionManager()
    {
        _interactionManager = FindAnyObjectByType<U3DInteractionManager>();

        if (_interactionManager == null)
        {
            GameObject interactionManagerObj = new GameObject("InteractionManager");
            _interactionManager = interactionManagerObj.AddComponent<U3DInteractionManager>();
        }
    }

    void Start()
    {
        var networkObject = GetComponent<NetworkObject>();
        if (networkObject != null && networkObject.IsValid)
            return;

        switch (perspectiveMode)
        {
            case PerspectiveMode.FirstPersonOnly: SetFirstPerson(); break;
            case PerspectiveMode.ThirdPersonOnly: SetThirdPerson(); break;
            case PerspectiveMode.SmoothScroll: SetFirstPerson(); break;
        }
    }

    void CreateNametag()
    {
        StartCoroutine(DelayedNametagCreation());
    }

    private System.Collections.IEnumerator DelayedNametagCreation()
    {
        while (!GetComponent<NetworkObject>() || !GetComponent<NetworkObject>().IsValid)
            yield return null;

        yield return new WaitForSeconds(0.1f);

        if (_isLocalPlayer) yield break;

        var nametagAnchor = new GameObject("NametagAnchor");
        nametagAnchor.transform.SetParent(transform);
        nametagAnchor.transform.localPosition = Vector3.up * 2.2f;

        var nametag = nametagAnchor.AddComponent<U3D.Networking.U3DPlayerNametag>();
        nametag.Initialize(this);
    }

    bool IsCursorLocked()
    {
        if (_isInVRMode) return true;
        if (_cursorManager != null) return _cursorManager.IsCursorLocked;
        return Cursor.lockState == CursorLockMode.Locked;
    }

    public void RefreshInputActionsFromNetworkManager(U3D.Networking.U3DFusionNetworkManager networkManager)
    {
        if (!_isLocalPlayer) return;
        _networkManager = networkManager;
    }

    public override void FixedUpdateNetwork()
    {
        if (_spawnFrameCount == 0 && _isLocalPlayer)
        {
            Vector3 spawnPos = NetworkPosition;
            if (spawnPos != Vector3.zero)
            {
                characterController.enabled = false;
                transform.position = spawnPos;
                characterController.enabled = true;
            }
        }

        if (!_isLocalPlayer) return;

        _spawnFrameCount++;

        if (GetInput<U3DNetworkInputData>(out var input))
        {
            var pressedThisFrame = input.Buttons.GetPressed(_buttonsPrevious);
            _jumpPressedThisFrame = pressedThisFrame.IsSet(U3DInputButtons.Jump);
            if (_jumpPressedThisFrame)
                _jumpPressedPending = true;

            HandleGroundCheck();

            if (_isInVRMode)
            {
                HandleVRMovement(input);
                HandleVRPoseSync();
            }
            else
            {
                // Any movement intent dismounts the player
                if (_currentRideable != null)
                {
                    bool wantsDismount = input.MovementInput.magnitude > 0.1f
                        || input.BothMouseHeld
                        || input.Buttons.GetPressed(_buttonsPrevious).IsSet(U3DInputButtons.Fly)
                        || input.Buttons.GetPressed(_buttonsPrevious).IsSet(U3DInputButtons.AutoRunToggle);

                    if (wantsDismount)
                        DismountRideable(_currentRideable);
                }

                if (_currentRideable == null)
                    HandleMovementFusion(input);

                if (_spawnFrameCount > SPAWN_PROTECTION_FRAMES)
                    HandleLookFusionFixed(input);
            }

            HandleButtonInputsFusion(input);
            HandleTeleportFusion(input);
            HandleCameraPositioning();

            // Parenting handles vertical carry — only apply gravity when not riding
            if (_currentRideable == null)
                ApplyGravityFixed();
            else
                NetworkPosition = transform.position;
        }
    }

    public override void Render()
    {
        if (_isLocalPlayer)
        {
            if (!_isInVRMode)
                HandleLocalCameraRender();

            HandleZoom();

            if (_justTeleported)
                _justTeleported = false;

            return;
        }

        if (NetworkRotation == Quaternion.identity ||
            float.IsNaN(NetworkRotation.x) || float.IsNaN(NetworkRotation.y) ||
            float.IsNaN(NetworkRotation.z) || float.IsNaN(NetworkRotation.w))
            return;

        if (_justTeleported)
        {
            _justTeleported = false;
            return;
        }

        float positionDifference = Vector3.Distance(transform.position, NetworkPosition);
        float rotationDifference = Quaternion.Angle(transform.rotation, NetworkRotation);

        if (positionDifference > 0.1f)
            transform.position = Vector3.Lerp(transform.position, NetworkPosition, Time.deltaTime * 15f);

        if (rotationDifference > 0.5f && rotationDifference < 180f)
            transform.rotation = Quaternion.Slerp(transform.rotation, NetworkRotation, Time.deltaTime * 12f);

        if (playerCamera != null)
            playerCamera.transform.localRotation = Quaternion.Euler(NetworkCameraPitch, 0f, 0f);

        if (NetworkIsInVR)
            UpdateRemoteVRVisuals();
        else
            HideHandVisuals();
    }

    void HandleLocalCameraRender()
    {
        if (!enableMovement || !_isLocalPlayer || playerCamera == null) return;
        if (!IsCursorLocked()) return;
        playerCamera.transform.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
    }

    // ==================== VR/WebXR MODE HANDLING ====================

    public void SetVRMode(bool enabled)
    {
        if (!_isLocalPlayer) return;

        bool wasInVR = _isInVRMode;
        _isInVRMode = enabled;
        NetworkIsInVR = enabled;

        if (enabled && !wasInVR) EnterVRMode();
        else if (!enabled && wasInVR) ExitVRMode();
    }

    private void EnterVRMode()
    {
        if (_cursorManager != null)
            _cursorManager.SetVRMode(true);

        CreateHandVisuals();

        if (_leftHandVisual != null) _leftHandVisual.gameObject.SetActive(true);
        if (_rightHandVisual != null) _rightHandVisual.gameObject.SetActive(true);

        cameraPitch = 0f;
        cameraPitchAdvanced = 0f;

        if (cameraPivot != null && playerCamera != null)
        {
            playerCamera.transform.SetParent(transform);
            playerCamera.transform.localPosition = firstPersonPosition;
            playerCamera.transform.localRotation = Quaternion.identity;
        }
    }

    private void ExitVRMode()
    {
        if (_leftHandVisual != null) _leftHandVisual.gameObject.SetActive(false);
        if (_rightHandVisual != null) _rightHandVisual.gameObject.SetActive(false);

        if (cameraPivot != null && playerCamera != null)
        {
            playerCamera.transform.SetParent(cameraPivot);
            UpdateCameraTransitionPosition();
        }

        if (playerCamera != null)
        {
            cameraYaw = transform.eulerAngles.y;
            cameraPitch = 0f;
        }

        if (_cursorManager != null)
            _cursorManager.SetVRMode(false);
    }

    private void CreateHandVisuals()
    {
        if (_leftHandVisual == null)
        {
            var leftHandGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            leftHandGO.name = "LeftHandVisual";
            leftHandGO.transform.SetParent(transform);
            leftHandGO.transform.localScale = Vector3.one * 0.1f;
            var collider = leftHandGO.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            leftHandGO.SetActive(false);
            _leftHandVisual = leftHandGO.transform;
        }
        if (_rightHandVisual == null)
        {
            var rightHandGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rightHandGO.name = "RightHandVisual";
            rightHandGO.transform.SetParent(transform);
            rightHandGO.transform.localScale = Vector3.one * 0.1f;
            var collider = rightHandGO.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            rightHandGO.SetActive(false);
            _rightHandVisual = rightHandGO.transform;
        }
    }

    private void HandleVRMovement(U3DNetworkInputData input)
    {
        if (!enableMovement || !_isLocalPlayer) return;

        Vector2 vrMoveInput = input.MovementInput;
        float snapTurnInput = input.LookInput.x;

        if (Mathf.Abs(snapTurnInput) > 0.1f)
        {
            float turnDelta = snapTurnInput * 90f * Runner.DeltaTime;
            transform.Rotate(Vector3.up, turnDelta);
            NetworkRotation = transform.rotation;
            cameraYaw += turnDelta;
        }

        Vector3 forward = playerCamera != null ? playerCamera.transform.forward : transform.forward;
        Vector3 right = playerCamera != null ? playerCamera.transform.right : transform.right;

        if (!isFlying)
        {
            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();
        }

        Vector3 moveDirection = (forward * vrMoveInput.y + right * vrMoveInput.x).normalized;
        float currentSpeed = GetCurrentSpeed() * VR_MOVEMENT_SPEED_MULTIPLIER;

        isSprinting = input.Buttons.IsSet(U3DInputButtons.Sprint);
        if (isSprinting)
            currentSpeed = runSpeed * VR_MOVEMENT_SPEED_MULTIPLIER;

        Vector3 moveVelocity = moveDirection * currentSpeed;

        if (isFlying)
        {
            Vector3 flyDirection = moveDirection;
            if (input.Buttons.IsSet(U3DInputButtons.Jump)) flyDirection += Vector3.up;
            if (input.Buttons.IsSet(U3DInputButtons.Crouch)) flyDirection += Vector3.down;
            characterController.Move(flyDirection * currentSpeed * Runner.DeltaTime);
        }
        else
        {
            characterController.Move(moveVelocity * Runner.DeltaTime);
        }

        NetworkPosition = transform.position;
        NetworkRotation = transform.rotation;
        NetworkIsMoving = moveVelocity.magnitude > 0.1f;
        NetworkIsSprinting = isSprinting;
    }

    private void HandleVRPoseSync()
    {
        if (!_isLocalPlayer) return;

        if (playerCamera != null)
        {
            NetworkHeadPosition = playerCamera.transform.localPosition;
            NetworkHeadRotation = playerCamera.transform.localRotation;
            NetworkCameraPitch = playerCamera.transform.localEulerAngles.x;
        }

        if (_leftHandVisual != null && _leftHandVisual.gameObject.activeSelf)
        {
            NetworkLeftHandPos = _leftHandVisual.localPosition;
            NetworkLeftHandRot = _leftHandVisual.localRotation;
        }

        if (_rightHandVisual != null && _rightHandVisual.gameObject.activeSelf)
        {
            NetworkRightHandPos = _rightHandVisual.localPosition;
            NetworkRightHandRot = _rightHandVisual.localRotation;
        }
    }

    private void UpdateRemoteVRVisuals()
    {
        if (_leftHandVisual == null || _rightHandVisual == null)
            CreateHandVisuals();

        if (_leftHandVisual != null)
        {
            _leftHandVisual.gameObject.SetActive(true);
            Vector3 worldLeftPos = transform.TransformPoint(NetworkLeftHandPos);
            Quaternion worldLeftRot = transform.rotation * NetworkLeftHandRot;
            _leftHandVisual.position = Vector3.Lerp(_leftHandVisual.position, worldLeftPos, Time.deltaTime * 15f);
            _leftHandVisual.rotation = Quaternion.Slerp(_leftHandVisual.rotation, worldLeftRot, Time.deltaTime * 15f);
        }

        if (_rightHandVisual != null)
        {
            _rightHandVisual.gameObject.SetActive(true);
            Vector3 worldRightPos = transform.TransformPoint(NetworkRightHandPos);
            Quaternion worldRightRot = transform.rotation * NetworkRightHandRot;
            _rightHandVisual.position = Vector3.Lerp(_rightHandVisual.position, worldRightPos, Time.deltaTime * 15f);
            _rightHandVisual.rotation = Quaternion.Slerp(_rightHandVisual.rotation, worldRightRot, Time.deltaTime * 15f);
        }
    }

    private void HideHandVisuals()
    {
        if (_leftHandVisual != null) _leftHandVisual.gameObject.SetActive(false);
        if (_rightHandVisual != null) _rightHandVisual.gameObject.SetActive(false);
    }

    // ==================== END VR/WebXR MODE HANDLING ====================

    void HandleGroundCheck()
    {
        if (!_isLocalPlayer) return;

        isGrounded = characterController.isGrounded;

        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
            jumpCount = 0;
        }

        if (isGrounded && velocity.y <= 0 && NetworkIsJumping)
            NetworkIsJumping = false;
    }

    void HandleMovementFusion(U3DNetworkInputData input)
    {
        if (!enableMovement || !_isLocalPlayer) return;

        moveInput = input.MovementInput;

        if (NetworkIsClimbing) return;

        Vector2 advancedMovement = HandleAdvancedKeyboardMovement(input);

        if (isBothMouseForward) advancedMovement.y = 1f;
        if (isAutoRunning) advancedMovement.y = 1f;

        Vector2 finalMovement = (advancedMovement.magnitude > 0.1f) ? advancedMovement : moveInput;

        if (enableAdvancedCamera && cameraPivot != null)
        {
            bool isStartingToMove = (finalMovement.magnitude > 0.1f && !NetworkIsMoving);
            if (isStartingToMove && !isRightMouseDragging)
            {
                transform.rotation = Quaternion.Euler(0, cameraYaw, 0);
                NetworkRotation = transform.rotation;
            }
        }

        Vector3 forward, right;

        if (enableAdvancedCamera && cameraPivot != null)
        {
            forward = cameraPivot.forward;
            right = cameraPivot.right;
        }
        else
        {
            forward = playerCamera.transform.forward;
            right = playerCamera.transform.right;
        }

        if (!isFlying)
        {
            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();
        }

        Vector3 moveDirection = (forward * finalMovement.y + right * finalMovement.x).normalized;
        float currentSpeed = GetCurrentSpeed();
        Vector3 moveVelocity = moveDirection * currentSpeed;

        if (isFlying)
        {
            Vector3 flyDirection = moveDirection;
            if (input.Buttons.IsSet(U3DInputButtons.Jump)) flyDirection += Vector3.up;
            if (input.Buttons.IsSet(U3DInputButtons.Crouch)) flyDirection += Vector3.down;
            characterController.Move(flyDirection * currentSpeed * Runner.DeltaTime);
        }
        else
        {
            characterController.Move(moveVelocity * Runner.DeltaTime);
        }

        NetworkPosition = transform.position;
        NetworkRotation = transform.rotation;
        NetworkIsMoving = moveVelocity.magnitude > 0.1f;
    }

    Vector2 HandleAdvancedKeyboardMovement(U3DNetworkInputData input)
    {
        Vector2 advancedMovement = Vector2.zero;

        if (moveInput.y != 0)
            advancedMovement.y = moveInput.y;

        if (enableAdvancedCamera)
        {
            if (!isRightMouseDragging)
            {
                if (input.TurnLeft)
                {
                    float turnDelta = -characterTurnSpeed * Runner.DeltaTime;
                    transform.Rotate(Vector3.up, turnDelta);
                    NetworkRotation = transform.rotation;
                    if (cameraPivot != null) cameraYaw += turnDelta;
                }
                if (input.TurnRight)
                {
                    float turnDelta = characterTurnSpeed * Runner.DeltaTime;
                    transform.Rotate(Vector3.up, turnDelta);
                    NetworkRotation = transform.rotation;
                    if (cameraPivot != null) cameraYaw += turnDelta;
                }
            }

            if (input.StrafeLeft) advancedMovement.x = -1f;
            if (input.StrafeRight) advancedMovement.x = 1f;
        }
        else
        {
            advancedMovement.x = moveInput.x;
        }

        return advancedMovement;
    }

    void HandleLookFusionFixed(U3DNetworkInputData input)
    {
        if (!enableMovement || !_isLocalPlayer) return;
        if (!IsCursorLocked()) return;

        Vector2 rawLookInput = input.LookInput;

        if (lookInverted)
            rawLookInput.y = -rawLookInput.y;

        Vector2 sensitivityAdjustedInput = rawLookInput * _runtimeMouseSensitivity;

        Vector2 finalLookInput;
        if (enableMouseSmoothing)
        {
            float currentTime = (float)Runner.SimulationTime;
            _mouseInputBuffer.Enqueue(sensitivityAdjustedInput);
            _mouseTimeBuffer.Enqueue(currentTime);

            while (_mouseTimeBuffer.Count > 0 && (currentTime - _mouseTimeBuffer.Peek()) > MOUSE_SMOOTHING_WINDOW)
            {
                _mouseInputBuffer.Dequeue();
                _mouseTimeBuffer.Dequeue();
            }

            Vector2 smoothedLookInput = Vector2.zero;
            if (_mouseInputBuffer.Count > 0)
            {
                foreach (Vector2 sample in _mouseInputBuffer)
                    smoothedLookInput += sample;
                smoothedLookInput /= _mouseInputBuffer.Count;
            }

            finalLookInput = Vector2.Lerp(_smoothedMouseInput, smoothedLookInput, mouseSmoothingAmount);
        }
        else
        {
            finalLookInput = sensitivityAdjustedInput;
        }

        _smoothedMouseInput = finalLookInput;
        lookInput = finalLookInput;

        HandleAdvancedMouseControls(input);

        if (enableAlwaysFreeLook && !isLeftMouseDragging && !isRightMouseDragging && !isBothMouseForward)
        {
            if (enableAdvancedCamera && cameraPivot != null)
            {
                if (Mathf.Abs(finalLookInput.x) > 0.01f)
                {
                    transform.Rotate(Vector3.up, finalLookInput.x);
                    cameraYaw += finalLookInput.x;
                    NetworkRotation = transform.rotation;
                }

                if (Mathf.Abs(finalLookInput.y) > 0.01f)
                {
                    cameraPitchAdvanced -= finalLookInput.y;
                    cameraPitchAdvanced = Mathf.Clamp(cameraPitchAdvanced, lookDownLimit, lookUpLimit);
                    NetworkCameraPitch = cameraPitchAdvanced;
                }

                if (cameraPivot != null)
                    cameraPivot.localRotation = Quaternion.Euler(cameraPitchAdvanced, 0f, 0f);
            }
            else
            {
                if (Mathf.Abs(finalLookInput.x) > 0.01f)
                {
                    transform.Rotate(Vector3.up, finalLookInput.x);
                    NetworkRotation = transform.rotation;
                }

                if (Mathf.Abs(finalLookInput.y) > 0.01f)
                {
                    cameraPitch -= finalLookInput.y;
                    cameraPitch = Mathf.Clamp(cameraPitch, lookDownLimit, lookUpLimit);
                    NetworkCameraPitch = cameraPitch;
                }
            }
        }
        else if (!enableAlwaysFreeLook && !isLeftMouseDragging && !isRightMouseDragging && !enableAdvancedCamera)
        {
            if (Mathf.Abs(finalLookInput.x) > 0.01f)
            {
                transform.Rotate(Vector3.up, finalLookInput.x);
                NetworkRotation = transform.rotation;
            }

            if (Mathf.Abs(finalLookInput.y) > 0.01f)
            {
                cameraPitch -= finalLookInput.y;
                cameraPitch = Mathf.Clamp(cameraPitch, lookDownLimit, lookUpLimit);
                NetworkCameraPitch = cameraPitch;
            }
        }
    }

    void HandleAdvancedMouseControls(U3DNetworkInputData input)
    {
        if (!enableAdvancedCamera || cameraPivot == null) return;

        isLeftMouseDragging = input.LeftMouseHeld;
        isRightMouseDragging = input.RightMouseHeld;
        isBothMouseForward = input.BothMouseHeld;

        Vector2 processedInput = _smoothedMouseInput;

        if (isBothMouseForward)
        {
            if (Mathf.Abs(processedInput.x) > 0.01f)
            {
                transform.Rotate(Vector3.up, processedInput.x);
                cameraYaw += processedInput.x;
                NetworkRotation = transform.rotation;
            }
            if (Mathf.Abs(processedInput.y) > 0.01f)
            {
                cameraPitchAdvanced -= processedInput.y;
                cameraPitchAdvanced = Mathf.Clamp(cameraPitchAdvanced, lookDownLimit, lookUpLimit);
                NetworkCameraPitch = cameraPitchAdvanced;
            }
        }
        else if (isRightMouseDragging && !isLeftMouseDragging)
        {
            if (Mathf.Abs(processedInput.x) > 0.01f)
            {
                transform.Rotate(Vector3.up, processedInput.x);
                cameraYaw += processedInput.x;
                NetworkRotation = transform.rotation;
            }
            if (Mathf.Abs(processedInput.y) > 0.01f)
            {
                cameraPitchAdvanced -= processedInput.y;
                cameraPitchAdvanced = Mathf.Clamp(cameraPitchAdvanced, lookDownLimit, lookUpLimit);
                NetworkCameraPitch = cameraPitchAdvanced;
            }
        }
        else if (isLeftMouseDragging && !isRightMouseDragging)
        {
            if (Mathf.Abs(processedInput.x) > 0.01f)
                cameraYaw += processedInput.x;
            if (Mathf.Abs(processedInput.y) > 0.01f)
            {
                cameraPitchAdvanced -= processedInput.y;
                cameraPitchAdvanced = Mathf.Clamp(cameraPitchAdvanced, lookDownLimit, lookUpLimit);
            }
            NetworkCameraPitch = cameraPitchAdvanced;
        }

        if (cameraPivot != null)
        {
            if (isLeftMouseDragging && !isRightMouseDragging)
                cameraPivot.rotation = Quaternion.Euler(cameraPitchAdvanced, cameraYaw, 0f);
            else
                cameraPivot.localRotation = Quaternion.Euler(cameraPitchAdvanced, 0f, 0f);
        }
    }

    void HandleButtonInputsFusion(U3DNetworkInputData input)
    {
        if (!_isLocalPlayer) return;

        var pressed = input.Buttons.GetPressed(_buttonsPrevious);
        var released = input.Buttons.GetReleased(_buttonsPrevious);

        if (enableJumping && pressed.IsSet(U3DInputButtons.Jump))
            HandleJumpFusionFixed();

        if (enableSprintToggle && pressed.IsSet(U3DInputButtons.Sprint))
        {
            isSprinting = !isSprinting;
            NetworkIsSprinting = isSprinting;
        }

        if (enableCrouchToggle && pressed.IsSet(U3DInputButtons.Crouch))
        {
            isCrouching = !isCrouching;
            NetworkIsCrouching = isCrouching;

            if (isCrouching)
            {
                characterController.height = 1f;
                characterController.center = new Vector3(0, 0.5f, 0);
            }
            else
            {
                characterController.height = 2f;
                characterController.center = new Vector3(0, 1f, 0);
            }
        }

        if (isCrouching && NetworkIsMoving && !isFlying)
        {
            isCrouching = false;
            NetworkIsCrouching = false;
            characterController.height = 2f;
            characterController.center = new Vector3(0, 1f, 0);
        }

        if (enableFlying && pressed.IsSet(U3DInputButtons.Fly))
        {
            isFlying = !isFlying;
            NetworkIsFlying = isFlying;
            velocity = Vector3.zero;
        }

        if (pressed.IsSet(U3DInputButtons.AutoRunToggle))
            isAutoRunning = !isAutoRunning;

        if (pressed.IsSet(U3DInputButtons.Interact))
        {
            NetworkIsInteracting = true;
            if (_interactionManager != null)
                _interactionManager.OnPlayerInteract();
            else
                Debug.LogWarning("No interaction manager found - interaction ignored");
        }

        isZooming = input.Buttons.IsSet(U3DInputButtons.Zoom);
        targetFOV = isZooming ? zoomFOV : defaultFOV;

        if (perspectiveMode == PerspectiveMode.SmoothScroll && Mathf.Abs(input.PerspectiveScroll) > 0.1f)
        {
            if (input.PerspectiveScroll > 0.1f && !isFirstPerson)
                SetFirstPerson();
            else if (input.PerspectiveScroll < -0.1f && isFirstPerson)
                SetThirdPerson();
        }

        _buttonsPrevious = input.Buttons;
    }

    void HandleJumpFusionFixed()
    {
        if (NetworkIsClimbing) return;
        if (isFlying) return;

        // Jumping dismounts the player
        if (_currentRideable != null)
            DismountRideable(_currentRideable);

        if (isGrounded || jumpCount < additionalJumps.Length + 1)
        {
            float jumpForce;
            if (jumpCount == 0)
                jumpForce = Mathf.Sqrt(jumpHeight * -2f * gravity);
            else if (jumpCount <= additionalJumps.Length)
                jumpForce = Mathf.Sqrt(additionalJumps[jumpCount - 1] * -2f * gravity);
            else
                return;

            velocity.y = jumpForce;
            jumpCount++;
            NetworkIsJumping = true;
        }
    }

    void HandleTeleportFusion(U3DNetworkInputData input)
    {
        if (!enableTeleport || !_isLocalPlayer) return;

        var pressed = input.Buttons.GetPressed(_buttonsPrevious);
        if (pressed.IsSet(U3DInputButtons.Teleport))
            PerformTeleport();
    }

    public void PerformTeleport()
    {
        if (playerCamera == null)
        {
            Debug.LogWarning("❌ Cannot teleport - player camera is null");
            return;
        }

        Vector3 screenCenter = new Vector3(Screen.width / 2, Screen.height / 2, 0);
        Ray ray = playerCamera.ScreenPointToRay(screenCenter);
        RaycastHit[] allHits = Physics.RaycastAll(ray, 100f);

        RaycastHit bestHit = new RaycastHit();
        bool foundHit = false;
        float closestDistance = float.MaxValue;

        foreach (RaycastHit hit in allHits)
        {
            if (hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform))
                continue;
            if (hit.collider.isTrigger)
                continue;
            if (hit.distance < closestDistance)
            {
                bestHit = hit;
                closestDistance = hit.distance;
                foundHit = true;
            }
        }

        if (foundHit)
        {
            Vector3 teleportPos = bestHit.point;
            float playerHeight = characterController != null ? characterController.height : 2f;
            teleportPos.y += (playerHeight * 0.5f) + 0.1f;

            _justTeleported = true;

            // Dismount if riding — must re-enable CC before teleport
            if (_currentRideable != null)
                DismountRideable(_currentRideable);
            else if (transform.parent != null)
            {
                characterController.enabled = false;
                transform.SetParent(null, true);
                characterController.enabled = true;
            }

            NetworkPosition = teleportPos;
            NetworkRotation = transform.rotation;

            characterController.enabled = false;
            transform.position = teleportPos;
            characterController.enabled = true;

            velocity = Vector3.zero;
        }
    }

    void HandleCameraPositioning()
    {
        if (!_isLocalPlayer) return;
        if (_isInVRMode) return;

        if (perspectiveMode == PerspectiveMode.SmoothScroll)
        {
            if (Mathf.Abs(currentTransitionValue - targetTransitionValue) > 0.001f)
            {
                currentTransitionValue = Mathf.MoveTowards(
                    currentTransitionValue,
                    targetTransitionValue,
                    Runner.DeltaTime / transitionTime
                );
                isTransitioning = true;
            }
            else
            {
                currentTransitionValue = targetTransitionValue;
                isTransitioning = false;
            }

            UpdateCameraTransitionPosition();
        }
        else if (enableSmoothTransitions)
        {
            Vector3 targetPosition = isFirstPerson ? firstPersonPosition : thirdPersonPosition;

            if (isCrouching)
                targetPosition.y += crouchCameraOffset;

            if (enableCameraCollision && !isFirstPerson)
                targetPosition = GetCollisionSafeCameraPosition(targetPosition);

            playerCamera.transform.localPosition = Vector3.Lerp(
                playerCamera.transform.localPosition,
                targetPosition,
                Runner.DeltaTime * perspectiveTransitionSpeed
            );
        }
    }

    void HandleZoom()
    {
        if (!enableViewZoom || !_isLocalPlayer) return;
        playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, targetFOV, Time.deltaTime * zoomSpeed);
    }

    Vector3 GetCollisionSafeCameraPosition(Vector3 desiredPosition)
    {
        Vector3 pivotWorldPosition = cameraPivot != null ? cameraPivot.position : (transform.position + firstPersonPosition);

        Vector3 cameraWorldTarget;
        if (cameraPivot != null)
            cameraWorldTarget = pivotWorldPosition + cameraPivot.rotation * desiredPosition;
        else
            cameraWorldTarget = transform.TransformPoint(desiredPosition);

        Vector3 direction = (cameraWorldTarget - pivotWorldPosition).normalized;
        float maxDistance = Vector3.Distance(pivotWorldPosition, cameraWorldTarget);

        if (maxDistance < 0.1f) return desiredPosition;

        int layerMask = ~(LayerMask.GetMask("Ignore Raycast") | LayerMask.GetMask("Player"));

        if (Physics.SphereCast(pivotWorldPosition, cameraCollisionRadius, direction, out RaycastHit hit, maxDistance, layerMask))
        {
            float safeDistance = Mathf.Max(0.1f, hit.distance - cameraCollisionBuffer);
            Vector3 safeWorldPosition = pivotWorldPosition + direction * safeDistance;

            if (cameraPivot != null)
                return Quaternion.Inverse(cameraPivot.rotation) * (safeWorldPosition - pivotWorldPosition);
            else
                return transform.InverseTransformPoint(safeWorldPosition);
        }

        return desiredPosition;
    }

    void ApplyGravityFixed()
    {
        if (isFlying || isGrounded || NetworkIsClimbing || !_isLocalPlayer) return;

        velocity.y += gravity * Runner.DeltaTime;
        characterController.Move(new Vector3(0, velocity.y, 0) * Runner.DeltaTime);
    }

    float GetCurrentSpeed()
    {
        if (isSprinting) return runSpeed;
        else if (isCrouching) return walkSpeed * 0.5f;
        else return walkSpeed;
    }

    void SetFirstPerson()
    {
        isFirstPerson = true;
        currentCameraDistance = 0f;
        if (perspectiveMode == PerspectiveMode.SmoothScroll)
            targetTransitionValue = 0f;
    }

    void SetThirdPerson()
    {
        isFirstPerson = false;
        currentCameraDistance = thirdPersonDistance;
        if (perspectiveMode == PerspectiveMode.SmoothScroll)
            targetTransitionValue = 1f;
    }

    void LoadPlayerPreferences()
    {
        lookInverted = PlayerPrefs.GetInt("U3D_LookInverted", 0) == 1;
        LoadSensitivitySettings();
    }

    public void SetMouseSmoothing(bool enabled)
    {
        enableMouseSmoothing = enabled;
        PlayerPrefs.SetInt("U3D_MouseSmoothing", enabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    public bool GetMouseSmoothing() => enableMouseSmoothing;

    public void SetMouseSmoothingAmount(float amount)
    {
        mouseSmoothingAmount = Mathf.Clamp01(amount);
        PlayerPrefs.SetFloat("U3D_MouseSmoothingAmount", mouseSmoothingAmount);
        PlayerPrefs.Save();
    }

    public float GetMouseSmoothingAmount() => mouseSmoothingAmount;
    public void SetLookInverted(bool inverted) { lookInverted = inverted; }
    public bool IsWebGLPlatform() => Application.platform == RuntimePlatform.WebGLPlayer;

    public float GetPlatformSensitivityMultiplier()
    {
        switch (Application.platform)
        {
            case RuntimePlatform.WebGLPlayer: return webglSensitivityMultiplier;
            case RuntimePlatform.IPhonePlayer:
            case RuntimePlatform.Android: return mobileSensitivityMultiplier;
            default: return 1.0f;
        }
    }

    public string GetPlatformName()
    {
        switch (Application.platform)
        {
            case RuntimePlatform.WebGLPlayer: return "WebGL";
            case RuntimePlatform.IPhonePlayer: return "iOS";
            case RuntimePlatform.Android: return "Android";
            case RuntimePlatform.WindowsPlayer:
            case RuntimePlatform.WindowsEditor: return "Windows";
            case RuntimePlatform.OSXPlayer:
            case RuntimePlatform.OSXEditor: return "macOS";
            case RuntimePlatform.LinuxPlayer:
            case RuntimePlatform.LinuxEditor: return "Linux";
            default: return "Desktop";
        }
    }

    public void OnMove(InputAction.CallbackContext context) { }
    public void OnLook(InputAction.CallbackContext context) { }
    public void OnJump(InputAction.CallbackContext context) { }
    public void OnSprint(InputAction.CallbackContext context) { }
    public void OnCrouch(InputAction.CallbackContext context) { }
    public void OnZoom(InputAction.CallbackContext context) { }
    public void OnFly(InputAction.CallbackContext context) { }
    public void OnAutoRun(InputAction.CallbackContext context) { }
    public void OnPerspectiveSwitch(InputAction.CallbackContext context) { }
    public void OnInteract(InputAction.CallbackContext context) { }
    public void OnPause(InputAction.CallbackContext context) { }
    public void OnTeleport(InputAction.CallbackContext context) { }

    public bool IsGrounded => isGrounded;
    public bool IsSprinting => isSprinting;
    public bool IsCrouching => isCrouching;
    public bool IsFlying => isFlying;
    public bool IsAutoRunning => isAutoRunning;
    public bool IsFirstPerson => isFirstPerson;
    public Vector3 Velocity => velocity;
    public float CurrentSpeed => GetCurrentSpeed();
    public bool IsLocalPlayer => _isLocalPlayer;
    public bool IsJumping => NetworkIsJumping;
    public bool IsInVRMode => _isInVRMode;
    public Vector2 MoveInput => moveInput;
    public bool JumpPressedThisFrame => _jumpPressedPending;
    public CharacterController CharacterController => characterController;

    public void SetPosition(Vector3 position)
    {
        if (!_isLocalPlayer)
        {
            Debug.LogWarning("SetPosition called on non-local player");
            return;
        }

        try
        {
            _currentRideable = null;

            // Unparent before repositioning
            if (transform.parent != null)
            {
                characterController.enabled = false;
                transform.SetParent(null, true);
                characterController.enabled = true;
            }

            NetworkPosition = position;
            NetworkRotation = transform.rotation;

            characterController.enabled = false;
            transform.position = position;
            characterController.enabled = true;

            velocity = Vector3.zero;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ SetPosition failed: {e.Message}");
        }
    }

    public void SetRotation(float yRotation)
    {
        if (!_isLocalPlayer) return;
        transform.rotation = Quaternion.Euler(0, yRotation, 0);
    }

    public void SetCameraPitch(float pitch)
    {
        if (!_isLocalPlayer) return;
        cameraPitch = pitch;
    }

    public void SetSwimmingState(bool isSwimming)
    {
        if (!_isLocalPlayer) return;
        NetworkIsSwimming = isSwimming;
    }

    public void SetClimbingState(bool isClimbing)
    {
        if (!_isLocalPlayer) return;
        NetworkIsClimbing = isClimbing;
        if (isClimbing) velocity = Vector3.zero;
    }

    public void SetClimbDetachVelocity(Vector3 detachVelocity)
    {
        velocity = detachVelocity;
    }

    public void ConsumeJumpPress()
    {
        _jumpPressedPending = false;
    }

    public void MountRideable(U3D.U3DRideableController rideable)
    {
        if (!_isLocalPlayer) return;

        _currentRideable = rideable;
        velocity = Vector3.zero;
        NetworkIsMoving = false;

        characterController.enabled = false;
        transform.SetParent(rideable.transform, true);
    }

    public void DismountRideable(U3D.U3DRideableController rideable)
    {
        if (!_isLocalPlayer) return;
        if (_currentRideable != rideable) return;

        _currentRideable = null;

        transform.SetParent(null, true);
        characterController.enabled = true;

        NetworkPosition = transform.position;
    }

    public bool IsRiding(U3D.U3DRideableController rideable) => _currentRideable == rideable;
}