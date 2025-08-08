using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace U3D.Input
{
    /// <summary>
    /// Simplified zone-based touch controller that provides raw input values
    /// Designed to feed directly into U3DFusionNetworkManager's polling system
    /// No virtual controls, no Input System dependency - just pure touch data
    /// </summary>
    public class U3DSimpleTouchZones : MonoBehaviour
    {
        [Header("Zone Configuration")]
        [SerializeField] private float screenDivider = 0.5f; // 0.5 = half screen
        [SerializeField] private float movementSensitivity = 1.0f;
        [SerializeField] private float lookSensitivity = 0.5f;

        [Header("Gesture Timing")]
        [SerializeField] private float doubleTapWindow = 0.3f;
        [SerializeField] private float longPressTime = 0.5f;

        [Header("Dead Zones")]
        [SerializeField] private float movementDeadZone = 20f; // pixels
        [SerializeField] private float lookDeadZone = 2f; // pixels per frame

        // Touch tracking
        private Dictionary<int, TouchData> activeTouches = new Dictionary<int, TouchData>();
        private TouchData movementTouch;
        private TouchData lookTouch;

        // Gesture detection
        private float lastRightTapTime;
        private Vector2 lastRightTapPosition;
        private float longPressStartTime;
        private bool isLongPressing;

        // Pinch-to-zoom tracking
        private float lastPinchDistance;
        private bool isPinching;
        private float pinchStartDistance;

        // Output values - READ BY NETWORK MANAGER
        public Vector2 MovementInput { get; private set; }
        public Vector2 LookInput { get; private set; }
        public bool JumpRequested { get; private set; }
        public bool SprintActive { get; private set; }
        public bool InteractRequested { get; private set; }
        public bool CrouchRequested { get; private set; }
        public bool FlyRequested { get; private set; }
        public float ZoomInput { get; private set; }  // -1 to 1, 0 = no zoom
        public bool PerspectiveSwitchRequested { get; private set; }  // For large pinch gesture

        // Singleton for easy access
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
        }

        void Update()
        {
            // Only process touch on mobile platforms or in editor with simulation
            if (!Application.isMobilePlatform && !Application.isEditor)
                return;

            ProcessTouches();
            ClearOneFrameInputs();
        }

        void ProcessTouches()
        {
            // Reset continuous inputs
            MovementInput = Vector2.zero;
            LookInput = Vector2.zero;
            ZoomInput = 0f;

            // Check for pinch gesture first (requires 2+ touches)
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

            // Calculate movement from left touch (but not during pinch)
            if (movementTouch != null && !isPinching)
            {
                Vector2 delta = movementTouch.currentPosition - movementTouch.startPosition;

                if (delta.magnitude > movementDeadZone)
                {
                    // Normalize to -1 to 1 range
                    delta /= Screen.width * 0.3f; // 30% of screen width = max movement
                    delta = Vector2.ClampMagnitude(delta, 1f);
                    MovementInput = delta * movementSensitivity;
                }

                // Check for long press sprint
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

            // Calculate look from right touch (but not during pinch)
            if (lookTouch != null && !isPinching)
            {
                Vector2 delta = lookTouch.currentPosition - lookTouch.lastPosition;

                if (delta.magnitude > lookDeadZone)
                {
                    // Convert to normalized screen space
                    delta.x /= Screen.width;
                    delta.y /= Screen.height;

                    // Scale and apply sensitivity
                    LookInput = new Vector2(delta.x, -delta.y) * lookSensitivity * 100f;
                }

                lookTouch.lastPosition = lookTouch.currentPosition;
            }
        }

        void HandleTouchBegan(Touch touch)
        {
            // Ignore if touching UI
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
                // Left side - movement
                movementTouch = data;
                longPressStartTime = Time.time;
                isLongPressing = true;
            }
            else if (!isLeftSide && lookTouch == null)
            {
                // Right side - camera look
                lookTouch = data;

                // Check for double tap jump
                float timeSinceLastTap = Time.time - lastRightTapTime;
                float distance = Vector2.Distance(touch.position, lastRightTapPosition);

                if (timeSinceLastTap < doubleTapWindow && distance < 50f)
                {
                    JumpRequested = true;
                    lastRightTapTime = 0; // Reset to prevent triple tap
                }
                else
                {
                    lastRightTapTime = Time.time;
                    lastRightTapPosition = touch.position;
                }
            }

            // Special gestures
            CheckSpecialGestures();
        }

        void HandleTouchMoved(Touch touch)
        {
            if (activeTouches.TryGetValue(touch.fingerId, out TouchData data))
            {
                data.currentPosition = touch.position;

                // Cancel long press if moved too much
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
                // Quick tap detection
                if (Time.time - data.startTime < 0.2f)
                {
                    Vector2 delta = data.currentPosition - data.startPosition;
                    if (delta.magnitude < 30f) // Barely moved
                    {
                        if (data.isLeftSide)
                        {
                            // Quick tap on left - interact
                            InteractRequested = true;
                        }
                    }
                }

                // Clean up
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
            // Two fingers on left side = crouch
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

            // Three finger tap anywhere = fly mode
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

            // Calculate current distance between touches
            float currentPinchDistance = Vector2.Distance(touch1.position, touch2.position);

            // Check if just started pinching
            if (!isPinching)
            {
                isPinching = true;
                pinchStartDistance = currentPinchDistance;
                lastPinchDistance = currentPinchDistance;
                return;
            }

            // Calculate pinch delta
            float pinchDelta = currentPinchDistance - lastPinchDistance;

            // Small continuous zoom (for FOV adjustment)
            if (Mathf.Abs(pinchDelta) > 1f) // Minimum threshold to filter noise
            {
                // Normalize to -1 to 1 based on screen size
                float normalizedDelta = pinchDelta / (Screen.width * 0.1f); // 10% of screen width
                ZoomInput = Mathf.Clamp(normalizedDelta, -1f, 1f);
            }

            // Large pinch gesture for perspective switch
            float totalPinchChange = currentPinchDistance - pinchStartDistance;
            if (Mathf.Abs(totalPinchChange) > Screen.width * 0.3f) // 30% of screen width
            {
                if (!PerspectiveSwitchRequested) // Only trigger once per gesture
                {
                    PerspectiveSwitchRequested = true;
                    Debug.Log($"Perspective switch triggered by large pinch: {totalPinchChange}");
                }
            }

            lastPinchDistance = currentPinchDistance;
        }

        void ClearOneFrameInputs()
        {
            // These are one-shot inputs that need to be cleared after being read
            JumpRequested = false;
            InteractRequested = false;
            CrouchRequested = false;
            FlyRequested = false;
            PerspectiveSwitchRequested = false;
        }

        // Debug visualization
        void OnGUI()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!Application.isMobilePlatform) return;

            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = 24;

            // Draw zone divider
            float dividerX = Screen.width * screenDivider;
            GUI.Box(new Rect(dividerX - 1, 0, 2, Screen.height), "");

            // Show active inputs
            if (movementTouch != null)
            {
                GUI.Label(new Rect(10, 10, 300, 30), $"Move: {MovementInput}", style);

                // Draw movement origin
                Vector2 screenPos = movementTouch.startPosition;
                screenPos.y = Screen.height - screenPos.y;
                GUI.Box(new Rect(screenPos.x - 40, screenPos.y - 40, 80, 80), "MOVE");
            }

            if (lookTouch != null)
            {
                GUI.Label(new Rect(10, 50, 300, 30), $"Look: {LookInput}", style);
            }

            if (SprintActive)
            {
                GUI.Label(new Rect(10, 90, 200, 30), "SPRINT", style);
            }
#endif
        }
    }
}