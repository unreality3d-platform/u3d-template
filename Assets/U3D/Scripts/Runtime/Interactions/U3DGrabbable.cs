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
    /// Aim detection delegated to U3DInteractionManager SphereCast — this component
    /// only gates proximity via min/max grab distance.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class U3DGrabbable : NetworkBehaviour, IU3DInteractable, IU3DInventoryActivatable
    {
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

        [Header("Starting State")]
        [Tooltip("When enabled, object spawns with gravity active and falls to the ground before becoming grabbable. Use this for objects spawned above ground level. When a Throwable component is also present, use Throwable's Start Active toggle instead.")]
        [SerializeField] private bool startActive = false;

        [Header("Optional Label")]
        [Tooltip("Assign a U3DWorldspaceUI in your scene to show a label near this object. Edit the text on that object directly. At runtime the label tracks this object's position so it travels with it.")]
        public U3DWorldspaceUI labelUI;

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

        // Rotation of the held object relative to the grabber's hand bone, captured at
        // grab moment on the state authority. Remote viewers multiply this by the
        // grabber's hand world rotation to reconstruct the exact orientation the
        // grabber sees, so non-identity initial rotations (like a sideways-held cup
        // or an upright magical critter) replicate correctly. Without this, remote
        // viewers would either see the object aligned to the hand's forward axis
        // (wrong for objects with meaningful starting orientation) or have to guess.
        [Networked] public Quaternion NetworkGrabLocalRotation { get; set; }

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

        // Remote-viewer cache for tracking the grabber's hand bone. Lets Render()
        // interpolate the held object toward the grabber's hand on every remote
        // client without walking the entire player hierarchy every frame. Cache
        // invalidates when NetworkGrabbedBy changes or when the object is released.
        private PlayerRef cachedGrabberRef = PlayerRef.None;
        private Transform cachedGrabberHand;

        // Deterministic state management
        private GrabState localGrabState = GrabState.Free;
        private bool isInRange = false;
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

        /// <summary>
        /// The grabbable currently held by the local player, or null if the player's hands are empty.
        /// Exposed so U3DInteractionManager can route R-press directly to the held object for release
        /// without needing to find it via SphereCast.
        /// </summary>
        public static U3DGrabbable CurrentlyGrabbed => currentlyGrabbed;

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
            LinkLabelUI();
        }

        /// <summary>
        /// Tell the assigned label UI to track this object's position from here on.
        /// The label is NOT reparented — it stays in its authored scene location and
        /// hierarchy. It just updates its own world position each frame to follow
        /// this transform horizontally, with the captured Y offset preserved.
        /// No-op if no label is assigned.
        /// </summary>
        private void LinkLabelUI()
        {
            if (labelUI == null) return;
            labelUI.BeginFollowing(transform);
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

            _startActiveSettleCoroutine = StartCoroutine(StartActiveAndSettle());
        }

        private System.Collections.IEnumerator StartActiveAndSettle()
        {
            // Sync NetworkRigidbody3D's interpolation target before flipping kinematic state,
            // so it doesn't reassert from a stale snapshot and undo our writes.
            if (hasNetworkRb3D && networkRb3D != null)
            {
                networkRb3D.Teleport(transform.position, transform.rotation);
            }

            // Wait one frame so Fusion has finished its initial spawn-tick processing
            // before we touch the Rigidbody.
            yield return null;

            rb.isKinematic = false;
            rb.useGravity = true;

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
                isRequestingAuthority = false;
                PerformGrab();
            }
            else if (!Object.HasStateAuthority && localGrabState == GrabState.Grabbed)
            {
                PerformLocalRelease();
            }
        }

        public override void Render()
        {
            base.Render();

            // Remote-viewer interpolation: when this object is grabbed and we are
            // NOT the state authority (i.e. someone else is holding it), follow
            // the grabber's hand bone so the held object appears in the right
            // place on our screen. Without this, the object stays frozen at
            // wherever it was teleported at the moment of grab, because the
            // grabber's local SetParent + SyncParent=false means no position
            // updates are replicated to us.
            //
            // Position uses TransformPoint(grabOffset) to match exactly what the
            // grabber does locally — handles non-unit avatar rig scale correctly.
            //
            // Rotation uses grabberHand.rotation * NetworkGrabLocalRotation, where
            // NetworkGrabLocalRotation was captured at grab moment by the state
            // authority. This reconstructs the grabber's transform.rotation as
            // (parent.rotation * localRotation), so non-identity grab orientations
            // (sideways cups, upright critters) replicate exactly.
            if (!isNetworked) return;
            if (!hasNetworkRb3D) return;
            if (Object == null || !Object.IsValid) return;
            if (Object.HasStateAuthority) return;
            if (!NetworkIsGrabbed) return;
            if (NetworkGrabbedBy == PlayerRef.None) return;

            Transform grabberHand = GetGrabberHandTransform();
            if (grabberHand == null) return;

            transform.position = Vector3.Lerp(
                transform.position,
                grabberHand.TransformPoint(grabOffset),
                0.5f
            );

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                grabberHand.rotation * NetworkGrabLocalRotation,
                0.5f
            );
        }

        /// <summary>
        /// Returns the hand bone Transform of whichever player is currently holding
        /// this object, from the perspective of a remote viewer (i.e. not the holder).
        /// Cached per-grab — the cache invalidates when NetworkGrabbedBy changes,
        /// avoiding a player-hierarchy walk every Render() frame.
        ///
        /// Returns null if the grabber's player controller isn't found yet (joining,
        /// despawning) or if their hand bone can't be located. Callers must handle null.
        /// </summary>
        private Transform GetGrabberHandTransform()
        {
            if (NetworkGrabbedBy != cachedGrabberRef)
            {
                cachedGrabberRef = NetworkGrabbedBy;
                cachedGrabberHand = null;
            }

            if (cachedGrabberHand != null) return cachedGrabberHand;
            if (NetworkGrabbedBy == PlayerRef.None) return null;

            // Find the player controller whose input authority matches NetworkGrabbedBy.
            // This is the remote grabber. U3DPlayerController.FindLocalPlayer() can't be
            // used because it finds the local viewer's player, not the grabber's.
            U3DPlayerController[] allPlayers = FindObjectsByType<U3DPlayerController>(FindObjectsSortMode.None);
            U3DPlayerController grabberController = null;
            for (int i = 0; i < allPlayers.Length; i++)
            {
                NetworkObject playerNetObj = allPlayers[i].GetComponent<NetworkObject>();
                if (playerNetObj != null && playerNetObj.InputAuthority == NetworkGrabbedBy)
                {
                    grabberController = allPlayers[i];
                    break;
                }
            }
            if (grabberController == null) return null;

            // Walk the grabber's children to find their hand bone by name. Same matching
            // logic as FindHandBone() — same handBoneName field, same exclusions —
            // applied to a different player's transform.
            if (string.IsNullOrEmpty(handBoneName)) return null;

            Transform[] allTransforms = grabberController.transform.GetComponentsInChildren<Transform>();
            for (int i = 0; i < allTransforms.Length; i++)
            {
                Transform t = allTransforms[i];
                if (t.name == handBoneName && !t.name.Contains("Camera"))
                {
                    cachedGrabberHand = t;
                    return cachedGrabberHand;
                }
            }

            return null;
        }

        /// <summary>
        /// Performs the grab without the proximity-distance gate. Called by
        /// U3DInventory when an item is summoned from inventory directly to the
        /// player's hand — the distance check exists for world-object grabs
        /// (where the player walked up to a thing) and is not appropriate for
        /// items the player intentionally summoned.
        /// </summary>
        public void OnInventoryActivate()
        {
            if (localGrabState == GrabState.Grabbed) return;

            if (playerTransform == null)
            {
                FindPlayer();
                if (playerTransform == null) return;
            }

            if (!isNetworked)
            {
                PerformGrab();
                return;
            }

            if (Object.HasStateAuthority)
            {
                PerformGrab();
            }
            else if (!isRequestingAuthority)
            {
                RequestGrabAuthority();
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
                PerformGrab();
            }
            else if (!isRequestingAuthority)
            {
                RequestGrabAuthority();
            }
        }

        private bool CanAttemptGrab()
        {
            if (localGrabState == GrabState.Grabbed) return false;

            if (playerTransform == null)
            {
                FindPlayer();
                if (playerTransform == null) return false;
            }

            float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);
            return distanceToPlayer >= minGrabDistance && distanceToPlayer <= maxGrabDistance;
        }

        private void RequestGrabAuthority()
        {
            if (isRequestingAuthority) return;

            isRequestingAuthority = true;
            authorityRequestTime = Time.time;

            if (!NetworkIsGrabbed)
                NetworkGrabState = (byte)GrabState.Requesting;

            Object.RequestStateAuthority();
        }

        private void PerformGrab()
        {
            if (_startActiveSettleCoroutine != null)
            {
                StopCoroutine(_startActiveSettleCoroutine);
                _startActiveSettleCoroutine = null;
            }

            if (currentlyGrabbed != null && currentlyGrabbed != this)
                currentlyGrabbed.Release();

            if (playerTransform == null)
                FindPlayer();

            if (handTransform == null)
            {
                FindHandBone();
                if (handTransform == null) return;
            }

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

            // Capture the object's rotation relative to the hand bone right after
            // parenting. This is what Render() on remote viewers needs to
            // reconstruct the same world rotation the grabber sees. Done AFTER
            // SetParent so localRotation is meaningful relative to the hand bone.
            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkGrabLocalRotation = transform.localRotation;
            }

            OnGrabbed?.Invoke();
            if (labelUI != null) labelUI.gameObject.SetActive(false);
        }

        public void Release()
        {
            if (localGrabState != GrabState.Grabbed) return;

            if (isNetworked && !Object.HasStateAuthority) return;

            PerformRelease();
        }

        public void ResetToSpawn()
        {
            if (IsGrabbed) Release();

            if (spawnPosition != Vector3.zero || hasRecordedSpawn)
            {
                CharacterController cc = null;
                U3DPlayerController player = U3DPlayerController.FindLocalPlayer();
                if (player != null) cc = player.GetComponent<CharacterController>();

                NetworkRigidbody3D nrb = GetComponent<NetworkRigidbody3D>();
                if (nrb != null)
                {
                    nrb.Teleport(spawnPosition, spawnRotation);
                }
                else
                {
                    transform.position = spawnPosition;
                    transform.rotation = spawnRotation;
                }
            }
        }

        private void PerformRelease()
        {
            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkGrabState = (byte)GrabState.Free;
                NetworkIsGrabbed = false;
                NetworkGrabbedBy = PlayerRef.None;
                NetworkGrabLocalRotation = Quaternion.identity;
            }

            // Only re-enable SyncParent if there's no throwable to manage it
            if (networkRb3D != null && throwable == null)
                networkRb3D.SyncParent = true;

            PerformLocalRelease();
        }

        private void PerformLocalRelease()
        {
            localGrabState = GrabState.Free;
            if (currentlyGrabbed == this)
                currentlyGrabbed = null;

            // Invalidate the remote-viewer's cached grabber hand transform. This runs
            // on whichever client is calling release; on remote viewers the cache will
            // also self-invalidate when NetworkGrabbedBy flips to PlayerRef.None in
            // GetGrabberHandTransform, but clearing here is belt-and-suspenders.
            cachedGrabberRef = PlayerRef.None;
            cachedGrabberHand = null;

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

            // Label timing: when a Throwable is present, the release is the start
            // of a flight (throw or drop). Don't restore the label here — the
            // object is still in motion. Throwable.ReturnToGrabbableSleepState
            // will restore the label when the object actually settles. This
            // mirrors the Start Active precedence pattern: when both components
            // are present, Throwable owns the post-release lifecycle.
            if (labelUI != null && throwable == null)
                labelUI.gameObject.SetActive(true);
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

            int originalLayer = PlayerPrefs.GetInt($"U3DGrabbable_OriginalLayer_{gameObject.GetInstanceID()}", 0);
            SetLayerRecursively(gameObject, originalLayer);
            PlayerPrefs.DeleteKey($"U3DGrabbable_OriginalLayer_{gameObject.GetInstanceID()}");

            col.isTrigger = false;

            // When a Throwable is present, Throwable owns the collision-ignore
            // lifecycle via its own penetration-polling coroutine. Two coroutines
            // both calling Physics.IgnoreCollision on the same collider pair don't
            // reference-count — whichever finishes first calls IgnoreCollision(...false)
            // and restores collision while the other is still waiting. That race
            // is what previously let collision restore while the dropped object
            // was still under the player capsule, shoving the player upward.
            if (throwable != null) return;

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
            playerController = U3DPlayerController.FindLocalPlayer();

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
            if (localGrabState == GrabState.Grabbed) return true;

            if (playerTransform == null)
            {
                FindPlayer();
                if (playerTransform == null) return false;
            }

            float dist = Vector3.Distance(transform.position, playerTransform.position);
            return dist >= minGrabDistance && dist <= maxGrabDistance;
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
        public bool IsNetworked => isNetworked;
        public bool HasRigidbody => hasRigidbody;
        public bool HasThrowable => throwable != null;
        public GrabState CurrentGrabState => localGrabState;
        public bool IsRequestingAuthority => isRequestingAuthority;
        public KeyCode GrabKey { get => grabKey; set => grabKey = value; }
        public Vector3 SpawnPosition => spawnPosition;
        public Quaternion SpawnRotation => spawnRotation;

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

                // No reparenting here. OnDestroy means this object is being torn down,
                // and SetParent during destruction logs a Unity warning that try/catch
                // cannot suppress (the warning fires before the exception). Reparenting a
                // destroyed object accomplishes nothing — its hierarchy is discarded the
                // same frame. This covers both shutdown and mid-play destruction, such as
                // a held object entering a Destroy-mode trash zone.

                if (col != null)
                    col.isTrigger = false;
            }
        }
    }
}