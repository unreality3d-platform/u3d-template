using UnityEngine;
using UnityEngine.Events;
using Fusion;

namespace U3D
{
    /// <summary>
    /// Universal grabbable component supporting both per-player instances and shared objects
    /// HYBRID MODE: Automatically detects whether to use instance mode or shared authority transfer
    /// Instance Mode: Each player has their own copy (collaborative experience)
    /// Shared Mode: Single object with authority transfer (traditional approach)
    /// Maintains backward compatibility with existing implementations
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

        [Tooltip("Called when object is smart-dropped to a surface")]
        public UnityEvent OnSmartDrop;

        [Tooltip("Called when object is recovered due to safety bounds")]
        public UnityEvent OnSafetyRecovery;

        // Network state (only used if NetworkObject is present)
        [Networked] public bool NetworkIsGrabbed { get; set; }
        [Networked] public PlayerRef NetworkGrabbedBy { get; set; }

        // HYBRID MODE: Instance vs Shared behavior
        private bool isInstanceMode = false;
        private PlayerRef instanceOwner = PlayerRef.None;
        private bool canLocalPlayerInteract = true;

        // SHARED MODE: Authority transfer RPCs (only used in shared mode)
        [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
        public void RPC_RequestGrab(PlayerRef requester, RpcInfo info = default)
        {
            if (isInstanceMode)
            {
                Debug.LogWarning($"Authority transfer RPC called on instance mode object '{name}' - this should not happen");
                return;
            }

            Debug.Log($"Authority transfer request: {requester} wants to grab '{name}' (current authority: {Object.StateAuthority})");

            // Transfer authority to the requesting player
            if (Object.StateAuthority != requester)
            {
                Object.RequestStateAuthority();
                Debug.Log($"Transferring authority of '{name}' from {Object.StateAuthority} to {requester}");
            }

            // Execute grab on authority holder
            Grab();
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
        public void RPC_RequestRelease(PlayerRef requester, RpcInfo info = default)
        {
            if (isInstanceMode)
            {
                Debug.LogWarning($"Release RPC called on instance mode object '{name}' - this should not happen");
                return;
            }

            Debug.Log($"Release request from {requester} for '{name}'");

            // Only allow release from the player who grabbed it
            if (NetworkGrabbedBy == requester)
            {
                Release();
            }
            else
            {
                Debug.LogWarning($"Player {requester} tried to release '{name}' but it's grabbed by {NetworkGrabbedBy}");
            }
        }

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

        // Hidden smart drop and safety recovery - enabled by default with optimal settings
        private const bool ENABLE_SMART_DROP = true;
        private const bool ENABLE_SAFETY_RECOVERY = true;
        private const float SAFETY_FLOOR_Y = -50f;
        private const float MAX_DISTANCE_FROM_SPAWN = 1000f;

        // Safety recovery state
        private Vector3 spawnPosition;
        private Quaternion spawnRotation;
        private bool hasRecordedSpawn = false;

        // FIXED: Store original physics state once at initialization for throwable objects
        private bool originalWasKinematic;
        private bool originalUsedGravity;
        private bool hasStoredOriginalPhysicsState = false;

        // Static tracking for single grab mode (only used in shared mode)
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
            // Record spawn position for safety recovery
            RecordSpawnPosition();

            // FIXED: Store original physics state after all components are initialized
            // This ensures we capture the intended "throwable" physics settings
            StoreOriginalPhysicsState();

            // Determine mode based on object setup
            DetectOperatingMode();
        }

        /// <summary>
        /// HYBRID MODE: Detect whether this object should use instance mode or shared mode
        /// </summary>
        private void DetectOperatingMode()
        {
            if (!isNetworked)
            {
                isInstanceMode = false;
                canLocalPlayerInteract = true;
                Debug.Log($"U3DGrabbable '{name}': Non-networked mode");
                return;
            }

            // Check if this object was spawned with a specific player as authority (instance mode)
            if (Object.InputAuthority != PlayerRef.None)
            {
                isInstanceMode = true;
                instanceOwner = Object.InputAuthority;
                canLocalPlayerInteract = (Runner.LocalPlayer == instanceOwner);
                Debug.Log($"U3DGrabbable '{name}': Instance mode - owned by player {instanceOwner}");
            }
            else
            {
                isInstanceMode = false;
                canLocalPlayerInteract = true;
                Debug.Log($"U3DGrabbable '{name}': Shared mode - authority transfer enabled");
            }
        }

        /// <summary>
        /// PUBLIC API: Set instance mode (called by U3DInstanceManager)
        /// </summary>
        public void SetInstanceMode(bool instanceMode, PlayerRef owner)
        {
            isInstanceMode = instanceMode;
            instanceOwner = owner;
            canLocalPlayerInteract = !instanceMode || (Runner != null && Runner.LocalPlayer == owner);

            Debug.Log($"U3DGrabbable '{name}': Set to {(instanceMode ? "instance" : "shared")} mode, owner: {owner}, can interact: {canLocalPlayerInteract}");
        }

        private void RecordSpawnPosition()
        {
            if (!hasRecordedSpawn)
            {
                spawnPosition = transform.position;
                spawnRotation = transform.rotation;
                hasRecordedSpawn = true;
                Debug.Log($"U3DGrabbable: Recorded spawn position for '{name}' at {spawnPosition}");
            }
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
            // HYBRID MODE: Only run Update logic if local player can interact with this object
            if (!canLocalPlayerInteract) return;

            // Authority check for networked shared objects (instances run on all clients)
            if (isNetworked && !isInstanceMode && !Object.HasStateAuthority) return;

            UpdatePlayerProximity();

            // Check aim only for distance grabbing (when max distance > 0)
            if (maxGrabDistance > 0f && !isGrabbed && Time.time - lastAimCheckTime > 0.1f)
            {
                lastAimCheckTime = Time.time;
                CheckIfAimedAt();
            }

            // Safety recovery check (only if enabled and not grabbed and has authority)
            if (ENABLE_SAFETY_RECOVERY && !isGrabbed && hasRecordedSpawn)
            {
                if (!isNetworked || isInstanceMode || Object.HasStateAuthority)
                {
                    CheckSafetyBounds();
                }
            }
        }

        private void CheckSafetyBounds()
        {
            bool needsRecovery = false;
            string reason = "";

            // Check if fallen through world
            if (transform.position.y < SAFETY_FLOOR_Y)
            {
                needsRecovery = true;
                reason = $"fell below safety floor (Y: {transform.position.y:F1})";
            }
            // Check if too far from spawn
            else if (Vector3.Distance(transform.position, spawnPosition) > MAX_DISTANCE_FROM_SPAWN)
            {
                needsRecovery = true;
                reason = $"moved too far from spawn ({Vector3.Distance(transform.position, spawnPosition):F1}m)";
            }

            if (needsRecovery)
            {
                PerformSafetyRecovery(reason);
            }
        }

        private void PerformSafetyRecovery(string reason)
        {
            // Authority check for networked objects (instances can always recover)
            if (isNetworked && !isInstanceMode && !Object.HasStateAuthority) return;

            Debug.LogWarning($"U3DGrabbable: Object '{name}' {reason} - performing safety recovery");

            // Reset to spawn position
            transform.position = spawnPosition;
            transform.rotation = spawnRotation;

            // Reset physics state
            if (hasRigidbody && hasStoredOriginalPhysicsState)
            {
                if (throwable != null)
                {
                    // Let throwable component handle its physics
                    rb.isKinematic = false;
                    rb.useGravity = true;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                else
                {
                    // Restore original state for non-throwables
                    rb.isKinematic = originalWasKinematic;
                    rb.useGravity = originalUsedGravity;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }

            // Ensure proper collider state
            col.isTrigger = false;

            OnSafetyRecovery?.Invoke();
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

            // HYBRID MODE: Instance vs Shared authority handling
            if (isNetworked)
            {
                if (isInstanceMode)
                {
                    // Instance mode: only owner can grab, no authority transfer needed
                    if (!canLocalPlayerInteract)
                    {
                        Debug.Log($"Cannot grab instance '{name}' - not owned by local player");
                        return;
                    }
                }
                else
                {
                    // Shared mode: use RPC system for authority management
                    if (!Object.HasStateAuthority)
                    {
                        // Request authority transfer via RPC
                        if (Runner != null && Runner.LocalPlayer != PlayerRef.None)
                        {
                            RPC_RequestGrab(Runner.LocalPlayer);
                            Debug.Log($"Requesting grab authority for '{name}' from player {Runner.LocalPlayer}");
                            return; // Exit here - RPC will call Grab() on the authority holder
                        }
                    }
                }
            }

            // Check single grab mode (only for shared objects)
            if (!isInstanceMode && !allowMultiGrab && currentlyGrabbed != null && currentlyGrabbed != this)
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
            if (!isInstanceMode) // Only set static tracking for shared objects
            {
                currentlyGrabbed = this;
            }

            // Update network state if networked
            if (isNetworked)
            {
                NetworkIsGrabbed = true;
                if (Runner != null && Runner.LocalPlayer != PlayerRef.None)
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

            // HYBRID MODE: Instance vs Shared authority handling
            if (isNetworked)
            {
                if (isInstanceMode)
                {
                    // Instance mode: only owner can release, no authority transfer needed
                    if (!canLocalPlayerInteract)
                    {
                        Debug.Log($"Cannot release instance '{name}' - not owned by local player");
                        return;
                    }
                }
                else
                {
                    // Shared mode: use RPC system for authority management
                    if (!Object.HasStateAuthority)
                    {
                        // Request release via RPC
                        if (Runner != null && Runner.LocalPlayer != PlayerRef.None)
                        {
                            RPC_RequestRelease(Runner.LocalPlayer);
                            Debug.Log($"Requesting release authority for '{name}' from player {Runner.LocalPlayer}");
                            return; // Exit here - RPC will call Release() on the authority holder
                        }
                    }
                }
            }

            isGrabbed = false;
            if (!isInstanceMode && currentlyGrabbed == this) // Only clear static tracking for shared objects
            {
                currentlyGrabbed = null;
            }

            // Update network state if networked
            if (isNetworked)
            {
                NetworkIsGrabbed = false;
                NetworkGrabbedBy = default(PlayerRef);
            }

            // Store current position for smart drop calculation
            Vector3 releasePosition = transform.position;

            // Unparent first
            transform.SetParent(originalParent);

            // FIXED: Restore collider and layer settings
            col.isTrigger = false;

            // Restore original layer
            int originalLayer = PlayerPrefs.GetInt($"U3DGrabbable_OriginalLayer_{gameObject.GetInstanceID()}", 0);
            SetLayerRecursively(gameObject, originalLayer);
            PlayerPrefs.DeleteKey($"U3DGrabbable_OriginalLayer_{gameObject.GetInstanceID()}");

            // ENHANCED: Smart drop behavior for non-throwable objects
            if (throwable == null && ENABLE_SMART_DROP)
            {
                PerformSmartDrop(releasePosition);
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

        private void PerformSmartDrop(Vector3 releasePosition)
        {
            // Use the object's collider bounds to determine proper drop position
            Bounds objectBounds = col.bounds;

            // Cast from the bottom of the object's collider
            Vector3 raycastStart = new Vector3(objectBounds.center.x, objectBounds.min.y, objectBounds.center.z);
            float maxDropDistance = 50f; // Reasonable search distance

            // Cast downward from bottom of collider to find surface
            if (Physics.Raycast(raycastStart, Vector3.down, out RaycastHit hit, maxDropDistance))
            {
                // Position object so its bottom collider edge rests on the surface
                float yOffset = objectBounds.center.y - objectBounds.min.y; // Distance from object center to bottom
                Vector3 surfacePosition = new Vector3(objectBounds.center.x, hit.point.y + yOffset, objectBounds.center.z);
                transform.position = surfacePosition;

                Debug.Log($"U3DGrabbable: Smart drop placed '{name}' on surface '{hit.collider.name}' using collider bounds");
                OnSmartDrop?.Invoke();
            }
            else
            {
                // No surface found within range - leave at release position
                transform.position = releasePosition;
                Debug.Log($"U3DGrabbable: No surface found for smart drop - left '{name}' at release position");
            }
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
            // HYBRID MODE: Instance mode interaction check
            if (isInstanceMode && !canLocalPlayerInteract)
            {
                return false; // Can't interact with other players' instances
            }

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

            // Draw safety bounds if recorded spawn position exists
            if (hasRecordedSpawn)
            {
                // Draw spawn position
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(spawnPosition, 0.5f);

                // Draw safety radius
                Gizmos.color = new Color(1, 0, 0, 0.1f);
                Gizmos.DrawSphere(spawnPosition, MAX_DISTANCE_FROM_SPAWN);
            }

            // HYBRID MODE: Visual indication of mode
            if (Application.isPlaying && isNetworked)
            {
                if (isInstanceMode)
                {
                    // Draw instance mode indicator
                    Gizmos.color = canLocalPlayerInteract ? Color.green : Color.red;
                    Gizmos.DrawWireCube(transform.position + Vector3.up * 2f, Vector3.one * 0.3f);
                }
                else
                {
                    // Draw shared mode indicator
                    Gizmos.color = Color.blue;
                    Gizmos.DrawWireSphere(transform.position + Vector3.up * 2f, 0.2f);
                }
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
        public Vector3 SpawnPosition => spawnPosition;
        public Quaternion SpawnRotation => spawnRotation;
        public bool IsInstanceMode => isInstanceMode;
        public PlayerRef InstanceOwner => instanceOwner;
        public bool CanLocalPlayerInteract => canLocalPlayerInteract;

        // Public method to update spawn position (useful for dynamic placement)
        public void UpdateSpawnPosition(Vector3 newPosition, Quaternion newRotation)
        {
            spawnPosition = newPosition;
            spawnRotation = newRotation;
            hasRecordedSpawn = true;
            Debug.Log($"U3DGrabbable: Updated spawn position for '{name}' to {spawnPosition}");
        }

        // Public method to manually trigger safety recovery
        public void TriggerSafetyRecovery()
        {
            if (ENABLE_SAFETY_RECOVERY && hasRecordedSpawn)
            {
                PerformSafetyRecovery("manual trigger");
            }
        }

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
            // Networked initialization handled in DetectOperatingMode
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
            public Vector3 spawnPosition;
            public float distanceFromSpawn;
            public bool isInstanceMode;
            public bool canLocalPlayerInteract;
            public PlayerRef instanceOwner;
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
                hasThrowableComponent = throwable != null,
                spawnPosition = spawnPosition,
                distanceFromSpawn = hasRecordedSpawn ? Vector3.Distance(transform.position, spawnPosition) : 0f,
                isInstanceMode = isInstanceMode,
                canLocalPlayerInteract = canLocalPlayerInteract,
                instanceOwner = instanceOwner
            };
        }
    }
}