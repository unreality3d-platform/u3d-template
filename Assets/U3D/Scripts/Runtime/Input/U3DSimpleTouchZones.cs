using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace U3D.Input
{
    /// <summary>
    /// Simplified zone-based touch controller that provides raw input values.
    /// Designed to feed directly into U3DFusionNetworkManager's polling system.
    /// No virtual controls, no Input System dependency — just pure touch data.
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
            public int fingerId;
            public Vector2 startPosition;
            public Vector2 currentPosition;
            public Vector2 lastPosition;
            public float startTime;
            public bool isLeftSide;
        }

        void Awake()
        {
            Instance = this;
            UnityEngine.Input.multiTouchEnabled = true;
            _isTouchEnabled = DetectTouchCapability();
        }

        /// <summary>
        /// Detects whether this device supports touch input.
        /// Application.isMobilePlatform is false on WebGL even when running on a phone,
        /// so we also check for WebGL + touch support + mobile user agent indicators.
        /// </summary>
        private bool DetectTouchCapability()
        {
            if (Application.isMobilePlatform)
                return true;

            if (Application.isEditor)
                return true;

            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                if (UnityEngine.Input.touchSupported)
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

            if (UnityEngine.Input.touchCount >= 2)
            {
                ProcessPinchGesture();
            }
            else
            {
                isPinching = false;
            }

            for (int i = 0; i < UnityEngine.Input.touchCount; i++)
            {
                Touch touch = UnityEngine.Input.GetTouch(i);

                switch (touch.phase)
                {
                    case UnityEngine.TouchPhase.Began:
                        HandleTouchBegan(touch);
                        break;

                    case UnityEngine.TouchPhase.Moved:
                    case UnityEngine.TouchPhase.Stationary:
                        HandleTouchMoved(touch);
                        break;

                    case UnityEngine.TouchPhase.Ended:
                    case UnityEngine.TouchPhase.Canceled:
                        HandleTouchEnded(touch);
                        break;
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
                Vector2 delta = lookTouch.currentPosition - lookTouch.lastPosition;

                if (delta.magnitude > lookDeadZone)
                {
                    delta.x /= Screen.width;
                    delta.y /= Screen.height;

                    LookInput = new Vector2(delta.x, -delta.y) * lookSensitivity * 100f;
                }

                lookTouch.lastPosition = lookTouch.currentPosition;
            }
        }

        void HandleTouchBegan(Touch touch)
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(touch.fingerId))
                return;

            bool isLeftSide = touch.position.x < Screen.width * screenDivider;

            TouchData data = new TouchData
            {
                fingerId = touch.fingerId,
                startPosition = touch.position,
                currentPosition = touch.position,
                lastPosition = touch.position,
                startTime = Time.time,
                isLeftSide = isLeftSide
            };

            activeTouches[touch.fingerId] = data;

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
                float distance = Vector2.Distance(touch.position, lastRightTapPosition);

                if (timeSinceLastTap < doubleTapWindow && distance < 50f)
                {
                    JumpRequested = true;
                    lastRightTapTime = 0;
                }
                else
                {
                    lastRightTapTime = Time.time;
                    lastRightTapPosition = touch.position;
                }
            }

            CheckSpecialGestures();
        }

        void HandleTouchMoved(Touch touch)
        {
            if (activeTouches.TryGetValue(touch.fingerId, out TouchData data))
            {
                data.currentPosition = touch.position;

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

        void HandleTouchEnded(Touch touch)
        {
            if (activeTouches.TryGetValue(touch.fingerId, out TouchData data))
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

                activeTouches.Remove(touch.fingerId);
            }
        }

        void CheckSpecialGestures()
        {
            if (UnityEngine.Input.touchCount >= 2)
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

            if (UnityEngine.Input.touchCount >= 3)
            {
                bool allNew = true;
                for (int i = 0; i < 3; i++)
                {
                    if (UnityEngine.Input.GetTouch(i).phase != UnityEngine.TouchPhase.Began)
                    {
                        allNew = false;
                        break;
                    }
                }

                if (allNew)
                {
                    FlyRequested = true;
                }
            }
        }

        void ProcessPinchGesture()
        {
            Touch touch1 = UnityEngine.Input.GetTouch(0);
            Touch touch2 = UnityEngine.Input.GetTouch(1);

            float currentPinchDistance = Vector2.Distance(touch1.position, touch2.position);

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