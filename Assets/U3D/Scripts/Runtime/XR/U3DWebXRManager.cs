using UnityEngine;
#if WEBXR_ENABLED
using WebXR;
#endif

namespace U3D.XR
{
    /// <summary>
    /// WebXR Manager for Unreality3D - Bridges WebXR Export package with U3D player system.
    /// Handles VR session start/end and notifies U3DPlayerController of mode changes.
    /// 
    /// ARCHITECTURE NOTES:
    /// - WebXR Export (De-Panther 0.20+) uses native Unity XR SDK subsystems
    /// - VR sessions are initiated by user clicking "Enter VR" button in WebGL template
    /// - This manager subscribes to WebXRManager.OnXRChange events
    /// - When VR activates, it finds the local player and calls SetVRMode(true)
    /// - WebXR takes over camera rendering automatically via XR subsystems
    /// - Controller input flows through Unity's Input System with XR bindings (U3DInputActions)
    /// 
    /// REQUIREMENTS:
    /// - WebXR Export 0.20+ package installed
    /// - WebXR Interactions package installed
    /// - WebXRFullView2020 template selected in Player Settings
    /// - XR Plug-in Management > WebGL > WebXR Export enabled
    /// - Input System configured with XR control scheme bindings
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
        public static event VRSupportDetected OnVRSupportDetected;

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
        /// Signature matches WebXRManager.OnXRChange delegate in 0.20+
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
                Debug.LogWarning("[U3DWebXRManager] No local player found to notify of VR mode change");
            }

            // Fire event for other systems (UI, analytics, etc.)
            OnVRModeChanged?.Invoke(enteringVR);
        }

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
            WebXRManager.OnXRChange -= OnXRChange;
#endif
        }

        void OnEnable()
        {
#if WEBXR_ENABLED && UNITY_WEBGL && !UNITY_EDITOR
            WebXRManager.OnXRChange -= OnXRChange; // Prevent double subscription
            WebXRManager.OnXRChange += OnXRChange;
#endif
        }
    }
}