using UnityEngine;
using UnityEngine.Events;
using Fusion;

namespace U3D
{
    /// <summary>
    /// Universal grabbable component for both near and far interactions
    /// Objects snap to the player's hand position when grabbed
    /// Distance-based grabbing uses avatar position, not camera position for third-person compatibility
    /// Supports both networked and non-networked modes automatically
    /// FIXED: Properly manages original physics state for throwable objects
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class U3DGrabbable : NetworkBehaviour, IU3DInteractable
    {
        [Header("Grab Detection Radius")]
        [Tooltip("Detection radius around the object (independent of collider size)")]
        [SerializeField] private float grabDetectionRadius = 1.0f;

        [Tooltip("Use radius-based detection instead of precise raycast")]
        [SerializeField] private bool useRadiusDetection = true;

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

        // Network state (only used if NetworkObject is present)
        [Networked] public bool NetworkIsGrabbed { get; set; }
        [Networked] public PlayerRef NetworkGrabbedBy { get; set; }

        // Components
        private Rigidbody rb; // Optional - only for throwable objects
        private U3DThrowable throwable; // Reference to throwable component if present
        private Collider col;
        private Transform originalParent;
        private Transform handTransform;
        private Transform playerTransform;
        private Camera playerCamera;
        private NetworkObject networkObject;

        // State
        private bool isGrabbed = false;
        private bool isInRange = false;
        private bool isAimedAt = false;
        private float lastAimCheckTime;
        private bool isNetworked = false;
        private bool hasRigidbody = false;

        // FIXED: Store original physics state once at initialization for throwable objects
        private bool originalWasKinematic;
        private bool originalUsedGravity;
        private bool hasStoredOriginalPhysicsState = false;

        // Static tracking for single grab mode
        private static U3DGrabbable currentlyGrabbed;

        private void Awake()
        {
            // Rigidbody is optional for grabbables
            rb = GetComponent<Rigidbody>();
            hasRigidbody = rb != null;

            // Check for throwable component
            throwable = GetComponent<U3DThrowable>();

            col = GetComponent<Collider>();
            originalParent = transform.parent;

            // Check if this object has networking support
            networkObject = GetComponent<NetworkObject>();
            isNetworked = networkObject != null;

            if (!isNetworked)
            {
                Debug.Log($"U3DGrabbable on '{name}' running in non-networked mode");
            }
        }

        private void Start()
        {
            // FIXED: Store original physics state after all components are initialized
            // This ensures we capture the intended "throwable" physics settings
            StoreOriginalPhysicsState();
        }

        private void StoreOriginalPhysicsState()
        {
            if (hasRigidbody && !hasStoredOriginalPhysicsState)
            {
                // If this object has a throwable component, we want to restore to "throwable ready" state
                // which means: isKinematic = false, useGravity = true (ready for physics)
                if (throwable != null)
                {
                    // For throwable objects, the original/desired state is physics-ready
                    originalWasKinematic = false; // Ready for physics when thrown
                    originalUsedGravity = true;   // Affected by gravity when thrown
                    Debug.Log($"U3DGrabbable: Stored throwable-ready physics state for '{name}'");
                }
                else
                {
                    // For non-throwable objects, store whatever the designer set
                    originalWasKinematic = rb.isKinematic;
                    originalUsedGravity = rb.useGravity;
                    Debug.Log($"U3DGrabbable: Stored original physics state for '{name}': kinematic={originalWasKinematic}, gravity={originalUsedGravity}");
                }

                hasStoredOriginalPhysicsState = true;
            }
        }

        private void Update()
        {
            // Only run Update logic on networked objects if they have authority, or always on non-networked objects
            if (isNetworked && !Object.HasStateAuthority) return;

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
            if (playerCamera == null || maxGrabDistance <= 0f || playerTransform == null)
            {
                isAimedAt = false;
                return;
            }

            bool wasAimedAt = isAimedAt;
            isAimedAt = false;

            float avatarDistance = Vector3.Distance(transform.position, playerTransform.position);

            if (avatarDistance >= minGrabDistance && avatarDistance <= maxGrabDistance)
            {
                if (useRadiusDetection)
                {
                    // NEW: Check if camera is looking toward object within detection radius
                    Vector3 cameraToObject = transform.position - playerCamera.transform.position;
                    float distanceToObject = cameraToObject.magnitude;

                    // Check if within max grab distance
                    if (distanceToObject <= maxGrabDistance)
                    {
                        // Check if generally looking in the object's direction
                        Vector3 cameraForward = playerCamera.transform.forward;
                        Vector3 directionToObject = cameraToObject.normalized;

                        // Calculate angle between camera direction and object direction
                        float angle = Vector3.Angle(cameraForward, directionToObject);

                        // Calculate maximum allowed angle based on distance and detection radius
                        // Closer objects allow wider angles, farther objects require more precision
                        float maxAllowedAngle = Mathf.Atan(grabDetectionRadius / distanceToObject) * Mathf.Rad2Deg;

                        if (angle <= maxAllowedAngle)
                        {
                            isAimedAt = true;
                            Debug.Log($"Grabbable '{name}' detected via radius - angle: {angle:F1}°, max: {maxAllowedAngle:F1}°, distance: {distanceToObject:F2}m");
                        }
                    }
                }
                else
                {
                    // Original precise raycast check
                    Vector3 avatarEyeLevel = playerTransform.position + Vector3.up * 1.5f;
                    Vector3 rayDirection = playerCamera.transform.forward;
                    Ray ray = new Ray(avatarEyeLevel, rayDirection);
                    RaycastHit hit;

                    if (Physics.Raycast(ray, out hit, maxGrabDistance))
                    {
                        if (hit.collider == col)
                        {
                            isAimedAt = true;
                            Debug.Log($"Grabbable '{name}' aimed at from avatar position - distance: {avatarDistance:F2}m");
                        }
                    }
                }
            }

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
            U3DPlayerController playerController = FindAnyObjectByType<U3DPlayerController>();
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

            handTransform = null; // Reset first

            if (!string.IsNullOrEmpty(handBoneName))
            {
                Transform[] allTransforms = playerTransform.GetComponentsInChildren<Transform>();
                foreach (Transform t in allTransforms)
                {
                    // FIXED: Avoid camera-related transforms to prevent camera interference
                    if (t.name == handBoneName &&
                        !t.name.Contains("Camera") &&
                        !t.name.Contains("Pivot") &&
                        t != playerCamera?.transform)
                    {
                        handTransform = t;
                        Debug.Log($"Found hand bone: {handBoneName} on transform: {t.name}");
                        break;
                    }
                }
            }

            // FIXED: Safe fallback - use player root, NOT camera or camera pivot
            if (handTransform == null)
            {
                // Create a dedicated hand anchor that won't interfere with camera
                GameObject handAnchor = GameObject.Find($"{playerTransform.name}_HandAnchor");
                if (handAnchor == null)
                {
                    handAnchor = new GameObject($"{playerTransform.name}_HandAnchor");
                    handAnchor.transform.SetParent(playerTransform);
                    handAnchor.transform.localPosition = Vector3.forward * 0.5f + Vector3.up * 1.2f; // In front of chest
                    handAnchor.transform.localRotation = Quaternion.identity;
                }
                handTransform = handAnchor.transform;
                Debug.Log($"Created safe hand anchor for grabbable objects at: {handTransform.localPosition}");
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

            // Authority check for networked objects
            if (isNetworked && !Object.HasStateAuthority) return;

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

            // Update network state if networked
            if (isNetworked)
            {
                NetworkIsGrabbed = true;
                if (Runner != null && Runner.LocalPlayer != null)
                {
                    NetworkGrabbedBy = Runner.LocalPlayer;
                }
            }

            // FIXED: Let U3DThrowable handle its own physics during grab
            // We don't modify rigidbody settings directly anymore
            if (throwable != null)
            {
                // Throwable component will handle physics state via its OnObjectGrabbed callback
                Debug.Log($"U3DGrabbable: Grabbed throwable object '{name}' - throwable component handles physics");
            }
            else if (hasRigidbody)
            {
                // For non-throwable objects with rigidbody, make kinematic while grabbed
                rb.isKinematic = true;
                rb.useGravity = false;
                Debug.Log($"U3DGrabbable: Made non-throwable rigidbody kinematic for '{name}'");
            }

            // FIXED: Set collider to trigger AND exclude from camera collision to prevent camera snapping
            col.isTrigger = true;

            // Move to a layer that camera collision ignores, or disable collision detection temporarily
            int originalLayer = gameObject.layer;
            SetLayerRecursively(gameObject, LayerMask.NameToLayer("Ignore Raycast"));

            // Store original layer for restoration on release
            PlayerPrefs.SetInt($"U3DGrabbable_OriginalLayer_{gameObject.GetInstanceID()}", originalLayer);

            // Parent to hand
            transform.SetParent(handTransform);
            transform.localPosition = grabOffset;

            OnGrabbed?.Invoke();

            Debug.Log($"U3DGrabbable: Object '{name}' grabbed and excluded from camera collision detection");
        }

        public void Release()
        {
            if (!isGrabbed) return;

            // Authority check for networked objects
            if (isNetworked && !Object.HasStateAuthority) return;

            isGrabbed = false;
            if (currentlyGrabbed == this)
            {
                currentlyGrabbed = null;
            }

            // Update network state if networked
            if (isNetworked)
            {
                NetworkIsGrabbed = false;
                NetworkGrabbedBy = default(PlayerRef);
            }

            // FIXED: Restore to original "throwable-ready" physics state
            if (throwable != null)
            {
                // Let throwable component handle its own physics restoration via OnObjectReleased callback
                // It will activate physics for throwing, then manage sleep cycles properly
                Debug.Log($"U3DGrabbable: Released throwable object '{name}' - throwable component handles physics");
            }
            else if (hasRigidbody && hasStoredOriginalPhysicsState)
            {
                // For non-throwable objects, restore their original state
                rb.isKinematic = originalWasKinematic;
                rb.useGravity = originalUsedGravity;
                Debug.Log($"U3DGrabbable: Restored original physics state for non-throwable '{name}': kinematic={originalWasKinematic}, gravity={originalUsedGravity}");
            }

            // FIXED: Restore collider and layer settings
            col.isTrigger = false;

            // Restore original layer
            int originalLayer = PlayerPrefs.GetInt($"U3DGrabbable_OriginalLayer_{gameObject.GetInstanceID()}", 0);
            SetLayerRecursively(gameObject, originalLayer);
            PlayerPrefs.DeleteKey($"U3DGrabbable_OriginalLayer_{gameObject.GetInstanceID()}");

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

            Debug.Log($"U3DGrabbable: Object '{name}' released and restored to original collision layer");
        }

        private void SetLayerRecursively(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        private bool CanGrabFromCurrentPosition()
        {
            if (playerTransform == null)
            {
                FindPlayer();
                if (playerTransform == null) return false;
            }

            float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);

            if (distanceToPlayer < minGrabDistance || distanceToPlayer > maxGrabDistance)
            {
                return false;
            }

            // For distance grabbing, check if looking at object
            if (maxGrabDistance > minGrabDistance && playerCamera != null)
            {
                if (useRadiusDetection)
                {
                    // NEW: Use radius-based detection
                    Vector3 cameraToObject = transform.position - playerCamera.transform.position;
                    float distanceToObject = cameraToObject.magnitude;

                    if (distanceToObject <= maxGrabDistance)
                    {
                        Vector3 cameraForward = playerCamera.transform.forward;
                        Vector3 directionToObject = cameraToObject.normalized;
                        float angle = Vector3.Angle(cameraForward, directionToObject);
                        float maxAllowedAngle = Mathf.Atan(grabDetectionRadius / distanceToObject) * Mathf.Rad2Deg;

                        return angle <= maxAllowedAngle;
                    }
                    return false;
                }
                else
                {
                    // Original precise raycast
                    Vector3 avatarEyeLevel = playerTransform.position + Vector3.up * 1.5f;
                    Vector3 rayDirection = playerCamera.transform.forward;
                    Ray ray = new Ray(avatarEyeLevel, rayDirection);
                    RaycastHit hit;

                    if (Physics.Raycast(ray, out hit, maxGrabDistance))
                    {
                        return hit.collider == col;
                    }
                    return false;
                }
            }

            return true;
        }

        private void OnDrawGizmosSelected()
        {
            if (useRadiusDetection)
            {
                Gizmos.color = Color.yellow;
                Gizmos.color = new Color(1, 1, 0, 0.3f);
                Gizmos.DrawSphere(transform.position, grabDetectionRadius);

                // Draw wire sphere for radius outline
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(transform.position, grabDetectionRadius);
            }
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
        public bool IsNetworked => isNetworked;
        public bool HasRigidbody => hasRigidbody;
        public bool HasThrowable => throwable != null;

        private void OnDestroy()
        {
            if (isGrabbed)
            {
                Release();
            }
        }

        // Override NetworkBehaviour methods for non-networked compatibility
        public override void Spawned()
        {
            if (!isNetworked) return;
            // Networked initialization if needed
        }

        // Debug information for development
        [System.Serializable]
        public struct PhysicsDebugInfo
        {
            public bool hasStoredState;
            public bool originalKinematic;
            public bool originalGravity;
            public bool currentKinematic;
            public bool currentGravity;
            public bool hasThrowableComponent;
        }

        public PhysicsDebugInfo GetPhysicsDebugInfo()
        {
            return new PhysicsDebugInfo
            {
                hasStoredState = hasStoredOriginalPhysicsState,
                originalKinematic = originalWasKinematic,
                originalGravity = originalUsedGravity,
                currentKinematic = hasRigidbody ? rb.isKinematic : false,
                currentGravity = hasRigidbody ? rb.useGravity : false,
                hasThrowableComponent = throwable != null
            };
        }
    }
}