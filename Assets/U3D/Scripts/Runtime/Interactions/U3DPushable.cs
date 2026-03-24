using UnityEngine;
using UnityEngine.Events;
using Fusion;
using Fusion.Addons.Physics;
using System.Collections;

namespace U3D
{
    /// <summary>
    /// Pushable interaction component for objects that can be pushed by the player.
    /// Toggle-based: press interaction key to start pushing, press again or walk out of range to stop.
    /// Direction follows camera forward (horizontal, normalized) — consistent with Kickable/Throwable.
    /// Speed derives from the player's actual movement velocity — no artificial push speed setting.
    /// Push Resistance adjusts the Rigidbody's mass directly for creator-friendly tuning.
    /// Supports both networked and non-networked modes with Photon Fusion 2 Shared Mode.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    public class U3DPushable : NetworkBehaviour, IU3DInteractable
    {
        [Header("Push Resistance")]
        [Tooltip("Higher values make this object harder to push. Adjusts this object's Rigidbody mass.")]
        [SerializeField] private float pushResistance = 5f;

        [Header("Interaction Settings")]
        [Tooltip("Key to toggle push mode (remappable)")]
        [SerializeField] private KeyCode pushKey = KeyCode.R;

        [Tooltip("Maximum distance to push from. Player walking beyond this auto-disengages push mode.")]
        [SerializeField] private float maxPushDistance = 2f;

        [Header("Starting State")]
        [Tooltip("When enabled, object spawns with gravity active and falls to the ground before becoming pushable. Use this for objects spawned above ground level.")]
        [SerializeField] private bool startActive = false;

        [Header("Optional Label")]
        [Tooltip("Assign a U3DBillboardUI in your scene to show a label near this object. Edit the text on that object directly.")]
        public U3DBillboardUI labelUI;

        [Header("Events")]
        [Tooltip("Called when player begins pushing this object")]
        public UnityEvent OnPushStart;

        [Tooltip("Called when player stops pushing this object")]
        public UnityEvent OnPushEnd;

        [Tooltip("Called when pushed object hits something with force")]
        public UnityEvent OnImpact;

        [Tooltip("Called when object returns to sleep after being pushed")]
        public UnityEvent OnSleep;

        [Tooltip("Called when object is reset due to world bounds violation")]
        public UnityEvent OnWorldBoundsReset;

        // HIDDEN PHYSICS MANAGEMENT - Optimal defaults
        [HideInInspector]
        [SerializeField] private float sleepCheckDelay = 1.5f;
        [HideInInspector]
        [SerializeField] private float sleepVelocityThreshold = 0.3f;
        [HideInInspector]
        [SerializeField] private float maxActiveTime = 15f;

        // HIDDEN WORLD BOUNDS SAFETY
        [HideInInspector]
        [SerializeField] private float worldBoundsFloor = -50f;
        [HideInInspector]
        [SerializeField] private float worldBoundsRadius = 1000f;
        [HideInInspector]
        [SerializeField] private float boundsCheckInterval = 1f;

        // Network state for physics management
        [Networked] public bool NetworkIsPushing { get; set; }
        [Networked] public bool NetworkIsPhysicsActive { get; set; }
        [Networked] public TickTimer NetworkSleepTimer { get; set; }

        // Components
        private Rigidbody rb;
        private U3DGrabbable grabbable;
        private Camera playerCamera;
        private Transform playerTransform;
        private U3DPlayerController playerController;
        private NetworkObject networkObject;
        private NetworkRigidbody3D networkRigidbody;
        private bool hasNetworkRb3D = false;
        private Collider col;

        // State tracking
        private bool isNetworked = false;
        private bool isInPushRange = false;
        private bool isPushActive = false;
        private Coroutine boundsCheckCoroutine;

        // Physics state management
        private PhysicsState currentPhysicsState = PhysicsState.Sleeping;
        private PhysicsState lastNetworkPhysicsState = PhysicsState.Sleeping;

        // Original position and rotation for reset purposes
        private Vector3 originalPosition;
        private Quaternion originalRotation;
        private bool hasRecordedOriginalTransform = false;

        // Physics state storage
        private bool originalWasKinematic;
        private bool originalUsedGravity;
        private bool hasStoredOriginalPhysicsState = false;

        public enum PhysicsState
        {
            Sleeping,      // Kinematic, no gravity - pushable state
            Active,        // Non-kinematic, gravity - physics simulation during/after push
            Resetting      // Temporarily kinematic while resetting position
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            grabbable = GetComponent<U3DGrabbable>();
            networkRigidbody = GetComponent<NetworkRigidbody3D>();
            hasNetworkRb3D = networkRigidbody != null;
            col = GetComponent<Collider>();
            networkObject = GetComponent<NetworkObject>();
            isNetworked = networkObject != null;
        }

        public override void Spawned()
        {
            if (!isNetworked) return;

            NetworkIsPushing = false;
            NetworkIsPhysicsActive = false;

            InitializePhysicsState();
        }

        private void Start()
        {
            FindPlayerComponents();
            RecordOriginalTransform();
            ApplyPushResistanceToMass();

            if (!isNetworked)
            {
                InitializePhysicsState();
            }

            StartBoundsMonitoring();
            CheckForInputConflicts();
        }

        private void Update()
        {
            UpdatePlayerProximity();

            if (isPushActive)
            {
                // Auto-disengage if player walks out of range
                if (!isInPushRange)
                {
                    EndPush();
                }
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!isNetworked || !Object.HasStateAuthority) return;

            // Skip checks if object has been grabbed
            if (grabbable != null && grabbable.IsGrabbed) return;

            if (isPushActive && NetworkIsPushing)
            {
                ApplyPushVelocity();
            }

            // Check for sleep conditions when physics is active but not being pushed
            if (!NetworkIsPushing && NetworkIsPhysicsActive)
            {
                bool shouldSleep = false;

                if (rb.linearVelocity.magnitude < sleepVelocityThreshold &&
                    rb.angularVelocity.magnitude < sleepVelocityThreshold)
                {
                    shouldSleep = true;
                }

                if (NetworkSleepTimer.Expired(Runner))
                {
                    shouldSleep = true;
                }

                if (shouldSleep)
                {
                    ReturnToPushableSleepState();
                }
            }
        }

        public override void Render()
        {
            if (!isNetworked) return;

            PhysicsState networkState = NetworkIsPhysicsActive ?
                PhysicsState.Active : PhysicsState.Sleeping;

            if (networkState != lastNetworkPhysicsState)
            {
                if (!Object.HasStateAuthority)
                {
                    ApplyPhysicsStateFromNetwork(networkState);
                }
                lastNetworkPhysicsState = networkState;
            }
        }

        public void OnStateAuthorityChanged()
        {
            if (!isNetworked) return;

            if (Object.HasStateAuthority)
            {
                SyncNetworkPhysicsState();
            }
            else
            {
                // Lost authority while pushing — disengage locally
                if (isPushActive)
                {
                    isPushActive = false;
                    if (labelUI != null) labelUI.gameObject.SetActive(true);
                    OnPushEnd?.Invoke();
                }
                SyncLocalPhysicsState();
            }
        }

        /// <summary>
        /// Apply push velocity each tick while push mode is active.
        /// Direction: camera forward projected horizontal, normalized (consistent with Kickable/Throwable).
        /// Magnitude: player's current movement speed from GetCurrentSpeed() (walk/sprint/crouch).
        /// PlayerController.Velocity only tracks gravity — use NetworkIsMoving + CurrentSpeed instead.
        /// </summary>
        private void ApplyPushVelocity()
        {
            if (playerCamera == null || playerController == null)
            {
                FindPlayerComponents();
                if (playerCamera == null || playerController == null) return;
            }

            // Player standing still = no force applied
            if (!playerController.NetworkIsMoving) return;

            // Use the intended movement speed (walk 4, sprint 8, crouch 2)
            float playerSpeed = playerController.CurrentSpeed;
            if (playerSpeed < 0.1f) return;

            // Camera forward projected onto horizontal plane (same as Kickable/Throwable)
            Vector3 pushDirection = playerCamera.transform.forward;
            pushDirection.y = 0f;
            pushDirection.Normalize();

            if (pushDirection.sqrMagnitude < 0.01f) return;

            // In networked mode, NetworkRigidbody3D manages kinematic state but may not
            // have flipped it yet after our physics state change. Force it non-kinematic
            // so velocity assignment works. Safe because we only reach here with authority.
            if (rb.isKinematic)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
            }

            // Apply velocity: camera direction * player movement speed
            // Rigidbody mass (set by Push Resistance) naturally resists this
            Vector3 pushVelocity = pushDirection * playerSpeed;

            // Preserve existing Y velocity (gravity, falling off edges)
            rb.linearVelocity = new Vector3(pushVelocity.x, rb.linearVelocity.y, pushVelocity.z);
        }

        private void StartPush()
        {
            if (grabbable != null && grabbable.IsGrabbed) return;
            if (!isInPushRange) return;

            // Request authority for networked objects
            if (isNetworked)
            {
                if (!Object.HasStateAuthority)
                {
                    Object.RequestStateAuthority();
                }
            }

            isPushActive = true;
            SetPhysicsState(PhysicsState.Active);

            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkIsPushing = true;
                NetworkSleepTimer = TickTimer.CreateFromSeconds(Runner, maxActiveTime);
            }

            if (labelUI != null) labelUI.gameObject.SetActive(false);
            OnPushStart?.Invoke();
        }

        private void EndPush()
        {
            if (!isPushActive) return;

            isPushActive = false;

            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkIsPushing = false;
                NetworkSleepTimer = TickTimer.CreateFromSeconds(Runner, maxActiveTime);
            }

            if (labelUI != null) labelUI.gameObject.SetActive(true);
            OnPushEnd?.Invoke();

            // Object remains in Active physics state — damping decelerates it,
            // sleep detection in FixedUpdateNetwork will return it to Sleeping
        }

        private void ReturnToPushableSleepState()
        {
            SetPhysicsState(PhysicsState.Sleeping);

            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkIsPushing = false;
                NetworkIsPhysicsActive = false;
            }

            if (labelUI != null) labelUI.gameObject.SetActive(true);
            OnSleep?.Invoke();
        }

        private void UpdatePlayerProximity()
        {
            if (playerTransform == null)
            {
                FindPlayer();
                return;
            }

            float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);
            isInPushRange = distanceToPlayer <= maxPushDistance;
        }

        private void FindPlayer()
        {
            U3DPlayerController controller = FindAnyObjectByType<U3DPlayerController>();
            if (controller != null)
            {
                playerTransform = controller.transform;
                playerController = controller;
                playerCamera = controller.GetComponentInChildren<Camera>();
            }
            else
            {
                playerTransform = null;
                playerController = null;
                playerCamera = null;
            }
        }

        private void FindPlayerComponents()
        {
            U3DPlayerController controller = FindAnyObjectByType<U3DPlayerController>();
            if (controller != null)
            {
                playerTransform = controller.transform;
                playerController = controller;
                playerCamera = controller.GetComponentInChildren<Camera>();
            }

            if (playerCamera == null)
            {
                playerCamera = Camera.main;
            }
        }

        private void InitializePhysicsState()
        {
            if (startActive)
            {
                SetPhysicsState(PhysicsState.Active);
                if (isNetworked && Object.HasStateAuthority)
                {
                    NetworkIsPhysicsActive = true;
                    NetworkSleepTimer = TickTimer.CreateFromSeconds(Runner, maxActiveTime);
                }
            }
            else
            {
                SetPhysicsState(PhysicsState.Sleeping);
            }
            StoreOriginalPhysicsState();
        }

        private void CheckForInputConflicts()
        {
            if (grabbable != null)
            {
                if (pushKey == KeyCode.R)
                {
                    pushKey = KeyCode.T;
                }
            }
        }

        private void SetPhysicsState(PhysicsState newState)
        {
            currentPhysicsState = newState;
            ApplyPhysicsState(newState);

            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkIsPhysicsActive = (newState == PhysicsState.Active);
            }
        }

        private void ApplyPhysicsState(PhysicsState state)
        {
            if (rb == null) return;

            switch (state)
            {
                case PhysicsState.Sleeping:
                case PhysicsState.Resetting:
                    if (!rb.isKinematic)
                    {
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }

                    if (!isNetworked)
                    {
                        rb.useGravity = false;
                        rb.isKinematic = true;
                    }
                    break;

                case PhysicsState.Active:
                    if (!isNetworked)
                    {
                        rb.isKinematic = false;
                        rb.useGravity = true;
                    }
                    break;
            }
        }

        private void ApplyPhysicsStateFromNetwork(PhysicsState networkState)
        {
            currentPhysicsState = networkState;
            ApplyPhysicsState(networkState);
        }

        private void SyncNetworkPhysicsState()
        {
            if (!isNetworked || !Object.HasStateAuthority) return;
            NetworkIsPhysicsActive = (currentPhysicsState == PhysicsState.Active);
        }

        private void SyncLocalPhysicsState()
        {
            if (!isNetworked) return;
            PhysicsState networkState = NetworkIsPhysicsActive ? PhysicsState.Active : PhysicsState.Sleeping;
            ApplyPhysicsStateFromNetwork(networkState);
        }

        private void RecordOriginalTransform()
        {
            if (!hasRecordedOriginalTransform)
            {
                originalPosition = transform.position;
                originalRotation = transform.rotation;
                hasRecordedOriginalTransform = true;
            }
        }

        private void StoreOriginalPhysicsState()
        {
            if (rb != null && !hasStoredOriginalPhysicsState)
            {
                originalWasKinematic = false;
                originalUsedGravity = true;
                hasStoredOriginalPhysicsState = true;
            }
        }

        private void StartBoundsMonitoring()
        {
            if (boundsCheckCoroutine == null)
            {
                boundsCheckCoroutine = StartCoroutine(MonitorWorldBounds());
            }
        }

        private IEnumerator MonitorWorldBounds()
        {
            while (true)
            {
                yield return new WaitForSeconds(boundsCheckInterval);

                if (grabbable != null && grabbable.IsGrabbed)
                {
                    continue;
                }

                if (isNetworked && (Object == null || !Object.HasStateAuthority))
                {
                    continue;
                }

                bool needsReset = false;

                if (transform.position.y < worldBoundsFloor)
                {
                    Debug.LogWarning($"U3DPushable: Object '{name}' fell below world bounds (Y: {transform.position.y})");
                    needsReset = true;
                }
                else if (Vector3.Distance(Vector3.zero, transform.position) > worldBoundsRadius)
                {
                    Debug.LogWarning($"U3DPushable: Object '{name}' went beyond world radius ({Vector3.Distance(Vector3.zero, transform.position):F1}m)");
                    needsReset = true;
                }

                if (needsReset)
                {
                    ResetToSpawnPosition();
                }
            }
        }

        private void ResetToSpawnPosition()
        {
            if (isNetworked && !Object.HasStateAuthority) return;

            // End push if active
            if (isPushActive)
            {
                EndPush();
            }

            SetPhysicsState(PhysicsState.Resetting);

            if (hasNetworkRb3D && networkRigidbody != null)
            {
                networkRigidbody.Teleport(originalPosition, originalRotation);
            }
            else
            {
                transform.position = originalPosition;
                transform.rotation = originalRotation;
            }

            SetPhysicsState(PhysicsState.Sleeping);

            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkIsPushing = false;
                NetworkIsPhysicsActive = false;
            }

            if (labelUI != null) labelUI.gameObject.SetActive(true);
            OnWorldBoundsReset?.Invoke();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (isNetworked && Object == null) return;

            bool wasActive = isNetworked ?
                NetworkIsPhysicsActive : (currentPhysicsState == PhysicsState.Active);

            if (wasActive && collision.relativeVelocity.magnitude > 1.5f)
            {
                OnImpact?.Invoke();
            }
        }

        /// <summary>
        /// Apply Push Resistance value to Rigidbody mass.
        /// Called on Start and whenever the value changes in the Inspector.
        /// </summary>
        private void ApplyPushResistanceToMass()
        {
            if (rb != null)
            {
                rb.mass = pushResistance;
            }
        }

        /// <summary>
        /// Check if push key was pressed using Input System (following existing pattern)
        /// </summary>
        private bool WasPushKeyPressed()
        {
            if (UnityEngine.InputSystem.Keyboard.current == null) return false;

            switch (pushKey)
            {
                case KeyCode.E:
                    return UnityEngine.InputSystem.Keyboard.current.eKey.wasPressedThisFrame;
                case KeyCode.F:
                    return UnityEngine.InputSystem.Keyboard.current.fKey.wasPressedThisFrame;
                case KeyCode.R:
                    return UnityEngine.InputSystem.Keyboard.current.rKey.wasPressedThisFrame;
                case KeyCode.T:
                    return UnityEngine.InputSystem.Keyboard.current.tKey.wasPressedThisFrame;
                case KeyCode.G:
                    return UnityEngine.InputSystem.Keyboard.current.gKey.wasPressedThisFrame;
                case KeyCode.Q:
                    return UnityEngine.InputSystem.Keyboard.current.qKey.wasPressedThisFrame;
                case KeyCode.X:
                    return UnityEngine.InputSystem.Keyboard.current.xKey.wasPressedThisFrame;
                case KeyCode.Z:
                    return UnityEngine.InputSystem.Keyboard.current.zKey.wasPressedThisFrame;
                case KeyCode.V:
                    return UnityEngine.InputSystem.Keyboard.current.vKey.wasPressedThisFrame;
                case KeyCode.B:
                    return UnityEngine.InputSystem.Keyboard.current.bKey.wasPressedThisFrame;
                case KeyCode.C:
                    return UnityEngine.InputSystem.Keyboard.current.cKey.wasPressedThisFrame;
                case KeyCode.Space:
                    return UnityEngine.InputSystem.Keyboard.current.spaceKey.wasPressedThisFrame;
                case KeyCode.LeftShift:
                    return UnityEngine.InputSystem.Keyboard.current.leftShiftKey.wasPressedThisFrame;
                case KeyCode.Tab:
                    return UnityEngine.InputSystem.Keyboard.current.tabKey.wasPressedThisFrame;
                case KeyCode.Alpha1:
                    return UnityEngine.InputSystem.Keyboard.current.digit1Key.wasPressedThisFrame;
                case KeyCode.Alpha2:
                    return UnityEngine.InputSystem.Keyboard.current.digit2Key.wasPressedThisFrame;
                case KeyCode.Alpha3:
                    return UnityEngine.InputSystem.Keyboard.current.digit3Key.wasPressedThisFrame;
                case KeyCode.Alpha4:
                    return UnityEngine.InputSystem.Keyboard.current.digit4Key.wasPressedThisFrame;
                case KeyCode.Alpha5:
                    return UnityEngine.InputSystem.Keyboard.current.digit5Key.wasPressedThisFrame;
                default:
                    return false;
            }
        }

        // Public method to manually end push
        public void StopPush()
        {
            if (isPushActive)
            {
                EndPush();
            }
        }

        // Public method to manually put object to sleep
        public void PutToSleep()
        {
            if (isPushActive)
            {
                EndPush();
            }
            ReturnToPushableSleepState();
        }

        // Public method to update spawn position (useful for dynamic spawn points)
        public void UpdateSpawnPosition(Vector3 newPosition, Quaternion newRotation)
        {
            originalPosition = newPosition;
            originalRotation = newRotation;
        }

        // IU3DInteractable implementation
        public void OnInteract()
        {
            if (isPushActive)
            {
                EndPush();
            }
            else if (CanStartPush())
            {
                StartPush();
            }
        }

        public void OnPlayerEnterRange()
        {
            // Handled by UpdatePlayerProximity
        }

        public void OnPlayerExitRange()
        {
            // Handled by UpdatePlayerProximity
        }

        public bool CanInteract()
        {
            // Can always interact if currently pushing (to toggle off)
            if (isPushActive) return true;
            return CanStartPush();
        }

        public int GetInteractionPriority()
        {
            return 30;
        }

        public string GetInteractionPrompt()
        {
            if (grabbable != null && grabbable.IsGrabbed)
            {
                return "Cannot push while grabbed";
            }
            if (isPushActive)
            {
                return $"Stop Pushing ({pushKey})";
            }
            return $"Push ({pushKey})";
        }

        private bool CanStartPush()
        {
            if (grabbable != null && grabbable.IsGrabbed)
            {
                return false;
            }

            if (!isInPushRange)
            {
                return false;
            }

            if (isNetworked)
            {
                if (Object == null) return false;

                if (!Object.HasStateAuthority)
                {
                    // Allow — we'll request authority in StartPush
                    return true;
                }
            }

            return true;
        }

        // Public properties
        public bool IsPushActive => isPushActive;
        public bool IsInPushRange => isInPushRange;
        public bool IsNetworked => isNetworked;
        public PhysicsState CurrentPhysicsState => currentPhysicsState;
        public Vector3 OriginalPosition => originalPosition;
        public Quaternion OriginalRotation => originalRotation;
        public bool HasNetworkRigidbody => networkRigidbody != null;
        public bool IsPhysicsActive => isNetworked ? NetworkIsPhysicsActive : (currentPhysicsState == PhysicsState.Active);
        public KeyCode PushKey { get => pushKey; set => pushKey = value; }

        private void OnDestroy()
        {
            if (boundsCheckCoroutine != null)
            {
                StopCoroutine(boundsCheckCoroutine);
            }
        }

        private void OnValidate()
        {
            if (pushResistance <= 0f)
            {
                Debug.LogWarning("U3DPushable: Push Resistance should be greater than 0");
            }

            if (maxPushDistance <= 0f)
            {
                Debug.LogWarning("U3DPushable: Max push distance should be positive");
            }

            ApplyPushResistanceToMass();
        }
    }
}