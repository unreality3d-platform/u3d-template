using UnityEngine;
using UnityEngine.Events;

namespace U3D
{
    /// <summary>
    /// Makes objects grabbable from a distance using point and click
    /// Objects snap to the player's hand position when grabbed
    /// </summary>
    [AddComponentMenu("U3D/Interactions/U3D Grabbable Far")]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Collider))]
    public class U3DGrabbableFar : MonoBehaviour, IU3DInteractable
    {
        [Header("Distance Grab Configuration")]
        [Tooltip("Minimum distance to grab from")]
        [SerializeField] private float minGrabDistance = 0.5f;

        [Tooltip("Maximum distance to grab from")]
        [SerializeField] private float maxGrabDistance = 10f;

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

        [Tooltip("Called when player aims at this object")]
        public UnityEvent OnAimEnter;

        [Tooltip("Called when player stops aiming at this object")]
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
        private bool isAimedAt = false;
        private bool wasKinematic;
        private bool usedGravity;
        private float lastAimCheckTime;

        // Static tracking for single grab mode
        private static U3DGrabbableFar currentlyGrabbed;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            col = GetComponent<Collider>();
            originalParent = transform.parent;
        }

        private void Update()
        {
            // Check if we're being aimed at (for events)
            if (!isGrabbed && Time.time - lastAimCheckTime > 0.1f)
            {
                lastAimCheckTime = Time.time;
                CheckIfAimedAt();
            }
        }

        private void CheckIfAimedAt()
        {
            if (playerCamera == null)
            {
                playerCamera = Camera.main;
                if (playerCamera == null) return;
            }

            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            RaycastHit hit;

            bool wasAimedAt = isAimedAt;
            isAimedAt = false;

            if (Physics.Raycast(ray, out hit, maxGrabDistance))
            {
                if (hit.collider == col)
                {
                    float distance = Vector3.Distance(playerCamera.transform.position, transform.position);
                    if (distance >= minGrabDistance && distance <= maxGrabDistance)
                    {
                        isAimedAt = true;
                    }
                }
            }

            // Fire events
            if (isAimedAt && !wasAimedAt)
            {
                OnAimEnter?.Invoke();
            }
            else if (!isAimedAt && wasAimedAt)
            {
                OnAimExit?.Invoke();
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

            // Find player if needed
            if (playerTransform == null)
            {
                U3DPlayerController playerController = Object.FindAnyObjectByType<U3DPlayerController>();
                if (playerController != null)
                {
                    playerTransform = playerController.transform;
                    FindHandBone();
                }
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

            OnReleased?.Invoke();
        }

        private bool CanGrabFromCurrentPosition()
        {
            if (playerCamera == null)
            {
                playerCamera = Camera.main;
                if (playerCamera == null) return false;
            }

            // Check if we're looking at this object and within range
            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, maxGrabDistance))
            {
                if (hit.collider == col)
                {
                    float distance = Vector3.Distance(playerCamera.transform.position, transform.position);
                    return distance >= minGrabDistance && distance <= maxGrabDistance;
                }
            }

            return false;
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
            // Not used for distance grabbing
        }

        public void OnPlayerExitRange()
        {
            // Not used for distance grabbing
        }

        public bool CanInteract()
        {
            return isGrabbed || CanGrabFromCurrentPosition();
        }

        public int GetInteractionPriority()
        {
            return 40; // Slightly lower than near grab
        }

        public string GetInteractionPrompt()
        {
            return isGrabbed ? "Release" : "Grab";
        }

        // Public properties
        public bool IsGrabbed => isGrabbed;
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