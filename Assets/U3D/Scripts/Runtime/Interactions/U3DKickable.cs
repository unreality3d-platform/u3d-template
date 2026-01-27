using UnityEngine;
using UnityEngine.Events;
using Fusion;
using Fusion.Addons.Physics;
using System.Collections;

namespace U3D
{
    /// <summary>
    /// Kickable interaction component for objects that can be kicked by player feet
    /// Supports both networked and non-networked modes with Unity 6.1+ standards
    /// Integrates with Photon Fusion 2 for multiplayer with Shared Mode topology
    /// Uses camera-based kick direction similar to throwable physics
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    public class U3DKickable : NetworkBehaviour, IU3DInteractable
    {
        [Header("Kick Configuration")]
        [Tooltip("Base kick force multiplier")]
        [SerializeField] private float kickForce = 8f;

        [Tooltip("Additional upward force when kicking")]
        [SerializeField] private float upwardKickBoost = 1.5f;

        [Tooltip("Maximum kick velocity")]
        [SerializeField] private float maxKickVelocity = 15f;

        [Tooltip("Minimum velocity required to trigger kick events")]
        [SerializeField] private float minKickVelocity = 0.8f;

        [Header("Interaction Settings")]
        [Tooltip("Key to trigger kick (remappable)")]
        [SerializeField] private KeyCode kickKey = KeyCode.R;

        [Tooltip("Maximum distance to kick from")]
        [SerializeField] private float maxKickDistance = 1.5f;

        [Tooltip("Ground-level detection radius for foot collision")]
        [SerializeField] private float kickDetectionRadius = 1.2f;

        [Header("Events")]
        [Tooltip("Called when object is kicked")]
        public UnityEvent OnKicked;

        [Tooltip("Called when kicked object hits something")]
        public UnityEvent OnImpact;

        [Tooltip("Called when object goes to sleep after kick")]
        public UnityEvent OnSleep;

        [Tooltip("Called when player enters kick range")]
        public UnityEvent OnEnterKickRange;

        [Tooltip("Called when player exits kick range")]
        public UnityEvent OnExitKickRange;

        [Tooltip("Called when kick attempt fails")]
        public UnityEvent OnKickFailed;

        [Tooltip("Called when object is reset due to world bounds violation")]
        public UnityEvent OnWorldBoundsReset;

        // HIDDEN PHYSICS MANAGEMENT - Optimal defaults
        [HideInInspector]
        [SerializeField] private float sleepCheckDelay = 1.5f;
        [HideInInspector]
        [SerializeField] private float sleepVelocityThreshold = 0.3f;
        [HideInInspector]
        [SerializeField] private float maxActiveTime = 8f;

        // HIDDEN WORLD BOUNDS SAFETY
        [HideInInspector]
        [SerializeField] private float worldBoundsFloor = -50f;
        [HideInInspector]
        [SerializeField] private float worldBoundsRadius = 1000f;
        [HideInInspector]
        [SerializeField] private float boundsCheckInterval = 1f;

        // Network state for physics management
        [Networked] public bool NetworkIsKicked { get; set; }
        [Networked] public bool NetworkIsPhysicsActive { get; set; }
        [Networked] public TickTimer NetworkSleepTimer { get; set; }

        // Components
        private Rigidbody rb;
        private U3DGrabbable grabbable;
        private Camera playerCamera;
        private Transform playerTransform;
        private NetworkObject networkObject;
        private NetworkRigidbody3D networkRigidbody;
        private bool hasNetworkRb3D = false;
        private Collider col;

        // State tracking
        private bool isNetworked = false;
        private bool isInKickRange = false;
        private float lastRangeCheckTime;
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
            Sleeping,      // Kinematic, no gravity - kickable state
            Active,        // Non-kinematic, gravity - physics simulation after kick
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

            if (!isNetworked)
            {
                Debug.Log($"U3DKickable on '{name}' running in non-networked mode");
            }
        }

        public override void Spawned()
        {
            if (!isNetworked) return;

            // Initialize network state
            NetworkIsKicked = false;
            NetworkIsPhysicsActive = false;

            // Initialize physics state after network spawn
            InitializePhysicsState();
        }

        private void Start()
        {
            // Find player components
            FindPlayerComponents();

            // Record original spawn position for reset purposes
            RecordOriginalTransform();

            // Initialize physics state for non-networked objects
            if (!isNetworked)
            {
                InitializePhysicsState();
            }

            // Start world bounds monitoring
            StartBoundsMonitoring();

            // Check for input key conflicts with grabbable
            CheckForInputConflicts();
        }

        private void Update()
        {
            UpdatePlayerProximity();

            // NOTE: Direct input handling removed - U3DInteractionManager handles input
            // via IU3DInteractable.OnInteract() to prevent double-input issues
        }

        /// <summary>
        /// Check if kick key was pressed using Input System (following existing pattern)
        /// </summary>
        private bool WasKickKeyPressed()
        {
            if (UnityEngine.InputSystem.Keyboard.current == null) return false;

            switch (kickKey)
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
                    // Fallback for other keys - can be expanded as needed
                    return false;
            }
        }

        private void InitializePhysicsState()
        {
            // Start in sleeping state (kickable and ready)
            SetPhysicsState(PhysicsState.Sleeping);
            StoreOriginalPhysicsState();
        }

        private void CheckForInputConflicts()
        {
            if (grabbable != null)
            {
                // Auto-remap grabbable to different key if both components present
                var grabbableScript = grabbable as MonoBehaviour;
                var kickableKeyField = this.GetType().GetField("kickKey",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (kickableKeyField != null)
                {
                    // If grabbable uses R key, remap kickable to T
                    if (kickKey == KeyCode.R)
                    {
                        kickKey = KeyCode.T;
                        Debug.Log($"U3DKickable: Auto-remapped kick key to {kickKey} due to grabbable component");
                    }
                }
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!isNetworked || !Object.HasStateAuthority) return;

            // Skip checks if object has been grabbed
            if (grabbable != null && grabbable.IsGrabbed) return;

            // Check for sleep conditions
            if (NetworkIsKicked && NetworkIsPhysicsActive)
            {
                bool shouldSleep = false;

                // Check velocity threshold
                if (rb.linearVelocity.magnitude < sleepVelocityThreshold &&
                    rb.angularVelocity.magnitude < sleepVelocityThreshold)
                {
                    shouldSleep = true;
                }

                // Check timeout
                if (NetworkSleepTimer.Expired(Runner))
                {
                    shouldSleep = true;
                }

                if (shouldSleep)
                {
                    ReturnToKickableSleepState();
                }
            }
        }

        public override void Render()
        {
            if (!isNetworked) return;

            // Sync physics state changes from network
            PhysicsState networkState = NetworkIsPhysicsActive ? PhysicsState.Active : PhysicsState.Sleeping;

            if (networkState != lastNetworkPhysicsState)
            {
                if (!Object.HasStateAuthority)
                {
                    // Apply network state to local physics (for non-authority clients)
                    ApplyPhysicsStateFromNetwork(networkState);
                }
                lastNetworkPhysicsState = networkState;
            }
        }

        public void OnStateAuthorityChanged()
        {
            if (!isNetworked) return;

            // When authority changes, sync physics state with network
            if (Object.HasStateAuthority)
            {
                // We gained authority - apply current local state to network
                SyncNetworkPhysicsState();
            }
            else
            {
                // We lost authority - sync local state with network
                SyncLocalPhysicsState();
            }
        }

        private void SetPhysicsState(PhysicsState newState)
        {
            currentPhysicsState = newState;
            ApplyPhysicsState(newState);

            // Update network state if we have authority
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
                    // Clear velocities if not kinematic to avoid Unity warnings
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
                    // In networked mode, let NetworkRigidbody3D manage kinematic state
                    break;

                case PhysicsState.Active:
                    if (!isNetworked)
                    {
                        rb.isKinematic = false;
                        rb.useGravity = true;
                    }
                    // In networked mode, NetworkRigidbody3D handles physics state automatically
                    break;
            }
        }

        private void ApplyPhysicsStateFromNetwork(PhysicsState networkState)
        {
            // Apply physics state received from network (non-authority clients)
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
                // For kickable objects, desired state is physics-ready when kicked
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

        private void FindPlayerComponents()
        {
            U3DPlayerController playerController = FindAnyObjectByType<U3DPlayerController>();
            if (playerController != null)
            {
                playerTransform = playerController.transform;
                playerCamera = playerController.GetComponentInChildren<Camera>();
            }

            if (playerCamera == null)
            {
                playerCamera = Camera.main;
            }
        }

        private bool CanAttemptKick()
        {
            // Check if object is grabbed (kickable while grabbed is not allowed)
            if (grabbable != null && grabbable.IsGrabbed)
            {
                return false;
            }

            // Check if we're in range
            if (!isInKickRange)
            {
                return false;
            }

            // Check if already in physics simulation
            if (currentPhysicsState == PhysicsState.Active)
            {
                return false;
            }

            // Check networking authority
            if (isNetworked && !Object.HasStateAuthority)
            {
                return false;
            }

            return true;
        }

        public void Kick()
        {
            if (!CanAttemptKick()) return;

            // Ensure we have player references
            if (playerCamera == null || playerTransform == null)
            {
                FindPlayerComponents();
                if (playerCamera == null)
                {
                    Debug.LogWarning("U3DKickable: No player camera found - cannot determine kick direction");
                    OnKickFailed?.Invoke();
                    return;
                }
            }

            // Authority check for networked objects
            if (isNetworked && !Object.HasStateAuthority) return;

            // Activate physics before applying velocity
            SetPhysicsState(PhysicsState.Active);

            // Apply the kick on the next frame after physics state changes
            StartCoroutine(ApplyKickVelocityAfterPhysicsActivation());
        }

        private IEnumerator ApplyKickVelocityAfterPhysicsActivation()
        {
            // Wait one frame to ensure physics state changes take effect
            yield return null;

            // Build kick vector from camera forward + upward boost
            float useForce = kickForce;
            Vector3 kickDirection = playerCamera.transform.forward;
            kickDirection.y += upwardKickBoost / Mathf.Max(0.01f, useForce);
            kickDirection.Normalize();

            Vector3 kickVelocity = kickDirection * useForce;
            if (kickVelocity.magnitude > maxKickVelocity)
                kickVelocity = kickVelocity.normalized * maxKickVelocity;

            // Ensure rigidbody is ready for physics
            const int maxTries = 3;
            int tries = 0;

            while (rb != null && rb.isKinematic && tries < maxTries)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                tries++;
                yield return null;
            }

            if (rb != null && !rb.isKinematic)
            {
                rb.linearVelocity = kickVelocity;
            }
            else
            {
                Debug.LogWarning("U3DKickable: Could not apply kick velocity (Rigidbody still kinematic or null).");
                SetPhysicsState(PhysicsState.Sleeping);
                OnKickFailed?.Invoke();
                yield break;
            }

            // Mark as kicked on the network if it's a meaningful kick
            if (kickVelocity.magnitude >= minKickVelocity)
            {
                if (isNetworked && Object.HasStateAuthority)
                {
                    NetworkIsKicked = true;
                    NetworkSleepTimer = TickTimer.CreateFromSeconds(Runner, maxActiveTime);
                }
                OnKicked?.Invoke();
            }
            else
            {
                SetPhysicsState(PhysicsState.Sleeping);
            }
        }

        private void ReturnToKickableSleepState()
        {
            SetPhysicsState(PhysicsState.Sleeping);

            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkIsKicked = false;
                NetworkIsPhysicsActive = false;
            }

            OnSleep?.Invoke();
        }

        private void UpdatePlayerProximity()
        {
            if (playerTransform == null)
            {
                FindPlayer();
                return;
            }

            // Ground-level kick detection
            Vector3 playerGroundPosition = new Vector3(playerTransform.position.x, transform.position.y, playerTransform.position.z);
            float distanceToPlayer = Vector3.Distance(transform.position, playerGroundPosition);

            bool wasInRange = isInKickRange;
            isInKickRange = distanceToPlayer <= maxKickDistance;

            if (isInKickRange && !wasInRange)
            {
                OnEnterKickRange?.Invoke();
            }
            else if (!isInKickRange && wasInRange)
            {
                OnExitKickRange?.Invoke();
            }
        }

        private void FindPlayer()
        {
            U3DPlayerController playerController = FindAnyObjectByType<U3DPlayerController>();
            if (playerController != null)
            {
                playerTransform = playerController.transform;
                playerCamera = playerController.GetComponentInChildren<Camera>();
            }
            else
            {
                playerTransform = null;
                playerCamera = null;
            }
        }

        private IEnumerator MonitorWorldBounds()
        {
            while (true)
            {
                yield return new WaitForSeconds(boundsCheckInterval);

                // Skip bounds check if object is currently being grabbed
                if (grabbable != null && grabbable.IsGrabbed)
                {
                    continue;
                }

                // Only check bounds on authority (or non-networked)
                if (isNetworked && (Object == null || !Object.HasStateAuthority))
                {
                    continue;
                }

                // Check if object has fallen through world or gone too far
                bool needsReset = false;

                if (transform.position.y < worldBoundsFloor)
                {
                    Debug.LogWarning($"U3DKickable: Object '{name}' fell below world bounds (Y: {transform.position.y})");
                    needsReset = true;
                }
                else if (Vector3.Distance(Vector3.zero, transform.position) > worldBoundsRadius)
                {
                    Debug.LogWarning($"U3DKickable: Object '{name}' went beyond world radius ({Vector3.Distance(Vector3.zero, transform.position):F1}m)");
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
            // Authority check for networked objects
            if (isNetworked && !Object.HasStateAuthority) return;

            // Set to resetting state to prevent physics interference
            SetPhysicsState(PhysicsState.Resetting);

            // Reset position and rotation to spawn point
            if (hasNetworkRb3D && networkRigidbody != null)
            {
                // For networked objects, use Teleport() to properly update Fusion's state
                networkRigidbody.Teleport(originalPosition, originalRotation);
            }
            else
            {
                // Non-networked: direct transform manipulation
                transform.position = originalPosition;
                transform.rotation = originalRotation;
            }

            // Return to kickable sleep state
            SetPhysicsState(PhysicsState.Sleeping);

            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkIsKicked = false;
                NetworkIsPhysicsActive = false;
            }

            OnWorldBoundsReset?.Invoke();
        }

        private void OnCollisionEnter(Collision collision)
        {
            // Fire impact event if this was kicked and hits with sufficient force
            bool wasKicked = isNetworked ? NetworkIsKicked : (currentPhysicsState == PhysicsState.Active);

            if (wasKicked && collision.relativeVelocity.magnitude > 1.5f)
            {
                OnImpact?.Invoke();
            }
        }

        // Public method to manually kick with specific direction and force
        public void KickInDirection(Vector3 direction, float force)
        {
            // Authority check for networked objects
            if (isNetworked && !Object.HasStateAuthority) return;

            // Don't kick if grabbed
            if (grabbable != null && grabbable.IsGrabbed) return;

            // Activate physics
            SetPhysicsState(PhysicsState.Active);

            // Apply kick force
            Vector3 kickVelocity = direction.normalized * force;

            // Clamp to max velocity
            if (kickVelocity.magnitude > maxKickVelocity)
            {
                kickVelocity = kickVelocity.normalized * maxKickVelocity;
            }

            rb.linearVelocity = kickVelocity;

            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkIsKicked = true;
                NetworkSleepTimer = TickTimer.CreateFromSeconds(Runner, maxActiveTime);
            }

            OnKicked?.Invoke();
        }

        // Public method to kick in camera direction with custom force
        public void KickInCameraDirection(float customForce = -1f)
        {
            if (playerCamera == null)
            {
                FindPlayerComponents();
                if (playerCamera == null)
                {
                    Debug.LogWarning("U3DKickable: No camera found for KickInCameraDirection");
                    return;
                }
            }

            float useForce = customForce > 0f ? customForce : kickForce;
            Vector3 kickDirection = playerCamera.transform.forward;
            kickDirection.y += upwardKickBoost / useForce;
            kickDirection.Normalize();

            KickInDirection(kickDirection, useForce);
        }

        // Public method to manually put object to sleep
        public void PutToSleep()
        {
            ReturnToKickableSleepState();
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
            if (CanAttemptKick())
            {
                Kick();
            }
            else
            {
                OnKickFailed?.Invoke();
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
            return CanAttemptKick();
        }

        public int GetInteractionPriority()
        {
            return 30; // Lower priority than grabbable but higher than triggers
        }

        public string GetInteractionPrompt()
        {
            if (grabbable != null && grabbable.IsGrabbed)
            {
                return "Cannot kick while grabbed";
            }
            return $"Kick ({kickKey})";
        }

        // Public properties for inspection
        public bool HasBeenKicked => isNetworked ? NetworkIsKicked : (currentPhysicsState == PhysicsState.Active);
        public bool IsInKickRange => isInKickRange;
        public bool IsNetworked => isNetworked;
        public PhysicsState CurrentPhysicsState => currentPhysicsState;
        public Vector3 OriginalPosition => originalPosition;
        public Quaternion OriginalRotation => originalRotation;
        public bool HasNetworkRigidbody => networkRigidbody != null;
        public bool IsPhysicsActive => isNetworked ? NetworkIsPhysicsActive : (currentPhysicsState == PhysicsState.Active);
        public KeyCode KickKey { get => kickKey; set => kickKey = value; }

        private void OnDestroy()
        {
            // Stop any running coroutines
            if (boundsCheckCoroutine != null)
            {
                StopCoroutine(boundsCheckCoroutine);
            }
        }

        // Editor helper to validate setup
        private void OnValidate()
        {
            if (kickForce <= 0f)
            {
                Debug.LogWarning("U3DKickable: Kick force should be greater than 0");
            }

            if (maxKickVelocity < kickForce)
            {
                Debug.LogWarning("U3DKickable: Max kick velocity is less than kick force - kicks will be clamped");
            }

            if (sleepVelocityThreshold < 0f)
            {
                Debug.LogWarning("U3DKickable: Sleep velocity threshold should be positive");
            }

            if (maxKickDistance <= 0f)
            {
                Debug.LogWarning("U3DKickable: Max kick distance should be positive");
            }

            if (kickDetectionRadius <= 0f)
            {
                Debug.LogWarning("U3DKickable: Kick detection radius should be positive");
            }
        }
    }
}