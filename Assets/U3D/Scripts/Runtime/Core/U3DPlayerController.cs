using U3D;
using UnityEngine;
using UnityEngine.InputSystem;
using Fusion;
using System.Collections.Generic;

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

    // ENHANCED: Platform-aware mouse sensitivity system
    [Header("Mouse Sensitivity Settings")]
    [SerializeField] private float baseMouseSensitivity = 1.0f; // Base sensitivity for desktop
    [SerializeField] private float webglSensitivityMultiplier = 0.25f; // WebGL reduction factor
    [SerializeField] private float mobileSensitivityMultiplier = 0.8f; // Mobile adjustment
    [SerializeField] private float userSensitivityMultiplier = 1.0f; // User preference (saved)
    [SerializeField] private bool enableMouseSmoothing = true;
    [SerializeField] private float mouseSmoothingAmount = 0.1f;

    // Legacy compatibility (calculated at runtime)
    [HideInInspector] private float mouseSensitivity; // Calculated from base + platform + user
    [HideInInspector] private float cameraOrbitSensitivity; // Calculated from base + platform + user

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
        new Keyframe(0f, 0f),     // First person: no distance
        new Keyframe(1f, 5f)      // Third person: full distance
    );
    [SerializeField]
    private AnimationCurve cameraHeightCurve = new AnimationCurve(
        new Keyframe(0f, 1.5f),   // FIXED: First person at eye level (1.5 units)
        new Keyframe(1f, 1.5f)    // FIXED: Third person also at eye level (1.5 units)
    );
    [SerializeField] private float transitionTime = 1.5f;

    // ENHANCED: Runtime sensitivity calculation
    private float _runtimeMouseSensitivity;
    private float _runtimeOrbitSensitivity;
    private RuntimePlatform _currentPlatform;

    // Camera transition state
    private float currentTransitionValue = 0f; // 0 = first person, 1 = third person
    private float targetTransitionValue = 0f;
    private bool isTransitioning = false;
    private Vector3 originalFirstPersonPosition;

    // Camera pivot system
    private Transform cameraPivot;
    private float cameraYaw = 0f;  // Horizontal orbit angle
    private float cameraPitchAdvanced = 0f;  // Vertical orbit angle (separate from existing cameraPitch)
    private bool isLeftMouseDragging = false;
    private bool isRightMouseDragging = false;
    private bool isBothMouseForward = false;

    // Advanced movement state
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

    // CLEANED: Core Networked Properties for Animation System (HIDDEN from Creator users)
    [HideInInspector][Networked] public Vector3 NetworkPosition { get; set; }
    [HideInInspector][Networked] public Quaternion NetworkRotation { get; set; }
    [HideInInspector][Networked] public bool NetworkIsMoving { get; set; }
    [HideInInspector][Networked] public bool NetworkIsSprinting { get; set; }
    [HideInInspector][Networked] public bool NetworkIsCrouching { get; set; }
    [HideInInspector][Networked] public bool NetworkIsFlying { get; set; }
    [HideInInspector][Networked] public float NetworkCameraPitch { get; set; }
    [HideInInspector][Networked] public bool NetworkIsInteracting { get; set; }
    [HideInInspector][Networked] public bool NetworkIsJumping { get; set; }

    // UPDATED: Environmental states (set by external trigger systems) - HIDDEN from Creator users
    [HideInInspector][Networked] public bool NetworkIsSwimming { get; set; }
    [HideInInspector][Networked] public bool NetworkIsClimbing { get; set; }

    // Mouse input smoothing (add after existing camera state variables)
    private Queue<Vector2> _mouseInputBuffer = new Queue<Vector2>();
    private Queue<float> _mouseTimeBuffer = new Queue<float>();
    private const float MOUSE_SMOOTHING_WINDOW = 0.015f; // 15ms smoothing window
    private Vector2 _smoothedMouseInput = Vector2.zero;

    // Core Components
    private CharacterController characterController;
    private PlayerInput playerInput;
    private Camera playerCamera;

    // Movement State
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

    // Camera State
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
    private const int SPAWN_PROTECTION_FRAMES = 5; // Protect spawn rotation for 5 frames

    // Network State
    private bool _isLocalPlayer;
    private float _lastNetworkSendTime;
    private Vector3 _lastSentPosition;
    private Quaternion _lastSentRotation;
    private bool _justTeleported = false;

    // WebGL cursor management
    private U3DWebGLCursorManager _cursorManager;

    // FUSION INPUT TRACKING
    private NetworkButtons _buttonsPrevious;

    // CORRECTED: No local input actions - NetworkManager handles all input
    private U3D.Networking.U3DFusionNetworkManager _networkManager;

    // ENHANCED: Mouse sensitivity calculation methods
    void CalculateRuntimeSensitivity()
    {
        _currentPlatform = Application.platform;

        // Start with base sensitivity
        float platformMultiplier = 1.0f;

        // Apply platform-specific adjustments based on web research
        switch (_currentPlatform)
        {
            case RuntimePlatform.WebGLPlayer:
                platformMultiplier = webglSensitivityMultiplier; // 0.25f default
                Debug.Log($"🌐 WebGL Platform: Applying {webglSensitivityMultiplier}x sensitivity reduction");
                break;

            case RuntimePlatform.IPhonePlayer:
            case RuntimePlatform.Android:
                platformMultiplier = mobileSensitivityMultiplier; // 0.8f default
                Debug.Log($"📱 Mobile Platform: Applying {mobileSensitivityMultiplier}x sensitivity adjustment");
                break;

            default:
                platformMultiplier = 1.0f; // Desktop - no adjustment needed
                Debug.Log("🖥️ Desktop Platform: Using base sensitivity");
                break;
        }

        // Calculate final runtime values
        _runtimeMouseSensitivity = baseMouseSensitivity * platformMultiplier * userSensitivityMultiplier;
        _runtimeOrbitSensitivity = baseMouseSensitivity * platformMultiplier * userSensitivityMultiplier;

        // Update legacy compatibility values
        mouseSensitivity = _runtimeMouseSensitivity;
        cameraOrbitSensitivity = _runtimeOrbitSensitivity;

        Debug.Log($"✅ Mouse Sensitivity Calculated: Base={baseMouseSensitivity}, Platform={platformMultiplier}, User={userSensitivityMultiplier}, Final={_runtimeMouseSensitivity}");
    }

    // ENHANCED: User settings methods
    public void SetUserSensitivity(float sensitivity)
    {
        userSensitivityMultiplier = Mathf.Clamp(sensitivity, 0.1f, 3.0f);
        CalculateRuntimeSensitivity();
        SaveSensitivitySettings();
        Debug.Log($"🎯 User sensitivity updated: {userSensitivityMultiplier}");
    }

    public float GetUserSensitivity()
    {
        return userSensitivityMultiplier;
    }

    public float GetEffectiveSensitivity()
    {
        return _runtimeMouseSensitivity;
    }

    void LoadSensitivitySettings()
    {
        userSensitivityMultiplier = PlayerPrefs.GetFloat("U3D_MouseSensitivity", 1.0f);
        Debug.Log($"📂 Loaded user sensitivity: {userSensitivityMultiplier}");
    }

    void SaveSensitivitySettings()
    {
        PlayerPrefs.SetFloat("U3D_MouseSensitivity", userSensitivityMultiplier);
        PlayerPrefs.Save();
        Debug.Log($"💾 Saved user sensitivity: {userSensitivityMultiplier}");
    }

    public override void Spawned()
    {
        // In Shared Mode, each client has authority over their own player
        _isLocalPlayer = Object.HasStateAuthority;

        // Initialize components
        InitializeComponents();

        // ENHANCED: Calculate platform-appropriate sensitivity
        LoadSensitivitySettings();
        CalculateRuntimeSensitivity();

        // Configure for local vs remote player
        ConfigurePlayerForNetworking();

        // Reset spawn frame counter
        _spawnFrameCount = 0;

        // Initialize camera system with spawn rotation
        if (_isLocalPlayer && enableAdvancedCamera && cameraPivot != null)
        {
            // Initialize camera yaw with the spawn rotation
            cameraYaw = transform.eulerAngles.y;
            Debug.Log($"🎯 Initialized camera yaw with spawn rotation: {cameraYaw}°");
        }
    }

    void InitializeCameraPivot()
    {
        if (!enableAdvancedCamera) return;

        // Store original first person position at eye level
        originalFirstPersonPosition = firstPersonPosition;

        // FIXED: Create camera pivot at eye level, NOT at ground level
        GameObject pivotGO = new GameObject("CameraPivot");
        cameraPivot = pivotGO.transform;
        cameraPivot.SetParent(transform);

        // CRITICAL FIX: Position pivot at eye level for proper rotation
        cameraPivot.localPosition = firstPersonPosition; // Eye level, not Vector3.zero
        cameraPivot.localRotation = Quaternion.identity;

        // Make camera child of pivot
        if (playerCamera != null)
        {
            playerCamera.transform.SetParent(cameraPivot);

            // Start in first person position (relative to the now eye-level pivot)
            UpdateCameraTransitionPosition();

            // Store initial yaw from character rotation
            cameraYaw = transform.eulerAngles.y;
            cameraPitchAdvanced = 0f;
        }

        Debug.Log("✅ Advanced AAA camera pivot system initialized at eye level with smooth transitions");
    }

    void UpdateCameraTransitionPosition()
    {
        if (cameraPivot == null || playerCamera == null) return;

        // Evaluate curves based on current transition value
        float distance = cameraDistanceCurve.Evaluate(currentTransitionValue);
        float heightOffset = cameraHeightCurve.Evaluate(currentTransitionValue);

        // Position camera relative to the eye-level pivot
        Vector3 targetPosition;

        if (currentTransitionValue <= 0.01f)
        {
            // Pure first person - camera at pivot origin (which is now at eye level)
            targetPosition = Vector3.zero; // Relative to eye-level pivot
        }
        else
        {
            // Transitioning or third person - offset from eye-level pivot
            // Height offset is now relative to eye level, not ground
            float relativeHeight = heightOffset - firstPersonPosition.y; // Convert to relative offset
            targetPosition = new Vector3(0f, relativeHeight, -distance);
        }

        // Apply crouch offset
        if (isCrouching)
        {
            targetPosition.y += crouchCameraOffset;
        }

        // Apply camera collision detection in third person
        if (currentTransitionValue > 0.01f && enableCameraCollision)
        {
            targetPosition = GetCollisionSafeCameraPosition(targetPosition);
        }

        playerCamera.transform.localPosition = targetPosition;
    }

    void Awake()
    {
        // Get required components
        characterController = GetComponent<CharacterController>();
        playerInput = GetComponent<PlayerInput>();
        playerCamera = GetComponentInChildren<Camera>();

        if (playerCamera == null)
        {
            Debug.LogError("U3DPlayerController: No Camera found in children. Please add a Camera as a child object.");
            enabled = false;
            return;
        }

        // Initialize camera settings
        firstPersonPosition = playerCamera.transform.localPosition;
        thirdPersonPosition = firstPersonPosition + Vector3.back * thirdPersonDistance;
        currentCameraDistance = 0f;
        targetFOV = defaultFOV;
        playerCamera.fieldOfView = defaultFOV;

        InitializeCameraPivot();

        // Load player preferences
        LoadPlayerPreferences();
    }

    void InitializeComponents()
    {
        if (!_isLocalPlayer) return;

        // Get cursor manager for WebGL builds
        _cursorManager = FindAnyObjectByType<U3DWebGLCursorManager>();

        if (_cursorManager != null)
        {
            // WebGL mode - cursor manager handles locking
            Debug.Log("✅ WebGL Cursor Manager found - delegating cursor control");
        }
        else
        {
            // Non-WebGL mode - lock cursor directly
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Debug.Log("✅ Non-WebGL mode - cursor locked directly");
        }
    }

    void ConfigurePlayerForNetworking()
    {
        if (_isLocalPlayer)
        {
            // CORRECTED: Keep PlayerInput ENABLED but disable its notifications
            if (playerInput != null)
            {
                Debug.Log("✅ Local Player: Configuring PlayerInput for Fusion compatibility");

                // Set notification behavior to disable Unity callbacks but keep device pairing
                playerInput.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;

                Debug.Log($"PlayerInput configured - notifications disabled, device pairing retained");
            }

            if (playerCamera != null)
            {
                playerCamera.enabled = true;
                playerCamera.tag = "MainCamera";
            }
        }
        else
        {
            // Remote player - disable input and camera
            if (playerInput != null)
                playerInput.enabled = false;
            if (playerCamera != null)
            {
                playerCamera.enabled = false;
                playerCamera.tag = "Untagged";
            }

            // Disable character controller for remote players
            if (characterController != null)
                characterController.enabled = false;
        }
    }

    void Start()
    {
        // Set initial perspective based on mode (local player only)
        if (_isLocalPlayer)
        {
            switch (perspectiveMode)
            {
                case PerspectiveMode.FirstPersonOnly:
                    SetFirstPerson();
                    break;
                case PerspectiveMode.ThirdPersonOnly:
                    SetThirdPerson();
                    break;
                case PerspectiveMode.SmoothScroll:
                    SetFirstPerson();
                    break;
            }
        }
        else
        {
            // CREATE NAMETAG FOR REMOTE PLAYERS ONLY
            CreateNametag();
        }
    }

    void CreateNametag()
    {
        // Create nametag anchor above player head
        var nametagAnchor = new GameObject("NametagAnchor");
        nametagAnchor.transform.SetParent(transform);
        nametagAnchor.transform.localPosition = Vector3.up * 2.5f;

        // Add and initialize nametag component
        var nametag = nametagAnchor.AddComponent<U3D.Networking.U3DPlayerNametag>();
        nametag.Initialize(this);
    }

    bool IsCursorLocked()
    {
        // Check cursor manager first (WebGL)
        if (_cursorManager != null)
        {
            return _cursorManager.IsCursorLocked;
        }

        // Fallback to Unity cursor state (non-WebGL)
        return Cursor.lockState == CursorLockMode.Locked;
    }

    // CORRECTED: Method called by NetworkManager after spawning
    public void RefreshInputActionsFromNetworkManager(U3D.Networking.U3DFusionNetworkManager networkManager)
    {
        if (!_isLocalPlayer) return;

        _networkManager = networkManager;

        Debug.Log("✅ PlayerController linked to NetworkManager for input");
    }

    // FUSION 2 REQUIRED: Replace Update with FixedUpdateNetwork
    public override void FixedUpdateNetwork()
    {
        // Only process for local player (StateAuthority in Shared Mode)
        if (!_isLocalPlayer) return;

        // Increment spawn frame counter
        _spawnFrameCount++;

        // Get Fusion input instead of Unity Input System
        if (GetInput<U3DNetworkInputData>(out var input))
        {
            // Process all input in the fixed network update
            HandleGroundCheck();
            HandleMovementFusion(input);

            // Only start handling look input after spawn protection period
            if (_spawnFrameCount > SPAWN_PROTECTION_FRAMES)
            {
                HandleLookFusionFixed(input);
            }

            HandleButtonInputsFusion(input);
            HandleTeleportFusion(input);
            HandleCameraPositioning();
            ApplyGravityFixed();
        }
    }

    // FUSION RENDER: For camera and visual updates
    public override void Render()
    {
        // Only apply interpolation for remote players
        if (_isLocalPlayer)
        {
            // Local player - handle camera rotation in Render for smooth visuals
            HandleLocalCameraRender();
            HandleZoom();

            // CRITICAL: Clear teleport flag after one Render frame
            if (_justTeleported)
            {
                _justTeleported = false;
                Debug.Log("🎯 Teleport flag cleared - Render won't override position this frame");
            }

            return; // Don't interpolate local player position!
        }

        // Remote player interpolation with Unity 6 optimization
        if (NetworkRotation == Quaternion.identity ||
            float.IsNaN(NetworkRotation.x) || float.IsNaN(NetworkRotation.y) ||
            float.IsNaN(NetworkRotation.z) || float.IsNaN(NetworkRotation.w))
        {
            return;
        }

        // SKIP INTERPOLATION if remote player just teleported
        if (_justTeleported)
        {
            _justTeleported = false;
            Debug.Log("🎯 Remote player teleported - skipping interpolation this frame");
            return;
        }

        // Only interpolate if there's a significant difference to prevent override issues
        float positionDifference = Vector3.Distance(transform.position, NetworkPosition);
        float rotationDifference = Quaternion.Angle(transform.rotation, NetworkRotation);

        // Only interpolate if positions are significantly different (prevents teleport override)
        if (positionDifference > 0.1f)
        {
            transform.position = Vector3.Lerp(transform.position, NetworkPosition, Time.deltaTime * 15f);
        }

        // Only interpolate rotation if significantly different
        if (rotationDifference > 0.5f && rotationDifference < 180f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, NetworkRotation, Time.deltaTime * 12f);
        }

        // Apply camera pitch for remote players (head movement)
        if (playerCamera != null)
        {
            Vector3 cameraRotation = playerCamera.transform.localEulerAngles;
            cameraRotation.x = NetworkCameraPitch;
            playerCamera.transform.localEulerAngles = cameraRotation;
        }
    }

    void HandleLocalCameraRender()
    {
        if (!enableMovement || !_isLocalPlayer || playerCamera == null) return;

        // Stop camera rotation updates when cursor is released
        if (!IsCursorLocked()) return;

        // Apply smooth camera rotation in Render for consistent timing
        Vector3 cameraRotation = playerCamera.transform.localEulerAngles;
        cameraRotation.x = cameraPitch;
        playerCamera.transform.localEulerAngles = cameraRotation;
    }

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
        {
            NetworkIsJumping = false; // Only reset when landed
        }
    }

    void HandleMovementFusion(U3DNetworkInputData input)
    {
        if (!enableMovement || !_isLocalPlayer) return;

        // Get base movement input
        moveInput = input.MovementInput;

        // Handle Advanced AAA-style keyboard controls
        Vector2 advancedMovement = HandleAdvancedKeyboardMovement(input);

        // Both mouse buttons = move forward (Advanced AAA style)
        if (isBothMouseForward)
        {
            advancedMovement.y = 1f; // Force forward movement
        }

        // Handle auto-run
        if (isAutoRunning)
        {
            advancedMovement.y = 1f;
        }

        // Use Advanced movement if any Advanced controls are active
        Vector2 finalMovement = (advancedMovement.magnitude > 0.1f) ? advancedMovement : moveInput;

        // NEW: Snap character to camera direction when starting to move after left-click camera orbiting
        if (enableAdvancedCamera && cameraPivot != null)
        {
            bool isStartingToMove = (finalMovement.magnitude > 0.1f && !NetworkIsMoving);

            if (isStartingToMove && !isRightMouseDragging)
            {
                // Snap character rotation to match camera facing direction
                float targetYaw = cameraYaw;
                transform.rotation = Quaternion.Euler(0, targetYaw, 0);
                NetworkRotation = transform.rotation;

                Debug.Log($"🔄 Character snapped to camera direction: {targetYaw}°");
            }
        }

        // Calculate movement direction relative to camera or character
        Vector3 forward, right;

        if (enableAdvancedCamera && cameraPivot != null)
        {
            // Use camera pivot forward for movement direction (Advanced AAA style)
            forward = cameraPivot.forward;
            right = cameraPivot.right;
        }
        else
        {
            // Use player camera forward (legacy)
            forward = playerCamera.transform.forward;
            right = playerCamera.transform.right;
        }

        // Remove Y component for ground movement (unless flying)
        if (!isFlying)
        {
            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();
        }

        Vector3 moveDirection = (forward * finalMovement.y + right * finalMovement.x).normalized;

        // Apply speed based on current state
        float currentSpeed = GetCurrentSpeed();
        Vector3 moveVelocity = moveDirection * currentSpeed;

        // Apply movement
        if (isFlying)
        {
            // 6DOF movement when flying
            Vector3 flyDirection = moveDirection;
            if (input.Buttons.IsSet(U3DInputButtons.Jump))
                flyDirection += Vector3.up;
            if (input.Buttons.IsSet(U3DInputButtons.Crouch))
                flyDirection += Vector3.down;

            characterController.Move(flyDirection * currentSpeed * Runner.DeltaTime);
        }
        else
        {
            // Ground-based movement
            characterController.Move(moveVelocity * Runner.DeltaTime);
        }

        // Update networked position
        NetworkPosition = transform.position;
        NetworkRotation = transform.rotation;
        NetworkIsMoving = moveVelocity.magnitude > 0.1f;
    }

    Vector2 HandleAdvancedKeyboardMovement(U3DNetworkInputData input)
    {
        Vector2 advancedMovement = Vector2.zero;

        // Forward/Backward (W/S) - unchanged behavior
        if (moveInput.y != 0)
        {
            advancedMovement.y = moveInput.y;
        }

        // Handle A/D as character turning (Advanced AAA style) OR legacy strafing
        if (enableAdvancedCamera)
        {
            // Advanced Mode: A/D turn character (only when not holding right mouse)
            if (!isRightMouseDragging)
            {
                if (input.TurnLeft)
                {
                    transform.Rotate(Vector3.up, -characterTurnSpeed * Runner.DeltaTime);
                    NetworkRotation = transform.rotation;

                    // Update camera yaw to follow character turning
                    if (cameraPivot != null)
                    {
                        cameraYaw = transform.eulerAngles.y;
                    }
                }
                if (input.TurnRight)
                {
                    transform.Rotate(Vector3.up, characterTurnSpeed * Runner.DeltaTime);
                    NetworkRotation = transform.rotation;

                    // Update camera yaw to follow character turning
                    if (cameraPivot != null)
                    {
                        cameraYaw = transform.eulerAngles.y;
                    }
                }
            }

            // Q/E strafe (Advanced AAA style)
            if (input.StrafeLeft)
                advancedMovement.x = -1f;
            if (input.StrafeRight)
                advancedMovement.x = 1f;
        }
        else
        {
            // Legacy mode: A/D strafe (existing behavior)
            advancedMovement.x = moveInput.x;
        }

        return advancedMovement;
    }

    void HandleLookFusionFixed(U3DNetworkInputData input)
    {
        if (!enableMovement || !_isLocalPlayer) return;

        // Stop camera movement when cursor is released for UI interaction
        if (!IsCursorLocked()) return;

        // Get raw mouse input
        Vector2 rawLookInput = input.LookInput;

        // Apply look inversion if enabled
        if (lookInverted)
            rawLookInput.y = -rawLookInput.y;

        // ENHANCED: Apply runtime sensitivity and optional smoothing
        Vector2 sensitivityAdjustedInput = rawLookInput * _runtimeMouseSensitivity;

        Vector2 finalLookInput;
        if (enableMouseSmoothing)
        {
            // Add current input to smoothing buffer
            float currentTime = (float)Runner.SimulationTime;
            _mouseInputBuffer.Enqueue(sensitivityAdjustedInput);
            _mouseTimeBuffer.Enqueue(currentTime);

            // Remove old entries outside smoothing window
            while (_mouseTimeBuffer.Count > 0 && (currentTime - _mouseTimeBuffer.Peek()) > MOUSE_SMOOTHING_WINDOW)
            {
                _mouseInputBuffer.Dequeue();
                _mouseTimeBuffer.Dequeue();
            }

            // Calculate smoothed mouse input (average over smoothing window)
            Vector2 smoothedLookInput = Vector2.zero;
            if (_mouseInputBuffer.Count > 0)
            {
                foreach (Vector2 sample in _mouseInputBuffer)
                {
                    smoothedLookInput += sample;
                }
                smoothedLookInput /= _mouseInputBuffer.Count;
            }

            finalLookInput = Vector2.Lerp(_smoothedMouseInput, smoothedLookInput, mouseSmoothingAmount);
        }
        else
        {
            finalLookInput = sensitivityAdjustedInput;
        }

        // Store processed input for use in other methods
        _smoothedMouseInput = finalLookInput;
        lookInput = finalLookInput; // Update existing lookInput for compatibility

        // Handle Advanced AAA-style mouse controls first (takes priority)
        HandleAdvancedMouseControls(input);

        // NEW: Always-on free look (AAA standard) when no advanced controls are active
        if (enableAlwaysFreeLook && !isLeftMouseDragging && !isRightMouseDragging && !isBothMouseForward)
        {
            if (enableAdvancedCamera && cameraPivot != null)
            {
                // Advanced camera system: Always-on free look
                if (Mathf.Abs(finalLookInput.x) > 0.01f)
                {
                    // Rotate character around Y-axis (like right-click mode but always on)
                    float yawDelta = finalLookInput.x;
                    transform.Rotate(Vector3.up, yawDelta);

                    // Keep camera yaw in sync with character
                    cameraYaw += yawDelta;

                    NetworkRotation = transform.rotation;
                }

                if (Mathf.Abs(finalLookInput.y) > 0.01f)
                {
                    // Camera pitch follows mouse
                    cameraPitchAdvanced -= finalLookInput.y;
                    cameraPitchAdvanced = Mathf.Clamp(cameraPitchAdvanced, lookDownLimit, lookUpLimit);
                    NetworkCameraPitch = cameraPitchAdvanced;
                }

                // Apply camera pivot rotation
                if (cameraPivot != null)
                {
                    cameraPivot.rotation = Quaternion.Euler(cameraPitchAdvanced, cameraYaw, 0f);
                }
            }
            else
            {
                // Legacy camera system: Always-on free look
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
        // Fallback: Legacy standard camera control (when always-on is disabled)
        else if (!enableAlwaysFreeLook && !isLeftMouseDragging && !isRightMouseDragging && !enableAdvancedCamera)
        {
            // Original legacy camera system for compatibility
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

        // Update mouse button states
        isLeftMouseDragging = input.LeftMouseHeld;
        isRightMouseDragging = input.RightMouseHeld;
        isBothMouseForward = input.BothMouseHeld;

        // Use processed mouse input (already has sensitivity and smoothing applied)
        Vector2 processedInput = _smoothedMouseInput;

        // BOTH mouse buttons: Move forward with mouse steering (NEW - AAA style)
        if (isBothMouseForward)
        {
            // Allow mouse steering while moving forward with both buttons
            if (Mathf.Abs(processedInput.x) > 0.01f)
            {
                // Rotate character around Y-axis for steering
                float yawDelta = processedInput.x;
                transform.Rotate(Vector3.up, yawDelta);

                // Keep camera yaw in sync with character
                cameraYaw += yawDelta;

                NetworkRotation = transform.rotation;
            }

            if (Mathf.Abs(processedInput.y) > 0.01f)
            {
                // Camera pitch follows mouse for look up/down while moving
                cameraPitchAdvanced -= processedInput.y;
                cameraPitchAdvanced = Mathf.Clamp(cameraPitchAdvanced, lookDownLimit, lookUpLimit);
                NetworkCameraPitch = cameraPitchAdvanced;
            }
        }
        // Right-click drag: Rotate character AND camera together (Advanced AAA style)
        else if (isRightMouseDragging && !isLeftMouseDragging)
        {
            if (Mathf.Abs(processedInput.x) > 0.01f)
            {
                // Rotate character around Y-axis
                float yawDelta = processedInput.x;
                transform.Rotate(Vector3.up, yawDelta);

                // Keep camera yaw in sync with character
                cameraYaw += yawDelta;

                NetworkRotation = transform.rotation;
            }

            if (Mathf.Abs(processedInput.y) > 0.01f)
            {
                // Camera pitch follows mouse
                cameraPitchAdvanced -= processedInput.y;
                cameraPitchAdvanced = Mathf.Clamp(cameraPitchAdvanced, lookDownLimit, lookUpLimit);
                NetworkCameraPitch = cameraPitchAdvanced;
            }
        }
        // Left-click drag: Orbit camera around character WITHOUT turning character
        else if (isLeftMouseDragging && !isRightMouseDragging)
        {
            if (Mathf.Abs(processedInput.x) > 0.01f)
            {
                // Orbit camera horizontally around character
                cameraYaw += processedInput.x;
            }

            if (Mathf.Abs(processedInput.y) > 0.01f)
            {
                // Orbit camera vertically around character
                cameraPitchAdvanced -= processedInput.y;
                cameraPitchAdvanced = Mathf.Clamp(cameraPitchAdvanced, lookDownLimit, lookUpLimit);
            }

            // Don't sync character rotation - only camera orbits
            NetworkCameraPitch = cameraPitchAdvanced;
        }

        // Apply camera pivot rotation (this creates the orbit effect)
        if (cameraPivot != null)
        {
            // Set pivot rotation for camera orbit
            cameraPivot.rotation = Quaternion.Euler(cameraPitchAdvanced, cameraYaw, 0f);
        }
    }

    void HandleButtonInputsFusion(U3DNetworkInputData input)
    {
        if (!_isLocalPlayer) return;

        // Get button press/release states
        var pressed = input.Buttons.GetPressed(_buttonsPrevious);
        var released = input.Buttons.GetReleased(_buttonsPrevious);

        // Jump
        if (enableJumping && pressed.IsSet(U3DInputButtons.Jump))
        {
            HandleJumpFusionFixed();
        }

        // Sprint (toggle)
        if (enableSprintToggle && pressed.IsSet(U3DInputButtons.Sprint))
        {
            isSprinting = !isSprinting;
            NetworkIsSprinting = isSprinting;
        }

        // UPDATED: Crouch (toggle) - with movement cancellation
        if (enableCrouchToggle && pressed.IsSet(U3DInputButtons.Crouch))
        {
            isCrouching = !isCrouching;
            NetworkIsCrouching = isCrouching;

            // Adjust character controller height
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

        // ADDED: Movement cancels crouch (add this AFTER the crouch toggle logic)
        if (isCrouching && NetworkIsMoving && !isFlying)
        {
            isCrouching = false;
            NetworkIsCrouching = false;

            // Reset character controller height
            characterController.height = 2f;
            characterController.center = new Vector3(0, 1f, 0);
        }

        // UPDATED: Flying (toggle) - fixed to stop immediately on second press
        if (enableFlying && pressed.IsSet(U3DInputButtons.Fly))
        {
            isFlying = !isFlying;
            NetworkIsFlying = isFlying;

            if (isFlying)
            {
                velocity = Vector3.zero; // Reset velocity when starting to fly
                Debug.Log("🛫 Flying started");
            }
            else
            {
                velocity = Vector3.zero; // Reset velocity when stopping flying
                Debug.Log("🛬 Flying stopped immediately");
            }
        }

        // Auto-run toggle (NumLock key)
        if (pressed.IsSet(U3DInputButtons.AutoRunToggle))
        {
            isAutoRunning = !isAutoRunning;
        }

        // Interact
        if (pressed.IsSet(U3DInputButtons.Interact))
        {
            NetworkIsInteracting = true;
            U3DInteractionManager.Instance?.OnPlayerInteract();
        }

        // Zoom
        isZooming = input.Buttons.IsSet(U3DInputButtons.Zoom);
        targetFOV = isZooming ? zoomFOV : defaultFOV;

        // Handle perspective switching with scroll input
        if (perspectiveMode == PerspectiveMode.SmoothScroll && Mathf.Abs(input.PerspectiveScroll) > 0.1f)
        {
            if (input.PerspectiveScroll > 0.1f && !isFirstPerson)
            {
                SetFirstPerson();
            }
            else if (input.PerspectiveScroll < -0.1f && isFirstPerson)
            {
                SetThirdPerson();
            }
        }

        // Store current buttons for next frame
        _buttonsPrevious = input.Buttons;
    }

    void HandleJumpFusionFixed()
    {
        if (isFlying)
        {
            return;
        }

        if (isGrounded || jumpCount < additionalJumps.Length + 1)
        {
            float jumpForce;
            if (jumpCount == 0)
            {
                jumpForce = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
            else if (jumpCount <= additionalJumps.Length)
            {
                float jumpHeightValue = additionalJumps[jumpCount - 1];
                jumpForce = Mathf.Sqrt(jumpHeightValue * -2f * gravity);
            }
            else
            {
                return;
            }

            velocity.y = jumpForce;
            jumpCount++;

            NetworkIsJumping = true;
        }
    }

    void HandleTeleportFusion(U3DNetworkInputData input)
    {
        if (!enableTeleport || !_isLocalPlayer) return;

        var pressed = input.Buttons.GetPressed(_buttonsPrevious);
        bool teleportPressed = pressed.IsSet(U3DInputButtons.Teleport);

        if (teleportPressed)
        {
            Debug.Log("✅ Teleport button pressed - performing teleport");
            PerformTeleport();
        }
    }

    // FIXED: Network-aware teleport method WITHOUT NetworkTransform
    public void PerformTeleport()
    {
        if (playerCamera == null)
        {
            Debug.LogWarning("❌ Cannot teleport - player camera is null");
            return;
        }

        // Create ray from center of screen
        Vector3 screenCenter = new Vector3(Screen.width / 2, Screen.height / 2, 0);
        Ray ray = playerCamera.ScreenPointToRay(screenCenter);

        // Enhanced raycast with better filtering
        RaycastHit[] allHits = Physics.RaycastAll(ray, 100f);

        // Find best teleport target
        RaycastHit bestHit = new RaycastHit();
        bool foundHit = false;
        float closestDistance = float.MaxValue;

        foreach (RaycastHit hit in allHits)
        {
            // Skip player and child objects
            if (hit.collider.transform == transform ||
                hit.collider.transform.IsChildOf(transform))
            {
                continue;
            }

            // Skip triggers unless specifically allowed
            if (hit.collider.isTrigger)
            {
                continue;
            }

            // Use closest valid hit
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

            // Add player height offset
            float playerHeight = characterController != null ? characterController.height : 2f;
            teleportPos.y += (playerHeight * 0.5f) + 0.1f;

            Debug.Log($"✅ Teleporting to: {teleportPos}");

            // CRITICAL: Set teleport flag to prevent Render() override
            _justTeleported = true;

            // Update NetworkPosition AND transform in FixedUpdateNetwork
            NetworkPosition = teleportPos;
            NetworkRotation = transform.rotation;

            // Perform the actual teleport
            if (characterController != null && characterController.enabled)
            {
                Debug.Log("Using CharacterController teleport method");
                characterController.enabled = false;
                transform.position = teleportPos;
                characterController.enabled = true;
            }
            else
            {
                transform.position = teleportPos;
            }

            // Reset velocity to prevent continued falling/movement
            velocity = Vector3.zero;

            Debug.Log($"✅ Teleport completed - Local: {transform.position}, Network: {NetworkPosition}");
        }
        else
        {
            Debug.LogWarning("❌ No valid teleport destination found");
        }
    }

    void HandleCameraPositioning()
    {
        if (!_isLocalPlayer) return;

        // Handle perspective mode transitions
        if (perspectiveMode == PerspectiveMode.SmoothScroll)
        {
            // Update transition value smoothly
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

            // Update camera position based on transition
            UpdateCameraTransitionPosition();
        }
        else if (enableSmoothTransitions)
        {
            // Legacy smooth transitions for fixed modes
            Vector3 targetPosition = isFirstPerson ? firstPersonPosition : thirdPersonPosition;

            if (isCrouching)
            {
                targetPosition.y += crouchCameraOffset;
            }

            if (enableCameraCollision && !isFirstPerson)
            {
                targetPosition = GetCollisionSafeCameraPosition(targetPosition);
            }

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

        // Smooth FOV transition
        playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, targetFOV, Time.deltaTime * zoomSpeed);
    }

    Vector3 GetCollisionSafeCameraPosition(Vector3 desiredPosition)
    {
        Vector3 playerHead = transform.position + firstPersonPosition;
        Vector3 cameraWorldTarget = transform.TransformPoint(desiredPosition);
        Vector3 direction = (cameraWorldTarget - playerHead).normalized;

        float maxDistance = Vector3.Distance(playerHead, cameraWorldTarget);

        if (Physics.SphereCast(playerHead, cameraCollisionRadius, direction, out RaycastHit hit, maxDistance))
        {
            float safeDistance = Mathf.Max(0.1f, hit.distance - cameraCollisionBuffer);
            Vector3 safeWorldPosition = playerHead + direction * safeDistance;
            return transform.InverseTransformPoint(safeWorldPosition);
        }

        return desiredPosition;
    }

    void ApplyGravityFixed()
    {
        if (isFlying || isGrounded || !_isLocalPlayer) return;

        // Unity 6 optimized gravity application
        velocity.y += gravity * Runner.DeltaTime;

        // Apply gravity movement separately for better physics accuracy
        Vector3 gravityMovement = new Vector3(0, velocity.y, 0) * Runner.DeltaTime;
        characterController.Move(gravityMovement);
    }

    float GetCurrentSpeed()
    {
        if (isSprinting)
            return runSpeed;
        else if (isCrouching)
            return walkSpeed * 0.5f;
        else
            return walkSpeed;
    }

    void SetFirstPerson()
    {
        isFirstPerson = true;
        currentCameraDistance = 0f;

        if (perspectiveMode == PerspectiveMode.SmoothScroll)
        {
            targetTransitionValue = 0f; // Smooth transition to first person
        }
    }

    void SetThirdPerson()
    {
        isFirstPerson = false;
        currentCameraDistance = thirdPersonDistance;

        if (perspectiveMode == PerspectiveMode.SmoothScroll)
        {
            targetTransitionValue = 1f; // Smooth transition to third person
        }
    }

    void LoadPlayerPreferences()
    {
        // Load look inversion preference (player-specific, not creator setting)
        lookInverted = PlayerPrefs.GetInt("U3D_LookInverted", 0) == 1;

        // ENHANCED: Load additional sensitivity preferences
        LoadSensitivitySettings();
    }

    // ENHANCED: Public methods for settings UI integration
    public void SetMouseSmoothing(bool enabled)
    {
        enableMouseSmoothing = enabled;
        PlayerPrefs.SetInt("U3D_MouseSmoothing", enabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    public bool GetMouseSmoothing()
    {
        return enableMouseSmoothing;
    }

    public void SetMouseSmoothingAmount(float amount)
    {
        mouseSmoothingAmount = Mathf.Clamp01(amount);
        PlayerPrefs.SetFloat("U3D_MouseSmoothingAmount", mouseSmoothingAmount);
        PlayerPrefs.Save();
    }

    public float GetMouseSmoothingAmount()
    {
        return mouseSmoothingAmount;
    }

    // ENHANCED: Platform detection utilities for settings UI
    public bool IsWebGLPlatform()
    {
        return Application.platform == RuntimePlatform.WebGLPlayer;
    }

    public float GetPlatformSensitivityMultiplier()
    {
        switch (Application.platform)
        {
            case RuntimePlatform.WebGLPlayer:
                return webglSensitivityMultiplier;
            case RuntimePlatform.IPhonePlayer:
            case RuntimePlatform.Android:
                return mobileSensitivityMultiplier;
            default:
                return 1.0f;
        }
    }

    public string GetPlatformName()
    {
        switch (Application.platform)
        {
            case RuntimePlatform.WebGLPlayer:
                return "WebGL";
            case RuntimePlatform.IPhonePlayer:
                return "iOS";
            case RuntimePlatform.Android:
                return "Android";
            case RuntimePlatform.WindowsPlayer:
            case RuntimePlatform.WindowsEditor:
                return "Windows";
            case RuntimePlatform.OSXPlayer:
            case RuntimePlatform.OSXEditor:
                return "macOS";
            case RuntimePlatform.LinuxPlayer:
            case RuntimePlatform.LinuxEditor:
                return "Linux";
            default:
                return "Desktop";
        }
    }

    // Unity Input System callbacks - DISABLED for networked players but kept for compatibility
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

    // Public methods for external access
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

    // UPDATED: Enhanced position setting with proper network sync
    public void SetPosition(Vector3 position)
    {
        if (!_isLocalPlayer)
        {
            Debug.LogWarning("SetPosition called on non-local player");
            return;
        }

        Debug.Log($"🔄 SetPosition Start: Current={transform.position}, Target={position}");

        Vector3 startPosition = transform.position;

        try
        {
            // CRITICAL: Update NetworkPosition FIRST in the same network tick
            NetworkPosition = position;
            NetworkRotation = transform.rotation;

            // Method 1: Standard CharacterController approach
            if (characterController != null && characterController.enabled)
            {
                Debug.Log("Using CharacterController method");
                characterController.enabled = false;
                transform.position = position;
                characterController.enabled = true;
            }
            else
            {
                // Method 2: Direct transform (if no CharacterController)
                Debug.Log("Using direct Transform method");
                transform.position = position;
            }

            // Reset physics state
            velocity = Vector3.zero;

            // Verify the position change
            Vector3 finalPosition = transform.position;
            float distanceMoved = Vector3.Distance(startPosition, finalPosition);

            Debug.Log($"✅ SetPosition Complete:");
            Debug.Log($"   Start: {startPosition}");
            Debug.Log($"   Target: {position}");
            Debug.Log($"   Final: {finalPosition}");
            Debug.Log($"   Network: {NetworkPosition}");
            Debug.Log($"   Distance Moved: {distanceMoved}");

            if (distanceMoved < 0.1f)
            {
                Debug.LogWarning("⚠️ Position barely changed - possible teleport failure");
            }

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

    // ADDED: Environmental state setters for external trigger systems
    /// <summary>
    /// Called by U3DSwimmingTrigger when entering/exiting water
    /// </summary>
    public void SetSwimmingState(bool isSwimming)
    {
        if (!_isLocalPlayer) return;
        NetworkIsSwimming = isSwimming;
        Debug.Log($"🏊 Swimming state set to: {isSwimming}");
    }

    /// <summary>
    /// Called by U3DClimbingTrigger when entering/exiting climbable surfaces
    /// </summary>
    public void SetClimbingState(bool isClimbing)
    {
        if (!_isLocalPlayer) return;
        NetworkIsClimbing = isClimbing;
        Debug.Log($"🧗 Climbing state set to: {isClimbing}");
    }
}