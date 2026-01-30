using UnityEngine;
#if WEBXR_ENABLED
using WebXR;
#endif

namespace U3D.XR
{
    /// <summary>
    /// WebXR Manager for Unreality3D - Bridges WebXR Export package with U3D player system.
    /// Handles VR session start/end and notifies U3DPlayerController of mode changes.
    /// </summary>
    public class U3DWebXRManager : MonoBehaviour
    {
        [Header("WebXR Configuration")]
        [SerializeField] private bool autoFindLocalPlayer = true;
        [SerializeField] private bool verboseLogging = false;

        public static U3DWebXRManager Instance { get; private set; }

        private bool _isVRActive = false;
        private bool _isVRSupported = false;
        private U3DPlayerController _localPlayerController;

#if WEBXR_ENABLED
        private WebXRState _currentXRState = WebXRState.NORMAL;
#endif

        public delegate void VRModeChanged(bool isVRActive);
        public static event VRModeChanged OnVRModeChanged;

        public delegate void VRSupportDetected(bool isSupported);
        public static event VRSupportDetected OnVRSupportDetected;

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
            Debug.Log("[U3DWebXRManager] Subscribing to WebXRManager.OnXRChange");
            WebXRManager.OnXRChange += OnXRChange;
            StartCoroutine(CheckVRSupportDelayed());
            Debug.Log("[U3DWebXRManager] WebXR event subscription active");
#else
            _isVRSupported = false;
            Debug.Log("[U3DWebXRManager] WebXR not available (Editor, non-WebGL build, or package not installed)");
#endif
        }

#if WEBXR_ENABLED && UNITY_WEBGL && !UNITY_EDITOR
        private System.Collections.IEnumerator CheckVRSupportDelayed()
        {
            yield return new WaitForSeconds(0.5f);
            
            if (WebXRManager.Instance != null)
            {
                _isVRSupported = WebXRManager.Instance.isSupportedVR;
                Debug.Log($"[U3DWebXRManager] VR Support detected: {_isVRSupported}");
                OnVRSupportDetected?.Invoke(_isVRSupported);
            }
            else
            {
                Debug.Log("[U3DWebXRManager] WebXRManager.Instance not found - VR support check failed");
                _isVRSupported = false;
                OnVRSupportDetected?.Invoke(false);
            }
        }

        private void OnXRChange(WebXRState state, int viewsCount, Rect leftRect, Rect rightRect)
        {
            Debug.Log($"[U3DWebXRManager] OnXRChange FIRED: state={state}, views={viewsCount}");
    
            _currentXRState = state;
            bool wasVRActive = _isVRActive;
            _isVRActive = (state == WebXRState.VR);
    
            Debug.Log($"[U3DWebXRManager] wasVRActive={wasVRActive}, _isVRActive={_isVRActive}");

            if (_isVRActive != wasVRActive)
            {
                HandleVRModeChange(_isVRActive);
            }
        }
#endif

        private void HandleVRModeChange(bool enteringVR)
        {
            Debug.Log($"[U3DWebXRManager] HandleVRModeChange: {(enteringVR ? "ENTERING" : "EXITING")} VR");

            if (_localPlayerController == null && autoFindLocalPlayer)
            {
                FindLocalPlayer();
            }

            if (_localPlayerController != null)
            {
                _localPlayerController.SetVRMode(enteringVR);
                Debug.Log($"[U3DWebXRManager] Notified player controller: SetVRMode({enteringVR})");
            }
            else
            {
                Debug.LogWarning("[U3DWebXRManager] No local player found to notify of VR mode change");
            }

            OnVRModeChanged?.Invoke(enteringVR);
        }

        public void FindLocalPlayer()
        {
            var allPlayers = FindObjectsByType<U3DPlayerController>(FindObjectsSortMode.None);

            foreach (var player in allPlayers)
            {
                if (player.IsLocalPlayer)
                {
                    _localPlayerController = player;
                    Debug.Log($"[U3DWebXRManager] Found local player: {player.gameObject.name}");
                    return;
                }
            }

            Debug.Log("[U3DWebXRManager] No local player found in scene");
        }

        public void RegisterLocalPlayer(U3DPlayerController player)
        {
            _localPlayerController = player;
            Debug.Log($"[U3DWebXRManager] Local player registered: {player.gameObject.name}");

            if (_isVRActive)
            {
                Debug.Log("[U3DWebXRManager] VR already active - notifying newly registered player");
                player.SetVRMode(true);
            }
        }

        public void UnregisterLocalPlayer(U3DPlayerController player)
        {
            if (_localPlayerController == player)
            {
                _localPlayerController = null;
                Debug.Log("[U3DWebXRManager] Local player unregistered");
            }
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
            WebXRManager.OnXRChange -= OnXRChange;
            WebXRManager.OnXRChange += OnXRChange;
#endif
        }
    }
}