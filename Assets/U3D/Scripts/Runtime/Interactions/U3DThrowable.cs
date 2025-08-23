using UnityEngine;
using UnityEngine.Events;
using Fusion;

namespace U3D
{
    /// <summary>
    /// Makes grabbed objects throwable using camera direction and physics
    /// Must be paired with U3DGrabbable component
    /// Throws objects in the direction the player camera is facing
    /// Supports both networked and non-networked modes automatically
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

        // Components
        private Rigidbody rb;
        private U3DGrabbable grabbable;
        private Camera playerCamera;
        private Transform playerTransform;
        private NetworkObject networkObject;

        // State tracking
        private bool hasBeenThrown = false;
        private bool isNetworked = false;

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
            // Reset throw state when grabbed
            hasBeenThrown = false;

            // Ensure we have player references
            if (playerCamera == null || playerTransform == null)
            {
                FindPlayerComponents();
            }
        }

        private void OnObjectReleased()
        {
            // Only throw if we have the necessary components
            if (playerCamera == null)
            {
                Debug.LogWarning("U3DThrowable: No player camera found - cannot determine throw direction");
                return;
            }

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

                Debug.Log($"Object thrown with velocity: {throwVelocity.magnitude:F2} in direction: {throwDirection}");
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            // Fire impact event if this was thrown and hits with sufficient force
            if (hasBeenThrown && collision.relativeVelocity.magnitude > 2f)
            {
                OnImpact?.Invoke();
                hasBeenThrown = false; // Reset after impact

                Debug.Log($"Thrown object impacted with force: {collision.relativeVelocity.magnitude:F2}");
            }
        }

        // Public method to manually throw with specific direction and force
        public void ThrowInDirection(Vector3 direction, float force)
        {
            // Release from grab if currently held
            if (grabbable != null && grabbable.IsGrabbed)
            {
                grabbable.Release();
            }

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

        // Public properties for inspection
        public bool HasBeenThrown => hasBeenThrown;
        public bool IsCurrentlyGrabbed => grabbable != null && grabbable.IsGrabbed;
        public bool IsNetworked => isNetworked;

        private void OnDestroy()
        {
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
        }

        // Override NetworkBehaviour methods for non-networked compatibility
        public override void Spawned()
        {
            if (!isNetworked) return;
            // Networked initialization if needed
        }
    }
}