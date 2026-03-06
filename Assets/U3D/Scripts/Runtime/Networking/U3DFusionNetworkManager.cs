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

        private NetworkRunner _runner;
        private Dictionary<PlayerRef, NetworkObject> _spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();
        private bool _isInitialized = false;
        private string _currentSessionName = "";
        private FirebaseIntegration _firebaseIntegration;

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

        private bool _leftMouseHeld = false;
        private bool _rightMouseHeld = false;
        private bool _bothMouseHeld = false;

        private bool _strafeLeftPressed = false;
        private bool _strafeRightPressed = false;
        private bool _turnLeftPressed = false;
        private bool _turnRightPressed = false;
        private bool _autoRunTogglePressed = false;

        private InputAction _mouseLeftAction;
        private InputAction _mouseRightAction;
        private InputAction _strafeLeftAction;
        private InputAction _strafeRightAction;
        private InputAction _turnLeftAction;
        private InputAction _turnRightAction;
        private InputAction _autoRunToggleAction;
        private U3DSimpleTouchZones touchZones;

        private U3D.XR.U3DWebXRManager _webXRManager;
        private bool _isVRModeActive = false;
        private bool _vrSprintTriggerWasDown = false;

        public static event Action<bool> OnNetworkStatusChanged;
        public static event Action<PlayerRef> OnPlayerJoinedEvent;
        public static event Action<PlayerRef> OnPlayerLeftEvent;
        public static event Action<int> OnPlayerCountChanged;

        public static U3DFusionNetworkManager Instance { get; private set; }

        public bool IsConnected => _runner != null && _runner.IsClient;
        public bool IsHost => _runner != null && _runner.IsServer;
        public int PlayerCount => _spawnedPlayers.Count;
        public NetworkRunner Runner => _runner;
        public bool IsUIFocused => _isUIFocused;
        public int ActiveUICount => _uiInputHandlers.Count(h => h != null && h.IsUIFocused());


        void Awake()
        {
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

            U3D.XR.U3DWebXRManager.OnVRModeChanged += OnVRModeChanged;

            if (autoStartHost && FindAnyObjectByType<FirebaseIntegration>() == null)
            {
                _ = StartNetworking("DefaultRoom");
            }
        }

        private void OnVRModeChanged(bool isVRActive)
        {
            _isVRModeActive = isVRActive;
            _webXRManager = U3D.XR.U3DWebXRManager.Instance;
        }

        void SetupTouchControls()
        {
            touchZones = UnityEngine.Object.FindFirstObjectByType<U3DSimpleTouchZones>();
            if (touchZones == null)
            {
                GameObject touchControllerObj = new GameObject("TouchZoneController");
                touchZones = touchControllerObj.AddComponent<U3DSimpleTouchZones>();
                DontDestroyOnLoad(touchControllerObj);
            }
        }

        void InitializeNetworking()
        {
            if (_isInitialized) return;

            NetworkProjectConfig.Global.PeerMode = NetworkProjectConfig.PeerModes.Single;

            _isInitialized = true;
        }

        void SetupInputActions()
        {
            if (inputActionAsset == null)
            {
                Debug.LogError("Input Action Asset not assigned! Please assign U3DInputActions in the inspector.");
                return;
            }

            var actionMap = inputActionAsset.FindActionMap("Player");
            if (actionMap == null)
            {
                Debug.LogError("'Player' action map not found in Input Actions");
                return;
            }

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

            _mouseLeftAction = actionMap.FindAction("MouseLeft");
            _mouseRightAction = actionMap.FindAction("MouseRight");
            _strafeLeftAction = actionMap.FindAction("StrafeLeft");
            _strafeRightAction = actionMap.FindAction("StrafeRight");
            _turnLeftAction = actionMap.FindAction("TurnLeft");
            _turnRightAction = actionMap.FindAction("TurnRight");
            _autoRunToggleAction = actionMap.FindAction("AutoRunToggle");

            actionMap.Enable();
        }

        void Update()
        {
            if (_moveAction == null) return;

            var cursorManager = FindAnyObjectByType<U3DWebGLCursorManager>();
            bool shouldProcessInput = cursorManager == null || cursorManager.ShouldProcessGameInput();

            bool isUIInteracting = CheckUIInteraction();

            if (!shouldProcessInput || isUIInteracting)
            {
                ClearInputCache();
                return;
            }

            if (_isVRModeActive && _webXRManager != null)
            {
                PollVRInput();
                return;
            }

            if (Application.isMobilePlatform && touchZones != null)
            {
                _cachedMovementInput = touchZones.MovementInput;
                _cachedLookInput = touchZones.LookInput;

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
                _cachedMovementInput = _moveAction.ReadValue<Vector2>();

                if (_lookAction != null)
                    _cachedLookInput = _lookAction.ReadValue<Vector2>();

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

                if (touchZones != null && Mathf.Abs(touchZones.ZoomInput) > 0.01f)
                {
                    _zoomPressed = touchZones.ZoomInput > 0;
                    _perspectiveScrollValue = touchZones.ZoomInput * 5f;
                }

                if (touchZones != null && touchZones.PerspectiveSwitchRequested)
                {
                    _perspectiveScrollValue = 10f;
                }

                if (_teleportAction != null && _teleportAction.WasPressedThisFrame())
                {
                    float currentTime = Time.time;
                    if (currentTime - _lastTeleportClickTime < 0.5f)
                    {
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

                if (_mouseLeftAction != null)
                    _leftMouseHeld = _mouseLeftAction.IsPressed();
                if (_mouseRightAction != null)
                    _rightMouseHeld = _mouseRightAction.IsPressed();
                _bothMouseHeld = _leftMouseHeld && _rightMouseHeld;

                if (_strafeLeftAction != null)
                    _strafeLeftPressed = _strafeLeftAction.IsPressed();
                if (_strafeRightAction != null)
                    _strafeRightPressed = _strafeRightAction.IsPressed();
                if (_turnLeftAction != null)
                    _turnLeftPressed = _turnLeftAction.IsPressed();
                if (_turnRightAction != null)
                    _turnRightPressed = _turnRightAction.IsPressed();

                if (_autoRunToggleAction != null && _autoRunToggleAction.WasPressedThisFrame())
                    _autoRunTogglePressed = true;
            }
        }

        private bool CheckUIInteraction()
        {
            _isUIFocused = false;
            _activeUIComponents.Clear();

            foreach (var handler in _uiInputHandlers)
            {
                if (handler != null && handler.IsUIFocused())
                {
                    _isUIFocused = true;
                    _activeUIComponents.Add(handler.GetHandlerName());
                }
            }

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
                    return false;

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

        private void PollVRInput()
        {
            Vector2 moveValue = Vector2.zero;
            Vector2 lookValue = Vector2.zero;

            if (_moveAction != null)
                moveValue = _moveAction.ReadValue<Vector2>();

            if (_lookAction != null)
                lookValue = _lookAction.ReadValue<Vector2>();

            if (moveValue.magnitude > 0.1f)
                _cachedMovementInput = moveValue;
            else if (_cachedMovementInput.magnitude > 0.1f)
                _cachedMovementInput = Vector2.Lerp(_cachedMovementInput, Vector2.zero, 0.3f);
            else
                _cachedMovementInput = Vector2.zero;

            if (lookValue.magnitude > 0.1f)
                _cachedLookInput = lookValue;
            else if (_cachedLookInput.magnitude > 0.1f)
                _cachedLookInput = Vector2.Lerp(_cachedLookInput, Vector2.zero, 0.3f);
            else
                _cachedLookInput = Vector2.zero;

            if (_jumpAction != null && _jumpAction.WasPressedThisFrame())
                _jumpPressed = true;

            if (_sprintAction != null)
            {
                float triggerValue = _sprintAction.ReadValue<float>();
                bool triggerDown = triggerValue > 0.5f;

                if (triggerDown && !_vrSprintTriggerWasDown)
                    _sprintPressed = true;

                _vrSprintTriggerWasDown = triggerDown;
            }

            if (_crouchAction != null && _crouchAction.WasPressedThisFrame())
                _crouchPressed = true;

            if (_flyAction != null && _flyAction.WasPressedThisFrame())
                _flyPressed = true;

            if (_interactAction != null && _interactAction.WasPressedThisFrame())
                _interactPressed = true;

            if (_teleportAction != null && _teleportAction.WasPressedThisFrame())
                _teleportPressed = true;

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
            }
        }

        public void UnregisterUIInputHandler(IUIInputHandler handler)
        {
            _uiInputHandlers.Remove(handler);
        }

        void TriggerDirectTeleport()
        {
            foreach (var kvp in _spawnedPlayers)
            {
                var playerController = kvp.Value.GetComponent<U3DPlayerController>();
                if (playerController != null && playerController.IsLocalPlayer)
                {
                    playerController.PerformTeleport();
                    return;
                }
            }
        }

        public async Task<bool> StartNetworking(string sessionName, string photonAppId = "")
        {
            U3D.Networking.U3DPlayerNametag.ResetPlayerNumbering();

            if (_runner != null)
            {
                await StopNetworking();
            }

            try
            {
                _currentSessionName = sessionName;

                var runnerObject = new GameObject($"NetworkRunner_{sessionName}");
                runnerObject.transform.SetParent(transform);

                _runner = runnerObject.AddComponent<NetworkRunner>();
                _runner.ProvideInput = true;

                var physicsSimulator = runnerObject.AddComponent<RunnerSimulatePhysics3D>();
                ConfigurePhysicsSimulatorForSharedMode(physicsSimulator);

                _runner.AddCallbacks(this);

                var args = new StartGameArgs()
                {
                    GameMode = GameMode.Shared,
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
                    Debug.LogWarning($"U3D Network Manager: Connection failed: {result.ShutdownReason}");
                    OnNetworkStatusChanged?.Invoke(false);
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"U3D Network Manager: Network start error: {e.Message}");
                OnNetworkStatusChanged?.Invoke(false);
                return false;
            }
        }

        private void ConfigurePhysicsSimulatorForSharedMode(RunnerSimulatePhysics3D physicsSimulator)
        {
            physicsSimulator.ClientPhysicsSimulation = ClientPhysicsSimulation.SimulateAlways;
        }

        public async Task StopNetworking()
        {
            if (_runner != null)
            {
                _runner.RemoveCallbacks(this);
                await _runner.Shutdown();

                if (_runner.gameObject != null)
                {
                    Destroy(_runner.gameObject);
                }

                _runner = null;
                _spawnedPlayers.Clear();

                OnNetworkStatusChanged?.Invoke(false);
                OnPlayerCountChanged?.Invoke(0);
            }
        }

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
        }

        // ========== FUSION CALLBACKS ==========

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            bool shouldSpawn = runner.GameMode == GameMode.Shared
                ? player == runner.LocalPlayer
                : runner.IsServer;

            if (shouldSpawn && playerPrefab.IsValid)
            {
                SpawnLocalPlayer(runner, player);
            }

            OnPlayerJoinedEvent?.Invoke(player);
            OnPlayerCountChanged?.Invoke(_spawnedPlayers.Count);
        }

        public void OnSceneLoadDone(NetworkRunner runner) { }

        private void SpawnLocalPlayer(NetworkRunner runner, PlayerRef player)
        {
            Vector3 spawnPosition;
            Quaternion spawnRotation;

            if (U3DPlayerSpawner.Instance != null)
            {
                var spawnData = U3DPlayerSpawner.Instance.GetSpawnData();
                spawnPosition = spawnData.position;
                spawnRotation = spawnData.rotation;
            }
            else
            {
                spawnPosition = GetSpawnPosition();
                spawnRotation = Quaternion.identity;
                Debug.LogWarning("No PlayerSpawner found, using NetworkManager fallback");
            }

            Vector3 pos = spawnPosition;
            Quaternion rot = spawnRotation;
            var playerObject = runner.Spawn(playerPrefab, pos, rot, player,
                (r, obj) =>
                {
                    obj.transform.position = pos;
                    obj.transform.rotation = rot;
                });

            if (playerObject != null)
            {
                _spawnedPlayers[player] = playerObject;
                var playerController = playerObject.GetComponent<U3DPlayerController>();
                if (playerController != null)
                {
                    playerController.RefreshInputActionsFromNetworkManager(this);
                    StartCoroutine(ForceSpawnPosition(playerController, pos, rot));
                }
                InitializePhysicsObjectsForPlayer(player);
            }
            else
            {
                Debug.LogWarning($"Failed to spawn player at {pos}");
            }
        }

        private System.Collections.IEnumerator ForceSpawnPosition(U3DPlayerController controller, Vector3 pos, Quaternion rot)
        {
            // Wait two frames for Fusion state sync to settle, then force position
            yield return null;
            yield return null;
            controller.SetPosition(pos);
            controller.SetRotation(rot.eulerAngles.y);
        }

        private System.Collections.IEnumerator DelayedSpawn(NetworkRunner runner, PlayerRef player)
        {
            yield return new WaitForSeconds(0.5f);

            Vector3 spawnPosition;
            Quaternion spawnRotation;

            if (U3DPlayerSpawner.Instance != null)
            {
                var spawnData = U3DPlayerSpawner.Instance.GetSpawnData();
                spawnPosition = spawnData.position;
                spawnRotation = spawnData.rotation;
            }
            else
            {
                spawnPosition = GetSpawnPosition();
                spawnRotation = Quaternion.identity;
                Debug.LogWarning("No PlayerSpawner found, using NetworkManager fallback");
            }

            Vector3 pos = spawnPosition;
            Quaternion rot = spawnRotation;
            var playerObject = runner.Spawn(playerPrefab, spawnPosition, spawnRotation, player,
                (runner, obj) =>
                {
                    obj.transform.position = pos;
                    obj.transform.rotation = rot;
                });

            if (playerObject != null)
            {
                _spawnedPlayers[player] = playerObject;

                var playerController = playerObject.GetComponent<U3DPlayerController>();
                if (playerController != null)
                {
                    playerController.RefreshInputActionsFromNetworkManager(this);
                }

                InitializePhysicsObjectsForPlayer(player);
            }
            else
            {
                Debug.LogError($"Failed to spawn player at {spawnPosition}");
            }
        }

        private void InitializePhysicsObjectsForPlayer(PlayerRef player)
        {
            if (_runner.GameMode != GameMode.Shared) return;

            U3DGrabbable[] grabbables = FindObjectsByType<U3DGrabbable>(FindObjectsSortMode.None);

            foreach (var grabbable in grabbables)
            {
                if (grabbable.IsNetworked && grabbable.GetComponent<NetworkObject>() != null)
                {
                    var netObj = grabbable.GetComponent<NetworkObject>();

                    if (netObj.HasStateAuthority && grabbable.IsGrabbed)
                    {
                        Debug.LogWarning($"Found grabbed object {grabbable.name} on player join - releasing");
                        grabbable.Release();
                    }
                }
            }
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            U3D.Networking.U3DPlayerNametag.RemovePlayer(player);

            if (_spawnedPlayers.TryGetValue(player, out NetworkObject playerObject))
            {
                if (playerObject != null)
                {
                    runner.Despawn(playerObject);
                }
                _spawnedPlayers.Remove(player);
            }

            OnPlayerLeftEvent?.Invoke(player);
            OnPlayerCountChanged?.Invoke(_spawnedPlayers.Count);
        }

        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
        {
            Debug.LogWarning($"U3D Network Manager: Connection failed: {reason}");
            OnNetworkStatusChanged?.Invoke(false);
        }

        public void OnConnectedToServer(NetworkRunner runner)
        {
        }

        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            Debug.LogWarning($"U3D Network Manager: Disconnected: {reason}");
            OnNetworkStatusChanged?.Invoke(false);
        }

        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            _spawnedPlayers.Clear();
            U3D.Networking.U3DPlayerNametag.ResetPlayerNumbering();
            OnNetworkStatusChanged?.Invoke(false);
            OnPlayerCountChanged?.Invoke(0);
        }

        public void OnInput(NetworkRunner runner, NetworkInput input)
        {
            var data = new U3DNetworkInputData();

            data.MovementInput = _cachedMovementInput;
            data.LookInput = _cachedLookInput;
            data.PerspectiveScroll = _perspectiveScrollValue;

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

            data.LeftMouseHeld = _leftMouseHeld;
            data.RightMouseHeld = _rightMouseHeld;
            data.BothMouseHeld = _bothMouseHeld;

            data.StrafeLeft = _strafeLeftPressed;
            data.StrafeRight = _strafeRightPressed;
            data.TurnLeft = _turnLeftPressed;
            data.TurnRight = _turnRightPressed;

            if (_autoRunTogglePressed)
                data.Buttons.Set(U3DInputButtons.AutoRunToggle, true);

            _autoRunTogglePressed = false;

            input.Set(data);

            _jumpPressed = false;
            _sprintPressed = false;
            _crouchPressed = false;
            _flyPressed = false;
            _interactPressed = false;
            _teleportPressed = false;
            _perspectiveScrollValue = 0f;
            _cachedLookInput = Vector2.zero; 
            _cachedMovementInput = Vector2.zero;
        }

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

        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<Fusion.SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey reliableKey, ArraySegment<byte> data) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey reliableKey, float progress) { }

        void OnDestroy()
        {
            U3D.XR.U3DWebXRManager.OnVRModeChanged -= OnVRModeChanged;

            if (_runner != null)
            {
                _runner.Shutdown();
            }

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