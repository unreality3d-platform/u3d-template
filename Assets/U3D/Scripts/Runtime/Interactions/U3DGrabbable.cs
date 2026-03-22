using Fusion;
using Fusion.Addons.Physics;
using UnityEngine;
using UnityEngine.Events;

namespace U3D
{
    /// <summary>
    /// Reliable authority management for Shared Mode grab/throw system
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

        [Tooltip("Allow multiple players to hold this object simultaneously. When disabled, grabbing steals from whoever currently holds it.")]
        [SerializeField] private bool allowMultiGrab = false;

        [Header("Starting State")]
        [Tooltip("When enabled, object spawns with gravity active and falls to the ground before becoming grabbable. Use this for objects spawned above ground level. When a Throwable component is also present, use Throwable's Start Active toggle instead.")]
        [SerializeField] private bool startActive = false;

        [Header("Optional Label")]
        [Tooltip("Assign a U3DBillboardUI in your scene to show a label near this object. Edit the text on that object directly.")]
        public U3DBillboardUI labelUI;

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

        // Proper network state management for Shared Mode
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
        private Coroutine _releaseCollisionCoroutine;
        private Coroutine _startActiveSettleCoroutine;

        // Static tracking for single grab mode - per client
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

            networkObject = GetComponent<NetworkObject>();
            isNetworked = networkObject != null;
        }

        private void Start()
        {
            RecordSpawnPosition();
            StoreOriginalPhysicsState();
            CheckForInputConflicts();
            ApplyStartActiveState();
        }

        /// <summary>
        /// When Start Active is enabled and no Throwable is present, activate gravity
        /// so the object falls to the ground, then settle back to original physics state.
        /// When a Throwable is present, Throwable's own Start Active handles this.
        /// </summary>
        private void ApplyStartActiveState()
        {
            if (!startActive) return;
            if (throwable != null) return;
            if (!hasRigidbody) return;

            rb.isKinematic = false;
            rb.useGravity = true;

            _startActiveSettleCoroutine = StartCoroutine(WaitForStartActiveSettle());
        }

        private System.Collections.IEnumerator WaitForStartActiveSettle()
        {
            yield return new WaitForSeconds(1.5f);

            while (rb != null && !rb.IsSleeping() &&
                   (rb.linearVelocity.magnitude > 0.3f || rb.angularVelocity.magnitude > 0.3f))
            {
                yield return new WaitForSeconds(0.5f);
            }

            if (rb != null && localGrabState == GrabState.Free)
            {
                rb.isKinematic = originalWasKinematic;
                rb.useGravity = originalUsedGravity;
            }

            _startActiveSettleCoroutine = null;
        }

        private void Update()
        {
            UpdatePlayerProximity();

            if (maxGrabDistance > 0f && !IsCurrentlyGrabbed() && Time.time - lastAimCheckTime > 0.1f)
            {
                lastAimCheckTime = Time.time;
                CheckIfAimedAt();
            }

            // NOTE: Direct input handling removed - U3DInteractionManager handles input
            // via IU3DInteractable.OnInteract() to prevent double-input issues

            if (isRequestingAuthority && Time.time - authorityRequestTime > AUTHORITY_REQUEST_TIMEOUT)
            {
                Debug.LogWarning($"Authority request timeout for {name}");
                isRequestingAuthority = false;
                OnGrabFailed?.Invoke();
            }
        }

        private void CheckForInputConflicts()
        {
            var kickable = GetComponent<U3DKickable>();
            if (kickable != null && kickable.KickKey == grabKey)
            {
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
            NetworkGrabState = (byte)GrabState.Free;
            NetworkIsGrabbed = false;
            NetworkGrabbedBy = PlayerRef.None;
        }

        public void OnStateAuthorityChanged()
        {
            if (!isNetworked) return;

            if (Object.HasStateAuthority && isRequestingAuthority)
            {
                // Successfully got authority - perform the grab now
                // This fires for both the free-object case and the steal-from-player case
                isRequestingAuthority = false;
                PerformGrab();
            }
            else if (!Object.HasStateAuthority && localGrabState == GrabState.Grabbed)
            {
                // Another player stole authority while we were holding this object
                // Force a local release so our hand doesn't keep tracking a stolen object
                PerformLocalRelease();
            }
        }

        public override void Render()
        {
            base.Render();

            // Only smooth-interpolate for remote clients viewing a grabbed object.
            // The local authority holder sets localPosition directly in PerformGrab() and
            // does not need interpolation - running it locally causes per-frame float drift
            // that accumulates into visible position/rotation change across repeated grabs.
            if (localGrabState == GrabState.Grabbed && handTransform != null && hasNetworkRb3D
                && isNetworked && !Object.HasStateAuthority)
            {
                transform.position = Vector3.Lerp(
                    transform.position,
                    handTransform.position + handTransform.TransformVector(grabOffset),
                    0.5f
                );

                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    handTransform.rotation,
                    0.5f
                );
            }
        }

        public void Grab()
        {
            if (!CanAttemptGrab()) return;

            if (!isNetworked)
            {
                PerformGrab();
                return;
            }

            if (Object.HasStateAuthority)
            {
                // We already own this object - grab immediately
                PerformGrab();
            }
            else if (!isRequestingAuthority)
            {
                // Request authority whether the object is free or held by another player.
                // If held by another player and allowMultiGrab is false, OnStateAuthorityChanged
                // will fire a PerformLocalRelease() on their client when they lose authority.
                RequestGrabAuthority();
            }
        }

        private bool CanAttemptGrab()
        {
            // Never grab something we're already holding
            if (localGrabState == GrabState.Grabbed) return false;

            // When multi-grab is disabled and someone else is holding this object,
            // we still allow the attempt - it becomes a steal via authority transfer.
            // We only block if WE are the ones already holding it (checked above).
            // Multi-grab: if another player holds it and allowMultiGrab is false,
            // we proceed with the steal. If allowMultiGrab is true, we also proceed.
            // Either way, we don't block here - the authority system handles the outcome.

            if (playerTransform == null)
            {
                FindPlayer();
                if (playerTransform == null) return false;
            }

            float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);
            if (distanceToPlayer < minGrabDistance || distanceToPlayer > maxGrabDistance)
                return false;

            if (maxGrabDistance > minGrabDistance && playerCamera != null)
                return CheckAimingAtObject();

            return true;
        }

        private void RequestGrabAuthority()
        {
            if (isRequestingAuthority) return;

            isRequestingAuthority = true;
            authorityRequestTime = Time.time;

            // Set grab state to Requesting so other players see this is contested.
            // If the object is currently Free we can set this directly; if it's Grabbed
            // by another player, we don't have authority to change network state yet -
            // the authority transfer itself will resolve that via OnStateAuthorityChanged.
            if (!NetworkIsGrabbed)
                NetworkGrabState = (byte)GrabState.Requesting;

            Object.RequestStateAuthority();
        }

        private void PerformGrab()
        {
            // Stop any start-active settle coroutine
            if (_startActiveSettleCoroutine != null)
            {
                StopCoroutine(_startActiveSettleCoroutine);
                _startActiveSettleCoroutine = null;
            }

            // Release whatever this client was previously holding
            if (currentlyGrabbed != null && currentlyGrabbed != this)
                currentlyGrabbed.Release();

            if (playerTransform == null)
                FindPlayer();

            if (handTransform == null)
            {
                FindHandBone();
                if (handTransform == null) return;
            }

            // Teleport the NetworkRigidbody3D to the hand's grab position BEFORE disabling
            // SyncParent and before parenting. This aligns Fusion's internal interpolation
            // target with where we're about to place the object, preventing Fusion's
            // FixedUpdateNetwork tick from reasserting its stale tracked position and
            // overwriting localPosition = grabOffset on the frame(s) after grab.
            if (networkRb3D != null)
            {
                Vector3 targetWorldPos = handTransform.TransformPoint(grabOffset);
                networkRb3D.Teleport(targetWorldPos, transform.rotation);
                networkRb3D.SyncParent = false;
            }

            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkGrabState = (byte)GrabState.Grabbed;
                NetworkIsGrabbed = true;
                if (Runner != null && Runner.LocalPlayer != null)
                    NetworkGrabbedBy = Runner.LocalPlayer;
            }

            localGrabState = GrabState.Grabbed;
            currentlyGrabbed = this;
            isRequestingAuthority = false;

            if (throwable != null)
            {
                // Throwable manages its own physics
            }
            else if (hasRigidbody)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            transform.SetParent(handTransform);
            transform.localPosition = grabOffset;

            OnGrabbed?.Invoke();
            if (labelUI != null) labelUI.gameObject.SetActive(false);
        }

        public void Release()
        {
            if (localGrabState != GrabState.Grabbed) return;

            if (isNetworked && !Object.HasStateAuthority) return;

            PerformRelease();
        }

        private void PerformRelease()
        {
            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkGrabState = (byte)GrabState.Free;
                NetworkIsGrabbed = false;
                NetworkGrabbedBy = PlayerRef.None;
            }

            if (networkRb3D != null)
                networkRb3D.SyncParent = true;

            PerformLocalRelease();
        }

        private void PerformLocalRelease()
        {
            localGrabState = GrabState.Free;
            if (currentlyGrabbed == this)
                currentlyGrabbed = null;

            PerformDirectUnparenting();

            if (throwable != null)
            {
                // Let throwable component handle physics via OnObjectReleased callback
            }
            else if (hasRigidbody && hasStoredOriginalPhysicsState)
            {
                rb.isKinematic = originalWasKinematic;
                rb.useGravity = originalUsedGravity;
            }

            if (!isInRange)
                ClearPlayerReferences();

            OnReleased?.Invoke();
            if (labelUI != null) labelUI.gameObject.SetActive(true);
        }

        private void OnRemoteGrab()
        {
            OnGrabbed?.Invoke();
        }

        private void OnRemoteRelease()
        {
            OnReleased?.Invoke();
        }

        private void PerformDirectParenting()
        {
            if (_releaseCollisionCoroutine != null)
            {
                StopCoroutine(_releaseCollisionCoroutine);
                _releaseCollisionCoroutine = null;
            }

            col.isTrigger = true;
            int originalLayer = gameObject.layer;
            SetLayerRecursively(gameObject, LayerMask.NameToLayer("Ignore Raycast"));
            PlayerPrefs.SetInt($"U3DGrabbable_OriginalLayer_{gameObject.GetInstanceID()}", originalLayer);

            transform.SetParent(handTransform);
            transform.localPosition = grabOffset;
        }

        private void PerformDirectUnparenting()
        {
            transform.SetParent(originalParent);

            // Restore layer before re-enabling collider
            int originalLayer = PlayerPrefs.GetInt($"U3DGrabbable_OriginalLayer_{gameObject.GetInstanceID()}", 0);
            SetLayerRecursively(gameObject, originalLayer);
            PlayerPrefs.DeleteKey($"U3DGrabbable_OriginalLayer_{gameObject.GetInstanceID()}");

            col.isTrigger = false;

            // Brief collision ignore so the re-enabled collider doesn't push the player
            // on the frame(s) immediately after release while still overlapping.
            if (playerController != null)
            {
                CharacterController cc = playerController.GetComponent<CharacterController>();
                if (cc != null)
                {
                    if (_releaseCollisionCoroutine != null)
                        StopCoroutine(_releaseCollisionCoroutine);
                    _releaseCollisionCoroutine = StartCoroutine(IgnorePlayerCollisionBriefly(cc));
                }
            }
        }

        private System.Collections.IEnumerator IgnorePlayerCollisionBriefly(CharacterController cc)
        {
            Physics.IgnoreCollision(col, cc, true);
            yield return new WaitForSeconds(0.15f);
            if (col != null && cc != null)
                Physics.IgnoreCollision(col, cc, false);
            _releaseCollisionCoroutine = null;
        }

        private void SetLayerRecursively(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        private bool IsCurrentlyGrabbed()
        {
            if (isNetworked && Object != null && Object.IsValid)
                return NetworkIsGrabbed || NetworkGrabState == (byte)GrabState.Grabbed;

            return localGrabState == GrabState.Grabbed;
        }

        private void FindPlayer()
        {
            playerController = FindAnyObjectByType<U3DPlayerController>();

            if (playerController != null)
            {
                playerTransform = playerController.transform;
                playerCamera = playerController.GetComponentInChildren<Camera>();
                FindHandBone();
            }
            else
            {
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
                OnEnterGrabRange?.Invoke();
            else if (!isInRange && wasInRange)
                OnExitGrabRange?.Invoke();
        }

        private void CheckIfAimedAt()
        {
            bool wasAimedAt = isAimedAt;
            isAimedAt = CheckAimingAtObject();
        }

        private bool CheckAimingAtObject()
        {
            if (playerCamera == null || maxGrabDistance <= 0f || playerTransform == null)
                return false;

            float avatarDistance = Vector3.Distance(transform.position, playerTransform.position);
            if (avatarDistance < minGrabDistance || avatarDistance > maxGrabDistance)
                return false;

            bool isThirdPerson = playerController != null && !playerController.IsFirstPerson;

            if (isThirdPerson)
            {
                Vector3 avatarToObject = transform.position - playerTransform.position;
                avatarToObject.y = 0f;

                Vector3 avatarForward = playerTransform.forward;
                avatarForward.y = 0f;
                avatarForward.Normalize();

                if (avatarToObject.magnitude > 0.1f)
                {
                    float angle = Vector3.Angle(avatarForward, avatarToObject.normalized);
                    return angle <= 60f;
                }
                return true;
            }
            else
            {
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
                    Ray ray = new Ray(avatarEyeLevel, playerCamera.transform.forward);

                    if (Physics.Raycast(ray, out RaycastHit hit, maxGrabDistance))
                        return hit.collider == col;
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
                    originalWasKinematic = false;
                    originalUsedGravity = true;
                }
                else
                {
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
            if (IsCurrentlyGrabbed() && localGrabState == GrabState.Grabbed)
                Release();
            else
                Grab();
        }

        public void OnPlayerEnterRange() { }
        public void OnPlayerExitRange() { }

        public bool CanInteract()
        {
            // Always interactable - either to grab or to steal from another player
            if (localGrabState == GrabState.Grabbed) return true;

            if (playerTransform == null)
            {
                FindPlayer();
                if (playerTransform == null) return false;
            }

            float dist = Vector3.Distance(transform.position, playerTransform.position);
            return dist >= minGrabDistance && dist <= maxGrabDistance;
        }

        public int GetInteractionPriority()
        {
            if (localGrabState == GrabState.Grabbed) return 60;
            if (minGrabDistance <= 0f) return 50;
            return 40;
        }

        public string GetInteractionPrompt()
        {
            if (isRequestingAuthority) return "Requesting...";
            if (localGrabState == GrabState.Grabbed) return $"Release ({grabKey})";
            if (NetworkIsGrabbed && isNetworked) return $"Take ({grabKey})";
            return $"Grab ({grabKey})";
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
            if (_startActiveSettleCoroutine != null)
            {
                StopCoroutine(_startActiveSettleCoroutine);
            }

            if (localGrabState == GrabState.Grabbed)
            {
                localGrabState = GrabState.Free;

                if (currentlyGrabbed == this)
                    currentlyGrabbed = null;

                if (networkRb3D != null)
                    networkRb3D.SyncParent = true;

                try { transform.SetParent(originalParent); }
                catch { }

                if (col != null)
                    col.isTrigger = false;
            }
        }
    }
}