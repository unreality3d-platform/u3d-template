using UnityEngine;
using UnityEngine.Events;

namespace U3D
{
    /// <summary>
    /// Universal grabbable component for both near and far interactions
    /// Objects snap to the player's hand position when grabbed
    /// Distance-based grabbing uses avatar position, not camera position for third-person compatibility
    /// </summary>

    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Collider))]
    public class U3DGrabbable : MonoBehaviour, IU3DInteractable
    {
        [Header("Grab Distance Configuration")]
        [Tooltip("Minimum distance to grab from (0 = touch only)")]
        [SerializeField] private float minGrabDistance = 0f;

        [Tooltip("Maximum distance to grab from")]
        [SerializeField] private float maxGrabDistance = 2f;

        [Header("Hand Attachment")]
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

        [Tooltip("Called when player aims at this object (distance grab only)")]
        public UnityEvent OnAimEnter;

        [Tooltip("Called when player stops aiming at this object (distance grab only)")]
        public UnityEvent OnAimExit;

        // Components
        private Rigidbody rb;
        private Collider col;
        private Transform originalParent;
        private Transform handTransform;
        private Transform playerTransform;
        private Camera playerCamera;

        // State
        private bool isGrabbed = false;
        private bool isInRange = false;
        private bool isAimedAt = false;
        private bool wasKinematic;
        private bool usedGravity;
        private float lastAimCheckTime;

        // Static tracking for single grab mode
        private static U3DGrabbable currentlyGrabbed;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            col = GetComponent<Collider>();
            originalParent = transform.parent;
        }

        private void Update()
        {
            // Update range and aim status
            UpdatePlayerProximity();

            // Check aim only for distance grabbing (when max distance > 0)
            if (maxGrabDistance > 0f && !isGrabbed && Time.time - lastAimCheckTime > 0.1f)
            {
                lastAimCheckTime = Time.time;
                CheckIfAimedAt();
            }
        }

        private void UpdatePlayerProximity()
        {
            if (playerTransform == null)
            {
                FindPlayer();
                return;
            }

            // Use avatar position for distance calculations (not camera)
            float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);
            bool wasInRange = isInRange;
            isInRange = distanceToPlayer >= minGrabDistance && distanceToPlayer <= maxGrabDistance;

            // Fire range events
            if (isInRange && !wasInRange)
            {
                OnEnterGrabRange?.Invoke();
            }
            else if (!isInRange && wasInRange)
            {
                OnExitGrabRange?.Invoke();
            }
        }

        private void CheckIfAimedAt()
        {
            if (playerCamera == null || maxGrabDistance <= 0f)
            {
                isAimedAt = false;
                return;
            }

            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            RaycastHit hit;

            bool wasAimedAt = isAimedAt;
            isAimedAt = false;

            if (Physics.Raycast(ray, out hit, maxGrabDistance))
            {
                if (hit.collider == col)
                {
                    // Use avatar position for distance check (not camera)
                    float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);
                    if (distanceToPlayer >= minGrabDistance && distanceToPlayer <= maxGrabDistance)
                    {
                        isAimedAt = true;
                    }
                }
            }

            // Fire aim events
            if (isAimedAt && !wasAimedAt)
            {
                OnAimEnter?.Invoke();
            }
            else if (!isAimedAt && wasAimedAt)
            {
                OnAimExit?.Invoke();
            }
        }

        private void FindPlayer()
        {
            U3DPlayerController playerController = Object.FindAnyObjectByType<U3DPlayerController>();
            if (playerController != null)
            {
                playerTransform = playerController.transform;
                playerCamera = playerController.GetComponentInChildren<Camera>();
                FindHandBone();
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

        private void OnTriggerEnter(Collider other)
        {
            // Handle touch-based near grabbing (when minGrabDistance is 0)
            if (minGrabDistance <= 0f && other.CompareTag("Player"))
            {
                if (playerTransform == null)
                {
                    playerTransform = other.transform;
                    playerCamera = other.GetComponentInChildren<Camera>();
                    FindHandBone();
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            // Handle touch-based range exit
            if (minGrabDistance <= 0f && other.CompareTag("Player"))
            {
                if (!isGrabbed)
                {
                    playerTransform = null;
                    handTransform = null;
                    playerCamera = null;
                }
            }
        }

        public void Grab()
        {
            if (isGrabbed || !CanGrabFromCurrentPosition()) return;

            // Check single grab mode
            if (!allowMultiGrab && currentlyGrabbed != null && currentlyGrabbed != this)
            {
                currentlyGrabbed.Release();
            }

            // Find player if needed
            if (playerTransform == null)
            {
                FindPlayer();
            }

            if (handTransform == null) return;

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
            transform.SetParent(handTransform);
            transform.localPosition = grabOffset;

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
                playerCamera = null;
            }

            OnReleased?.Invoke();
        }

        private bool CanGrabFromCurrentPosition()
        {
            if (playerTransform == null)
            {
                FindPlayer();
                if (playerTransform == null) return false;
            }

            // Use avatar position for distance check (not camera)
            float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);

            // Check if within grab range
            if (distanceToPlayer < minGrabDistance || distanceToPlayer > maxGrabDistance)
            {
                return false;
            }

            // For distance grabbing (max > min), also check if looking at object
            if (maxGrabDistance > minGrabDistance && playerCamera != null)
            {
                Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
                RaycastHit hit;

                if (Physics.Raycast(ray, out hit, maxGrabDistance))
                {
                    return hit.collider == col;
                }
                return false;
            }

            // For touch-only grabbing, just check range
            return true;
        }

        // IU3DInteractable implementation
        public void OnInteract()
        {
            if (isGrabbed)
            {
                Release();
            }
            else if (CanGrabFromCurrentPosition())
            {
                Grab();
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
            return isGrabbed || CanGrabFromCurrentPosition();
        }

        public int GetInteractionPriority()
        {
            if (isGrabbed) return 60; // High priority when already grabbed
            if (minGrabDistance <= 0f) return 50; // Touch grabbing gets standard priority
            return 40; // Distance grabbing gets slightly lower priority
        }

        public string GetInteractionPrompt()
        {
            return isGrabbed ? "Release" : "Grab";
        }

        // Public properties
        public bool IsGrabbed => isGrabbed;
        public bool IsInRange => isInRange;
        public bool IsAimedAt => isAimedAt;

        private void OnDestroy()
        {
            if (isGrabbed)
            {
                Release();
            }
        }
    }
}