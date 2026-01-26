using Fusion;
using Fusion.Addons.Physics;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using U3D.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace U3D.Networking
{
    /// <summary>
    /// CORRECTED: Unity 6 + Fusion 2 Network Manager with proper Input System integration
    /// Key Fix: Uses PlayerInput for device pairing but polls actions directly for Fusion input
    /// </summary>
    public class U3DFusionNetworkManager : MonoBehaviour, INetworkRunnerCallbacks
    {
        [Header("Network Configuration")]
        [SerializeField] private NetworkPrefabRef playerPrefab;
        [SerializeField] private int maxPlayers = 10;
        [SerializeField] private bool autoStartHost = false;
        [SerializeField] private GameMode gameMode = GameMode.Shared;

        [Header("Spawn Configuration")]
        [SerializeField] private Transform[] spawnPoints;
        [SerializeField] private bool randomizeSpawnPoints = true;
        [SerializeField] private float spawnRadius = 2f;

        [Header("WebGL Optimization")]
        [SerializeField] private bool useClientPrediction = true;
        [SerializeField] private bool enableLagCompensation = true;
        [SerializeField] private int sendRate = 20;
        [SerializeField] private int simulationTickRate = 60;

        [Header("Input System Integration")]
        [SerializeField] private InputActionAsset inputActionAsset;

        [Header("UI Interaction Management")]
        [SerializeField] private bool pauseInputDuringUIFocus = true;

        private bool _isUIFocused = false;
        private List<IUIInputHandler> _uiInputHandlers = new List<IUIInputHandler>();
        private HashSet<string> _activeUIComponents = new HashSet<string>();

        // Network State
        private NetworkRunner _runner;
        private Dictionary<PlayerRef, NetworkObject> _spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();
        private bool _isInitialized = false;
        private string _currentSessionName = "";
        private FirebaseIntegration _firebaseIntegration;

        // Input handling using direct Action polling
        private InputAction _moveAction;
        private InputAction _lookAction;
        private InputAction _jumpAction;
        private InputAction _sprintAction;
        private InputAction _crouchAction;
        private InputAction _flyAction;
        private InputAction _interactAction;
        private InputAction _zoomAction;
        private InputAction _teleportAction;
        private InputAction _perspectiveSwitchAction;
        private InputAction _pauseAction;
        private InputAction _escapeAction;

        // Input state caching
        private Vector2 _cachedMovementInput;
        private Vector2 _cachedLookInput;
        private bool _jumpPressed;
        private bool _sprintPressed;
        private bool _crouchPressed;
        private bool _flyPressed;
        private bool _interactPressed;
        private bool _zoomPressed;
        private bool _teleportPressed;
        private float _perspectiveScrollValue;
        private float _lastTeleportClickTime = 0f;

        // Advanced AAA-style mouse input tracking
        private bool _leftMouseHeld = false;
        private bool _rightMouseHeld = false;
        private bool _bothMouseHeld = false;

        // Advanced movement input tracking
        private bool _strafeLeftPressed = false;
        private bool _strafeRightPressed = false;
        private bool _turnLeftPressed = false;
        private bool _turnRightPressed = false;
        private bool _autoRunTogglePressed = false;

        // Additional input actions for Advanced controls
        private InputAction _mouseLeftAction;
        private InputAction _mouseRightAction;
        private InputAction _strafeLeftAction;
        private InputAction _strafeRightAction;
        private InputAction _turnLeftAction;
        private InputAction _turnRightAction;
        private InputAction _autoRunToggleAction;
        private U3DSimpleTouchZones touchZones;

        // VR/WebXR input state
        private U3D.XR.U3DWebXRManager _webXRManager;
        private bool _isVRModeActive = false;

        // Events for UI integration
        public static event Action<bool> OnNetworkStatusChanged;
        public static event Action<PlayerRef> OnPlayerJoinedEvent;
        public static event Action<PlayerRef> OnPlayerLeftEvent;
        public static event Action<int> OnPlayerCountChanged;

        // Singleton access
        public static U3DFusionNetworkManager Instance { get; private set; }

        // Public Properties
        public bool IsConnected => _runner != null && _runner.IsClient;
        public bool IsHost => _runner != null && _runner.IsServer;
        public int PlayerCount => _spawnedPlayers.Count;
        public NetworkRunner Runner => _runner;
        public bool IsUIFocused => _isUIFocused;
        public int ActiveUICount => _uiInputHandlers.Count(h => h != null && h.IsUIFocused());


        void Awake()
        {
            // Singleton pattern
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }
        }

        void Start()
        {
            InitializeNetworking();
            SetupInputActions();

            if (Application.isMobilePlatform || Application.isEditor)
            {
                SetupTouchControls();
            }

            // Subscribe to VR mode changes
            U3D.XR.U3DWebXRManager.OnVRModeChanged += OnVRModeChanged;

            if (autoStartHost)
            {
                _ = StartNetworking("DefaultRoom");
            }
        }

        private void OnVRModeChanged(bool isVRActive)
        {
            _isVRModeActive = isVRActive;
            _webXRManager = U3D.XR.U3DWebXRManager.Instance;
            Debug.Log($"[U3DFusionNetworkManager] VR Mode changed: {isVRActive}");
        }

        void SetupTouchControls()
        {
            // Find existing or create touch zone controller
            touchZones = UnityEngine.Object.FindFirstObjectByType<U3DSimpleTouchZones>();
            if (touchZones == null)
            {
                GameObject touchControllerObj = new GameObject("TouchZoneController");
                touchZones = touchControllerObj.AddComponent<U3DSimpleTouchZones>();
                DontDestroyOnLoad(touchControllerObj);
                Debug.Log("✅ Touch zone controller created for mobile input");
            }
        }

        void InitializeNetworking()
        {
            if (_isInitialized) return;

            // WebGL-specific Fusion configuration
            NetworkProjectConfig.Global.PeerMode = NetworkProjectConfig.PeerModes.Single;

            _isInitialized = true;
            Debug.Log("U3D Fusion Network Manager initialized for WebGL");
        }

        // Setup input actions properly for Unity 6 + Fusion 2
        void SetupInputActions()
        {
            if (inputActionAsset == null)
            {
                Debug.LogError("Input Action Asset not assigned! Please assign U3DInputActions in the inspector.");
                return;
            }

            // Get the Player action map
            var actionMap = inputActionAsset.FindActionMap("Player");
            if (actionMap == null)
            {
                Debug.LogError("'Player' action map not found in Input Actions");
                return;
            }

            // Cache all input actions directly from the asset
            _moveAction = actionMap.FindAction("Move");
            _lookAction = actionMap.FindAction("Look");
            _jumpAction = actionMap.FindAction("Jump");
            _sprintAction = actionMap.FindAction("Sprint");
            _crouchAction = actionMap.FindAction("Crouch");
            _flyAction = actionMap.FindAction("Fly");
            _interactAction = actionMap.FindAction("Interact");
            _zoomAction = actionMap.FindAction("Zoom");
            _teleportAction = actionMap.FindAction("Teleport");
            _perspectiveSwitchAction = actionMap.FindAction("PerspectiveSwitch");
            _pauseAction = actionMap.FindAction("Pause");
            _escapeAction = actionMap.FindAction("Escape");

            // Advanced AAA-style controls
            _mouseLeftAction = actionMap.FindAction("MouseLeft");
            _mouseRightAction = actionMap.FindAction("MouseRight");
            _strafeLeftAction = actionMap.FindAction("StrafeLeft");
            _strafeRightAction = actionMap.FindAction("StrafeRight");
            _turnLeftAction = actionMap.FindAction("TurnLeft");
            _turnRightAction = actionMap.FindAction("TurnRight");
            _autoRunToggleAction = actionMap.FindAction("AutoRunToggle");

            // CRITICAL: Enable the action map for input polling
            actionMap.Enable();

            Debug.Log("✅ Input actions setup complete");
            Debug.Log($"Action map enabled: {actionMap.enabled}");
            Debug.Log($"Found actions: Move={_moveAction != null}, Look={_lookAction != null}, Jump={_jumpAction != null}");
        }

        void Update()
        {
            // Poll input actions directly for Fusion
            if (_moveAction == null) return;

            // Check if we should process input (not escaped from WebGL)
            var cursorManager = FindAnyObjectByType<U3DWebGLCursorManager>();
            bool shouldProcessInput = cursorManager == null || cursorManager.ShouldProcessGameInput();

            // Check for UI focus to prevent input conflicts
            bool isUIInteracting = CheckUIInteraction();

            if (!shouldProcessInput || isUIInteracting)
            {
                // Clear input when UI is active to prevent conflicts
                ClearInputCache();
                return;
            }

            // VR Mode: Get input from WebXR controllers
            if (_isVRModeActive && _webXRManager != null)
            {
                PollVRInput();
                return;
            }

            if (Application.isMobilePlatform && touchZones != null)
            {
                // On mobile, use touch zones
                _cachedMovementInput = touchZones.MovementInput;
                _cachedLookInput = touchZones.LookInput;

                // Handle touch gestures
                if (touchZones.JumpRequested)
                    _jumpPressed = true;
                if (touchZones.SprintActive)
                    _sprintPressed = true;
                if (touchZones.CrouchRequested)
                    _crouchPressed = true;
                if (touchZones.FlyRequested)
                    _flyPressed = true;
                if (touchZones.InteractRequested)
                    _interactPressed = true;
            }
            else
            {
                // On desktop, use traditional input
                _cachedMovementInput = _moveAction.ReadValue<Vector2>();

                if (_lookAction != null)
                    _cachedLookInput = _lookAction.ReadValue<Vector2>();

                // Cache button states using proper Input System polling
                if (_jumpAction != null && _jumpAction.WasPressedThisFrame())
                    _jumpPressed = true;

                if (_sprintAction != null && _sprintAction.WasPressedThisFrame())
                    _sprintPressed = true;

                if (_crouchAction != null && _crouchAction.WasPressedThisFrame())
                    _crouchPressed = true;

                if (_flyAction != null && _flyAction.WasPressedThisFrame())
                    _flyPressed = true;

                if (_interactAction != null && _interactAction.WasPressedThisFrame())
                    _interactPressed = true;

                // Handle zoom from pinch gesture
                if (touchZones != null && Mathf.Abs(touchZones.ZoomInput) > 0.01f)
                {
                    // Use zoom input for FOV adjustment
                    _zoomPressed = touchZones.ZoomInput > 0; // Pinch out = zoom in

                    // Or use it for smooth scroll value
                    _perspectiveScrollValue = touchZones.ZoomInput * 5f; // Scale as needed
                }

                // Handle perspective switch from large pinch
                if (touchZones != null && touchZones.PerspectiveSwitchRequested)
                {
                    // Toggle perspective mode
                    _perspectiveScrollValue = 10f; // Large value to trigger switch
                }

                // Direct double-click teleportation (bypasses network button system)
                if (_teleportAction != null && _teleportAction.WasPressedThisFrame())
                {
                    float currentTime = Time.time;
                    if (currentTime - _lastTeleportClickTime < 0.5f) // Within 0.5 seconds
                    {
                        Debug.Log("✅ Double-click teleport detected - triggering directly");
                        TriggerDirectTeleport();
                    }
                    _lastTeleportClickTime = currentTime;
                }

                if (_zoomAction != null)
                    _zoomPressed = _zoomAction.IsPressed();

                if (_perspectiveSwitchAction != null)
                {
                    float scroll = _perspectiveSwitchAction.ReadValue<float>();
                    if (Mathf.Abs(scroll) > 0.1f)
                        _perspectiveScrollValue = scroll;
                }

                // Advanced AAA-style mouse controls
                if (_mouseLeftAction != null)
                    _leftMouseHeld = _mouseLeftAction.IsPressed();
                if (_mouseRightAction != null)
                    _rightMouseHeld = _mouseRightAction.IsPressed();
                _bothMouseHeld = _leftMouseHeld && _rightMouseHeld;

                // Advanced keyboard movement
                if (_strafeLeftAction != null)
                    _strafeLeftPressed = _strafeLeftAction.IsPressed();
                if (_strafeRightAction != null)
                    _strafeRightPressed = _strafeRightAction.IsPressed();
                if (_turnLeftAction != null)
                    _turnLeftPressed = _turnLeftAction.IsPressed();
                if (_turnRightAction != null)
                    _turnRightPressed = _turnRightAction.IsPressed();

                // NumLock auto-run toggle (one-shot press)
                if (_autoRunToggleAction != null && _autoRunToggleAction.WasPressedThisFrame())
                    _autoRunTogglePressed = true;
            }
        }

        private bool CheckUIInteraction()
        {
            _isUIFocused = false;
            _activeUIComponents.Clear();

            // Check all registered UI handlers (PayPal, Quizzes, Forms, etc.)
            foreach (var handler in _uiInputHandlers)
            {
                if (handler != null && handler.IsUIFocused())
                {
                    _isUIFocused = true;
                    _activeUIComponents.Add(handler.GetHandlerName());
                }
            }

            // Additional platform-specific UI detection
            if (!_isUIFocused)
            {
                _isUIFocused = DetectPlatformSpecificUI();
            }

            return _isUIFocused;
        }

        private bool DetectPlatformSpecificUI()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WebGLPlayer:
                    return UnityEngine.EventSystems.EventSystem.current?.IsPointerOverGameObject() == true;

                case RuntimePlatform.Android:
                case RuntimePlatform.IPhonePlayer:
                    for (int i = 0; i < UnityEngine.Input.touchCount; i++)
                    {
                        if (UnityEngine.EventSystems.EventSystem.current?.IsPointerOverGameObject(UnityEngine.Input.GetTouch(i).fingerId) == true)
                        {
                            return true;
                        }
                    }
                    return false; // THIS LINE WAS MISSING

                default:
                    return UnityEngine.EventSystems.EventSystem.current?.IsPointerOverGameObject() == true;
            }
        }

        private void ClearInputCache()
        {
            _cachedMovementInput = Vector2.zero;
            _cachedLookInput = Vector2.zero;
            _jumpPressed = false;
            _sprintPressed = false;
            _crouchPressed = false;
            _flyPressed = false;
            _interactPressed = false;
            _zoomPressed = false;
            _perspectiveScrollValue = 0f;
        }

        /// <summary>
        /// Poll VR controller input via Unity Input System
        /// WebXR Export 0.20+ integrates with Input System - XR bindings handle controller input automatically
        /// </summary>
        private void PollVRInput()
        {
            // In VR mode, the Input System XR bindings automatically receive controller input
            // from WebXR Export's integration with Unity's XR subsystems.
            // We use the same Input Actions as flat-screen mode - the XR control scheme
            // bindings in U3DInputActions handle the mapping.

            // Movement from left thumbstick (XR binding: <XRController>{LeftHand}/{Primary2DAxis})
            if (_moveAction != null)
                _cachedMovementInput = _moveAction.ReadValue<Vector2>();

            // Look/snap turn from right thumbstick (XR binding: <XRController>{RightHand}/{Secondary2DAxis})
            if (_lookAction != null)
                _cachedLookInput = _lookAction.ReadValue<Vector2>();

            // Jump = XR secondary button (XR binding: <XRController>/secondaryButton)
            if (_jumpAction != null && _jumpAction.WasPressedThisFrame())
                _jumpPressed = true;

            // Sprint = Left trigger (XR binding: <XRController>{LeftHand}/{PrimaryTrigger})
            if (_sprintAction != null && _sprintAction.IsPressed())
                _sprintPressed = true;

            // Crouch = Left thumbstick click (XR binding: <XRController>{LeftHand}/primary2DAxisClick)
            if (_crouchAction != null && _crouchAction.WasPressedThisFrame())
                _crouchPressed = true;

            // Fly = Left primary button (XR binding: <XRController>{LeftHand}/{PrimaryButton})
            if (_flyAction != null && _flyAction.WasPressedThisFrame())
                _flyPressed = true;

            // Interact = Right trigger (XR binding: <XRController>{RightHand}/triggerButton)
            if (_interactAction != null && _interactAction.WasPressedThisFrame())
                _interactPressed = true;

            // Teleport = Left grip or dedicated teleport action
            if (_teleportAction != null && _teleportAction.WasPressedThisFrame())
                _teleportPressed = true;

            // Clear mouse-based inputs in VR mode (not applicable)
            _leftMouseHeld = false;
            _rightMouseHeld = false;
            _bothMouseHeld = false;
            _strafeLeftPressed = false;
            _strafeRightPressed = false;
            _turnLeftPressed = false;
            _turnRightPressed = false;
        }

        public void RegisterUIInputHandler(IUIInputHandler handler)
        {
            if (!_uiInputHandlers.Contains(handler))
            {
                _uiInputHandlers.Add(handler);
                Debug.Log($"🎮 UI input handler registered: {handler.GetHandlerName()}");
            }
        }

        public void UnregisterUIInputHandler(IUIInputHandler handler)
        {
            _uiInputHandlers.Remove(handler);
            Debug.Log($"🎮 UI input handler unregistered: {handler.GetHandlerName()}");
        }


        /// <summary>
        /// Trigger teleportation directly on double-click (bypasses network button delay)
        /// </summary>
        void TriggerDirectTeleport()
        {
            // Find the local player and call teleport immediately
            foreach (var kvp in _spawnedPlayers)
            {
                var playerController = kvp.Value.GetComponent<U3DPlayerController>();
                if (playerController != null && playerController.IsLocalPlayer)
                {
                    playerController.PerformTeleport();
                    return;
                }
            }

            Debug.LogWarning("❌ No local player found for direct teleport");
        }

        /// <summary>
        /// Start networking with proper physics configuration
        /// </summary>
        public async Task<bool> StartNetworking(string sessionName, string photonAppId = "")
        {
            U3D.Networking.U3DPlayerNametag.ResetPlayerNumbering();

            if (_runner != null)
            {
                Debug.LogWarning("Network already running, shutting down first");
                await StopNetworking();
            }

            try
            {
                _currentSessionName = sessionName;

                // Create NetworkRunner for this session
                var runnerObject = new GameObject($"NetworkRunner_{sessionName}");
                runnerObject.transform.SetParent(transform);

                _runner = runnerObject.AddComponent<NetworkRunner>();
                _runner.ProvideInput = true;

                // FIXED: Proper physics simulator configuration for Shared Mode
                var physicsSimulator = runnerObject.AddComponent<RunnerSimulatePhysics3D>();
                ConfigurePhysicsSimulatorForSharedMode(physicsSimulator);

                // Register this component as callback handler
                _runner.AddCallbacks(this);

                // FIXED: WebGL Shared Mode configuration
                var args = new StartGameArgs()
                {
                    GameMode = GameMode.Shared, // Correct mode for WebGL
                    SessionName = sessionName,
                    Scene = SceneRef.FromIndex(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex),
                    SceneManager = runnerObject.AddComponent<NetworkSceneManagerDefault>(),
                    PlayerCount = maxPlayers
                };

                UpdateStatus($"Connecting to session: {sessionName}");

                var result = await _runner.StartGame(args);

                if (result.Ok)
                {
                    UpdateStatus($"Successfully connected to: {sessionName}");
                    OnNetworkStatusChanged?.Invoke(true);
                    return true;
                }
                else
                {
                    UpdateStatus($"Connection failed: {result.ShutdownReason}");
                    OnNetworkStatusChanged?.Invoke(false);
                    return false;
                }
            }
            catch (Exception e)
            {
                UpdateStatus($"Network start error: {e.Message}");
                OnNetworkStatusChanged?.Invoke(false);
                return false;
            }
        }


        /// <summary>
        /// Configure physics simulator for proper Shared Mode operation
        /// </summary>
        private void ConfigurePhysicsSimulatorForSharedMode(RunnerSimulatePhysics3D physicsSimulator)
        {
            // CRITICAL: Set ClientPhysicsSimulation for Shared Mode
            physicsSimulator.ClientPhysicsSimulation = ClientPhysicsSimulation.SimulateAlways;

            Debug.Log("✅ Configured RunnerSimulatePhysics3D for Shared Mode:");
            Debug.Log($"   - ClientPhysicsSimulation: {physicsSimulator.ClientPhysicsSimulation}");

            // Note: PhysicsSimulationTiming is handled automatically by Fusion 2
        }


        /// <summary>
        /// Stop networking and cleanup
        /// </summary>
        public async Task StopNetworking()
        {
            if (_runner != null)
            {
                UpdateStatus("Disconnecting from multiplayer...");

                _runner.RemoveCallbacks(this);
                await _runner.Shutdown();

                if (_runner.gameObject != null)
                {
                    Destroy(_runner.gameObject);
                }

                _runner = null;
                _spawnedPlayers.Clear();

                UpdateStatus("Disconnected from multiplayer");
                OnNetworkStatusChanged?.Invoke(false);
                OnPlayerCountChanged?.Invoke(0);
            }
        }

        /// <summary>
        /// Get appropriate spawn position for new player
        /// </summary>
        Vector3 GetSpawnPosition()
        {
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                Vector3 randomOffset = UnityEngine.Random.insideUnitSphere * spawnRadius;
                randomOffset.y = 0;
                return randomOffset;
            }

            if (randomizeSpawnPoints)
            {
                var spawnPoint = spawnPoints[UnityEngine.Random.Range(0, spawnPoints.Length)];
                Vector3 randomOffset = UnityEngine.Random.insideUnitSphere * spawnRadius;
                randomOffset.y = 0;
                return spawnPoint.position + randomOffset;
            }
            else
            {
                int spawnIndex = _spawnedPlayers.Count % spawnPoints.Length;
                return spawnPoints[spawnIndex].position;
            }
        }

        void UpdateStatus(string message)
        {
            Debug.Log($"U3D Network Manager: {message}");
        }

        // ========== FUSION CALLBACKS ==========

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            Debug.Log($"=== U3DFusionNetworkManager: Player joined: {player} ===");

            bool shouldSpawn = false;

            if (runner.GameMode == GameMode.Shared)
            {
                shouldSpawn = (player == runner.LocalPlayer);
            }
            else
            {
                shouldSpawn = runner.IsServer;
            }

            if (shouldSpawn && playerPrefab.IsValid)
            {
                // Add delay to prevent falling through floor
                StartCoroutine(DelayedSpawn(runner, player));
            }

            if (player == runner.LocalPlayer)
            {
                UpdateStatus($"Joined multiplayer session: {_currentSessionName}");
            }
            else
            {
                UpdateStatus($"Player {player} joined the session");
            }

            OnPlayerJoinedEvent?.Invoke(player);
            OnPlayerCountChanged?.Invoke(_spawnedPlayers.Count);
        }

        /// <summary>
        /// Delayed spawn with proper physics initialization timing
        /// </summary>
        private System.Collections.IEnumerator DelayedSpawn(NetworkRunner runner, PlayerRef player)
        {
            // CRITICAL: Wait for physics system to initialize before spawning physics objects
            yield return new WaitForSeconds(0.5f); // Increased delay for physics initialization

            // Get spawn data (position AND rotation) from spawner
            Vector3 spawnPosition;
            Quaternion spawnRotation;

            if (U3DPlayerSpawner.Instance != null)
            {
                var spawnData = U3DPlayerSpawner.Instance.GetSpawnData();
                spawnPosition = spawnData.position;
                spawnRotation = spawnData.rotation;

                Debug.Log($"✅ Using PlayerSpawner: pos={spawnPosition}, rot={spawnRotation.eulerAngles.y}°");
            }
            else
            {
                spawnPosition = GetSpawnPosition();
                spawnRotation = Quaternion.identity;
                Debug.LogWarning("⚠️ No PlayerSpawner found, using NetworkManager fallback");
            }

            Debug.Log($"🎯 Spawning player {player} at: {spawnPosition} facing: {spawnRotation.eulerAngles.y}°");

            // Spawn with both position AND rotation
            var playerObject = runner.Spawn(playerPrefab, spawnPosition, spawnRotation, player);

            if (playerObject != null)
            {
                _spawnedPlayers[player] = playerObject;
                Debug.Log($"✅ Player spawned successfully: {playerObject.name}");

                // Setup input
                var playerController = playerObject.GetComponent<U3DPlayerController>();
                if (playerController != null)
                {
                    playerController.RefreshInputActionsFromNetworkManager(this);
                }

                // FIXED: Initialize any grab/throw objects in the scene after player spawn
                InitializePhysicsObjectsForPlayer(player);
            }
            else
            {
                Debug.LogError($"❌ Failed to spawn player at {spawnPosition}");
            }
        }

        /// <summary>
        /// Initialize physics objects when players join to prevent authority conflicts
        /// </summary>
        private void InitializePhysicsObjectsForPlayer(PlayerRef player)
        {
            if (_runner.GameMode != GameMode.Shared) return;

            // Find all grabbable objects in the scene and ensure proper authority state
            U3DGrabbable[] grabbables = FindObjectsByType<U3DGrabbable>(FindObjectsSortMode.None);

            foreach (var grabbable in grabbables)
            {
                if (grabbable.IsNetworked && grabbable.GetComponent<NetworkObject>() != null)
                {
                    var netObj = grabbable.GetComponent<NetworkObject>();

                    // Ensure objects are in proper initial state (not grabbed)
                    if (netObj.HasStateAuthority && grabbable.IsGrabbed)
                    {
                        Debug.LogWarning($"Found grabbed object {grabbable.name} on player join - releasing");
                        grabbable.Release();
                    }
                }
            }

            Debug.Log($"✅ Initialized {grabbables.Length} grabbable objects for player {player}");
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            Debug.Log($"Player left: {player}");

            U3D.Networking.U3DPlayerNametag.RemovePlayer(player);

            if (_spawnedPlayers.TryGetValue(player, out NetworkObject playerObject))
            {
                if (playerObject != null)
                {
                    runner.Despawn(playerObject);
                }
                _spawnedPlayers.Remove(player);
            }

            UpdateStatus($"Player {player} left the session");
            OnPlayerLeftEvent?.Invoke(player);
            OnPlayerCountChanged?.Invoke(_spawnedPlayers.Count);
        }

        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
        {
            Debug.Log($"Connection failed: {reason}");
            UpdateStatus($"Connection failed: {reason}");
            OnNetworkStatusChanged?.Invoke(false);
        }

        public void OnConnectedToServer(NetworkRunner runner)
        {
            Debug.Log("Connected to Photon server");
            UpdateStatus("Connected to Photon server");
        }

        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            Debug.Log($"Disconnected from server: {reason}");
            UpdateStatus($"Disconnected: {reason}");
            OnNetworkStatusChanged?.Invoke(false);
        }

        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            Debug.Log($"Network shutdown: {shutdownReason}");
            UpdateStatus($"Network shutdown: {shutdownReason}");

            _spawnedPlayers.Clear();

            U3D.Networking.U3DPlayerNametag.ResetPlayerNumbering();

            OnNetworkStatusChanged?.Invoke(false);
            OnPlayerCountChanged?.Invoke(0);
        }

        // CORRECTED: Fusion input processing using cached values
        public void OnInput(NetworkRunner runner, NetworkInput input)
        {
            var data = new U3DNetworkInputData();

            // Use cached input values from Update()
            data.MovementInput = _cachedMovementInput;
            data.LookInput = _cachedLookInput;
            data.PerspectiveScroll = _perspectiveScrollValue;

            // Set button states
            if (_jumpPressed)
                data.Buttons.Set(U3DInputButtons.Jump, true);
            if (_sprintPressed)
                data.Buttons.Set(U3DInputButtons.Sprint, true);
            if (_crouchPressed)
                data.Buttons.Set(U3DInputButtons.Crouch, true);
            if (_flyPressed)
                data.Buttons.Set(U3DInputButtons.Fly, true);
            if (_interactPressed)
                data.Buttons.Set(U3DInputButtons.Interact, true);
            if (_zoomPressed)
                data.Buttons.Set(U3DInputButtons.Zoom, true);
            if (_teleportPressed)
                data.Buttons.Set(U3DInputButtons.Teleport, true);

            // Advanced AAA-style mouse button states
            data.LeftMouseHeld = _leftMouseHeld;
            data.RightMouseHeld = _rightMouseHeld;
            data.BothMouseHeld = _bothMouseHeld;

            // Advanced movement states  
            data.StrafeLeft = _strafeLeftPressed;
            data.StrafeRight = _strafeRightPressed;
            data.TurnLeft = _turnLeftPressed;
            data.TurnRight = _turnRightPressed;

            // Set auto-run toggle button
            if (_autoRunTogglePressed)
                data.Buttons.Set(U3DInputButtons.AutoRunToggle, true);

            // Reset auto-run toggle (add to existing reset section)
            _autoRunTogglePressed = false;

            // Send input to Fusion
            input.Set(data);

            // Reset one-shot button presses
            _jumpPressed = false;
            _sprintPressed = false;
            _crouchPressed = false;
            _flyPressed = false;
            _interactPressed = false;
            _teleportPressed = false;
            _perspectiveScrollValue = 0f;
        }

        // Get input actions for PlayerController integration
        public InputAction GetMoveAction() => _moveAction;
        public InputAction GetLookAction() => _lookAction;
        public InputAction GetJumpAction() => _jumpAction;
        public InputAction GetSprintAction() => _sprintAction;
        public InputAction GetCrouchAction() => _crouchAction;
        public InputAction GetFlyAction() => _flyAction;
        public InputAction GetInteractAction() => _interactAction;
        public InputAction GetZoomAction() => _zoomAction;
        public InputAction GetTeleportAction() => _teleportAction;
        public InputAction GetPerspectiveSwitchAction() => _perspectiveSwitchAction;
        public InputAction GetPauseAction() => _pauseAction;
        public InputAction GetEscapeAction() => _escapeAction;

        // Standard Fusion callbacks
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<Fusion.SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey reliableKey, ArraySegment<byte> data) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey reliableKey, float progress) { }

        void OnDestroy()
        {
            // Unsubscribe from VR mode changes
            U3D.XR.U3DWebXRManager.OnVRModeChanged -= OnVRModeChanged;

            // Clean shutdown
            if (_runner != null)
            {
                _runner.Shutdown();
            }

            // Disable input actions
            if (inputActionAsset != null)
            {
                var actionMap = inputActionAsset.FindActionMap("Player");
                if (actionMap != null && actionMap.enabled)
                {
                    actionMap.Disable();
                }
            }
        }
    }
}