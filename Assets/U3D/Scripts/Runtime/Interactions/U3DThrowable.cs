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

        [Header("Events")]
        [Tooltip("Called when object is thrown")]
        public UnityEvent OnThrown;

        [Tooltip("Called when thrown object hits something")]
        public UnityEvent OnImpact;

        [Tooltip("Called when object goes to sleep")]
        public UnityEvent OnSleep;

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

            // Subscribe to release event
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
            // Reset throw state and stop any sleep checking when grabbed
            hasBeenThrown = false;

            if (sleepCheckCoroutine != null)
            {
                StopCoroutine(sleepCheckCoroutine);
                sleepCheckCoroutine = null;
            }

            // Put physics to sleep while grabbed
            SetPhysicsSleeping();

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
        }

        private IEnumerator CheckForSleep()
        {
            float elapsedTime = 0f;

            // Wait initial delay before starting checks
            yield return new WaitForSeconds(sleepCheckDelay);

            while (elapsedTime < maxActiveTime)
            {
                // Check if velocity is low enough to sleep
                if (rb.linearVelocity.magnitude < sleepVelocityThreshold &&
                    rb.angularVelocity.magnitude < sleepVelocityThreshold)
                {
                    // Object has come to rest
                    SetPhysicsSleeping();
                    OnSleep?.Invoke();
                    Debug.Log($"Object '{name}' put to sleep due to low velocity");
                    yield break;
                }

                // Wait before next check
                yield return new WaitForSeconds(0.5f);
                elapsedTime += 0.5f;
            }

            // Force sleep after maximum time
            SetPhysicsSleeping();
            OnSleep?.Invoke();
            Debug.Log($"Object '{name}' forced to sleep after {maxActiveTime} seconds");
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

            SetPhysicsSleeping();
            hasBeenThrown = false;
            OnSleep?.Invoke();
        }

        // Public method to wake up object
        public void WakeUp()
        {
            ActivatePhysics();
        }

        // Public properties for inspection
        public bool HasBeenThrown => hasBeenThrown;
        public bool IsCurrentlyGrabbed => grabbable != null && grabbable.IsGrabbed;
        public bool IsNetworked => isNetworked;
        public bool IsPhysicsActive => isPhysicsActive;

        private void OnDestroy()
        {
            // Stop any running coroutines
            if (sleepCheckCoroutine != null)
            {
                StopCoroutine(sleepCheckCoroutine);
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
        }

        // Override NetworkBehaviour methods for non-networked compatibility
        public override void Spawned()
        {
            if (!isNetworked) return;
            // Ensure physics starts sleeping on spawn
            SetPhysicsSleeping();
        }
    }
}