using UnityEngine;
using UnityEngine.Events;
using Fusion;
using Fusion.Addons.Physics;
using System.Collections;

namespace U3D
{
    /// <summary>
    /// Pullable interaction component for objects that can be pulled along surfaces by the player.
    /// Toggle-based: press interaction key to start pulling, press again or walk out of range to stop.
    /// Direction is camera backward (horizontal, normalized) — the inverse of Pushable.
    /// Speed derives from the player's actual movement velocity — no artificial pull speed setting.
    /// Pull Resistance adjusts the Rigidbody's mass directly for creator-friendly tuning.
    /// Supports both networked and non-networked modes with Photon Fusion 2 Shared Mode.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    public class U3DPullable : NetworkBehaviour, IU3DInteractable
    {
        [Header("Pull Resistance")]
        [Tooltip("Higher values make this object harder to pull. Adjusts this object's Rigidbody mass.")]
        [SerializeField] private float pullResistance = 5f;

        [Header("Interaction Settings")]
        [Tooltip("Key to toggle pull mode (remappable)")]
        [SerializeField] private KeyCode pullKey = KeyCode.R;

        [Tooltip("Maximum distance to pull from. Player walking beyond this auto-disengages pull mode.")]
        [SerializeField] private float maxPullDistance = 5f;

        [Header("Starting State")]
        [Tooltip("When enabled, object spawns with gravity active and falls to the ground before becoming pullable. Use this for objects spawned above ground level.")]
        [SerializeField] private bool startActive = false;

        [Tooltip("When enabled, this object is permanently destroyed when it falls out of world bounds instead of respawning. Requires a U3DDestroyable component on this object.")]
        [SerializeField] private bool destroyOnOutOfBounds = false;

        [Header("Optional Label")]
        [Tooltip("Assign a U3DWorldspaceUI in your scene to show a label near this object. Edit the text on that object directly. At runtime the label tracks this object's position so it travels with it.")]
        public U3DWorldspaceUI labelUI;

        [Header("Events")]
        [Tooltip("Called when player begins pulling this object")]
        public UnityEvent OnPullStart;

        [Tooltip("Called when player stops pulling this object")]
        public UnityEvent OnPullEnd;

        [Tooltip("Called when pulled object hits something with force")]
        public UnityEvent OnImpact;

        [Tooltip("Called when object returns to sleep after being pulled")]
        public UnityEvent OnSleep;

        [Tooltip("Called when object is reset due to world bounds violation")]
        public UnityEvent OnWorldBoundsReset;

        // HIDDEN PHYSICS MANAGEMENT - Optimal defaults
        [HideInInspector]
        [SerializeField] private float sleepCheckDelay = 1.5f;
        [HideInInspector]
        [SerializeField] private float sleepVelocityThreshold = 0.3f;
        [HideInInspector]
        [SerializeField] private float maxActiveTime = 15f;

        // HIDDEN WORLD BOUNDS SAFETY
        [HideInInspector]
        [SerializeField] private float worldBoundsFloor = -50f;
        [HideInInspector]
        [SerializeField] private float worldBoundsRadius = 1000f;
        [HideInInspector]
        [SerializeField] private float boundsCheckInterval = 1f;

        // Network state for physics management
        [Networked] public bool NetworkIsPulling { get; set; }
        [Networked] public bool NetworkIsPhysicsActive { get; set; }
        [Networked] public TickTimer NetworkSleepTimer { get; set; }
        [Networked] public TickTimer NetworkSettleGraceTimer { get; set; }

        // Components
        private Rigidbody rb;
        private U3DGrabbable grabbable;
        private Camera playerCamera;
        private Transform playerTransform;
        private U3DPlayerController playerController;
        private NetworkObject networkObject;
        private NetworkRigidbody3D networkRigidbody;
        private bool hasNetworkRb3D = false;
        private Collider col;

        // State tracking
        private bool isNetworked = false;
        private bool isInPullRange = false;
        private bool isPullActive = false;
        private Coroutine boundsCheckCoroutine;

        // Authority request management
        private bool isRequestingAuthority = false;
        private float authorityRequestTime = 0f;
        private const float AUTHORITY_REQUEST_TIMEOUT = 2f;

        // Physics state management
        private PhysicsState currentPhysicsState = PhysicsState.Sleeping;
        private PhysicsState lastNetworkPhysicsState = PhysicsState.Sleeping;

        // Original position and rotation for reset purposes
        private Vector3 originalPosition;
        private Quaternion originalRotation;
        private bool hasRecordedOriginalTransform = false;

        // Physics state storage
        private bool originalWasKinematic;
        private bool originalUsedGravity;
        private bool hasStoredOriginalPhysicsState = false;

        // Animation state tracking
        private U3DNetworkedAnimator cachedNetworkedAnimator;
        private bool lastPullAnimState = false;

        public enum PhysicsState
        {
            Sleeping,      // Kinematic, no gravity - pullable state
            Active,        // Non-kinematic, gravity - physics simulation during/after pull
            Resetting      // Temporarily kinematic while resetting position
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            grabbable = GetComponent<U3DGrabbable>();
            networkRigidbody = GetComponent<NetworkRigidbody3D>();
            hasNetworkRb3D = networkRigidbody != null;
            col = GetComponent<Collider>();
            networkObject = GetComponent<NetworkObject>();
            isNetworked = networkObject != null;
        }

        public override void Spawned()
        {
            if (!isNetworked) return;

            NetworkIsPulling = false;
            NetworkIsPhysicsActive = false;

            InitializePhysicsState();
        }

        private void Start()
        {
            FindPlayerComponents();
            RecordOriginalTransform();
            ApplyPullResistanceToMass();
            LinkLabelUI();

            if (!isNetworked)
            {
                InitializePhysicsState();
            }

            StartBoundsMonitoring();
            CheckForInputConflicts();
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

        private void Update()
        {
            UpdatePlayerProximity();

            if (isPullActive)
            {
                if (!isInPullRange)
                    EndPull();
            }

            // IsPulling drives the animation only while pull mode is on AND the player is moving.
            // Evaluating every frame ensures the animator exits pull state immediately when the
            // player stops, rather than waiting for the pull session to end or the object to sleep.
            bool shouldPullAnimate = isPullActive && playerController != null && playerController.NetworkIsMoving;
            if (shouldPullAnimate != lastPullAnimState)
            {
                if (cachedNetworkedAnimator != null)
                    cachedNetworkedAnimator.SetAnimationBool("IsPulling", shouldPullAnimate);
                lastPullAnimState = shouldPullAnimate;
            }

            if (isRequestingAuthority && Time.time - authorityRequestTime > AUTHORITY_REQUEST_TIMEOUT)
            {
                Debug.LogWarning($"U3DPullable: Authority request timeout for {name}");
                isRequestingAuthority = false;
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!isNetworked || !Object.HasStateAuthority) return;

            if (grabbable != null && grabbable.IsGrabbed) return;

            if (isPullActive && NetworkIsPulling)
            {
                ApplyPullVelocity();
            }

            if (!NetworkIsPulling && NetworkIsPhysicsActive)
            {
                bool inGracePeriod = NetworkSettleGraceTimer.IsRunning &&
                                     !NetworkSettleGraceTimer.Expired(Runner);
                if (inGracePeriod) return;

                bool shouldSleep = false;

                if (rb.linearVelocity.magnitude < sleepVelocityThreshold &&
                    rb.angularVelocity.magnitude < sleepVelocityThreshold)
                {
                    shouldSleep = true;
                }

                if (NetworkSleepTimer.Expired(Runner))
                {
                    shouldSleep = true;
                }

                if (shouldSleep)
                {
                    ReturnToPullableSleepState();
                }
            }
        }

        public override void Render()
        {
            if (!isNetworked) return;

            PhysicsState networkState = NetworkIsPhysicsActive ?
                PhysicsState.Active : PhysicsState.Sleeping;

            if (networkState != lastNetworkPhysicsState)
            {
                if (!Object.HasStateAuthority)
                {
                    ApplyPhysicsStateFromNetwork(networkState);
                }
                lastNetworkPhysicsState = networkState;
            }
        }

        public void OnStateAuthorityChanged()
        {
            if (!isNetworked) return;

            if (Object.HasStateAuthority)
            {
                if (isRequestingAuthority)
                {
                    ActivatePull();
                }
                else
                {
                    SyncNetworkPhysicsState();
                }
            }
            else
            {
                isRequestingAuthority = false;
                if (isPullActive)
                {
                    isPullActive = false;
                    OnPullEnd?.Invoke();
                }
                SyncLocalPhysicsState();
            }
        }

        /// <summary>
        /// Apply pull velocity each tick while pull mode is active.
        /// Direction: camera BACKWARD projected horizontal, normalized (inverse of Pushable).
        /// Magnitude: player's current movement speed from GetCurrentSpeed() (walk/sprint/crouch).
        /// PlayerController.Velocity only tracks gravity — use NetworkIsMoving + CurrentSpeed instead.
        /// </summary>
        private void ApplyPullVelocity()
        {
            if (playerCamera == null || playerController == null)
            {
                FindPlayerComponents();
                if (playerCamera == null || playerController == null) return;
            }

            if (!playerController.NetworkIsMoving) return;

            float playerSpeed = playerController.CurrentSpeed;
            if (playerSpeed < 0.1f) return;

            // Camera backward projected onto horizontal plane (inverse of Pushable's direction)
            Vector3 pullDirection = -playerCamera.transform.forward;
            pullDirection.y = 0f;
            pullDirection.Normalize();

            if (pullDirection.sqrMagnitude < 0.01f) return;

            if (rb.isKinematic)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
            }

            Vector3 pullVelocity = pullDirection * playerSpeed;

            // Preserve existing Y velocity (gravity, falling off edges)
            rb.linearVelocity = new Vector3(pullVelocity.x, rb.linearVelocity.y, pullVelocity.z);
        }

        private void StartPull()
        {
            if (grabbable != null && grabbable.IsGrabbed) return;
            if (!isInPullRange) return;

            if (isNetworked && !Object.HasStateAuthority)
            {
                if (!isRequestingAuthority)
                {
                    isRequestingAuthority = true;
                    authorityRequestTime = Time.time;
                    Object.RequestStateAuthority();
                }
                return;
            }

            ActivatePull();
        }

        private void ActivatePull()
        {
            isPullActive = true;
            isRequestingAuthority = false;
            SetPhysicsState(PhysicsState.Active);

            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkIsPulling = true;
                NetworkSleepTimer = TickTimer.CreateFromSeconds(Runner, maxActiveTime);
                NetworkSettleGraceTimer = TickTimer.CreateFromSeconds(Runner, 1.0f);
            }

            if (labelUI != null) labelUI.gameObject.SetActive(false);
            OnPullStart?.Invoke();
        }

        private void EndPull()
        {
            if (!isPullActive) return;

            isPullActive = false;

            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkIsPulling = false;
                NetworkSleepTimer = TickTimer.CreateFromSeconds(Runner, maxActiveTime);
                NetworkSettleGraceTimer = TickTimer.CreateFromSeconds(Runner, 0.3f);
            }

            OnPullEnd?.Invoke();
        }

        private void ReturnToPullableSleepState()
        {
            SetPhysicsState(PhysicsState.Sleeping);

            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkIsPulling = false;
                NetworkIsPhysicsActive = false;
            }

            if (labelUI != null) labelUI.gameObject.SetActive(true);
            OnSleep?.Invoke();
        }

        private void UpdatePlayerProximity()
        {
            if (playerTransform == null)
            {
                FindPlayer();
                return;
            }

            float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);
            isInPullRange = distanceToPlayer <= maxPullDistance;
        }

        private void FindPlayer()
        {
            U3DPlayerController controller = U3DPlayerController.FindLocalPlayer();
            if (controller != null)
            {
                playerTransform = controller.transform;
                playerController = controller;
                playerCamera = controller.GetComponentInChildren<Camera>();
                cachedNetworkedAnimator = controller.GetComponent<U3DNetworkedAnimator>();
            }
            else
            {
                playerTransform = null;
                playerController = null;
                playerCamera = null;
                cachedNetworkedAnimator = null;
            }
        }

        private void FindPlayerComponents()
        {
            U3DPlayerController controller = U3DPlayerController.FindLocalPlayer();
            if (controller != null)
            {
                playerTransform = controller.transform;
                playerController = controller;
                playerCamera = controller.GetComponentInChildren<Camera>();
                cachedNetworkedAnimator = controller.GetComponent<U3DNetworkedAnimator>();
            }

            if (playerCamera == null)
                playerCamera = Camera.main;
        }

        private void InitializePhysicsState()
        {
            if (startActive)
            {
                StartCoroutine(ApplyStartActiveAfterPhysicsSettle());
            }
            else
            {
                SetPhysicsState(PhysicsState.Sleeping);
            }
            StoreOriginalPhysicsState();
        }

        private IEnumerator ApplyStartActiveAfterPhysicsSettle()
        {
            if (hasNetworkRb3D && networkRigidbody != null)
            {
                networkRigidbody.Teleport(transform.position, transform.rotation);
            }

            yield return null;

            rb.isKinematic = false;
            rb.useGravity = true;
            currentPhysicsState = PhysicsState.Active;

            if (isNetworked && Object != null && Object.HasStateAuthority)
            {
                NetworkIsPhysicsActive = true;
                NetworkSleepTimer = TickTimer.CreateFromSeconds(Runner, maxActiveTime);
                NetworkSettleGraceTimer = TickTimer.CreateFromSeconds(Runner, 1.0f);
            }
        }

        private void CheckForInputConflicts()
        {
            // Grabbable claims R — shift to T
            if (GetComponent<U3DGrabbable>() != null && pullKey == KeyCode.R)
                pullKey = KeyCode.T;

            // Pushable present on same object — avoid whichever key it settled on
            U3DPushable pushable = GetComponent<U3DPushable>();
            if (pushable != null && pullKey == pushable.PushKey)
                pullKey = KeyCode.G;
        }

        private void SetPhysicsState(PhysicsState newState)
        {
            currentPhysicsState = newState;
            ApplyPhysicsState(newState);

            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkIsPhysicsActive = (newState == PhysicsState.Active);
            }
        }

        private void ApplyPhysicsState(PhysicsState state)
        {
            if (rb == null) return;

            switch (state)
            {
                case PhysicsState.Sleeping:
                case PhysicsState.Resetting:
                    if (!rb.isKinematic)
                    {
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }
                    rb.useGravity = false;
                    rb.isKinematic = true;
                    break;

                case PhysicsState.Active:
                    rb.isKinematic = false;
                    rb.useGravity = true;
                    break;
            }
        }

        private void ApplyPhysicsStateFromNetwork(PhysicsState networkState)
        {
            currentPhysicsState = networkState;
            ApplyPhysicsState(networkState);
        }

        private void SyncNetworkPhysicsState()
        {
            if (!isNetworked || !Object.HasStateAuthority) return;
            NetworkIsPhysicsActive = (currentPhysicsState == PhysicsState.Active);
        }

        private void SyncLocalPhysicsState()
        {
            if (!isNetworked) return;
            PhysicsState networkState = NetworkIsPhysicsActive ? PhysicsState.Active : PhysicsState.Sleeping;
            ApplyPhysicsStateFromNetwork(networkState);
        }

        private void RecordOriginalTransform()
        {
            if (!hasRecordedOriginalTransform)
            {
                originalPosition = transform.position;
                originalRotation = transform.rotation;
                hasRecordedOriginalTransform = true;
            }
        }

        private void StoreOriginalPhysicsState()
        {
            if (rb != null && !hasStoredOriginalPhysicsState)
            {
                originalWasKinematic = false;
                originalUsedGravity = true;
                hasStoredOriginalPhysicsState = true;
            }
        }

        private void StartBoundsMonitoring()
        {
            if (boundsCheckCoroutine == null)
            {
                boundsCheckCoroutine = StartCoroutine(MonitorWorldBounds());
            }
        }

        private IEnumerator MonitorWorldBounds()
        {
            while (true)
            {
                yield return new WaitForSeconds(boundsCheckInterval);

                if (grabbable != null && grabbable.IsGrabbed)
                    continue;

                if (isNetworked && (Object == null || !Object.HasStateAuthority))
                    continue;

                bool needsReset = false;

                if (transform.position.y < worldBoundsFloor)
                {
                    Debug.LogWarning($"U3DPullable: Object '{name}' fell below world bounds (Y: {transform.position.y})");
                    needsReset = true;
                }
                else if (Vector3.Distance(Vector3.zero, transform.position) > worldBoundsRadius)
                {
                    Debug.LogWarning($"U3DPullable: Object '{name}' went beyond world radius ({Vector3.Distance(Vector3.zero, transform.position):F1}m)");
                    needsReset = true;
                }

                if (needsReset)
                {
                    if (destroyOnOutOfBounds)
                    {
                        U3DDestroyable destroyable = GetComponent<U3DDestroyable>();
                        if (destroyable != null)
                            destroyable.RequestDestroy();
                        else
                            Debug.LogWarning($"U3DPullable: '{name}' has Destroy On Out Of Bounds enabled but no U3DDestroyable component.");
                    }
                    else
                    {
                        ResetToSpawnPosition();
                    }
                }
            }
        }

        private void ResetToSpawnPosition()
        {
            if (isNetworked && (Object == null || !Object.HasStateAuthority)) return;

            if (isPullActive)
                EndPull();

            SetPhysicsState(PhysicsState.Resetting);

            if (hasNetworkRb3D && networkRigidbody != null)
            {
                networkRigidbody.Teleport(originalPosition, originalRotation);
            }
            else
            {
                transform.position = originalPosition;
                transform.rotation = originalRotation;
            }

            SetPhysicsState(PhysicsState.Sleeping);

            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkIsPulling = false;
                NetworkIsPhysicsActive = false;
            }

            if (labelUI != null) labelUI.gameObject.SetActive(true);
            OnWorldBoundsReset?.Invoke();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (isNetworked && Object == null) return;

            bool wasActive = isNetworked ?
                NetworkIsPhysicsActive : (currentPhysicsState == PhysicsState.Active);

            if (wasActive && collision.relativeVelocity.magnitude > 1.5f)
            {
                OnImpact?.Invoke();
            }
        }

        private void ApplyPullResistanceToMass()
        {
            if (rb != null)
                rb.mass = pullResistance;
        }

        // Public methods
        public void StopPull()
        {
            if (isPullActive)
                EndPull();
        }

        public void PutToSleep()
        {
            if (isPullActive)
                EndPull();
            ReturnToPullableSleepState();
        }

        public void ResetToSpawn()
        {
            ResetToSpawnPosition();
        }

        public void UpdateSpawnPosition(Vector3 newPosition, Quaternion newRotation)
        {
            originalPosition = newPosition;
            originalRotation = newRotation;
        }

        // IU3DInteractable implementation
        public void OnInteract()
        {
            if (isPullActive)
                EndPull();
            else if (CanStartPull())
                StartPull();
        }

        public void OnPlayerEnterRange() { }
        public void OnPlayerExitRange() { }

        public bool CanInteract()
        {
            if (isPullActive) return true;
            return CanStartPull();
        }

        public string GetInteractionPrompt()
        {
            if (grabbable != null && grabbable.IsGrabbed)
                return "Cannot pull while grabbed";
            if (isRequestingAuthority) return "Requesting...";
            if (isPullActive)
                return $"Stop Pulling ({pullKey})";
            return $"Pull ({pullKey})";
        }

        private bool CanStartPull()
        {
            if (grabbable != null && grabbable.IsGrabbed)
                return false;

            if (!isInPullRange)
                return false;

            if (isNetworked)
            {
                if (Object == null) return false;
                // Allow — authority requested in StartPull if needed
            }

            return true;
        }

        // Public properties
        public bool IsPullActive => isPullActive;
        public bool IsInPullRange => isInPullRange;
        public bool IsNetworked => isNetworked;
        public PhysicsState CurrentPhysicsState => currentPhysicsState;
        public Vector3 OriginalPosition => originalPosition;
        public Quaternion OriginalRotation => originalRotation;
        public bool HasNetworkRigidbody => networkRigidbody != null;
        public bool IsPhysicsActive => isNetworked ? NetworkIsPhysicsActive : (currentPhysicsState == PhysicsState.Active);
        public KeyCode PullKey { get => pullKey; set => pullKey = value; }

        private void OnDestroy()
        {
            if (boundsCheckCoroutine != null)
                StopCoroutine(boundsCheckCoroutine);

            if (isPullActive && cachedNetworkedAnimator != null)
                cachedNetworkedAnimator.SetAnimationBool("IsPulling", false);
        }

        private void OnValidate()
        {
            if (pullResistance <= 0f)
                Debug.LogWarning("U3DPullable: Pull Resistance should be greater than 0");

            if (maxPullDistance <= 0f)
                Debug.LogWarning("U3DPullable: Max pull distance should be positive");

            ApplyPullResistanceToMass();
        }
    }
}