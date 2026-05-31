using Fusion;
using System.Collections.Generic;
using U3D;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

[DefaultExecutionOrder(100)]
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

    [Header("Third-Person Camera")]
    [Tooltip("How high the camera sits in third-person view, in player-root-local space. First-person height comes from the Camera child's Y position in the prefab. Set this to match the prefab Camera child's Y if you want consistent height across the perspective switch, or set it lower for an over-the-shoulder feel.")]
    [SerializeField] private float thirdPersonCameraHeight = 1.65f;

    [Tooltip("How far behind the player the third-person camera sits.")]
    [SerializeField] private float thirdPersonCameraDistance = 5f;

    [Tooltip("How long, in seconds, the camera takes to transition between first and third person when using SmoothScroll perspective mode.")]
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

    [Header("Jump Settings")]
    [SerializeField] private bool enableJumping = true;
    [SerializeField] private KeyCode jumpKey = KeyCode.Space;
    [SerializeField] private float jumpHeight = 2f;
    [SerializeField] private float[] additionalJumps = new float[] { 4f };

    [Header("Interaction")]
    [SerializeField] private KeyCode interactKey = KeyCode.R;

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
    [HideInInspector][Networked] public NetworkBool NetworkIsFirstPerson { get; set; }
    [HideInInspector][Networked] public NetworkBehaviourId NetworkRideableRef { get; set; }
    [HideInInspector][Networked, Capacity(40)] public string NetworkedDisplayName { get; set; }

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
    private bool isSwimming;
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
    private bool _justTeleported = false;

    private U3DWebGLCursorManager _cursorManager;
    private NetworkButtons _buttonsPrevious;
    private U3D.Networking.U3DFusionNetworkManager _networkManager;

    private bool _isInVRMode = false;
    private U3D.XR.U3DWebXRManager _webXRManager;
    private U3D.XR.U3DVRTeleporter _vrTeleporter;
    private bool _vrLocomotionSuppressed;
    private U3DGazePointer _gazePointer;

    private UnityEngine.SpatialTracking.TrackedPoseDriver _headTrackedPoseDriver;
    private UnityEngine.SpatialTracking.TrackedPoseDriver.TrackingType _headOriginalTrackingType;
    private Transform _avatarHeadBone;
    private Transform _rawHmdReference;
    private U3DAvatarManager _avatarManager;

    [Header("VR Eye Offset")]
    [Tooltip("Camera position relative to the avatar's head bone in player-local space. Increase Y if the camera sits too low (pointing at the neck). Increase Z to move the camera forward inside the head. Adjust until the camera lands at eye level when you look at the avatar in a mirror.")]
    [SerializeField] private Vector3 vrEyeOffset = new Vector3(0f, 0.2f, 0.08f);

    [Header("VR Body-Follow-Head")]
    [Tooltip("How far (in degrees) the head can rotate off-body before the body starts lazily catching up. 60 means you have a 120-degree free-look range before the body rotates. Higher = more independent head movement. Lower = body follows head more closely.")]
    [SerializeField] private float vrBodyFollowDeadzone = 60f;

    [Tooltip("How fast (degrees per second) the body rotates to catch up to the head once outside the deadzone. Lower = lazier follow. Higher = snappier follow. 90 matches characterTurnSpeed.")]
    [SerializeField] private float vrBodyFollowSpeed = 90f;

    private bool _vrRecenterPending = false;
    private float _vrRecenterTargetYaw = 0f;
    private bool _vrSnapTurnedThisFrame = false;

    [Header("VR Teleport")]
    [Tooltip("Material used for the VR teleport arc and reticle line renderers. Assign any URP/Unlit material in the Inspector. Required — without it the arc will not render in WebGL builds.")]
    [SerializeField] private Material vrTeleportMaterial;

    private const float VR_MOVEMENT_SPEED_MULTIPLIER = 1.0f;

    private U3D.U3DRideableController _currentRideable;
    private U3D.U3DRideableController _remoteRideable;

    void CalculateRuntimeSensitivity()
    {
        _currentPlatform = Application.platform;

        bool touchInputActive = U3D.Input.U3DSimpleTouchZones.Instance != null
            && U3D.Input.U3DSimpleTouchZones.Instance.IsTouchEnabled;

        float platformMultiplier;

        if (touchInputActive)
        {
            // Touch look must not be scaled by the desktop-browser multiplier.
            // Application.platform reports WebGLPlayer on mobile, which would
            // otherwise quarter finger-drag look via webglSensitivityMultiplier.
            // Touch uses the shared mouse base scaled only by the user's Settings
            // preference — no platform damper.
            platformMultiplier = 1.0f;
        }
        else
        {
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

            // Identity wiring: subscribe to the local-profile-ready event and
            // apply immediately if the profile already landed before we spawned.
            // Push-driven — no polling, no timing assumptions. Despawned handles
            // unsubscription to prevent leaks when scenes unload.
            FirebaseIntegration.OnLocalProfileReady += HandleLocalProfileReady;
            if (FirebaseIntegration.IsLocalProfileReady)
                ApplyDisplayNameFromProfile(FirebaseIntegration.LocalProfile);
        }
        else
        {
            CreateNametag();
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        base.Despawned(runner, hasState);

        if (_isLocalPlayer)
        {
            FirebaseIntegration.OnLocalProfileReady -= HandleLocalProfileReady;

            if (_webXRManager != null)
                _webXRManager.UnregisterLocalPlayer(this);
        }
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

        Vector3 targetPosition;

        if (currentTransitionValue <= 0.01f)
        {
            // Pure first-person: camera sits exactly at firstPersonPosition,
            // which is the prefab Camera child's local position captured in Awake.
            targetPosition = Vector3.zero;
        }
        else
        {
            // Linearly interpolate distance and height from first-person to third-person
            // values across the transition. First-person height is firstPersonPosition.y
            // (the prefab Camera Y), third-person height is the Inspector field.
            float distance = Mathf.Lerp(0f, thirdPersonCameraDistance, currentTransitionValue);
            float heightAtTransition = Mathf.Lerp(firstPersonPosition.y, thirdPersonCameraHeight, currentTransitionValue);
            float relativeHeight = heightAtTransition - firstPersonPosition.y;
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
        thirdPersonPosition = firstPersonPosition + Vector3.back * thirdPersonCameraDistance;
        currentCameraDistance = 0f;
        targetFOV = defaultFOV;
        playerCamera.fieldOfView = defaultFOV;

        _headTrackedPoseDriver = playerCamera.GetComponent<UnityEngine.SpatialTracking.TrackedPoseDriver>();
        if (_headTrackedPoseDriver != null)
            _headOriginalTrackingType = _headTrackedPoseDriver.trackingType;

        _avatarManager = GetComponent<U3DAvatarManager>();

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

        // Spawn the gaze pointer for the local player. Camera-forward raycast drives worldspace UI
        // events globally — works on desktop and in VR with the same code path. Fully local and
        // frame-based: it reads the Interact action itself and is not part of the networked input.
        if (_gazePointer == null && playerCamera != null)
        {
            GameObject pointerGO = new GameObject("U3DGazePointer");
            pointerGO.transform.SetParent(transform, false);
            _gazePointer = pointerGO.AddComponent<U3DGazePointer>();
            _gazePointer.Initialize(playerCamera, this);
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
            GameObject interactionManagerObj = new GameObject("U3DInteractionManager");
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
            // Compute pressed/released once and update _buttonsPrevious immediately
            // so all handlers read consistent edge state regardless of call order.
            var pressedThisFrame = input.Buttons.GetPressed(_buttonsPrevious);
            var releasedThisFrame = input.Buttons.GetReleased(_buttonsPrevious);
            _buttonsPrevious = input.Buttons;

            _jumpPressedThisFrame = pressedThisFrame.IsSet(U3DInputButtons.Jump);
            if (_jumpPressedThisFrame)
                _jumpPressedPending = true;

            HandleGroundCheck();

            if (_isInVRMode)
            {
                HandleVRMovement(input, pressedThisFrame);
                HandleVRPoseSync();
            }
            else
            {
                if (_currentRideable != null)
                {
                    bool wantsDismount = input.MovementInput.magnitude > 0.1f
                        || input.BothMouseHeld
                        || pressedThisFrame.IsSet(U3DInputButtons.Fly)
                        || pressedThisFrame.IsSet(U3DInputButtons.AutoRunToggle);

                    if (wantsDismount)
                        DismountRideable(_currentRideable);
                }

                if (_currentRideable == null)
                    HandleMovementFusion(input);

                if (_spawnFrameCount > SPAWN_PROTECTION_FRAMES)
                    HandleLookFusionFixed(input);
            }

            HandleButtonInputsFusion(input, pressedThisFrame, releasedThisFrame);
            HandleTeleportFusion(input, pressedThisFrame);
            HandleCameraPositioning();

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

        U3D.U3DRideableController resolvedRideable = null;
        if (NetworkRideableRef != default)
            Runner.TryFindBehaviour(NetworkRideableRef, out resolvedRideable);

        if (resolvedRideable != _remoteRideable)
        {
            if (resolvedRideable != null)
            {
                transform.SetParent(resolvedRideable.transform, true);
                transform.position = NetworkPosition;
            }
            else
            {
                transform.SetParent(null, true);
                transform.position = NetworkPosition;
            }
            _remoteRideable = resolvedRideable;
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
        if (_isInVRMode) return;
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

        cameraPitch = 0f;
        cameraPitchAdvanced = 0f;

        // Capture the player root's current yaw before TPD starts overriding the
        // camera rotation. This is the spawn-rotation direction we want the user
        // to face when VR initializes. The recenter logic in LateUpdate will rotate
        // the player root once TPD has reported a valid HMD pose, so that
        // playerRoot.rotation * hmdRotation lands the camera on this target yaw.
        _vrRecenterTargetYaw = transform.eulerAngles.y;
        _vrRecenterPending = true;

        if (_headTrackedPoseDriver != null)
        {
            _headTrackedPoseDriver.trackingType = UnityEngine.SpatialTracking.TrackedPoseDriver.TrackingType.RotationOnly;
            _headTrackedPoseDriver.enabled = true;
        }

        TryResolveHeadBone();

        if (cameraPivot != null && playerCamera != null)
        {
            playerCamera.transform.SetParent(transform);
            playerCamera.transform.localRotation = Quaternion.identity;
        }

        EnsureRawHmdReference();

        if (_vrTeleporter == null)
        {
            var go = new GameObject("U3DVRTeleporter");
            _vrTeleporter = go.AddComponent<U3D.XR.U3DVRTeleporter>();
            _vrTeleporter.ArcMaterial = vrTeleportMaterial;
            _vrTeleporter.Initialize(this, playerCamera);
        }
        else
        {
            // Re-entry path: teleporter already exists, just refresh its camera ref
            // in case anything reparented during the previous VR session.
            _vrTeleporter.UpdateCamera(playerCamera);
        }

        if (vrTeleportMaterial == null)
            Debug.LogWarning("U3DPlayerController: vrTeleportMaterial is not assigned. VR teleport arc will not render. Assign a URP/Unlit material in the Inspector on the player prefab.");

        if (_avatarManager != null) _avatarManager.SetVRMode(true);
    }

    private void EnsureRawHmdReference()
    {
        if (_rawHmdReference != null) return;

        GameObject hmdRefGO = new GameObject("U3D_RawHmdReference");
        hmdRefGO.transform.SetParent(transform, false);
        hmdRefGO.transform.localPosition = Vector3.zero;
        hmdRefGO.transform.localRotation = Quaternion.identity;

        var tpd = hmdRefGO.AddComponent<UnityEngine.SpatialTracking.TrackedPoseDriver>();
        tpd.SetPoseSource(
            UnityEngine.SpatialTracking.TrackedPoseDriver.DeviceType.GenericXRDevice,
            UnityEngine.SpatialTracking.TrackedPoseDriver.TrackedPose.Center);
        tpd.trackingType = UnityEngine.SpatialTracking.TrackedPoseDriver.TrackingType.PositionOnly;
        tpd.updateType = UnityEngine.SpatialTracking.TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        tpd.UseRelativeTransform = false;

        _rawHmdReference = hmdRefGO.transform;
    }

    private void ExitVRMode()
    {
        if (_headTrackedPoseDriver != null)
        {
            _headTrackedPoseDriver.trackingType = _headOriginalTrackingType;
            _headTrackedPoseDriver.enabled = false;
        }

        _avatarHeadBone = null;
        _vrRecenterPending = false;
        _vrSnapTurnedThisFrame = false;

        if (_rawHmdReference != null)
        {
            Destroy(_rawHmdReference.gameObject);
            _rawHmdReference = null;
        }

        if (_vrTeleporter != null)
        {
            _vrTeleporter.CancelAim();
            Destroy(_vrTeleporter.gameObject);
            _vrTeleporter = null;
        }

        _vrLocomotionSuppressed = false;

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

        if (_avatarManager != null) _avatarManager.SetVRMode(false);
    }

    private void TryResolveHeadBone()
    {
        if (_avatarHeadBone != null) return;
        if (_avatarManager == null) return;

        Animator avatarAnimator = _avatarManager.GetAvatarAnimator();
        if (avatarAnimator == null || !avatarAnimator.isHuman) return;

        _avatarHeadBone = avatarAnimator.GetBoneTransform(HumanBodyBones.Head);
    }

    void LateUpdate()
    {
        if (!_isInVRMode || !_isLocalPlayer || playerCamera == null) return;

        if (_avatarHeadBone == null)
        {
            TryResolveHeadBone();
            if (_avatarHeadBone == null) return;
        }

        // Recenter pass: runs once per VR session entry, on the first frame TPD
        // has written a non-identity rotation to the camera. The HMD's local-floor
        // reference space orientation is arbitrary on each session start (Quest 3
        // browser is especially unpredictable), so we rotate the body so that the
        // camera's WORLD yaw ends up at the captured target. Because the camera is
        // parented to the body and TPD writes localRotation, the body's required
        // rotation is computed in world space, not added to localRotation.
        if (_vrRecenterPending)
        {
            bool hmdHasValidPose = playerCamera.transform.localRotation != Quaternion.identity;

            if (hmdHasValidPose)
            {
                float cameraWorldYaw = playerCamera.transform.eulerAngles.y;
                float yawCorrection = Mathf.DeltaAngle(cameraWorldYaw, _vrRecenterTargetYaw);
                transform.Rotate(Vector3.up, yawCorrection);
                NetworkRotation = transform.rotation;
                cameraYaw = transform.eulerAngles.y;
                _vrRecenterPending = false;
            }
        }

        // Position the camera at the avatar's eye position. Runs every frame because
        // the avatar head bone is animated by the Animator and moves between frames.
        playerCamera.transform.position = _avatarHeadBone.position + transform.TransformVector(vrEyeOffset);

        // Body-follow-head with deadzone. The HMD's yaw relative to the body is the
        // camera's LOCAL yaw — because the camera is parented to the body and TPD
        // writes localRotation. We must NOT use the camera's world yaw here; that
        // would create an infinite spin loop, since rotating the body to "catch up"
        // would carry the camera with it and the world-yaw delta would never close.
        // Skipped on snap-turn frames so the catch-up doesn't fight intentional turns.
        if (!_vrRecenterPending && !_vrSnapTurnedThisFrame)
        {
            // localEulerAngles.y returns 0-360; convert to -180..180 for signed delta.
            float localYaw = playerCamera.transform.localEulerAngles.y;
            if (localYaw > 180f) localYaw -= 360f;
            float absDelta = Mathf.Abs(localYaw);

            if (absDelta > vrBodyFollowDeadzone)
            {
                float overshoot = absDelta - vrBodyFollowDeadzone;
                float catchUpThisFrame = Mathf.Min(overshoot, vrBodyFollowSpeed * Time.deltaTime);
                float rotateBy = catchUpThisFrame * Mathf.Sign(localYaw);

                transform.Rotate(Vector3.up, rotateBy);
                NetworkRotation = transform.rotation;
                cameraYaw = transform.eulerAngles.y;
            }
        }

        _vrSnapTurnedThisFrame = false;
    }

    /// <summary>
    /// Returns the authoritative VR eye position, computed the same way LateUpdate
    /// positions the camera in VR mode. Used by U3DVRTeleporter so the arc origin
    /// is anchored to the same point as the camera, regardless of script execution
    /// order. In non-VR mode, returns the camera's current world position.
    /// </summary>
    public Vector3 GetVREyePosition()
    {
        if (_isInVRMode && _avatarHeadBone != null)
            return _avatarHeadBone.position + transform.TransformVector(vrEyeOffset);

        if (playerCamera != null)
            return playerCamera.transform.position;

        return transform.position;
    }

    private void CreateHandVisuals() { }

    private void HandleVRMovement(U3DNetworkInputData input, NetworkButtons pressedThisFrame)
    {
        if (!enableMovement || !_isLocalPlayer) return;

        Vector2 vrMoveInput = input.MovementInput;
        if (isAutoRunning) vrMoveInput.y = 1f;
        float snapTurnInput = input.LookInput.x;

        // Snap turn always works, regardless of teleport state.
        if (Mathf.Abs(snapTurnInput) > 0.1f)
        {
            float turnDelta = snapTurnInput * 90f * Runner.DeltaTime;
            transform.Rotate(Vector3.up, turnDelta);
            NetworkRotation = transform.rotation;
            cameraYaw += turnDelta;

            // Tell LateUpdate's body-follow logic to skip this frame. Snap-turn
            // intentionally moves the body without moving the head; the catch-up
            // logic would otherwise see the resulting body-head mismatch as
            // "head outside deadzone" and rotate the body backward.
            _vrSnapTurnedThisFrame = true;
        }

        // Teleport gesture: stick click toggles arm/disarm. While armed, Tick consumes
        // the stick and suppresses locomotion. Forward push past threshold enters aim;
        // release fires.
        if (enableTeleport && _vrTeleporter != null)
        {
            if (pressedThisFrame.IsSet(U3DInputButtons.Teleport))
                _vrTeleporter.OnTeleportButtonPressed();

            bool suppressLocomotion = _vrTeleporter.Tick(vrMoveInput.y);
            _vrLocomotionSuppressed = suppressLocomotion;
            if (suppressLocomotion)
            {
                // Tick the CharacterController with zero motion so isGrounded refreshes
                // and ApplyGravityFixed can pull the player down after a teleport. Without
                // this, the CC's isGrounded state stays stuck at its pre-teleport value
                // and gravity early-returns, leaving the player floating indefinitely.
                characterController.Move(Vector3.zero);

                // Clear network movement state so remote players and the local animator
                // don't see "still walking" while standing in armed/aiming mode.
                NetworkIsMoving = false;
                NetworkIsSprinting = false;
                moveInput = Vector2.zero;
                return;
            }
        }

        // Movement-intent dismount while riding. Mirrors the desktop branch in spirit:
        // any signal that means "I want to move under my own power" dismounts the player.
        // Stick is only considered when the teleporter isn't consuming it (handled above
        // via the early return). Fly is a discrete press, unambiguous regardless of state.
        // Jump dismount is handled in HandleJumpFusionFixed and stays where it is.
        if (_currentRideable != null)
        {
            bool wantsDismount = vrMoveInput.magnitude > 0.1f
                || pressedThisFrame.IsSet(U3DInputButtons.Fly);

            if (wantsDismount)
                DismountRideable(_currentRideable);
        }

        if (NetworkIsClimbing)
        {
            moveInput = vrMoveInput;
            return;
        }

        Vector3 forward = playerCamera != null ? playerCamera.transform.forward : transform.forward;
        Vector3 right = playerCamera != null ? playerCamera.transform.right : transform.right;

        bool freeMovement = isFlying || isSwimming;

        if (!freeMovement)
        {
            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();
        }

        Vector3 moveDirection = (forward * vrMoveInput.y + right * vrMoveInput.x).normalized;
        float currentSpeed = GetCurrentSpeed() * VR_MOVEMENT_SPEED_MULTIPLIER;

        // Read the toggle state set by HandleButtonInputsFusion, not the held button state.
        // Reading IsSet directly would make VR sprint a hold (one-frame-only) while the
        // desktop path is a toggle, and the two would fight over isSprinting / NetworkIsSprinting.
        if (isSprinting)
            currentSpeed = runSpeed * VR_MOVEMENT_SPEED_MULTIPLIER;

        Vector3 moveVelocity = moveDirection * currentSpeed;

        if (freeMovement)
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

        moveInput = vrMoveInput;

        NetworkPosition = transform.position;
        NetworkRotation = transform.rotation;
        NetworkIsMoving = moveVelocity.magnitude > 0.1f;
        NetworkIsSprinting = isSprinting;
    }

    private void HandleVRPoseSync()
    {
        if (!_isLocalPlayer) return;
        if (playerCamera != null)
            NetworkCameraPitch = playerCamera.transform.localEulerAngles.x;
    }

    private void UpdateRemoteVRVisuals() { }
    private void HideHandVisuals() { }

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
        if (!IsLocalLookMoveInputAuthoritative()) return;

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

        bool freeMovement = isFlying || isSwimming;

        if (!freeMovement)
        {
            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();
        }

        Vector3 moveDirection = (forward * finalMovement.y + right * finalMovement.x).normalized;
        float currentSpeed = GetCurrentSpeed();
        Vector3 moveVelocity = moveDirection * currentSpeed;

        if (freeMovement)
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
        if (!IsLocalLookMoveInputAuthoritative()) return;

        Vector2 rawLookInput = input.LookInput;

        if (lookInverted)
            rawLookInput.y = -rawLookInput.y;

        Vector2 sensitivityAdjustedInput = rawLookInput * _runtimeMouseSensitivity;

        bool touchInputActive = U3D.Input.U3DSimpleTouchZones.Instance != null
            && U3D.Input.U3DSimpleTouchZones.Instance.IsTouchEnabled;

        Vector2 finalLookInput;
        if (enableMouseSmoothing && !touchInputActive)
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

    /// <summary>
    /// Single authority for whether local non-VR game look/move input is live
    /// this frame. Replaces the previous per-handler re-derivation (IsCursorLocked
    /// gate in look, implicit assumption in movement). Consulted only on the non-VR
    /// local path — VR routes through HandleVRMovement before these handlers are
    /// reached, so this method intentionally contains no VR branch.
    ///
    /// Desktop: authoritative only when the pointer is captured (IsCursorLocked),
    /// preserving free-cursor-over-UI suppression exactly as before.
    /// Touch: authoritative when touch is the active input source. Touch has no
    /// pointer-capture concept; the touch zone component already owns the "is the
    /// user driving input" determination, and the network manager already uses the
    /// same signal to decide it is reading touch into the networked input struct.
    /// </summary>
    private bool IsLocalLookMoveInputAuthoritative()
    {
        bool touchInputActive = U3D.Input.U3DSimpleTouchZones.Instance != null
            && U3D.Input.U3DSimpleTouchZones.Instance.IsTouchEnabled;

        if (touchInputActive)
            return true;

        return IsCursorLocked();
    }

    void HandleAdvancedMouseControls(U3DNetworkInputData input)
    {
        if (!enableAdvancedCamera || cameraPivot == null) return;

        bool wasLeftMouseDragging = isLeftMouseDragging;

        isLeftMouseDragging = input.LeftMouseHeld;
        isRightMouseDragging = input.RightMouseHeld;
        isBothMouseForward = input.BothMouseHeld;

        if (wasLeftMouseDragging && !isLeftMouseDragging && !isRightMouseDragging && !isBothMouseForward)
            cameraYaw = transform.eulerAngles.y;

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

    void HandleButtonInputsFusion(U3DNetworkInputData input, NetworkButtons pressed, NetworkButtons released)
    {
        if (!_isLocalPlayer) return;

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

        if (isCrouching && NetworkIsMoving && !isFlying && !isSwimming)
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
    }

    void HandleJumpFusionFixed()
    {
        if (NetworkIsClimbing) return;
        if (isFlying) return;

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

    void HandleTeleportFusion(U3DNetworkInputData input, NetworkButtons pressed)
    {
        if (!enableTeleport || !_isLocalPlayer || _isInVRMode) return;
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

            _mouseInputBuffer.Clear();
            _mouseTimeBuffer.Clear();
            _smoothedMouseInput = Vector2.zero;
            lookInput = Vector2.zero;
        }
    }

    void HandleCameraPositioning()
    {
        if (!_isLocalPlayer) return;
        if (_isInVRMode) return;

        NetworkIsFirstPerson = isFirstPerson;

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
        if (isFlying || isSwimming || isGrounded || NetworkIsClimbing || !_isLocalPlayer) return;

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
        currentCameraDistance = thirdPersonCameraDistance;
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

    // Push-driven identity callback. Invoked by FirebaseIntegration when the
    // local user's profile resolves (either initially or on re-delivery).
    // Only the local player ever subscribes, so the guard is defensive.
    private void HandleLocalProfileReady(FirebaseIntegration.UserInfo profile)
    {
        if (!_isLocalPlayer) return;
        ApplyDisplayNameFromProfile(profile);
    }

    // Writes the chosen displayName to the networked field, truncated to the
    // 40-character Capacity defined on NetworkedDisplayName. Empty/null names
    // leave NetworkedDisplayName unset so U3DPlayerNametag.ResolveDisplayName
    // falls through to the deterministic generated-name path.
    private void ApplyDisplayNameFromProfile(FirebaseIntegration.UserInfo profile)
    {
        if (profile == null) return;
        if (string.IsNullOrEmpty(profile.displayName)) return;

        string chosenName = profile.displayName;
        if (chosenName.Length > 40)
            chosenName = chosenName.Substring(0, 40);

        NetworkedDisplayName = chosenName;
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
    public bool IsCameraTransitioning => isTransitioning;
    public Vector3 Velocity => velocity;
    public float CurrentSpeed => GetCurrentSpeed();
    public bool IsLocalPlayer => _isLocalPlayer;
    public bool IsJumping => NetworkIsJumping;
    public bool IsInVRMode => _isInVRMode;
    public Vector2 MoveInput => moveInput;
    public bool JumpPressedThisFrame => _jumpPressedPending;
    public CharacterController CharacterController => characterController;
    public Transform CameraTransform => playerCamera != null ? playerCamera.transform : null;
    public bool IsInVR => _isInVRMode;
    public bool EnableMovement => enableMovement;
    public bool EnableJumping => enableJumping;
    public bool EnableSprintToggle => enableSprintToggle;
    public bool EnableCrouchToggle => enableCrouchToggle;
    public bool EnableFlying => enableFlying;
    public bool EnableAutoRun => enableAutoRun;
    public bool EnableTeleport => enableTeleport;
    public bool EnableViewZoom => enableViewZoom;
    public bool EnableAdvancedCamera => enableAdvancedCamera;
    public Transform RawHmdReference => _rawHmdReference;

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
        NetworkRotation = transform.rotation;

        // HandleMovementFusion snaps the body to cameraYaw on the first step after standing
        // still. An external rotation (e.g. respawn) that doesn't update cameraYaw leaves it
        // stale, so that snap swings the view back to the pre-respawn heading. Keep them matched.
        cameraYaw = yRotation;
    }

    public void SetCameraPitch(float pitch)
    {
        if (!_isLocalPlayer) return;
        cameraPitch = pitch;
    }

    public static U3DPlayerController FindLocalPlayer()
    {
        U3DPlayerController[] allPlayers = FindObjectsByType<U3DPlayerController>(FindObjectsSortMode.None);
        foreach (U3DPlayerController player in allPlayers)
        {
            if (player.IsLocalPlayer)
                return player;
        }
        return null;
    }

    public void SetSwimmingState(bool isSwimming)
    {
        if (!_isLocalPlayer) return;
        this.isSwimming = isSwimming;
        NetworkIsSwimming = isSwimming;
        if (isSwimming) velocity = Vector3.zero;
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

        NetworkRideableRef = rideable;
    }

    public void DismountRideable(U3D.U3DRideableController rideable)
    {
        if (!_isLocalPlayer) return;
        if (_currentRideable != rideable) return;

        _currentRideable = null;

        transform.SetParent(null, true);
        characterController.enabled = true;

        NetworkPosition = transform.position;

        NetworkRideableRef = default;
    }

    public bool IsRiding(U3D.U3DRideableController rideable) => _currentRideable == rideable;
}