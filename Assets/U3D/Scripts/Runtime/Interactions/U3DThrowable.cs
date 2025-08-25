using UnityEngine;
using UnityEngine.Events;
using Fusion;
using System.Collections;

namespace U3D
{
    /// <summary>
    /// Makes grabbed objects throwable using camera direction and physics
    /// Must be paired with U3DGrabbable component
    /// Throws objects in the direction the player camera is facing
    /// Manages Rigidbody physics activation and auto-sleep
    /// ENHANCED: Includes world bounds safety and proper grab-throw cycling
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

        [Header("Physics Management")]
        [Tooltip("Time to wait before checking if object should sleep after throwing")]
        [SerializeField] private float sleepCheckDelay = 2f;

        [Tooltip("Velocity threshold below which object will be put to sleep")]
        [SerializeField] private float sleepVelocityThreshold = 0.5f;

        [Tooltip("Maximum time to wait before forcing sleep")]
        [SerializeField] private float maxActiveTime = 10f;

        [Header("World Bounds Safety")]
        [Tooltip("Y position below which object is considered fallen through world")]
        [SerializeField] private float worldBoundsFloor = -50f;

        [Tooltip("Distance from origin beyond which object resets")]
        [SerializeField] private float worldBoundsRadius = 1000f;

        [Tooltip("How often to check world bounds (in seconds)")]
        [SerializeField] private float boundsCheckInterval = 1f;

        [Header("Events")]
        [Tooltip("Called when object is thrown")]
        public UnityEvent OnThrown;

        [Tooltip("Called when thrown object hits something")]
        public UnityEvent OnImpact;

        [Tooltip("Called when object goes to sleep")]
        public UnityEvent OnSleep;

        [Tooltip("Called when object is reset due to world bounds violation")]
        public UnityEvent OnWorldBoundsReset;

        // Components
        private Rigidbody rb;
        private U3DGrabbable grabbable;
        private Camera playerCamera;
        private Transform playerTransform;
        private NetworkObject networkObject;

        // State tracking
        private bool hasBeenThrown = false;
        private bool isNetworked = false;
        private bool isPhysicsActive = false;
        private Coroutine sleepCheckCoroutine;
        private Coroutine boundsCheckCoroutine;

        // Original position and rotation for reset purposes
        private Vector3 originalPosition;
        private Quaternion originalRotation;
        private bool hasRecordedOriginalTransform = false;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            grabbable = GetComponent<U3DGrabbable>();

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

            // Ensure rigidbody starts in sleep state
            SetPhysicsSleeping();

            if (!isNetworked)
            {
                Debug.Log($"U3DThrowable on '{name}' running in non-networked mode");
            }
        }

        private void Start()
        {
            // Find player components
            FindPlayerComponents();

            // Record original spawn position for reset purposes
            RecordOriginalTransform();

            // Start world bounds monitoring
            StartBoundsMonitoring();
        }

        private void RecordOriginalTransform()
        {
            if (!hasRecordedOriginalTransform)
            {
                originalPosition = transform.position;
                originalRotation = transform.rotation;
                hasRecordedOriginalTransform = true;
                Debug.Log($"U3DThrowable: Recorded spawn transform for '{name}' at {originalPosition}");
            }
        }

        private void StartBoundsMonitoring()
        {
            if (boundsCheckCoroutine == null)
            {
                boundsCheckCoroutine = StartCoroutine(MonitorWorldBounds());
            }
        }

        private void SetPhysicsSleeping()
        {
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                isPhysicsActive = false;
                Debug.Log($"Physics put to sleep on '{name}'");
            }
        }

        private void ActivatePhysics()
        {
            if (rb != null && !isPhysicsActive)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                isPhysicsActive = true;
                Debug.Log($"Physics activated on '{name}'");
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

            // Put physics to sleep while grabbed - this ensures stable hand attachment
            SetPhysicsSleeping();

            // Ensure we have player references
            if (playerCamera == null || playerTransform == null)
            {
                FindPlayerComponents();
            }

            Debug.Log($"U3DThrowable: Object '{name}' grabbed - physics sleeping, ready for throw");
        }

        private void OnObjectReleased()
        {
            // Authority check for networked objects
            if (isNetworked && !Object.HasStateAuthority) return;

            // Only throw if we have the necessary components
            if (playerCamera == null)
            {
                Debug.LogWarning("U3DThrowable: No player camera found - cannot determine throw direction");
                return;
            }

            // Activate physics for throwing
            ActivatePhysics();

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

            // Apply velocity to rigidbody
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

                Debug.Log($"Object thrown with velocity: {throwVelocity.magnitude:F2} in direction: {throwDirection}");
            }
            else
            {
                // If throw velocity too low, just put back to sleep immediately
                SetPhysicsSleeping();
                Debug.Log($"U3DThrowable: Throw velocity too low ({throwVelocity.magnitude:F2}), returning to sleep");
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
                    Debug.Log($"U3DThrowable: Object '{name}' was re-grabbed during sleep check - stopping monitoring");
                    yield break;
                }

                // Check if velocity is low enough to sleep
                if (rb.linearVelocity.magnitude < sleepVelocityThreshold &&
                    rb.angularVelocity.magnitude < sleepVelocityThreshold)
                {
                    // Object has come to rest - put to sleep and ensure grabbable
                    ReturnToGrabbableSleepState();
                    yield break;
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
            SetPhysicsSleeping();
            hasBeenThrown = false;
            OnSleep?.Invoke();

            Debug.Log($"U3DThrowable: Object '{name}' returned to grabbable sleep state - ready for next grab/throw cycle");
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

            // Reset position and rotation to spawn point
            transform.position = originalPosition;
            transform.rotation = originalRotation;

            // Return to grabbable sleep state
            ReturnToGrabbableSleepState();

            OnWorldBoundsReset?.Invoke();
            Debug.Log($"U3DThrowable: Reset '{name}' to spawn position {originalPosition} - ready for interaction");
        }

        private void OnCollisionEnter(Collision collision)
        {
            // Fire impact event if this was thrown and hits with sufficient force
            if (hasBeenThrown && collision.relativeVelocity.magnitude > 2f)
            {
                OnImpact?.Invoke();
                Debug.Log($"Thrown object impacted with force: {collision.relativeVelocity.magnitude:F2}");
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
            ActivatePhysics();

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

            Debug.Log($"Object manually thrown with velocity: {throwVelocity.magnitude:F2}");
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
                ActivatePhysics();
                Debug.Log($"U3DThrowable: Manually woke up '{name}'");
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
            Debug.Log($"U3DThrowable: Updated spawn position for '{name}' to {originalPosition}");
        }

        // Public properties for inspection
        public bool HasBeenThrown => hasBeenThrown;
        public bool IsCurrentlyGrabbed => grabbable != null && grabbable.IsGrabbed;
        public bool IsNetworked => isNetworked;
        public bool IsPhysicsActive => isPhysicsActive;
        public Vector3 OriginalPosition => originalPosition;
        public Quaternion OriginalRotation => originalRotation;

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

        // Override NetworkBehaviour methods for non-networked compatibility
        public override void Spawned()
        {
            if (!isNetworked) return;
            // Ensure physics starts sleeping on spawn
            SetPhysicsSleeping();
        }

        // Debug information for development
        [System.Serializable]
        public struct ThrowableDebugInfo
        {
            public bool hasBeenThrown;
            public bool isPhysicsActive;
            public bool isCurrentlyGrabbed;
            public bool isSleepCheckActive;
            public bool isBoundsCheckActive;
            public Vector3 currentPosition;
            public Vector3 spawnPosition;
            public float currentVelocity;
            public float distanceFromSpawn;
        }

        public ThrowableDebugInfo GetDebugInfo()
        {
            return new ThrowableDebugInfo
            {
                hasBeenThrown = hasBeenThrown,
                isPhysicsActive = isPhysicsActive,
                isCurrentlyGrabbed = IsCurrentlyGrabbed,
                isSleepCheckActive = sleepCheckCoroutine != null,
                isBoundsCheckActive = boundsCheckCoroutine != null,
                currentPosition = transform.position,
                spawnPosition = originalPosition,
                currentVelocity = rb != null ? rb.linearVelocity.magnitude : 0f,
                distanceFromSpawn = Vector3.Distance(transform.position, originalPosition)
            };
        }
    }
}