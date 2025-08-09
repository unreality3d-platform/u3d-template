using UnityEngine;
using UnityEngine.Events;

namespace U3D
{
    /// <summary>
    /// Makes objects grabbable when in direct contact with the player
    /// Objects snap to the player's hand position
    /// </summary>
    [AddComponentMenu("U3D/Interactions/U3D Grabbable Near")]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Collider))]
    public class U3DGrabbableNear : MonoBehaviour, IU3DInteractable
    {
        [Header("Grab Configuration")]
        [Tooltip("Name of the hand bone to attach to (leave empty to use player position)")]
        [SerializeField] private string handBoneName = "RightHand";

        [Tooltip("Offset from the hand position")]
        [SerializeField] private Vector3 grabOffset = Vector3.zero;

        [Tooltip("Can this object be grabbed while another is held?")]
        [SerializeField] private bool allowMultiGrab = false;

        [Header("Events")]
        [Tooltip("Called when object is grabbed")]
        public UnityEvent OnGrabbed;

        [Tooltip("Called when object is released")]
        public UnityEvent OnReleased;

        [Tooltip("Called when player enters grab range")]
        public UnityEvent OnEnterGrabRange;

        [Tooltip("Called when player exits grab range")]
        public UnityEvent OnExitGrabRange;

        // Components
        private Rigidbody rb;
        private Collider col;
        private Transform originalParent;
        private Transform handTransform;
        private Transform playerTransform;

        // State
        private bool isGrabbed = false;
        private bool isInRange = false;
        private bool wasKinematic;
        private bool usedGravity;

        // Static tracking for single grab mode
        private static U3DGrabbableNear currentlyGrabbed;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            col = GetComponent<Collider>();
            originalParent = transform.parent;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player") && !isInRange)
            {
                isInRange = true;
                playerTransform = other.transform;
                FindHandBone();
                OnEnterGrabRange?.Invoke();
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Player") && isInRange)
            {
                isInRange = false;
                OnExitGrabRange?.Invoke();

                if (!isGrabbed)
                {
                    playerTransform = null;
                    handTransform = null;
                }
            }
        }

        private void FindHandBone()
        {
            if (playerTransform == null) return;

            if (!string.IsNullOrEmpty(handBoneName))
            {
                Transform[] allTransforms = playerTransform.GetComponentsInChildren<Transform>();
                foreach (Transform t in allTransforms)
                {
                    if (t.name == handBoneName)
                    {
                        handTransform = t;
                        break;
                    }
                }
            }

            // Fallback to player transform if hand not found
            if (handTransform == null)
            {
                handTransform = playerTransform;
            }
        }

        public void Grab()
        {
            if (isGrabbed) return;

            // Check if we can grab (single grab mode)
            if (!allowMultiGrab && currentlyGrabbed != null && currentlyGrabbed != this)
            {
                currentlyGrabbed.Release();
            }

            isGrabbed = true;
            currentlyGrabbed = this;

            // Store original physics settings
            wasKinematic = rb.isKinematic;
            usedGravity = rb.useGravity;

            // Configure for grabbing
            rb.isKinematic = true;
            rb.useGravity = false;
            col.isTrigger = true;

            // Parent to hand
            if (handTransform != null)
            {
                transform.SetParent(handTransform);
                transform.localPosition = grabOffset;
            }

            OnGrabbed?.Invoke();
        }

        public void Release()
        {
            if (!isGrabbed) return;

            isGrabbed = false;
            if (currentlyGrabbed == this)
            {
                currentlyGrabbed = null;
            }

            // Restore physics settings
            rb.isKinematic = wasKinematic;
            rb.useGravity = usedGravity;
            col.isTrigger = false;

            // Unparent
            transform.SetParent(originalParent);

            // Clear references if not in range
            if (!isInRange)
            {
                playerTransform = null;
                handTransform = null;
            }

            OnReleased?.Invoke();
        }

        // IU3DInteractable implementation
        public void OnInteract()
        {
            if (isGrabbed)
                Release();
            else if (isInRange)
                Grab();
        }

        public void OnPlayerEnterRange()
        {
            // Handled by OnTriggerEnter
        }

        public void OnPlayerExitRange()
        {
            // Handled by OnTriggerExit
        }

        public bool CanInteract()
        {
            return isInRange || isGrabbed;
        }

        public int GetInteractionPriority()
        {
            return 50; // Standard priority
        }

        public string GetInteractionPrompt()
        {
            return isGrabbed ? "Release" : "Grab";
        }

        // Public properties
        public bool IsGrabbed => isGrabbed;
        public bool IsInRange => isInRange;

        private void OnDestroy()
        {
            if (isGrabbed)
            {
                Release();
            }
        }
    }
}