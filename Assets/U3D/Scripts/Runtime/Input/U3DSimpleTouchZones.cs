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
    ///
    /// Zones:
    ///   Left half (continuous move stick) and right half (continuous look
    ///   stick) — both run as virtual analog sticks that respond every frame
    ///   the finger is down, not just while dragging. Two fingers can coexist
    ///   so move + look works simultaneously.
    ///
    ///   Middle-middle band (35-65% width, 35-65% height) — double-tap fires
    ///   Interact. Aligned with the gaze pointer reticle so "tap what you're
    ///   looking at" is the mental model.
    ///
    ///   Bottom-middle band (35-65% width, 0-25% height) — double-tap fires
    ///   Jump.
    ///
    /// Touches that land in the middle-middle or bottom-middle zones are
    /// provisionally action-candidates. If the touch drags significantly or
    /// stays down past the double-tap window, it converts to a look/move
    /// stick touch. This is the cost of overlapping action zones with the
    /// stick zones: a brief tap in those zones won't move the camera/avatar.
    /// </summary>
    public class U3DSimpleTouchZones : MonoBehaviour
    {
        [Header("Zone Configuration")]
        [SerializeField] private float screenDivider = 0.5f;
        [SerializeField] private float movementSensitivity = 1.0f;
        [SerializeField] private float lookSensitivity = 1.0f;

        [Tooltip("Pixels of thumb travel that maps to maximum look speed. Smaller = more sensitive, larger = more precise. Constant in pixels, so it feels the same in portrait and landscape.")]
        [SerializeField] private float lookMaxTravel = 200f;

        [Tooltip("Tuning multiplier so 'fully deflected' actually moves the camera at a reasonable speed. The old code used screen-normalized delta * 100f; this replaces that magic 100.")]
        [SerializeField] private float lookSpeedMultiplier = 4f;

        [Header("Action Zone Bounds (normalized 0-1)")]
        [SerializeField] private Vector2 middleZoneX = new Vector2(0.35f, 0.65f);
        [SerializeField] private Vector2 middleZoneY = new Vector2(0.35f, 0.65f);
        [SerializeField] private Vector2 bottomZoneX = new Vector2(0.35f, 0.65f);
        [SerializeField] private Vector2 bottomZoneY = new Vector2(0.00f, 0.25f);

        [Header("Gesture Timing")]
        [SerializeField] private float doubleTapWindow = 0.3f;
        [SerializeField] private float longPressTime = 0.5f;

        [Header("Dead Zones")]
        [SerializeField] private float movementDeadZone = 20f;
        [SerializeField] private float lookDeadZone = 2f;
        [Tooltip("Drag distance (pixels) past which an action-zone touch converts to a stick touch.")]
        [SerializeField] private float actionConvertDistance = 30f;

        private Dictionary<int, TouchData> activeTouches = new Dictionary<int, TouchData>();
        private TouchData movementTouch;
        private TouchData lookTouch;

        // Pending action-candidate touches — touches that landed in an action zone
        // and haven't yet been classified as a tap (released quickly, no drag) or
        // converted to a stick touch (dragged or held).
        private TouchData pendingInteractTouch;
        private TouchData pendingJumpTouch;

        private float lastInteractTapTime;
        private Vector2 lastInteractTapPosition;
        private float lastJumpTapTime;
        private Vector2 lastJumpTapPosition;

        private float longPressStartTime;
        private bool isLongPressing;

        private bool _isTouchEnabled;
        private bool _enhancedTouchEnabled;
        private bool _touchObservedThisSession;

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

        private enum TouchRole { Unassigned, Move, Look, PendingInteract, PendingJump }

        private class TouchData
        {
            public int touchId;
            public Vector2 startPosition;
            public Vector2 currentPosition;
            public Vector2 frameDelta;
            public float startTime;
            public bool isLeftSide;
            public TouchRole role;
        }

        void Awake()
        {
            Instance = this;
            // Touch capability now starts false and flips true the first frame an
            // actual touch is observed. This means keyboard/mouse work in editor
            // and on desktop WebGL by default; touch takes over only when the
            // user actually uses it.
            _isTouchEnabled = false;
            _touchObservedThisSession = false;
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
        /// Returns true once a touch has been observed this session. Lets
        /// U3DFusionNetworkManager use keyboard/mouse by default and switch to
        /// touch the moment the user actually touches the screen. No platform
        /// or build-target sniffing — pure runtime observation.
        /// </summary>
        public bool IsTouchEnabled => _isTouchEnabled;

        void Update()
        {
            // Observe whether any touch is currently active. The first time we
            // see one this session, flip the touch-enabled flag on permanently.
            if (!_touchObservedThisSession && ETouch.activeTouches.Count > 0)
            {
                _touchObservedThisSession = true;
                _isTouchEnabled = true;
            }

            if (!_isTouchEnabled)
                return;

            ProcessTouches();
        }

        /// <summary>
        /// Per-frame clear of one-shot inputs. Called by U3DFusionNetworkManager
        /// after it reads them, so the request flags survive until consumed
        /// rather than being cleared on the same frame they're set.
        /// </summary>
        public void ConsumeOneFrameInputs()
        {
            JumpRequested = false;
            InteractRequested = false;
            CrouchRequested = false;
            FlyRequested = false;
            PerspectiveSwitchRequested = false;
        }

        void ProcessTouches()
        {
            MovementInput = Vector2.zero;
            LookInput = Vector2.zero;
            ZoomInput = 0f;

            var touches = ETouch.activeTouches;

            // Pinch is intentionally disabled. The previous implementation flipped
            // isPinching on any two-finger contact, which zeroed Movement and Look
            // — breaking the move-and-look-at-the-same-time pattern this controller
            // is designed for. Nothing in the project currently consumes ZoomInput
            // or PerspectiveSwitchRequested from touch, so neutralizing pinch is a
            // no-op for features and a fix for the move/look bug.

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

            // Convert pending action-zone touches to stick touches if they've
            // dragged far enough or been held past the double-tap window. This
            // is what makes "tap in middle = interact, but slow drag in middle
            // becomes look" work without the player thinking about it.
            ResolvePendingTouch(pendingInteractTouch);
            ResolvePendingTouch(pendingJumpTouch);

            // Drive the move stick.
            if (movementTouch != null)
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

            // Drive the look stick as a virtual analog stick. Reads displacement
            // from the touch's start point (like the move stick), not per-frame
            // delta — so holding the thumb parked off-center keeps the camera
            // turning at a steady rate, matching the mobile FPS convention.
            // Normalizing against a fixed pixel travel distance (not Screen.width)
            // keeps the gesture's physical feel consistent across portrait and
            // landscape orientations.
            if (lookTouch != null)
            {
                Vector2 delta = lookTouch.currentPosition - lookTouch.startPosition;

                if (delta.magnitude > lookDeadZone)
                {
                    delta /= lookMaxTravel;
                    delta = Vector2.ClampMagnitude(delta, 1f);

                    // Y is inverted: dragging up should look up. Screen Y increases
                    // upward, world pitch decreases looking up — the negate stays.
                    LookInput = new Vector2(delta.x, -delta.y) * lookSensitivity * lookSpeedMultiplier;
                }
            }
        }

        /// <summary>
        /// If a pending action-zone touch has dragged past the convert distance
        /// or been held past the double-tap window without lifting, demote it to
        /// a stick touch on whichever side it sits on. Called every frame for
        /// each pending touch.
        /// </summary>
        private void ResolvePendingTouch(TouchData pending)
        {
            if (pending == null) return;

            Vector2 delta = pending.currentPosition - pending.startPosition;
            bool draggedTooFar = delta.magnitude > actionConvertDistance;
            bool heldTooLong = (Time.time - pending.startTime) > doubleTapWindow;

            if (!draggedTooFar && !heldTooLong) return;

            // Convert to a stick touch based on which side the touch is on.
            bool toLeft = pending.currentPosition.x < Screen.width * screenDivider;

            if (toLeft && movementTouch == null)
            {
                pending.role = TouchRole.Move;
                pending.startPosition = pending.currentPosition; // recenter the virtual stick
                movementTouch = pending;
                longPressStartTime = Time.time;
                isLongPressing = true;
            }
            else if (!toLeft && lookTouch == null)
            {
                pending.role = TouchRole.Look;
                lookTouch = pending;
            }
            else
            {
                // The side this touch is on already has a stick assigned. Just
                // mark it unassigned so it stops being a pending candidate; it
                // won't drive anything but will still get cleaned up on lift.
                pending.role = TouchRole.Unassigned;
            }

            if (pending == pendingInteractTouch) pendingInteractTouch = null;
            if (pending == pendingJumpTouch) pendingJumpTouch = null;
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
            bool inInteractZone = IsInZone(position, middleZoneX, middleZoneY);
            bool inJumpZone = IsInZone(position, bottomZoneX, bottomZoneY);

            TouchData data = new TouchData
            {
                touchId = id,
                startPosition = position,
                currentPosition = position,
                frameDelta = Vector2.zero,
                startTime = Time.time,
                isLeftSide = isLeftSide,
                role = TouchRole.Unassigned
            };

            activeTouches[id] = data;

            // Action zones take priority over stick zones for the initial touch.
            // The pending touch gets resolved into a stick later if it doesn't
            // turn out to be a tap.
            if (inInteractZone && pendingInteractTouch == null)
            {
                data.role = TouchRole.PendingInteract;
                pendingInteractTouch = data;
                CheckDoubleTap(position, ref lastInteractTapTime, ref lastInteractTapPosition, isInteract: true);
                return;
            }

            if (inJumpZone && pendingJumpTouch == null)
            {
                data.role = TouchRole.PendingJump;
                pendingJumpTouch = data;
                CheckDoubleTap(position, ref lastJumpTapTime, ref lastJumpTapPosition, isInteract: false);
                return;
            }

            // Outside action zones: assign to the appropriate stick if free.
            if (isLeftSide && movementTouch == null)
            {
                data.role = TouchRole.Move;
                movementTouch = data;
                longPressStartTime = Time.time;
                isLongPressing = true;
            }
            else if (!isLeftSide && lookTouch == null)
            {
                data.role = TouchRole.Look;
                lookTouch = data;
            }
        }

        /// <summary>
        /// Records the tap time/position. If this tap landed within doubleTapWindow
        /// and close enough to the previous tap of the same kind, fire the
        /// corresponding request. Otherwise just store this as the latest tap.
        /// </summary>
        private void CheckDoubleTap(Vector2 position, ref float lastTapTime, ref Vector2 lastTapPosition, bool isInteract)
        {
            float timeSince = Time.time - lastTapTime;
            float distance = Vector2.Distance(position, lastTapPosition);

            if (timeSince < doubleTapWindow && distance < 50f)
            {
                if (isInteract)
                    InteractRequested = true;
                else
                    JumpRequested = true;

                // Consume the first tap so a triple tap doesn't fire twice.
                lastTapTime = 0f;
            }
            else
            {
                lastTapTime = Time.time;
                lastTapPosition = position;
            }
        }

        private bool IsInZone(Vector2 screenPos, Vector2 xRange, Vector2 yRange)
        {
            float nx = screenPos.x / Screen.width;
            float ny = screenPos.y / Screen.height;
            return nx >= xRange.x && nx <= xRange.y && ny >= yRange.x && ny <= yRange.y;
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
                else if (data == pendingInteractTouch)
                {
                    pendingInteractTouch = null;
                }
                else if (data == pendingJumpTouch)
                {
                    pendingJumpTouch = null;
                }

                activeTouches.Remove(touchId);
            }
        }
    }
}