using UnityEngine;
#if WEBXR_ENABLED
using WebXR;
#endif
#if WEBXR_INTERACTIONS_ENABLED
using WebXR.Interactions;
#endif

namespace U3D.XR
{
    /// <summary>
    /// WebXR Manager for Unreality3D - Bridges WebXR Export package with U3D player system.
    /// Handles VR session start/end and notifies U3DPlayerController of mode changes.
    /// 
    /// ARCHITECTURE NOTES:
    /// - WebXR Export (De-Panther 0.22.x) uses native Unity XR SDK subsystems
    /// - VR sessions are initiated by user clicking "Enter VR" button in WebGL template
    /// - This manager subscribes to WebXRManager.OnXRChange events
    /// - When VR activates, it finds the local player and calls SetVRMode(true)
    /// - WebXR takes over camera rendering automatically via XR subsystems
    /// 
    /// REQUIREMENTS:
    /// - WebXR Export 0.22.x package installed
    /// - WebXRFullView2020 template selected in Player Settings
    /// - XR Plug-in Management > WebGL > WebXR Export enabled
    /// </summary>
    public class U3DWebXRManager : MonoBehaviour
    {
        [Header("WebXR Configuration")]
        [SerializeField] private bool autoFindLocalPlayer = true;
        [SerializeField] private bool verboseLogging = false;

        [Header("VR Hand Visuals")]
        [SerializeField] private GameObject leftHandPrefab;
        [SerializeField] private GameObject rightHandPrefab;
        [SerializeField] private float handVisualScale = 0.1f;
        [SerializeField] private Color handVisualColor = new Color(0.3f, 0.6f, 1f, 0.8f);

        // Singleton
        public static U3DWebXRManager Instance { get; private set; }

        // State
        private bool _isVRActive = false;
        private bool _isVRSupported = false;
        private U3DPlayerController _localPlayerController;

#if WEBXR_ENABLED
        private WebXRState _currentXRState = WebXRState.NORMAL;
#endif

        // Events for external systems
        public delegate void VRModeChanged(bool isVRActive);
        public static event VRModeChanged OnVRModeChanged;

        public delegate void VRSupportDetected(bool isSupported);
#pragma warning disable CS0067
        public static event VRSupportDetected OnVRSupportDetected;
#pragma warning restore CS0067

        // Public Properties
        public bool IsVRActive => _isVRActive;
        public bool IsVRSupported => _isVRSupported;
        public U3DPlayerController LocalPlayer => _localPlayerController;

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                LogVerbose("U3DWebXRManager initialized");
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }
        }

        void Start()
        {
            InitializeWebXR();
        }

        void InitializeWebXR()
        {
#if WEBXR_ENABLED && UNITY_WEBGL && !UNITY_EDITOR
            // Subscribe to WebXR state changes
            WebXRManager.OnXRChange += OnXRChange;
            
            // Check initial VR support
            StartCoroutine(CheckVRSupportDelayed());
            
            LogVerbose("WebXR event subscription active");
#else
            // In Editor or non-WebGL builds, or when WebXR package not installed
            _isVRSupported = false;
            LogVerbose("WebXR not available (Editor, non-WebGL build, or package not installed)");
#endif
        }

#if WEBXR_ENABLED && UNITY_WEBGL && !UNITY_EDITOR
        private System.Collections.IEnumerator CheckVRSupportDelayed()
        {
            // Wait for WebXRManager to initialize
            yield return new WaitForSeconds(0.5f);
            
            if (WebXRManager.Instance != null)
            {
                _isVRSupported = WebXRManager.Instance.isSupportedVR;
                LogVerbose($"VR Support detected: {_isVRSupported}");
                OnVRSupportDetected?.Invoke(_isVRSupported);
            }
            else
            {
                LogVerbose("WebXRManager.Instance not found - VR support check failed");
                _isVRSupported = false;
                OnVRSupportDetected?.Invoke(false);
            }
        }

        /// <summary>
        /// Called by WebXR Export when XR state changes (user enters/exits VR)
        /// Signature matches WebXRManager.OnXRChange delegate in 0.22.x
        /// </summary>
        private void OnXRChange(WebXRState state, int viewsCount, Rect leftRect, Rect rightRect)
        {
            _currentXRState = state;
            bool wasVRActive = _isVRActive;
            _isVRActive = (state == WebXRState.VR);
            
            LogVerbose($"WebXR state changed: {state}, Views: {viewsCount}, VR Active: {_isVRActive}");

            if (_isVRActive != wasVRActive)
            {
                HandleVRModeChange(_isVRActive);
            }
        }
#endif

        private void HandleVRModeChange(bool enteringVR)
        {
            LogVerbose($"VR Mode Change: {(enteringVR ? "ENTERING" : "EXITING")} VR");

#if WEBXR_ENABLED && UNITY_WEBGL && !UNITY_EDITOR
            if (enteringVR)
            {
                // Refresh controller references when entering VR
                // Small delay to allow WebXRController components to initialize
                StartCoroutine(RefreshControllersDelayed());
            }
#endif

            // Find local player if needed
            if (_localPlayerController == null && autoFindLocalPlayer)
            {
                FindLocalPlayer();
            }

            if (_localPlayerController != null)
            {
                // Notify player controller of VR mode change
                _localPlayerController.SetVRMode(enteringVR);
                LogVerbose($"Notified player controller: SetVRMode({enteringVR})");
            }
            else
            {
                Debug.LogWarning("U3DWebXRManager: No local player found to notify of VR mode change");
            }

            // Fire event for other systems (UI, analytics, etc.)
            OnVRModeChanged?.Invoke(enteringVR);
        }

#if WEBXR_ENABLED && UNITY_WEBGL && !UNITY_EDITOR
        private System.Collections.IEnumerator RefreshControllersDelayed()
        {
            // Wait a frame for WebXRController components to activate
            yield return null;
            yield return new WaitForSeconds(0.1f);
            RefreshControllerReferences();
        }
#endif

        /// <summary>
        /// Find the local player controller in the scene
        /// </summary>
        public void FindLocalPlayer()
        {
            var allPlayers = FindObjectsByType<U3DPlayerController>(FindObjectsSortMode.None);

            foreach (var player in allPlayers)
            {
                if (player.IsLocalPlayer)
                {
                    _localPlayerController = player;
                    LogVerbose($"Found local player: {player.gameObject.name}");
                    return;
                }
            }

            LogVerbose("No local player found in scene");
        }

        /// <summary>
        /// Register a player controller as the local player (called by U3DPlayerController.Spawned)
        /// </summary>
        public void RegisterLocalPlayer(U3DPlayerController player)
        {
            _localPlayerController = player;
            LogVerbose($"Local player registered: {player.gameObject.name}");

            // If VR was already active when player spawned, notify immediately
            if (_isVRActive)
            {
                LogVerbose("VR already active - notifying newly registered player");
                player.SetVRMode(true);
            }
        }

        /// <summary>
        /// Unregister player controller (called when player despawns)
        /// </summary>
        public void UnregisterLocalPlayer(U3DPlayerController player)
        {
            if (_localPlayerController == player)
            {
                _localPlayerController = null;
                LogVerbose("Local player unregistered");
            }
        }

#if WEBXR_INTERACTIONS_ENABLED && UNITY_WEBGL && !UNITY_EDITOR
        // Cache WebXRController references for performance
        private WebXRController _leftController;
        private WebXRController _rightController;

        /// <summary>
        /// Find WebXRController components in scene (typically on WebXRCameraSet prefab)
        /// Call this after VR mode activates or when controllers need refreshing
        /// </summary>
        public void RefreshControllerReferences()
        {
            var controllers = FindObjectsByType<WebXRController>(FindObjectsSortMode.None);
            foreach (var controller in controllers)
            {
                if (controller.hand == WebXRControllerHand.LEFT)
                    _leftController = controller;
                else if (controller.hand == WebXRControllerHand.RIGHT)
                    _rightController = controller;
            }
            LogVerbose($"Controller references refreshed: Left={_leftController != null}, Right={_rightController != null}");
        }

        /// <summary>
        /// Get controller data from WebXR (for hand pose syncing)
        /// In WebXR Export 0.20+, use WebXRController component which handles pose tracking
        /// </summary>
        public bool TryGetControllerPose(bool isLeftHand, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (!_isVRActive) 
                return false;

            var controller = isLeftHand ? _leftController : _rightController;
            
            if (controller != null && controller.isControllerActive)
            {
                // WebXRController transforms are already in world space
                position = controller.transform.position;
                rotation = controller.transform.rotation;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Get head pose from WebXR (for avatar head tracking)
        /// In WebXR Export 0.20+, head pose comes from Unity's XR subsystem
        /// The camera is automatically positioned by the WebXR Display subsystem
        /// </summary>
        public bool TryGetHeadPose(out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (!_isVRActive) 
                return false;

            var mainCam = Camera.main;
            if (mainCam != null)
            {
                position = mainCam.transform.localPosition;
                rotation = mainCam.transform.localRotation;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Get controller button state using string-based action names
        /// WebXRController uses string identifiers: "Trigger", "Grip", "Thumbstick", "ButtonA", "ButtonB"
        /// </summary>
        public bool GetControllerButton(bool isLeftHand, string buttonName)
        {
            if (!_isVRActive)
                return false;

            var controller = isLeftHand ? _leftController : _rightController;
            
            if (controller != null && controller.isControllerActive)
            {
                return controller.GetButton(buttonName);
            }

            return false;
        }

        /// <summary>
        /// Get controller button down (pressed this frame)
        /// </summary>
        public bool GetControllerButtonDown(bool isLeftHand, string buttonName)
        {
            if (!_isVRActive)
                return false;

            var controller = isLeftHand ? _leftController : _rightController;
            
            if (controller != null && controller.isControllerActive)
            {
                return controller.GetButtonDown(buttonName);
            }

            return false;
        }

        /// <summary>
        /// Get controller axis value
        /// Axis names: "Trigger", "Grip", "ThumbstickX", "ThumbstickY"
        /// </summary>
        public float GetControllerAxis(bool isLeftHand, string axisName)
        {
            if (!_isVRActive)
                return 0f;

            var controller = isLeftHand ? _leftController : _rightController;
            
            if (controller != null && controller.isControllerActive)
            {
                return controller.GetAxis(axisName);
            }

            return 0f;
        }

        /// <summary>
        /// Get thumbstick as Vector2
        /// </summary>
        public Vector2 GetThumbstick(bool isLeftHand)
        {
            if (!_isVRActive)
                return Vector2.zero;

            var controller = isLeftHand ? _leftController : _rightController;
            
            if (controller != null && controller.isControllerActive)
            {
                return new Vector2(
                    controller.GetAxis("ThumbstickX"),
                    controller.GetAxis("ThumbstickY")
                );
            }

            return Vector2.zero;
        }
        
        /// <summary>
        /// Get trigger value (0-1)
        /// </summary>
        public float GetTriggerValue(bool isLeftHand)
        {
            return GetControllerAxis(isLeftHand, "Trigger");
        }

        /// <summary>
        /// Get grip value (0-1)
        /// </summary>
        public float GetGripValue(bool isLeftHand)
        {
            return GetControllerAxis(isLeftHand, "Grip");
        }
        
        /// <summary>
        /// Check if controller is currently active/tracked
        /// </summary>
        public bool IsControllerActive(bool isLeftHand)
        {
            var controller = isLeftHand ? _leftController : _rightController;
            return controller != null && controller.isControllerActive;
        }
#else
        // Editor/non-WebGL/no-WebXR stubs
        public void RefreshControllerReferences() { }

        public bool TryGetControllerPose(bool isLeftHand, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            return false;
        }

        public bool TryGetHeadPose(out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            return false;
        }

        public bool GetControllerButton(bool isLeftHand, string buttonName)
        {
            return false;
        }

        public bool GetControllerButtonDown(bool isLeftHand, string buttonName)
        {
            return false;
        }

        public float GetControllerAxis(bool isLeftHand, string axisName)
        {
            return 0f;
        }

        public Vector2 GetThumbstick(bool isLeftHand)
        {
            return Vector2.zero;
        }

        public float GetTriggerValue(bool isLeftHand)
        {
            return 0f;
        }

        public float GetGripValue(bool isLeftHand)
        {
            return 0f;
        }

        public bool IsControllerActive(bool isLeftHand)
        {
            return false;
        }
#endif

        /// <summary>
        /// Create simple hand visuals (spheres) if no prefabs assigned
        /// </summary>
        public GameObject CreateDefaultHandVisual(bool isLeftHand, Transform parent)
        {
            GameObject handVisual;

            // Check for assigned prefab first
            var prefab = isLeftHand ? leftHandPrefab : rightHandPrefab;
            if (prefab != null)
            {
                handVisual = Instantiate(prefab, parent);
            }
            else
            {
                // Create simple sphere
                handVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                handVisual.transform.SetParent(parent);
                handVisual.transform.localScale = Vector3.one * handVisualScale;

                // Remove collider (visual only)
                var collider = handVisual.GetComponent<Collider>();
                if (collider != null)
                    Destroy(collider);

                // Apply material color
                var renderer = handVisual.GetComponent<Renderer>();
                if (renderer != null)
                {
                    var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    if (mat != null)
                    {
                        mat.color = handVisualColor;
                        renderer.material = mat;
                    }
                }
            }

            handVisual.name = isLeftHand ? "LeftHandVisual" : "RightHandVisual";
            handVisual.SetActive(false); // Start hidden, VR mode enables them

            return handVisual;
        }

        private void LogVerbose(string message)
        {
            if (verboseLogging)
            {
                Debug.Log($"[U3DWebXRManager] {message}");
            }
        }

        void OnDestroy()
        {
#if WEBXR_ENABLED && UNITY_WEBGL && !UNITY_EDITOR
            // Unsubscribe from WebXR events
            WebXRManager.OnXRChange -= OnXRChange;
#endif

            if (Instance == this)
            {
                Instance = null;
            }
        }

        void OnDisable()
        {
#if WEBXR_ENABLED && UNITY_WEBGL && !UNITY_EDITOR
            // Also unsubscribe when disabled to prevent stale references
            WebXRManager.OnXRChange -= OnXRChange;
#endif
        }

        void OnEnable()
        {
#if WEBXR_ENABLED && UNITY_WEBGL && !UNITY_EDITOR
            // Re-subscribe when re-enabled
            WebXRManager.OnXRChange -= OnXRChange; // Prevent double subscription
            WebXRManager.OnXRChange += OnXRChange;
#endif
        }
    }
}