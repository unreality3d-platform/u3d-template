using UnityEngine;
using UnityEngine.Events;
using Fusion;
using Fusion.Addons.Physics;
using System.Collections;

namespace U3D
{
    /// <summary>
    /// FIXED: Proper physics state management that works with NetworkRigidbody3D
    /// Eliminates conflicts with Fusion 2's automatic interpolation system
    /// Handles authority-based physics control for Shared Mode
    /// Enhanced with remappable interaction keys using Unity Input System
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class U3DThrowable : NetworkBehaviour
    {
        [Header("Throw Configuration")]
        [Tooltip("Base throw force multiplier")]
        [SerializeField] private float throwForce = 10f;

        [Tooltip("Additional upward force when throwing")]
        [SerializeField] private float upwardThrowBoost = 2f;

        [Tooltip("Maximum throw velocity")]
        [SerializeField] private float maxThrowVelocity = 20f;

        [Tooltip("Minimum velocity required to trigger throw events")]
        [SerializeField] private float minThrowVelocity = 1f;

        [Header("Interaction Settings")]
        [Tooltip("Key to trigger throw (when not grabbed - remappable)")]
        [SerializeField] private KeyCode throwKey = KeyCode.T;

        [Header("Events")]
        [Tooltip("Called when object is thrown")]
        public UnityEvent OnThrown;

        [Tooltip("Called when thrown object hits something")]
        public UnityEvent OnImpact;

        [Tooltip("Called when object goes to sleep")]
        public UnityEvent OnSleep;

        [Tooltip("Called when object is reset due to world bounds violation")]
        public UnityEvent OnWorldBoundsReset;

        // HIDDEN PHYSICS MANAGEMENT - Optimal defaults
        [HideInInspector]
        [SerializeField] private float sleepCheckDelay = 2f;
        [HideInInspector]
        [SerializeField] private float sleepVelocityThreshold = 0.5f;
        [HideInInspector]
        [SerializeField] private float maxActiveTime = 10f;

        // HIDDEN WORLD BOUNDS SAFETY
        [HideInInspector]
        [SerializeField] private float worldBoundsFloor = -50f;
        [HideInInspector]
        [SerializeField] private float worldBoundsRadius = 1000f;
        [HideInInspector]
        [SerializeField] private float boundsCheckInterval = 1f;

        // Network state for physics management
        [Networked] public bool NetworkIsThrown { get; set; }
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
        private U3DPlayerController playerController;

        // State tracking
        private bool isNetworked = false;
        private Coroutine boundsCheckCoroutine;

        // Physics state management - FIXED
        private PhysicsState currentPhysicsState = PhysicsState.Sleeping;
        private PhysicsState lastNetworkPhysicsState = PhysicsState.Sleeping;

        // Original position and rotation for reset purposes
        private Vector3 originalPosition;
        private Quaternion originalRotation;
        private bool hasRecordedOriginalTransform = false;

        public enum PhysicsState
        {
            Sleeping,      // Kinematic, no gravity - grabbable state
            Grabbed,       // Kinematic, no gravity - held in hand
            Active,        // Non-kinematic, gravity - physics simulation
            Resetting      // Temporarily kinematic while resetting position
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            grabbable = GetComponent<U3DGrabbable>();
            networkRigidbody = GetComponent<NetworkRigidbody3D>();
            hasNetworkRb3D = networkRigidbody != null;

            networkObject = GetComponent<NetworkObject>();
            isNetworked = networkObject != null;

            if (grabbable == null)
            {
                Debug.LogError("U3DThrowable requires U3DGrabbable component!");
                enabled = false;
                return;
            }

            if (!isNetworked)
            {
                Debug.Log($"U3DThrowable on '{name}' running in non-networked mode");
            }
        }

        public override void Spawned()
        {
            if (!isNetworked) return;

            // Initialize network state
            NetworkIsThrown = false;
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

            // FIXED: Subscribe to grabbable events AFTER all components are initialized
            if (grabbable != null)
            {
                grabbable.OnReleased.AddListener(OnObjectReleased);
                grabbable.OnGrabbed.AddListener(OnObjectGrabbed);
            }

            // Check for input conflicts
            CheckForInputConflicts();
        }

        private void Update()
        {
            // NOTE: Direct input handling removed - throwing is triggered via:
            // 1. U3DGrabbable release -> OnObjectReleased callback (for throw-on-release)
            // 2. U3DInteractionManager could be extended to support direct throw if needed
        }

        /// <summary>
        /// Check if throw key was pressed using Input System (following established pattern)
        /// </summary>
        private bool WasThrowKeyPressed()
        {
            if (UnityEngine.InputSystem.Keyboard.current == null) return false;

            switch (throwKey)
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

        private void CheckForInputConflicts()
        {
            // Check for other interaction components and auto-remap if needed
            var kickable = GetComponent<U3DKickable>();
            if (kickable != null && kickable.KickKey == throwKey)
            {
                // If kickable uses the same key, remap throwable to G
                if (throwKey == KeyCode.T)
                {
                    throwKey = KeyCode.G;
                    Debug.Log($"U3DThrowable: Auto-remapped throw key to {throwKey} due to kickable component");
                }
            }

            // Check for grabbable conflicts
            if (grabbable != null && grabbable.GrabKey == throwKey)
            {
                // If grabbable uses the same key, remap throwable to G
                if (throwKey == KeyCode.R)
                {
                    throwKey = KeyCode.G;
                    Debug.Log($"U3DThrowable: Auto-remapped throw key to {throwKey} due to grabbable component");
                }
            }
        }

        private void InitializePhysicsState()
        {
            // Start in sleeping state (grabbable and ready)
            SetPhysicsState(PhysicsState.Sleeping);
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

        // FIXED: Proper network state synchronization
        public override void Render()
        {
            if (!isNetworked) return;

            // Sync physics state changes from network
            PhysicsState networkState = NetworkIsPhysicsActive ? PhysicsState.Active : PhysicsState.Sleeping;

            if (grabbable != null && grabbable.IsGrabbed)
            {
                networkState = PhysicsState.Grabbed;
            }

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

        // FIXED: Authority-aware physics state management
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
                case PhysicsState.Grabbed:
                case PhysicsState.Resetting:
                    // FIXED: Only clear velocities if not kinematic to avoid Unity warnings
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
            if (grabbable != null && grabbable.IsGrabbed)
            {
                networkState = PhysicsState.Grabbed;
            }

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

        private void StartBoundsMonitoring()
        {
            if (boundsCheckCoroutine == null)
            {
                boundsCheckCoroutine = StartCoroutine(MonitorWorldBounds());
            }
        }

        private void FindPlayerComponents()
        {
            playerController = FindAnyObjectByType<U3DPlayerController>();
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

        private void OnObjectGrabbed()
        {
            // Reset throw state when grabbed
            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkIsThrown = false;
            }

            // Set to grabbed state - ensures stable hand attachment
            SetPhysicsState(PhysicsState.Grabbed);

            // Ensure we have player references
            if (playerCamera == null || playerTransform == null)
            {
                FindPlayerComponents();
            }
        }

        private void OnObjectReleased()
        {
            // If it's networked but we don't have authority, we can't throw.
            if (isNetworked && !Object.HasStateAuthority) return;

            if (playerCamera == null)
            {
                FindPlayerComponents();
                if (playerCamera == null)
                {
                    Debug.LogWarning("U3DThrowable: No player camera found - cannot determine throw direction");
                    SetPhysicsState(PhysicsState.Sleeping);
                    return;
                }
            }

            // IMPORTANT: Be sure physics can accept a velocity this frame.
            // If Fusion had the body kinematic while held/parented, flip it back now.
            if (rb != null)
            {
                rb.isKinematic = false;   // we are about to simulate
                rb.useGravity = true;
            }

            // Enter active state before applying velocity (so the sim updates)
            SetPhysicsState(PhysicsState.Active);

            // Apply the throw on the next frame AFTER physics state change and unparenting settle.
            StartCoroutine(ApplyThrowVelocityAfterPhysicsActivation());
        }

        private IEnumerator ApplyThrowVelocityAfterPhysicsActivation()
        {
            // Wait one frame to ensure:
            // - Unparent is done
            // - Kinematic toggles take effect
            // - Any network parent sync flip completes
            yield return null;

            // Build throw vector based on camera mode
            float useForce = throwForce;
            Vector3 throwDirection = GetThrowDirection();
            throwDirection.y += upwardThrowBoost / Mathf.Max(0.01f, useForce);
            throwDirection.Normalize();

            Vector3 throwVelocity = throwDirection * useForce;
            if (throwVelocity.magnitude > maxThrowVelocity)
                throwVelocity = throwVelocity.normalized * maxThrowVelocity;

            // If some addon still has us kinematic this frame, try a tiny retry window
            const int maxTries = 3;
            int tries = 0;

            while (rb != null && rb.isKinematic && tries < maxTries)
            {
                // Force writable (we have state authority here)
                rb.isKinematic = false;
                rb.useGravity = true;
                tries++;
                yield return null; // wait one more frame
            }

            if (rb != null && !rb.isKinematic)
            {
                // Use the standard Unity property for widest compatibility
                rb.linearVelocity = throwVelocity;
            }
            else
            {
                Debug.LogWarning("U3DThrowable: Could not apply throw velocity (Rigidbody still kinematic or null).");
                SetPhysicsState(PhysicsState.Sleeping);
                yield break;
            }

            // Mark as thrown on the network if it's a meaningful toss
            if (throwVelocity.magnitude >= minThrowVelocity)
            {
                if (isNetworked && Object.HasStateAuthority)
                {
                    NetworkIsThrown = true;
                    NetworkSleepTimer = TickTimer.CreateFromSeconds(Runner, maxActiveTime);
                }
                OnThrown?.Invoke();
            }
            else
            {
                SetPhysicsState(PhysicsState.Sleeping);
            }
        }

        /// <summary>
        /// Gets the appropriate throw direction based on camera mode.
        /// First person: Use camera forward (precise aiming)
        /// Third person: Use avatar forward (throw where character is facing)
        /// </summary>
        private Vector3 GetThrowDirection()
        {
            bool isThirdPerson = playerController != null && !playerController.IsFirstPerson;

            if (isThirdPerson && playerTransform != null)
            {
                // Third person: throw in the direction the avatar is facing
                return playerTransform.forward;
            }
            else if (playerCamera != null)
            {
                // First person: throw where camera is looking
                return playerCamera.transform.forward;
            }
            else
            {
                // Fallback: forward
                return Vector3.forward;
            }
        }

        // FIXED: Use Fusion's FixedUpdateNetwork for networked sleep checking
        public override void FixedUpdateNetwork()
        {
            if (!isNetworked || !Object.HasStateAuthority) return;

            // Skip checks if object has been grabbed again
            if (grabbable != null && grabbable.IsGrabbed) return;

            // Check for sleep conditions
            if (NetworkIsThrown && NetworkIsPhysicsActive)
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
                    ReturnToGrabbableSleepState();
                }
            }
        }

        /// <summary>
        /// FIXED: Returns object to sleep state while ensuring it remains grabbable
        /// </summary>
        private void ReturnToGrabbableSleepState()
        {
            SetPhysicsState(PhysicsState.Sleeping);

            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkIsThrown = false;
                NetworkIsPhysicsActive = false;
            }

            OnSleep?.Invoke();
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
                    Debug.LogWarning($"U3DThrowable: Object '{name}' fell below world bounds (Y: {transform.position.y})");
                    needsReset = true;
                }
                else if (Vector3.Distance(Vector3.zero, transform.position) > worldBoundsRadius)
                {
                    Debug.LogWarning($"U3DThrowable: Object '{name}' went beyond world radius ({Vector3.Distance(Vector3.zero, transform.position):F1}m)");
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

            // Return to grabbable sleep state
            SetPhysicsState(PhysicsState.Sleeping);

            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkIsThrown = false;
                NetworkIsPhysicsActive = false;
            }

            OnWorldBoundsReset?.Invoke();
        }

        private void OnCollisionEnter(Collision collision)
        {
            // Fire impact event if this was thrown and hits with sufficient force
            bool wasThrown = isNetworked ? NetworkIsThrown : (currentPhysicsState == PhysicsState.Active);

            if (wasThrown && collision.relativeVelocity.magnitude > 2f)
            {
                OnImpact?.Invoke();
            }
        }

        // Public method to manually throw with specific direction and force
        public void ThrowInDirection(Vector3 direction, float force)
        {
            // Authority check for networked objects
            if (isNetworked && !Object.HasStateAuthority) return;

            // Release from grab if currently held
            if (grabbable != null && grabbable.IsGrabbed)
            {
                grabbable.Release();
            }

            // Activate physics
            SetPhysicsState(PhysicsState.Active);

            // Apply throw force
            Vector3 throwVelocity = direction.normalized * force;

            // Clamp to max velocity
            if (throwVelocity.magnitude > maxThrowVelocity)
            {
                throwVelocity = throwVelocity.normalized * maxThrowVelocity;
            }

            rb.linearVelocity = throwVelocity;

            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkIsThrown = true;
                NetworkSleepTimer = TickTimer.CreateFromSeconds(Runner, maxActiveTime);
            }

            OnThrown?.Invoke();
        }

        // Public method to throw in camera/avatar direction with custom force
        public void ThrowInCameraDirection(float customForce = -1f)
        {
            if (playerCamera == null || playerTransform == null)
            {
                FindPlayerComponents();
                if (playerCamera == null && playerTransform == null)
                {
                    Debug.LogWarning("U3DThrowable: No player found for ThrowInCameraDirection");
                    return;
                }
            }

            float useForce = customForce > 0f ? customForce : throwForce;
            Vector3 throwDirection = GetThrowDirection();
            throwDirection.y += upwardThrowBoost / useForce;
            throwDirection.Normalize();

            ThrowInDirection(throwDirection, useForce);
        }

        // Public method to manually put object to sleep
        public void PutToSleep()
        {
            ReturnToGrabbableSleepState();
        }

        // Public method to wake up object (for external triggers)
        public void WakeUp()
        {
            // Only activate physics if not currently grabbed
            if (grabbable == null || !grabbable.IsGrabbed)
            {
                SetPhysicsState(PhysicsState.Active);
            }
        }

        // Public method to reset object to spawn position
        public void ResetToSpawn()
        {
            ResetToSpawnPosition();
        }

        // Public method to update spawn position (useful for dynamic spawn points)
        public void UpdateSpawnPosition(Vector3 newPosition, Quaternion newRotation)
        {
            originalPosition = newPosition;
            originalRotation = newRotation;
        }

        // Public properties for inspection
        public bool HasBeenThrown => isNetworked ? NetworkIsThrown : (currentPhysicsState == PhysicsState.Active);
        public bool IsCurrentlyGrabbed => grabbable != null && grabbable.IsGrabbed;
        public bool IsNetworked => isNetworked;
        public PhysicsState CurrentPhysicsState => currentPhysicsState;
        public Vector3 OriginalPosition => originalPosition;
        public Quaternion OriginalRotation => originalRotation;
        public bool HasNetworkRigidbody => networkRigidbody != null;
        public bool IsPhysicsActive => isNetworked ? NetworkIsPhysicsActive : (currentPhysicsState == PhysicsState.Active);
        public KeyCode ThrowKey { get => throwKey; set => throwKey = value; }

        private void OnDestroy()
        {
            // Stop any running coroutines
            if (boundsCheckCoroutine != null)
            {
                StopCoroutine(boundsCheckCoroutine);
            }

            // Unsubscribe from events
            if (grabbable != null)
            {
                grabbable.OnReleased.RemoveListener(OnObjectReleased);
                grabbable.OnGrabbed.RemoveListener(OnObjectGrabbed);
            }
        }

        // Editor helper to validate setup
        private void OnValidate()
        {
            if (throwForce <= 0f)
            {
                Debug.LogWarning("U3DThrowable: Throw force should be greater than 0");
            }

            if (maxThrowVelocity < throwForce)
            {
                Debug.LogWarning("U3DThrowable: Max throw velocity is less than throw force - throws will be clamped");
            }

            if (sleepVelocityThreshold < 0f)
            {
                Debug.LogWarning("U3DThrowable: Sleep velocity threshold should be positive");
            }

            if (worldBoundsFloor > 0f)
            {
                Debug.LogWarning("U3DThrowable: World bounds floor should typically be negative (below ground level)");
            }

            if (worldBoundsRadius <= 0f)
            {
                Debug.LogWarning("U3DThrowable: World bounds radius should be positive");
            }
        }
    }
}