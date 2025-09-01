using UnityEngine;
using UnityEngine.Events;
using Fusion;
using Fusion.Addons.Physics;
using System.Collections;

namespace U3D
{
    /// <summary>
    /// Makes grabbed objects throwable using camera direction and physics
    /// Must be paired with U3DGrabbable component
    /// Throws objects in the direction the player camera is facing
    /// Manages Rigidbody physics activation and auto-sleep
    /// ENHANCED: Includes world bounds safety and proper grab-throw cycling
    /// MULTIPLAYER: Compatible with NetworkRigidbody3D for Fusion 2 physics sync
    /// PHYSICS: Proper state management that doesn't conflict with NetworkRigidbody3D
    /// SIMPLIFIED INSPECTOR: Complex physics and bounds settings hidden from creators
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

        [Header("Events")]
        [Tooltip("Called when object is thrown")]
        public UnityEvent OnThrown;

        [Tooltip("Called when thrown object hits something")]
        public UnityEvent OnImpact;

        [Tooltip("Called when object goes to sleep")]
        public UnityEvent OnSleep;

        [Tooltip("Called when object is reset due to world bounds violation")]
        public UnityEvent OnWorldBoundsReset;

        // HIDDEN PHYSICS MANAGEMENT - Optimal defaults that creators don't need to adjust
        [HideInInspector]
        [Tooltip("Time to wait before checking if object should sleep after throwing")]
        [SerializeField] private float sleepCheckDelay = 2f;

        [HideInInspector]
        [Tooltip("Velocity threshold below which object will be put to sleep")]
        [SerializeField] private float sleepVelocityThreshold = 0.5f;

        [HideInInspector]
        [Tooltip("Maximum time to wait before forcing sleep")]
        [SerializeField] private float maxActiveTime = 10f;

        // HIDDEN WORLD BOUNDS SAFETY - Protective defaults that creators don't need to change
        [HideInInspector]
        [Tooltip("Y position below which object is considered fallen through world")]
        [SerializeField] private float worldBoundsFloor = -50f;

        [HideInInspector]
        [Tooltip("Distance from origin beyond which object resets")]
        [SerializeField] private float worldBoundsRadius = 1000f;

        [HideInInspector]
        [Tooltip("How often to check world bounds (in seconds)")]
        [SerializeField] private float boundsCheckInterval = 1f;

        // Components
        private Rigidbody rb;
        private U3DGrabbable grabbable;
        private Camera playerCamera;
        private Transform playerTransform;
        private NetworkObject networkObject;
        private NetworkRigidbody3D networkRigidbody;

        // State tracking
        private bool hasBeenThrown = false;
        private bool isNetworked = false;
        private Coroutine sleepCheckCoroutine;
        private Coroutine boundsCheckCoroutine;

        // Physics state management
        private PhysicsState currentPhysicsState = PhysicsState.Sleeping;
        private bool physicsStateInitialized = false;

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

            // Check if this object has networking support
            networkObject = GetComponent<NetworkObject>();
            isNetworked = networkObject != null;

            // Ensure we have a grabbable component
            if (grabbable == null)
            {
                Debug.LogError("U3DThrowable requires U3DGrabbable component!");
                enabled = false;
                return;
            }

            // Subscribe to grab/release events
            grabbable.OnReleased.AddListener(OnObjectReleased);
            grabbable.OnGrabbed.AddListener(OnObjectGrabbed);

            if (!isNetworked)
            {
                Debug.Log($"U3DThrowable on '{name}' running in non-networked mode");
            }
        }

        public override void Spawned()
        {
            if (!isNetworked) return;

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
        }

        private void InitializePhysicsState()
        {
            if (physicsStateInitialized) return;

            // Start in sleeping state (grabbable and ready)
            SetPhysicsState(PhysicsState.Sleeping);
            physicsStateInitialized = true;
        }

        public void OnStateAuthorityChanged()
        {
            if (!isNetworked) return;

            // When authority changes, ensure physics state is appropriate
            if (Object.HasStateAuthority)
            {
                // We gained authority - maintain current state
                ApplyCurrentPhysicsState();
            }
            else
            {
                // We lost authority - NetworkRigidbody3D will handle remote sync
                // Don't modify physics directly on non-authority clients
            }
        }

        private void SetPhysicsState(PhysicsState newState)
        {
            // Only allow state changes on authority (or non-networked)
            if (isNetworked && !Object.HasStateAuthority && newState != PhysicsState.Sleeping)
            {
                return;
            }

            currentPhysicsState = newState;
            ApplyCurrentPhysicsState();
        }

        private void ApplyCurrentPhysicsState()
        {
            // Only apply physics changes on authority (or non-networked)
            if (isNetworked && !Object.HasStateAuthority)
            {
                return;
            }

            if (rb == null) return;

            switch (currentPhysicsState)
            {
                case PhysicsState.Sleeping:
                    rb.isKinematic = true;
                    rb.useGravity = false;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    break;

                case PhysicsState.Grabbed:
                    rb.isKinematic = true;
                    rb.useGravity = false;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    break;

                case PhysicsState.Active:
                    rb.isKinematic = false;
                    rb.useGravity = true;
                    break;

                case PhysicsState.Resetting:
                    rb.isKinematic = true;
                    rb.useGravity = false;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    break;
            }

            // NetworkRigidbody3D will automatically sync these changes to non-authority clients
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

        private void OnObjectGrabbed()
        {
            // Reset throw state and stop any monitoring when grabbed
            hasBeenThrown = false;

            // Stop sleep checking
            if (sleepCheckCoroutine != null)
            {
                StopCoroutine(sleepCheckCoroutine);
                sleepCheckCoroutine = null;
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
            // Authority check for networked objects
            if (isNetworked && !Object.HasStateAuthority) return;

            // Only throw if we have the necessary components
            if (playerCamera == null)
            {
                Debug.LogWarning("U3DThrowable: No player camera found - cannot determine throw direction");
                SetPhysicsState(PhysicsState.Sleeping);
                return;
            }

            // Activate physics for throwing
            SetPhysicsState(PhysicsState.Active);

            // Calculate throw direction based on camera forward
            Vector3 throwDirection = playerCamera.transform.forward;

            // Add upward boost to the throw direction
            throwDirection.y += upwardThrowBoost / throwForce; // Scale boost relative to throw force
            throwDirection.Normalize();

            // Calculate final throw velocity
            Vector3 throwVelocity = throwDirection * throwForce;

            // Clamp to max velocity
            if (throwVelocity.magnitude > maxThrowVelocity)
            {
                throwVelocity = throwVelocity.normalized * maxThrowVelocity;
            }

            // Apply velocity
            rb.linearVelocity = throwVelocity;

            // Mark as thrown if velocity is significant
            if (throwVelocity.magnitude >= minThrowVelocity)
            {
                hasBeenThrown = true;
                OnThrown?.Invoke();

                // Start sleep checking coroutine
                if (sleepCheckCoroutine != null)
                {
                    StopCoroutine(sleepCheckCoroutine);
                }
                sleepCheckCoroutine = StartCoroutine(CheckForSleep());
            }
            else
            {
                // If throw velocity too low, just put back to sleep immediately
                SetPhysicsState(PhysicsState.Sleeping);
            }
        }

        private IEnumerator CheckForSleep()
        {
            float elapsedTime = 0f;

            // Wait initial delay before starting checks
            yield return new WaitForSeconds(sleepCheckDelay);

            while (elapsedTime < maxActiveTime)
            {
                // Skip checks if object has been grabbed again
                if (grabbable != null && grabbable.IsGrabbed)
                {
                    yield break;
                }

                // Only check sleep on authority (or non-networked)
                if (!isNetworked || (Object != null && Object.HasStateAuthority))
                {
                    // Check if velocity is low enough to sleep
                    if (rb.linearVelocity.magnitude < sleepVelocityThreshold &&
                        rb.angularVelocity.magnitude < sleepVelocityThreshold)
                    {
                        // Object has come to rest - return to grabbable sleep state
                        ReturnToGrabbableSleepState();
                        yield break;
                    }
                }

                // Wait before next check
                yield return new WaitForSeconds(0.5f);
                elapsedTime += 0.5f;
            }

            // Force sleep after maximum time
            ReturnToGrabbableSleepState();
        }

        /// <summary>
        /// CRITICAL METHOD: Returns object to sleep state while ensuring it remains grabbable
        /// This is the key to fixing the grab-throw-grab cycle
        /// </summary>
        private void ReturnToGrabbableSleepState()
        {
            SetPhysicsState(PhysicsState.Sleeping);
            hasBeenThrown = false;
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

            // Stop any active physics monitoring
            if (sleepCheckCoroutine != null)
            {
                StopCoroutine(sleepCheckCoroutine);
                sleepCheckCoroutine = null;
            }

            // Set to resetting state to prevent physics interference
            SetPhysicsState(PhysicsState.Resetting);

            // Reset position and rotation to spawn point
            transform.position = originalPosition;
            transform.rotation = originalRotation;

            // Return to grabbable sleep state
            SetPhysicsState(PhysicsState.Sleeping);
            hasBeenThrown = false;

            OnWorldBoundsReset?.Invoke();
        }

        private void OnCollisionEnter(Collision collision)
        {
            // Fire impact event if this was thrown and hits with sufficient force
            if (hasBeenThrown && collision.relativeVelocity.magnitude > 2f)
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
            hasBeenThrown = true;
            OnThrown?.Invoke();

            // Start sleep checking
            if (sleepCheckCoroutine != null)
            {
                StopCoroutine(sleepCheckCoroutine);
            }
            sleepCheckCoroutine = StartCoroutine(CheckForSleep());
        }

        // Public method to throw in camera direction with custom force
        public void ThrowInCameraDirection(float customForce = -1f)
        {
            if (playerCamera == null)
            {
                FindPlayerComponents();
                if (playerCamera == null)
                {
                    Debug.LogWarning("U3DThrowable: No camera found for ThrowInCameraDirection");
                    return;
                }
            }

            float useForce = customForce > 0f ? customForce : throwForce;
            Vector3 throwDirection = playerCamera.transform.forward;
            throwDirection.y += upwardThrowBoost / useForce;
            throwDirection.Normalize();

            ThrowInDirection(throwDirection, useForce);
        }

        // Public method to manually put object to sleep
        public void PutToSleep()
        {
            if (sleepCheckCoroutine != null)
            {
                StopCoroutine(sleepCheckCoroutine);
                sleepCheckCoroutine = null;
            }

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
        public bool HasBeenThrown => hasBeenThrown;
        public bool IsCurrentlyGrabbed => grabbable != null && grabbable.IsGrabbed;
        public bool IsNetworked => isNetworked;
        public PhysicsState CurrentPhysicsState => currentPhysicsState;
        public Vector3 OriginalPosition => originalPosition;
        public Quaternion OriginalRotation => originalRotation;
        public bool HasNetworkRigidbody => networkRigidbody != null;

        private void OnDestroy()
        {
            // Stop any running coroutines
            if (sleepCheckCoroutine != null)
            {
                StopCoroutine(sleepCheckCoroutine);
            }

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

        // Debug information for development
        [System.Serializable]
        public struct ThrowableDebugInfo
        {
            public bool hasBeenThrown;
            public PhysicsState currentPhysicsState;
            public bool isCurrentlyGrabbed;
            public bool isSleepCheckActive;
            public bool isBoundsCheckActive;
            public Vector3 currentPosition;
            public Vector3 spawnPosition;
            public float currentVelocity;
            public float distanceFromSpawn;
            public bool hasAuthority;
            public bool hasNetworkRigidbody;
            public bool rigidbodyIsKinematic;
            public bool physicsStateInitialized;
        }

        public ThrowableDebugInfo GetDebugInfo()
        {
            return new ThrowableDebugInfo
            {
                hasBeenThrown = hasBeenThrown,
                currentPhysicsState = currentPhysicsState,
                isCurrentlyGrabbed = IsCurrentlyGrabbed,
                isSleepCheckActive = sleepCheckCoroutine != null,
                isBoundsCheckActive = boundsCheckCoroutine != null,
                currentPosition = transform.position,
                spawnPosition = originalPosition,
                currentVelocity = rb != null ? rb.linearVelocity.magnitude : 0f,
                distanceFromSpawn = Vector3.Distance(transform.position, originalPosition),
                hasAuthority = isNetworked ? Object.HasStateAuthority : true,
                hasNetworkRigidbody = networkRigidbody != null,
                rigidbodyIsKinematic = rb != null ? rb.isKinematic : false,
                physicsStateInitialized = physicsStateInitialized
            };
        }
    }
}