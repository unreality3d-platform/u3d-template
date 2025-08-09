using UnityEngine;
using UnityEngine.Events;

namespace U3D
{
    /// <summary>
    /// Makes grabbed objects throwable with physics-based velocity
    /// Must be paired with either U3DGrabbableNear or U3DGrabbableFar
    /// </summary>
    [AddComponentMenu("U3D/Interactions/U3D Throwable")]
    [RequireComponent(typeof(Rigidbody))]
    public class U3DThrowable : MonoBehaviour
    {
        [Header("Throw Configuration")]
        [Tooltip("Multiplier for throw velocity")]
        [SerializeField] private float throwForceMultiplier = 1f;

        [Tooltip("Additional upward force when throwing")]
        [SerializeField] private float upwardThrowBoost = 2f;

        [Tooltip("Maximum throw velocity")]
        [SerializeField] private float maxThrowVelocity = 20f;

        [Header("Events")]
        [Tooltip("Called when object is thrown")]
        public UnityEvent OnThrown;

        [Tooltip("Called when thrown object hits something")]
        public UnityEvent OnImpact;

        // Components
        private Rigidbody rb;
        private U3DGrabbableNear grabbableNear;
        private U3DGrabbableFar grabbableFar;

        // Velocity tracking
        private Vector3[] velocityHistory = new Vector3[5];
        private int velocityIndex = 0;
        private Vector3 lastPosition;
        private bool wasGrabbed = false;
        private bool hasBeenThrown = false;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            grabbableNear = GetComponent<U3DGrabbableNear>();
            grabbableFar = GetComponent<U3DGrabbableFar>();

            // Ensure we have at least one grabbable component
            if (grabbableNear == null && grabbableFar == null)
            {
                Debug.LogError("U3DThrowable requires either U3DGrabbableNear or U3DGrabbableFar component!");
                enabled = false;
                return;
            }

            // Subscribe to release events
            if (grabbableNear != null)
            {
                grabbableNear.OnReleased.AddListener(OnObjectReleased);
                grabbableNear.OnGrabbed.AddListener(OnObjectGrabbed);
            }

            if (grabbableFar != null)
            {
                grabbableFar.OnReleased.AddListener(OnObjectReleased);
                grabbableFar.OnGrabbed.AddListener(OnObjectGrabbed);
            }

            lastPosition = transform.position;
        }

        private void FixedUpdate()
        {
            // Track velocity while grabbed
            if (IsCurrentlyGrabbed())
            {
                Vector3 currentVelocity = (transform.position - lastPosition) / Time.fixedDeltaTime;
                velocityHistory[velocityIndex] = currentVelocity;
                velocityIndex = (velocityIndex + 1) % velocityHistory.Length;
                lastPosition = transform.position;
                wasGrabbed = true;
            }
            else if (wasGrabbed)
            {
                wasGrabbed = false;
            }
        }

        private bool IsCurrentlyGrabbed()
        {
            if (grabbableNear != null && grabbableNear.IsGrabbed) return true;
            if (grabbableFar != null && grabbableFar.IsGrabbed) return true;
            return false;
        }

        private void OnObjectGrabbed()
        {
            // Reset velocity history
            for (int i = 0; i < velocityHistory.Length; i++)
            {
                velocityHistory[i] = Vector3.zero;
            }
            velocityIndex = 0;
            lastPosition = transform.position;
            hasBeenThrown = false;
        }

        private void OnObjectReleased()
        {
            // Calculate average velocity from history
            Vector3 averageVelocity = Vector3.zero;
            int validSamples = 0;

            for (int i = 0; i < velocityHistory.Length; i++)
            {
                if (velocityHistory[i].magnitude > 0.01f)
                {
                    averageVelocity += velocityHistory[i];
                    validSamples++;
                }
            }

            if (validSamples > 0)
            {
                averageVelocity /= validSamples;

                // Apply throw force
                Vector3 throwVelocity = averageVelocity * throwForceMultiplier;

                // Add upward boost
                throwVelocity.y += upwardThrowBoost;

                // Clamp to max velocity
                if (throwVelocity.magnitude > maxThrowVelocity)
                {
                    throwVelocity = throwVelocity.normalized * maxThrowVelocity;
                }

                // Apply velocity to rigidbody
                rb.linearVelocity = throwVelocity;

                // Mark as thrown if velocity is significant
                if (throwVelocity.magnitude > 1f)
                {
                    hasBeenThrown = true;
                    OnThrown?.Invoke();
                }
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            // Fire impact event if this was thrown
            if (hasBeenThrown && collision.relativeVelocity.magnitude > 2f)
            {
                OnImpact?.Invoke();
                hasBeenThrown = false;
            }
        }

        // Public method to manually throw with specific force
        public void ThrowInDirection(Vector3 direction, float force)
        {
            if (IsCurrentlyGrabbed())
            {
                // Release first
                if (grabbableNear != null && grabbableNear.IsGrabbed)
                    grabbableNear.Release();
                if (grabbableFar != null && grabbableFar.IsGrabbed)
                    grabbableFar.Release();
            }

            // Apply throw force
            Vector3 throwVelocity = direction.normalized * force * throwForceMultiplier;

            // Clamp to max velocity
            if (throwVelocity.magnitude > maxThrowVelocity)
            {
                throwVelocity = throwVelocity.normalized * maxThrowVelocity;
            }

            rb.linearVelocity = throwVelocity;
            hasBeenThrown = true;
            OnThrown?.Invoke();
        }

        private void OnDestroy()
        {
            // Unsubscribe from events
            if (grabbableNear != null)
            {
                grabbableNear.OnReleased.RemoveListener(OnObjectReleased);
                grabbableNear.OnGrabbed.RemoveListener(OnObjectGrabbed);
            }

            if (grabbableFar != null)
            {
                grabbableFar.OnReleased.RemoveListener(OnObjectReleased);
                grabbableFar.OnGrabbed.RemoveListener(OnObjectGrabbed);
            }
        }
    }
}