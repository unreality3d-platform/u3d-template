using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem.Utilities;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace U3D.Input
{
    /// <summary>
    /// Zone-based touch controller that provides raw input values.
    /// Feeds U3DFusionNetworkManager's polling system via public properties.
    ///
    /// Built on the Input System's EnhancedTouch API. The legacy
    /// UnityEngine.Input touch API was abandoned here because its phase
    /// reporting is unreliable on iOS Safari/WebKit WebGL (continuous drags
    /// misreported as repeated Began events). EnhancedTouch flushes its event
    /// queue later in the frame and reports phase/position correctly on that
    /// platform. This is the project's touch input path; it runs polled,
    /// parallel to the .inputactions-driven desktop/VR input, and shares no
    /// state with it.
    /// </summary>
    public class U3DSimpleTouchZones : MonoBehaviour
    {
        [Header("Zone Configuration")]
        [SerializeField] private float screenDivider = 0.5f;
        [SerializeField] private float movementSensitivity = 1.0f;
        [SerializeField] private float lookSensitivity = 0.5f;

        [Header("Gesture Timing")]
        [SerializeField] private float doubleTapWindow = 0.3f;
        [SerializeField] private float longPressTime = 0.5f;

        [Header("Dead Zones")]
        [SerializeField] private float movementDeadZone = 20f;
        [SerializeField] private float lookDeadZone = 2f;

        private Dictionary<int, TouchData> activeTouches = new Dictionary<int, TouchData>();
        private TouchData movementTouch;
        private TouchData lookTouch;

        private float lastRightTapTime;
        private Vector2 lastRightTapPosition;
        private float longPressStartTime;
        private bool isLongPressing;

        private float lastPinchDistance;
        private bool isPinching;
        private float pinchStartDistance;

        private bool _isTouchEnabled;
        private bool _enhancedTouchEnabled;

        public Vector2 MovementInput { get; private set; }
        public Vector2 LookInput { get; private set; }
        public bool JumpRequested { get; private set; }
        public bool SprintActive { get; private set; }
        public bool InteractRequested { get; private set; }
        public bool CrouchRequested { get; private set; }
        public bool FlyRequested { get; private set; }
        public float ZoomInput { get; private set; }
        public bool PerspectiveSwitchRequested { get; private set; }

        public static U3DSimpleTouchZones Instance { get; private set; }

        private class TouchData
        {
            public int touchId;
            public Vector2 startPosition;
            public Vector2 currentPosition;
            public Vector2 frameDelta;
            public float startTime;
            public bool isLeftSide;
        }

        void Awake()
        {
            Instance = this;
            _isTouchEnabled = DetectTouchCapability();
        }

        void OnEnable()
        {
            if (!_enhancedTouchEnabled)
            {
                EnhancedTouchSupport.Enable();
                _enhancedTouchEnabled = true;
            }
        }

        void OnDisable()
        {
            if (_enhancedTouchEnabled)
            {
                EnhancedTouchSupport.Disable();
                _enhancedTouchEnabled = false;
            }
        }

        /// <summary>
        /// Detects whether this device supports touch input.
        /// Application.isMobilePlatform is false on WebGL even when running on a phone,
        /// so we also check for WebGL + a present Touchscreen device + mobile hints.
        /// </summary>
        private bool DetectTouchCapability()
        {
            if (Application.isMobilePlatform)
                return true;

            if (Application.isEditor)
                return true;

            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                if (Touchscreen.current != null)
                    return true;

                string deviceModel = SystemInfo.deviceModel.ToLower();
                if (deviceModel.Contains("mobile") ||
                    deviceModel.Contains("android") ||
                    deviceModel.Contains("iphone") ||
                    deviceModel.Contains("ipad") ||
                    deviceModel.Contains("ipod"))
                    return true;

                if (Screen.width < 1200 && Screen.height < 1200)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Returns true if this instance has determined touch input is available.
        /// Used by U3DFusionNetworkManager to decide whether to read touch zone data.
        /// </summary>
        public bool IsTouchEnabled => _isTouchEnabled;

        void Update()
        {
            if (!_isTouchEnabled)
                return;

            ProcessTouches();
            ClearOneFrameInputs();
        }

        void ProcessTouches()
        {
            MovementInput = Vector2.zero;
            LookInput = Vector2.zero;
            ZoomInput = 0f;

            var touches = ETouch.activeTouches;

            if (touches.Count >= 2)
            {
                ProcessPinchGesture(touches);
            }
            else
            {
                isPinching = false;
            }

            for (int i = 0; i < touches.Count; i++)
            {
                ETouch touch = touches[i];
                int id = touch.touchId;

                if (touch.began || !activeTouches.ContainsKey(id))
                {
                    HandleTouchBegan(touch);
                }
                else if (touch.ended)
                {
                    HandleTouchEnded(id);
                }
                else
                {
                    HandleTouchMoved(touch);
                }
            }

            if (movementTouch != null && !isPinching)
            {
                Vector2 delta = movementTouch.currentPosition - movementTouch.startPosition;

                if (delta.magnitude > movementDeadZone)
                {
                    delta /= Screen.width * 0.3f;
                    delta = Vector2.ClampMagnitude(delta, 1f);
                    MovementInput = delta * movementSensitivity;
                }

                if (isLongPressing && Time.time - longPressStartTime > longPressTime)
                {
                    SprintActive = true;
                    isLongPressing = false;
                }
            }
            else
            {
                SprintActive = false;
            }

            if (lookTouch != null && !isPinching)
            {
                Vector2 delta = lookTouch.frameDelta;

                if (delta.magnitude > lookDeadZone)
                {
                    delta.x /= Screen.width;
                    delta.y /= Screen.height;

                    LookInput = new Vector2(delta.x, -delta.y) * lookSensitivity * 100f;
                }

                lookTouch.frameDelta = Vector2.zero;
            }
        }

        void HandleTouchBegan(ETouch touch)
        {
            int id = touch.touchId;

            if (activeTouches.ContainsKey(id))
            {
                // Spurious re-Began for an already-tracked touch: treat as a move
                // so we never lose continuity or reassign the touch's role.
                HandleTouchMoved(touch);
                return;
            }

            Vector2 position = touch.screenPosition;

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(id))
                return;

            bool isLeftSide = position.x < Screen.width * screenDivider;

            TouchData data = new TouchData
            {
                touchId = id,
                startPosition = position,
                currentPosition = position,
                frameDelta = Vector2.zero,
                startTime = Time.time,
                isLeftSide = isLeftSide
            };

            activeTouches[id] = data;

            if (isLeftSide && movementTouch == null)
            {
                movementTouch = data;
                longPressStartTime = Time.time;
                isLongPressing = true;
            }
            else if (!isLeftSide && lookTouch == null)
            {
                lookTouch = data;

                float timeSinceLastTap = Time.time - lastRightTapTime;
                float distance = Vector2.Distance(position, lastRightTapPosition);

                if (timeSinceLastTap < doubleTapWindow && distance < 50f)
                {
                    JumpRequested = true;
                    lastRightTapTime = 0;
                }
                else
                {
                    lastRightTapTime = Time.time;
                    lastRightTapPosition = position;
                }
            }

            CheckSpecialGestures();
        }

        void HandleTouchMoved(ETouch touch)
        {
            if (activeTouches.TryGetValue(touch.touchId, out TouchData data))
            {
                Vector2 position = touch.screenPosition;
                data.frameDelta += position - data.currentPosition;
                data.currentPosition = position;

                if (data == movementTouch && isLongPressing)
                {
                    Vector2 delta = data.currentPosition - data.startPosition;
                    if (delta.magnitude > movementDeadZone * 2)
                    {
                        isLongPressing = false;
                    }
                }
            }
        }

        void HandleTouchEnded(int touchId)
        {
            if (activeTouches.TryGetValue(touchId, out TouchData data))
            {
                if (Time.time - data.startTime < 0.2f)
                {
                    Vector2 delta = data.currentPosition - data.startPosition;
                    if (delta.magnitude < 30f)
                    {
                        if (data.isLeftSide)
                        {
                            InteractRequested = true;
                        }
                    }
                }

                if (data == movementTouch)
                {
                    movementTouch = null;
                    isLongPressing = false;
                    SprintActive = false;
                }
                else if (data == lookTouch)
                {
                    lookTouch = null;
                }

                activeTouches.Remove(touchId);
            }
        }

        void CheckSpecialGestures()
        {
            if (activeTouches.Count >= 2)
            {
                int leftTouches = 0;
                foreach (var touch in activeTouches.Values)
                {
                    if (touch.isLeftSide) leftTouches++;
                }

                if (leftTouches >= 2)
                {
                    CrouchRequested = true;
                }
            }

            if (activeTouches.Count >= 3)
            {
                FlyRequested = true;
            }
        }

        void ProcessPinchGesture(ReadOnlyArray<ETouch> touches)
        {
            ETouch touch1 = touches[0];
            ETouch touch2 = touches[1];

            float currentPinchDistance = Vector2.Distance(touch1.screenPosition, touch2.screenPosition);

            if (!isPinching)
            {
                isPinching = true;
                pinchStartDistance = currentPinchDistance;
                lastPinchDistance = currentPinchDistance;
                return;
            }

            float pinchDelta = currentPinchDistance - lastPinchDistance;

            if (Mathf.Abs(pinchDelta) > 1f)
            {
                float normalizedDelta = pinchDelta / (Screen.width * 0.1f);
                ZoomInput = Mathf.Clamp(normalizedDelta, -1f, 1f);
            }

            float totalPinchChange = currentPinchDistance - pinchStartDistance;
            if (Mathf.Abs(totalPinchChange) > Screen.width * 0.3f)
            {
                if (!PerspectiveSwitchRequested)
                {
                    PerspectiveSwitchRequested = true;
                }
            }

            lastPinchDistance = currentPinchDistance;
        }

        void ClearOneFrameInputs()
        {
            JumpRequested = false;
            InteractRequested = false;
            CrouchRequested = false;
            FlyRequested = false;
            PerspectiveSwitchRequested = false;
        }
    }
}