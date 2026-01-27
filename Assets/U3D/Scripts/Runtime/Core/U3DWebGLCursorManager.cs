using UnityEngine;
using UnityEngine.InputSystem;

namespace U3D
{
    /// <summary>
    /// WebGL-specific cursor management that integrates with existing NetworkManager
    /// Enables Tab to unlock for UI, Esc to escape WebGL, click to re-lock for FPS controls
    /// No duplicate Input Actions - uses NetworkManager's input system
    /// 
    /// VR/WebXR Note: Cursor lock is disabled during immersive VR sessions as browsers
    /// reject pointer lock requests when WebXR has exclusive input control.
    /// </summary>
    public class U3DWebGLCursorManager : MonoBehaviour
    {
        [Header("WebGL Cursor Configuration")]
        [SerializeField] private bool enableWebGLCursorManagement = true;
        [SerializeField] private bool startWithLockedCursor = true;

        [Header("UI References")]
        [SerializeField] private GameObject pauseMenu;
        [SerializeField] private Canvas gameUI;

        // Cursor state management
        private bool _isCursorLocked = false; // Start unlocked until user gesture
        private bool _isUIMode = false; // Tab mode - cursor free but WebGL has focus
        private bool _isEscapedMode = false; // Esc mode - cursor free and WebGL lost focus
        private bool _isInVRMode = false; // VR mode - cursor lock disabled entirely
        private bool _hasReceivedUserGesture = false; // Track if we've had a valid user gesture

        // Network manager reference (auto-found)
        private U3D.Networking.U3DFusionNetworkManager _networkManager;

        // Events
        public static event System.Action<bool> OnCursorLockStateChanged;

        // Public properties
        // In VR mode, report as "locked" so input code proceeds normally
        public bool IsCursorLocked => _isInVRMode || _isCursorLocked;
        public bool IsUIMode => _isUIMode;
        public bool IsEscapedMode => _isEscapedMode;
        public bool IsInVRMode => _isInVRMode;

        void Awake()
        {
            // Enable on WebGL builds and in Editor for testing
            bool isWebGLOrEditor = Application.platform == RuntimePlatform.WebGLPlayer ||
                                   Application.platform == RuntimePlatform.WindowsEditor ||
                                   Application.platform == RuntimePlatform.OSXEditor ||
                                   Application.platform == RuntimePlatform.LinuxEditor;

            if (!isWebGLOrEditor)
            {
                enableWebGLCursorManagement = false;
                enabled = false;
                Debug.Log("U3DWebGLCursorManager: Disabled on non-WebGL/Editor platform");
                return;
            }

            // Find network manager automatically
            _networkManager = FindAnyObjectByType<U3D.Networking.U3DFusionNetworkManager>();
            if (_networkManager == null)
            {
                Debug.LogWarning("U3DWebGLCursorManager: No NetworkManager found. Cursor management disabled.");
                enabled = false;
                return;
            }

            // DON'T lock cursor on Awake - wait for user gesture
            // Browser will reject pointer lock without user interaction
            _isCursorLocked = false;

            string platform = Application.platform == RuntimePlatform.WebGLPlayer ? "WebGL" : "Editor";
            Debug.Log($"✅ U3DWebGLCursorManager: Initialized for {platform} - awaiting user gesture for cursor lock");
        }

        void Update()
        {
            if (!enableWebGLCursorManagement || _networkManager == null) return;

            // Skip cursor management input during VR - VR controllers handle everything
            if (_isInVRMode) return;

            // Monitor Tab key via NetworkManager
            if (_networkManager.GetPauseAction() != null && _networkManager.GetPauseAction().WasPressedThisFrame())
            {
                OnTabPressed();
            }

            // Monitor Escape key via NetworkManager  
            if (_networkManager.GetEscapeAction() != null && _networkManager.GetEscapeAction().WasPressedThisFrame())
            {
                OnEscapePressed();
            }

            // Monitor mouse clicks for returning to game mode or initial lock
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                OnClickPressed();
            }
        }

        void OnTabPressed()
        {
            _hasReceivedUserGesture = true;
            if (_isEscapedMode) return; // Can't toggle while escaped
            ToggleUIMode();
        }

        void OnEscapePressed()
        {
            _hasReceivedUserGesture = true;
            // Esc key - release cursor and lose WebGL focus
            SetEscapedMode(true);
        }

        void OnClickPressed()
        {
            _hasReceivedUserGesture = true;

            // Skip if in VR mode
            if (_isInVRMode) return;

            if (_isEscapedMode)
            {
                // Click detected - WebGL regaining focus
                OnWebGLWindowRegainedFocus();
                return;
            }

            if (_isUIMode)
            {
                // Check if we clicked on UI elements
                if (!IsPointerOverUI())
                {
                    // Clicked on game area - return to game mode
                    SetUIMode(false);
                }
            }
            else if (!_isCursorLocked && startWithLockedCursor)
            {
                // First click - lock cursor for FPS controls (deferred from Awake)
                TrySetCursorLocked(true);
            }
        }

        public void ToggleUIMode()
        {
            SetUIMode(!_isUIMode);
        }

        public void SetUIMode(bool uiMode)
        {
            if (_isEscapedMode) return; // Can't change UI mode while escaped
            if (_isInVRMode) return; // UI mode not applicable in VR

            _isUIMode = uiMode;

            if (_isUIMode)
            {
                // Entering UI mode - unlock cursor but keep WebGL focus
                TrySetCursorLocked(false);

                // Show pause menu
                if (pauseMenu != null)
                    pauseMenu.SetActive(true);

                Debug.Log("🎯 Entered UI Mode (Tab) - Cursor free, WebGL focused");
            }
            else
            {
                // Exiting UI mode - lock cursor
                TrySetCursorLocked(true);

                // Hide pause menu
                if (pauseMenu != null)
                    pauseMenu.SetActive(false);

                Debug.Log("🎯 Exited UI Mode - Cursor locked for FPS controls");
            }
        }

        public void SetEscapedMode(bool escapedMode)
        {
            if (_isInVRMode) return; // Escape mode not applicable in VR

            _isEscapedMode = escapedMode;

            if (_isEscapedMode)
            {
                // Esc pressed - release cursor completely
                _isUIMode = false; // Clear UI mode
                TrySetCursorLocked(false);

                // Hide all game UI
                if (pauseMenu != null)
                    pauseMenu.SetActive(false);
                if (gameUI != null)
                    gameUI.enabled = false;

                Debug.Log("🚪 Escaped WebGL - Cursor free, browser has control");
            }
            else
            {
                // Returning from escape - restore game UI
                if (gameUI != null)
                    gameUI.enabled = true;

                Debug.Log("🎯 Returned to WebGL - Ready to resume");
            }
        }

        /// <summary>
        /// Called by U3DWebXRManager or U3DPlayerController when VR session starts/ends.
        /// Disables cursor lock management during VR as browsers reject pointer lock
        /// requests when WebXR has exclusive input control.
        /// </summary>
        public void SetVRMode(bool enabled)
        {
            bool wasInVR = _isInVRMode;
            _isInVRMode = enabled;

            if (enabled && !wasInVR)
            {
                // Entering VR - just update internal state
                // Don't touch Cursor.lockState at all - browser handles VR input exclusively
                _isCursorLocked = false;

                // Clear UI/escape modes
                _isUIMode = false;
                _isEscapedMode = false;

                Debug.Log("🥽 U3DWebGLCursorManager: VR mode enabled - cursor lock management suspended");
            }
            else if (!enabled && wasInVR)
            {
                // Exiting VR - restore cursor lock for FPS controls
                // Use try-catch as browser state may be unpredictable after VR session
                TrySetCursorLocked(true);

                Debug.Log("🖱️ U3DWebGLCursorManager: VR mode disabled - cursor lock restored");
            }
        }

        void OnWebGLWindowRegainedFocus()
        {
            if (_isEscapedMode)
            {
                // Player clicked back into WebGL window
                SetEscapedMode(false);
                TrySetCursorLocked(true); // Resume FPS mode
                Debug.Log("🎯 WebGL Window Regained Focus - Resuming FPS controls");
            }
        }

        /// <summary>
        /// Safely attempt to set cursor lock state with error handling.
        /// Browsers may reject pointer lock requests in various situations.
        /// </summary>
        void TrySetCursorLocked(bool locked)
        {
            // Skip during VR - browser will reject and we don't need cursor lock anyway
            if (_isInVRMode)
            {
                _isCursorLocked = false;
                return;
            }

            try
            {
                if (locked)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    _isCursorLocked = true;
                }
                else
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                    _isCursorLocked = false;
                }

                OnCursorLockStateChanged?.Invoke(_isCursorLocked);
            }
            catch (System.Exception e)
            {
                // Browser rejected pointer lock request - this is expected in many situations:
                // - No user gesture yet
                // - VR session active
                // - Page not focused
                // - Permission denied
                Debug.LogWarning($"🖱️ Cursor lock request rejected: {e.Message}");
                _isCursorLocked = false;
            }
        }

        bool IsPointerOverUI()
        {
            // Check if mouse is over UI elements
            return UnityEngine.EventSystems.EventSystem.current != null &&
                   UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
        }

        // Public method to check if game input should be processed
        public bool ShouldProcessGameInput()
        {
            // In VR mode, always process game input
            if (_isInVRMode) return true;

            return !_isEscapedMode;
        }
    }
}