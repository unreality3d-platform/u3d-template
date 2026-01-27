using Fusion;
using Fusion.Addons.Physics;
using UnityEngine;
using UnityEngine.Events;

namespace U3D
{
    /// <summary>
    /// FIXED: Reliable authority management for Shared Mode grab/throw system
    /// Prevents race conditions and ensures deterministic state synchronization
    /// Enhanced with remappable interaction keys using Unity Input System
    /// Input handling delegated to U3DInteractionManager to prevent double-input
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

        [Header("Interaction Settings")]
        [Tooltip("Key to trigger grab (remappable) - shown in UI prompt")]
        [SerializeField] private KeyCode grabKey = KeyCode.R;

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

        [Tooltip("Called when grab attempt fails")]
        public UnityEvent OnGrabFailed;

        // FIXED: Proper network state management for Shared Mode
        [Networked] public bool NetworkIsGrabbed { get; set; }
        [Networked] public PlayerRef NetworkGrabbedBy { get; set; }
        [Networked] public byte NetworkGrabState { get; set; } // 0=Free, 1=Grabbing, 2=Grabbed

        // Components
        private Rigidbody rb;
        private NetworkRigidbody3D networkRb3D;
        private U3DThrowable throwable;
        private Collider col;
        private Transform originalParent;
        private Transform handTransform;
        private Transform playerTransform;
        private Camera playerCamera;
        private NetworkObject networkObject;
        private U3DPlayerController playerController;

        // Deterministic state management
        private GrabState localGrabState = GrabState.Free;
        private bool isInRange = false;
        private bool isAimedAt = false;
        private float lastAimCheckTime;
        private bool isNetworked = false;
        private bool hasRigidbody = false;
        private bool hasNetworkRb3D = false;

        // Authority management - FIXED for race conditions
        private bool isRequestingAuthority = false;
        private float authorityRequestTime = 0f;
        private const float AUTHORITY_REQUEST_TIMEOUT = 2f;

        // Safety recovery state
        private Vector3 spawnPosition;
        private Quaternion spawnRotation;
        private bool hasRecordedSpawn = false;

        // Physics state management - FIXED
        private bool originalWasKinematic;
        private bool originalUsedGravity;
        private bool hasStoredOriginalPhysicsState = false;

        // Static tracking for single grab mode
        private static U3DGrabbable currentlyGrabbed;

        public enum GrabState : byte
        {
            Free = 0,           // Available for grabbing
            Requesting = 1,     // Authority request in progress
            Grabbed = 2,        // Successfully grabbed
            Released = 3        // Recently released (cooldown)
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            hasRigidbody = rb != null;
            networkRb3D = GetComponent<NetworkRigidbody3D>();
            hasNetworkRb3D = networkRb3D != null;
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
            RecordSpawnPosition();
            StoreOriginalPhysicsState();
            CheckForInputConflicts();
        }

        private void Update()
        {
            UpdatePlayerProximity();

            // Check aim only for distance grabbing
            if (maxGrabDistance > 0f && !IsCurrentlyGrabbed() && Time.time - lastAimCheckTime > 0.1f)
            {
                lastAimCheckTime = Time.time;
                CheckIfAimedAt();
            }

            // NOTE: Direct input handling removed - U3DInteractionManager handles input
            // via IU3DInteractable.OnInteract() to prevent double-input issues

            // Timeout authority requests to prevent hanging
            if (isRequestingAuthority && Time.time - authorityRequestTime > AUTHORITY_REQUEST_TIMEOUT)
            {
                Debug.LogWarning($"Authority request timeout for {name}");
                isRequestingAuthority = false;
                OnGrabFailed?.Invoke();
            }
        }

        private void CheckForInputConflicts()
        {
            // Check for other interaction components and auto-remap if needed
            var kickable = GetComponent<U3DKickable>();
            if (kickable != null && kickable.KickKey == grabKey)
            {
                // If kickable uses the same key, remap grabbable to F
                if (grabKey == KeyCode.R)
                {
                    grabKey = KeyCode.F;
                    Debug.Log($"U3DGrabbable: Auto-remapped grab key to {grabKey} due to kickable component");
                }
            }
        }

        public override void Spawned()
        {
            if (!isNetworked) return;
            // Reset network state on spawn
            NetworkGrabState = (byte)GrabState.Free;
            NetworkIsGrabbed = false;
            NetworkGrabbedBy = PlayerRef.None;
        }

        // FIXED: Reliable authority change handling
        public void OnStateAuthorityChanged()
        {
            if (!isNetworked) return;

            if (Object.HasStateAuthority && isRequestingAuthority)
            {
                // Successfully got authority
                isRequestingAuthority = false;
                PerformGrab();
            }
            else if (!Object.HasStateAuthority && localGrabState == GrabState.Grabbed)
            {
                // Lost authority while grabbed - force release
                PerformLocalRelease();
            }
        }

        // Networked property change detection
        public override void Render()
        {
            base.Render();

            // Only do this if we have a hand, are grabbed, and a networked rigidbody is present
            if (localGrabState == GrabState.Grabbed && handTransform != null && hasNetworkRb3D)
            {
                // Smoothly interpolate toward the hand's position/rotation
                transform.position = Vector3.Lerp(
                    transform.position,
                    handTransform.position + handTransform.TransformVector(grabOffset),
                    0.5f // tweak this smoothing factor (0.5 = medium smooth, 1.0 = instant snap)
                );

                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    handTransform.rotation,
                    0.5f
                );
            }
        }

        // FIXED: Deterministic grab attempt
        public void Grab()
        {
            if (!CanAttemptGrab()) return;

            if (!isNetworked)
            {
                // Non-networked mode - immediate grab
                PerformGrab();
                return;
            }

            // Networked mode - proper authority handling
            if (Object.HasStateAuthority)
            {
                // Already have authority - grab immediately
                PerformGrab();
            }
            else if (NetworkGrabState == (byte)GrabState.Free && !isRequestingAuthority)
            {
                // Request authority for free object
                RequestGrabAuthority();
            }
            else
            {
                // Object is not available
                OnGrabFailed?.Invoke();
            }
        }

        private void RequestGrabAuthority()
        {
            if (isRequestingAuthority) return;

            // Set network state to prevent other grab attempts
            if (Object.HasStateAuthority || NetworkGrabState == (byte)GrabState.Free)
            {
                NetworkGrabState = (byte)GrabState.Requesting;
                isRequestingAuthority = true;
                authorityRequestTime = Time.time;

                // Request state authority
                Object.RequestStateAuthority();
            }
            else
            {
                OnGrabFailed?.Invoke();
            }
        }

        private void PerformGrab()
        {
            // Single grab mode - release any currently grabbed object
            if (currentlyGrabbed != null && currentlyGrabbed != this)
            {
                currentlyGrabbed.Release();
            }

            // Find player if needed
            if (playerTransform == null)
            {
                FindPlayer();
            }

            if (handTransform == null)
            {
                // As a safety net, make sure there's always a hand anchor
                FindHandBone();
                if (handTransform == null) return;
            }

            // --- NEW: tell Fusion not to fight our parenting while grabbed ---
            if (networkRb3D != null)
            {
                networkRb3D.SyncParent = false;
            }

            // Update network state
            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkGrabState = (byte)GrabState.Grabbed;
                NetworkIsGrabbed = true;
                if (Runner != null && Runner.LocalPlayer != null)
                {
                    NetworkGrabbedBy = Runner.LocalPlayer;
                }
            }

            // Update local state
            localGrabState = GrabState.Grabbed;
            currentlyGrabbed = this;
            isRequestingAuthority = false;

            // Handle physics
            if (throwable != null)
            {
                // Throwable manages its own physics
            }
            else if (hasRigidbody)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            // Parent to hand
            transform.SetParent(handTransform);
            transform.localPosition = grabOffset;

            OnGrabbed?.Invoke();
        }

        public void Release()
        {
            if (localGrabState != GrabState.Grabbed) return;

            // For networked objects, only release if we have authority
            if (isNetworked && !Object.HasStateAuthority) return;

            PerformRelease();
        }

        private void PerformRelease()
        {
            // Update network state
            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkGrabState = (byte)GrabState.Free;
                NetworkIsGrabbed = false;
                NetworkGrabbedBy = PlayerRef.None;
            }

            // --- NEW: re-enable Fusion's parent syncing after release ---
            if (networkRb3D != null)
            {
                networkRb3D.SyncParent = true;
            }

            PerformLocalRelease();
        }

        private void PerformLocalRelease()
        {
            localGrabState = GrabState.Free;
            if (currentlyGrabbed == this)
            {
                currentlyGrabbed = null;
            }

            // Unparent and restore physics
            PerformDirectUnparenting();

            // Handle physics restoration
            if (throwable != null)
            {
                // Let throwable component handle physics via OnObjectReleased callback
            }
            else if (hasRigidbody && hasStoredOriginalPhysicsState)
            {
                // For non-throwable objects, restore original state
                rb.isKinematic = originalWasKinematic;
                rb.useGravity = originalUsedGravity;
            }

            // Clear references if not in range
            if (!isInRange)
            {
                ClearPlayerReferences();
            }

            OnReleased?.Invoke();
        }

        // Handle remote player grabs
        private void OnRemoteGrab()
        {
            // Visual feedback for remote grab
            OnGrabbed?.Invoke();
        }

        private void OnRemoteRelease()
        {
            // Visual feedback for remote release
            OnReleased?.Invoke();
        }

        private void PerformDirectParenting()
        {
            // Set collider to trigger and change layer while grabbed
            col.isTrigger = true;
            int originalLayer = gameObject.layer;
            SetLayerRecursively(gameObject, LayerMask.NameToLayer("Ignore Raycast"));
            PlayerPrefs.SetInt($"U3DGrabbable_OriginalLayer_{gameObject.GetInstanceID()}", originalLayer);

            // Parent to hand
            transform.SetParent(handTransform);
            transform.localPosition = grabOffset;
        }

        private void PerformDirectUnparenting()
        {
            // Unparent first
            transform.SetParent(originalParent);

            // Restore collider and layer settings
            col.isTrigger = false;
            int originalLayer = PlayerPrefs.GetInt($"U3DGrabbable_OriginalLayer_{gameObject.GetInstanceID()}", 0);
            SetLayerRecursively(gameObject, originalLayer);
            PlayerPrefs.DeleteKey($"U3DGrabbable_OriginalLayer_{gameObject.GetInstanceID()}");
        }

        private void SetLayerRecursively(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        private bool CanAttemptGrab()
        {
            // Check if object is already grabbed
            if (IsCurrentlyGrabbed()) return false;

            // Check if we can grab from current position
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
                return CheckAimingAtObject();
            }

            return true;
        }

        private bool IsCurrentlyGrabbed()
        {
            if (isNetworked && Object != null && Object.IsValid)
            {
                return NetworkIsGrabbed || NetworkGrabState == (byte)GrabState.Grabbed;
            }
            return localGrabState == GrabState.Grabbed;
        }

        private void FindPlayer()
        {
            // Just grab the first available U3DPlayerController in the scene
            playerController = FindAnyObjectByType<U3DPlayerController>();

            if (playerController != null)
            {
                playerTransform = playerController.transform;
                playerCamera = playerController.GetComponentInChildren<Camera>();
                FindHandBone();
            }
            else
            {
                // No player found, clear references
                playerTransform = null;
                playerCamera = null;
                handTransform = null;
            }
        }

        private void FindHandBone()
        {
            if (playerTransform == null) return;

            handTransform = null;

            if (!string.IsNullOrEmpty(handBoneName))
            {
                Transform[] allTransforms = playerTransform.GetComponentsInChildren<Transform>();
                foreach (Transform t in allTransforms)
                {
                    if (t.name == handBoneName && !t.name.Contains("Camera") && t != playerCamera?.transform)
                    {
                        handTransform = t;
                        break;
                    }
                }
            }

            // Safe fallback - use player root with offset
            if (handTransform == null)
            {
                GameObject handAnchor = GameObject.Find($"{playerTransform.name}_HandAnchor");
                if (handAnchor == null)
                {
                    handAnchor = new GameObject($"{playerTransform.name}_HandAnchor");
                    handAnchor.transform.SetParent(playerTransform);
                    handAnchor.transform.localPosition = Vector3.forward * 0.5f + Vector3.up * 1.2f;
                    handAnchor.transform.localRotation = Quaternion.identity;
                }
                handTransform = handAnchor.transform;
            }
        }

        private void UpdatePlayerProximity()
        {
            if (playerTransform == null)
            {
                FindPlayer();
                return;
            }

            float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);
            bool wasInRange = isInRange;
            isInRange = distanceToPlayer >= minGrabDistance && distanceToPlayer <= maxGrabDistance;

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
            bool wasAimedAt = isAimedAt;
            isAimedAt = CheckAimingAtObject();

            if (isAimedAt && !wasAimedAt)
            {
                // OnAimEnter event if needed
            }
            else if (!isAimedAt && wasAimedAt)
            {
                // OnAimExit event if needed
            }
        }

        private bool CheckAimingAtObject()
        {
            if (playerCamera == null || maxGrabDistance <= 0f || playerTransform == null)
            {
                return false;
            }

            float avatarDistance = Vector3.Distance(transform.position, playerTransform.position);
            if (avatarDistance < minGrabDistance || avatarDistance > maxGrabDistance)
            {
                return false;
            }

            // Check if we're in third person mode
            bool isThirdPerson = playerController != null && !playerController.IsFirstPerson;

            if (isThirdPerson)
            {
                // Third person: Check if object is in front of the avatar
                // Use avatar's forward direction, not camera (which is behind the player)
                Vector3 avatarToObject = transform.position - playerTransform.position;
                avatarToObject.y = 0f; // Flatten to horizontal plane

                Vector3 avatarForward = playerTransform.forward;
                avatarForward.y = 0f;
                avatarForward.Normalize();

                if (avatarToObject.magnitude > 0.1f)
                {
                    float angle = Vector3.Angle(avatarForward, avatarToObject.normalized);
                    // Allow ~120 degree cone in front of avatar for third person
                    return angle <= 60f;
                }
                return true; // Very close, allow grab
            }
            else
            {
                // First person: Use precise camera-based detection
                if (useRadiusDetection)
                {
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
                }
                else
                {
                    Vector3 avatarEyeLevel = playerTransform.position + Vector3.up * 1.5f;
                    Vector3 rayDirection = playerCamera.transform.forward;
                    Ray ray = new Ray(avatarEyeLevel, rayDirection);

                    if (Physics.Raycast(ray, out RaycastHit hit, maxGrabDistance))
                    {
                        return hit.collider == col;
                    }
                }
            }

            return false;
        }

        private void RecordSpawnPosition()
        {
            if (!hasRecordedSpawn)
            {
                spawnPosition = transform.position;
                spawnRotation = transform.rotation;
                hasRecordedSpawn = true;
            }
        }

        private void StoreOriginalPhysicsState()
        {
            if (hasRigidbody && !hasStoredOriginalPhysicsState)
            {
                if (throwable != null)
                {
                    // For throwable objects, desired state is physics-ready
                    originalWasKinematic = false;
                    originalUsedGravity = true;
                }
                else
                {
                    // For non-throwable objects, store designer settings
                    originalWasKinematic = rb.isKinematic;
                    originalUsedGravity = rb.useGravity;
                }

                hasStoredOriginalPhysicsState = true;
            }
        }

        private void ClearPlayerReferences()
        {
            playerTransform = null;
            playerController = null;
            handTransform = null;
            playerCamera = null;
        }

        // IU3DInteractable implementation
        public void OnInteract()
        {
            if (IsCurrentlyGrabbed())
            {
                Release();
            }
            else if (CanAttemptGrab())
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
            return CanAttemptGrab() || IsCurrentlyGrabbed();
        }

        public int GetInteractionPriority()
        {
            if (IsCurrentlyGrabbed()) return 60;
            if (minGrabDistance <= 0f) return 50;
            return 40;
        }

        public string GetInteractionPrompt()
        {
            if (isRequestingAuthority) return "Requesting...";
            return IsCurrentlyGrabbed() ? $"Release ({grabKey})" : $"Grab ({grabKey})";
        }

        // Public properties
        public bool IsGrabbed => IsCurrentlyGrabbed();
        public bool IsInRange => isInRange;
        public bool IsAimedAt => isAimedAt;
        public bool IsNetworked => isNetworked;
        public bool HasRigidbody => hasRigidbody;
        public bool HasThrowable => throwable != null;
        public GrabState CurrentGrabState => localGrabState;
        public bool IsRequestingAuthority => isRequestingAuthority;
        public KeyCode GrabKey { get => grabKey; set => grabKey = value; }

        private void OnDestroy()
        {
            if (IsCurrentlyGrabbed())
            {
                Release();
            }
        }
    }
}