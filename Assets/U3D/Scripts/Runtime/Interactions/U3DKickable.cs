using UnityEngine;
using UnityEngine.Events;
using Fusion;
using Fusion.Addons.Physics;
using System.Collections;

namespace U3D
{
    /// <summary>
    /// Kickable interaction component for objects that can be kicked by player feet
    /// Supports both networked and non-networked modes with Unity 6.1+ standards
    /// Integrates with Photon Fusion 2 for multiplayer with Shared Mode topology
    /// Uses camera-based kick direction similar to throwable physics
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    public class U3DKickable : NetworkBehaviour, IU3DInteractable
    {
        [Header("Kick Configuration")]
        [Tooltip("Base kick force multiplier")]
        [SerializeField] private float kickForce = 8f;

        [Tooltip("Additional upward force when kicking")]
        [SerializeField] private float upwardKickBoost = 1.5f;

        [Tooltip("Maximum kick velocity")]
        [SerializeField] private float maxKickVelocity = 15f;

        [Tooltip("Minimum velocity required to trigger kick events")]
        [SerializeField] private float minKickVelocity = 0.8f;

        [Header("Interaction Settings")]
        [Tooltip("Key to trigger kick (remappable)")]
        [SerializeField] private KeyCode kickKey = KeyCode.R;

        [Tooltip("Maximum distance to kick from")]
        [SerializeField] private float maxKickDistance = 1.5f;

        [Tooltip("Ground-level detection radius for foot collision")]
        [SerializeField] private float kickDetectionRadius = 1.2f;

        [Header("Starting State")]
        [Tooltip("When enabled, object spawns with gravity active and falls to the ground before becoming kickable. Use this for objects spawned above ground level.")]
        [SerializeField] private bool startActive = false;

        [Header("Optional Label")]
        [Tooltip("Assign a U3DWorldspaceUI in your scene to show a label near this object. Edit the text on that object directly.")]
        public U3DWorldspaceUI labelUI;

        [Header("Events")]
        [Tooltip("Called when object is kicked")]
        public UnityEvent OnKicked;

        [Tooltip("Called when kicked object hits something")]
        public UnityEvent OnImpact;

        [Tooltip("Called when object goes to sleep after kick")]
        public UnityEvent OnSleep;

        [Tooltip("Called when player enters kick range")]
        public UnityEvent OnEnterKickRange;

        [Tooltip("Called when player exits kick range")]
        public UnityEvent OnExitKickRange;

        [Tooltip("Called when kick attempt fails")]
        public UnityEvent OnKickFailed;

        [Tooltip("Called when object is reset due to world bounds violation")]
        public UnityEvent OnWorldBoundsReset;

        // HIDDEN PHYSICS MANAGEMENT - Optimal defaults
        [HideInInspector]
        [SerializeField] private float sleepCheckDelay = 1.5f;
        [HideInInspector]
        [SerializeField] private float sleepVelocityThreshold = 0.3f;
        [HideInInspector]
        [SerializeField] private float maxActiveTime = 8f;

        // HIDDEN WORLD BOUNDS SAFETY
        [HideInInspector]
        [SerializeField] private float worldBoundsFloor = -50f;
        [HideInInspector]
        [SerializeField] private float worldBoundsRadius = 1000f;
        [HideInInspector]
        [SerializeField] private float boundsCheckInterval = 1f;

        // Network state for physics management
        [Networked] public bool NetworkIsKicked { get; set; }
        [Networked] public bool NetworkIsPhysicsActive { get; set; }
        [Networked] public TickTimer NetworkSleepTimer { get; set; }

        // Components
        private Rigidbody rb;
        private U3DGrabbable grabbable;
        private Camera playerCamera;
        private Transform playerTransform;
        private NetworkObject networkObject;
        private NetworkRigidbody3D networkRigidbody;
        private bool hasNetworkRb3D = false;
        private Collider col;

        // State tracking
        private bool isNetworked = false;
        private bool isInKickRange = false;
        private float lastRangeCheckTime;
        private Coroutine boundsCheckCoroutine;

        // Authority request management (modeled on U3DGrabbable)
        private bool isRequestingAuthority = false;
        private float authorityRequestTime = 0f;
        private const float AUTHORITY_REQUEST_TIMEOUT = 2f;

        // Deferred kick state — stored when authority is requested, executed on grant
        private bool hasPendingKick = false;
        private Vector3 pendingKickDirection;
        private float pendingKickForce;
        private bool pendingKickUsesCamera = false;

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

        public enum PhysicsState
        {
            Sleeping,      // Kinematic, no gravity - kickable state
            Active,        // Non-kinematic, gravity - physics simulation after kick
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

            NetworkIsKicked = false;
            NetworkIsPhysicsActive = false;

            InitializePhysicsState();
        }

        private void Start()
        {
            FindPlayerComponents();
            RecordOriginalTransform();

            if (!isNetworked)
            {
                InitializePhysicsState();
            }

            StartBoundsMonitoring();
            CheckForInputConflicts();
        }

        private void Update()
        {
            UpdatePlayerProximity();

            // Authority request timeout
            if (isRequestingAuthority && Time.time - authorityRequestTime > AUTHORITY_REQUEST_TIMEOUT)
            {
                Debug.LogWarning($"U3DKickable: Authority request timeout for {name}");
                isRequestingAuthority = false;
                hasPendingKick = false;
                OnKickFailed?.Invoke();
            }
        }

        /// <summary>
        /// Check if kick key was pressed using Input System (following existing pattern)
        /// </summary>
        private bool WasKickKeyPressed()
        {
            if (UnityEngine.InputSystem.Keyboard.current == null) return false;

            switch (kickKey)
            {
                case KeyCode.E:
                    return UnityEngine.InputSystem.Keyboard.current.eKey.wasPressedThisFrame;
                case KeyCode.F:
                    return UnityEngine.InputSystem.Keyboard.current.fKey.wasPressedThisFrame;
                case KeyCode.R:
                    return UnityEngine.InputSystem.Keyboard.current.rKey.wasPressedThisFrame;
                case KeyCode.T:
                    return UnityEngine.InputSystem.Keyboard.current.tKey.wasPressedThisFrame;
                case KeyCode.G:
                    return UnityEngine.InputSystem.Keyboard.current.gKey.wasPressedThisFrame;
                case KeyCode.Q:
                    return UnityEngine.InputSystem.Keyboard.current.qKey.wasPressedThisFrame;
                case KeyCode.X:
                    return UnityEngine.InputSystem.Keyboard.current.xKey.wasPressedThisFrame;
                case KeyCode.Z:
                    return UnityEngine.InputSystem.Keyboard.current.zKey.wasPressedThisFrame;
                case KeyCode.V:
                    return UnityEngine.InputSystem.Keyboard.current.vKey.wasPressedThisFrame;
                case KeyCode.B:
                    return UnityEngine.InputSystem.Keyboard.current.bKey.wasPressedThisFrame;
                case KeyCode.C:
                    return UnityEngine.InputSystem.Keyboard.current.cKey.wasPressedThisFrame;
                case KeyCode.Space:
                    return UnityEngine.InputSystem.Keyboard.current.spaceKey.wasPressedThisFrame;
                case KeyCode.LeftShift:
                    return UnityEngine.InputSystem.Keyboard.current.leftShiftKey.wasPressedThisFrame;
                case KeyCode.Tab:
                    return UnityEngine.InputSystem.Keyboard.current.tabKey.wasPressedThisFrame;
                case KeyCode.Alpha1:
                    return UnityEngine.InputSystem.Keyboard.current.digit1Key.wasPressedThisFrame;
                case KeyCode.Alpha2:
                    return UnityEngine.InputSystem.Keyboard.current.digit2Key.wasPressedThisFrame;
                case KeyCode.Alpha3:
                    return UnityEngine.InputSystem.Keyboard.current.digit3Key.wasPressedThisFrame;
                case KeyCode.Alpha4:
                    return UnityEngine.InputSystem.Keyboard.current.digit4Key.wasPressedThisFrame;
                case KeyCode.Alpha5:
                    return UnityEngine.InputSystem.Keyboard.current.digit5Key.wasPressedThisFrame;
                default:
                    return false;
            }
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

            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkIsPhysicsActive = true;
            }
        }

        private void CheckForInputConflicts()
        {
            if (grabbable != null)
            {
                if (kickKey == KeyCode.R)
                {
                    kickKey = KeyCode.T;
                }
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!isNetworked || !Object.HasStateAuthority) return;

            if (grabbable != null && grabbable.IsGrabbed) return;

            // Start Active settle: if physics is active but the object hasn't been kicked,
            // it's settling under gravity. Check if it has come to rest so we can transition
            // to the normal kickable sleep state, ready for player interaction.
            if (!NetworkIsKicked && NetworkIsPhysicsActive)
            {
                if (rb.linearVelocity.magnitude < sleepVelocityThreshold &&
                    rb.angularVelocity.magnitude < sleepVelocityThreshold)
                {
                    ReturnToKickableSleepState();
                }
                return;
            }

            if (NetworkIsKicked && NetworkIsPhysicsActive)
            {
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
                    ReturnToKickableSleepState();
                }
            }
        }

        public override void Render()
        {
            if (!isNetworked) return;

            PhysicsState networkState = NetworkIsPhysicsActive ? PhysicsState.Active : PhysicsState.Sleeping;

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
                isRequestingAuthority = false;

                // Authority granted — execute the deferred kick if one is pending
                if (hasPendingKick)
                {
                    hasPendingKick = false;

                    if (pendingKickUsesCamera)
                    {
                        ExecuteCameraKick();
                    }
                    else
                    {
                        ExecuteDirectionalKick(pendingKickDirection, pendingKickForce);
                    }
                }
                else
                {
                    SyncNetworkPhysicsState();
                }
            }
            else
            {
                // Lost authority — cancel any pending kick
                isRequestingAuthority = false;
                hasPendingKick = false;
                SyncLocalPhysicsState();
            }
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

                    if (!isNetworked)
                    {
                        rb.useGravity = false;
                        rb.isKinematic = true;
                    }
                    break;

                case PhysicsState.Active:
                    if (!isNetworked)
                    {
                        rb.isKinematic = false;
                        rb.useGravity = true;
                    }
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

        private void FindPlayerComponents()
        {
            U3DPlayerController playerController = U3DPlayerController.FindLocalPlayer();
            if (playerController != null)
            {
                playerTransform = playerController.transform;
                playerCamera = playerController.GetComponentInChildren<Camera>();
            }

            if (playerCamera == null)
            {
                playerCamera = Camera.main;
            }
        }

        private bool CanAttemptKick()
        {
            if (grabbable != null && grabbable.IsGrabbed)
            {
                return false;
            }

            if (!isInKickRange)
            {
                return false;
            }

            // Authority is no longer a gate here — we request it if we don't have it.
            // Only block if we're already mid-request to prevent spamming.
            if (isNetworked && isRequestingAuthority)
            {
                return false;
            }

            return true;
        }

        public void Kick()
        {
            if (!CanAttemptKick()) return;

            if (playerCamera == null || playerTransform == null)
            {
                FindPlayerComponents();
                if (playerCamera == null)
                {
                    Debug.LogWarning("U3DKickable: No player camera found - cannot determine kick direction");
                    OnKickFailed?.Invoke();
                    return;
                }
            }

            if (!isNetworked)
            {
                ExecuteCameraKick();
                return;
            }

            if (Object.HasStateAuthority)
            {
                ExecuteCameraKick();
            }
            else
            {
                // Defer the kick until authority is granted
                RequestKickAuthority(useCamera: true, direction: Vector3.zero, force: 0f);
            }
        }

        /// <summary>
        /// Public method to manually kick with specific direction and force.
        /// Requests authority if needed in networked mode.
        /// </summary>
        public void KickInDirection(Vector3 direction, float force)
        {
            if (grabbable != null && grabbable.IsGrabbed) return;
            if (isNetworked && isRequestingAuthority) return;

            if (!isNetworked)
            {
                ExecuteDirectionalKick(direction, force);
                return;
            }

            if (Object.HasStateAuthority)
            {
                ExecuteDirectionalKick(direction, force);
            }
            else
            {
                RequestKickAuthority(useCamera: false, direction: direction, force: force);
            }
        }

        /// <summary>
        /// Public method to kick in camera direction with custom force.
        /// Requests authority if needed in networked mode.
        /// </summary>
        public void KickInCameraDirection(float customForce = -1f)
        {
            if (playerCamera == null)
            {
                FindPlayerComponents();
                if (playerCamera == null)
                {
                    Debug.LogWarning("U3DKickable: No camera found for KickInCameraDirection");
                    return;
                }
            }

            float useForce = customForce > 0f ? customForce : kickForce;
            Vector3 kickDirection = playerCamera.transform.forward;
            kickDirection.y += upwardKickBoost / Mathf.Max(0.01f, useForce);
            kickDirection.Normalize();

            KickInDirection(kickDirection, useForce);
        }

        private void RequestKickAuthority(bool useCamera, Vector3 direction, float force)
        {
            if (isRequestingAuthority) return;

            isRequestingAuthority = true;
            authorityRequestTime = Time.time;

            hasPendingKick = true;
            pendingKickUsesCamera = useCamera;
            pendingKickDirection = direction;
            pendingKickForce = force;

            Object.RequestStateAuthority();
        }

        /// <summary>
        /// Execute a camera-directed kick. Only call when we have authority (or non-networked).
        /// </summary>
        private void ExecuteCameraKick()
        {
            SetPhysicsState(PhysicsState.Active);
            StartCoroutine(ApplyKickVelocityAfterPhysicsActivation());
        }

        /// <summary>
        /// Execute a directional kick with explicit direction and force.
        /// Only call when we have authority (or non-networked).
        /// </summary>
        private void ExecuteDirectionalKick(Vector3 direction, float force)
        {
            SetPhysicsState(PhysicsState.Active);

            Vector3 kickVelocity = direction.normalized * force;
            if (kickVelocity.magnitude > maxKickVelocity)
            {
                kickVelocity = kickVelocity.normalized * maxKickVelocity;
            }

            rb.linearVelocity = kickVelocity;

            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkIsKicked = true;
                NetworkSleepTimer = TickTimer.CreateFromSeconds(Runner, maxActiveTime);
            }

            OnKicked?.Invoke();
        }

        private IEnumerator ApplyKickVelocityAfterPhysicsActivation()
        {
            yield return null;

            float useForce = kickForce;
            Vector3 kickDirection = playerCamera.transform.forward;
            kickDirection.y += upwardKickBoost / Mathf.Max(0.01f, useForce);
            kickDirection.Normalize();

            Vector3 kickVelocity = kickDirection * useForce;
            if (kickVelocity.magnitude > maxKickVelocity)
                kickVelocity = kickVelocity.normalized * maxKickVelocity;

            const int maxTries = 3;
            int tries = 0;

            while (rb != null && rb.isKinematic && tries < maxTries)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                tries++;
                yield return null;
            }

            if (rb != null && !rb.isKinematic)
            {
                rb.linearVelocity = kickVelocity;
            }
            else
            {
                Debug.LogWarning("U3DKickable: Could not apply kick velocity (Rigidbody still kinematic or null).");
                SetPhysicsState(PhysicsState.Sleeping);
                OnKickFailed?.Invoke();
                yield break;
            }

            if (kickVelocity.magnitude >= minKickVelocity)
            {
                if (isNetworked && Object.HasStateAuthority)
                {
                    NetworkIsKicked = true;
                    NetworkSleepTimer = TickTimer.CreateFromSeconds(Runner, maxActiveTime);
                }
                if (labelUI != null) labelUI.gameObject.SetActive(false);
                OnKicked?.Invoke();
            }
            else
            {
                SetPhysicsState(PhysicsState.Sleeping);
            }
        }

        private void ReturnToKickableSleepState()
        {
            SetPhysicsState(PhysicsState.Sleeping);

            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkIsKicked = false;
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

            Vector3 playerGroundPosition = new Vector3(playerTransform.position.x, transform.position.y, playerTransform.position.z);
            float distanceToPlayer = Vector3.Distance(transform.position, playerGroundPosition);

            bool wasInRange = isInKickRange;
            isInKickRange = distanceToPlayer <= maxKickDistance;

            if (isInKickRange && !wasInRange)
            {
                OnEnterKickRange?.Invoke();
            }
            else if (!isInKickRange && wasInRange)
            {
                OnExitKickRange?.Invoke();
            }
        }

        private void FindPlayer()
        {
            U3DPlayerController playerController = U3DPlayerController.FindLocalPlayer();
            if (playerController != null)
            {
                playerTransform = playerController.transform;
                playerCamera = playerController.GetComponentInChildren<Camera>();
            }
            else
            {
                playerTransform = null;
                playerCamera = null;
            }
        }

        private IEnumerator MonitorWorldBounds()
        {
            while (true)
            {
                yield return new WaitForSeconds(boundsCheckInterval);

                if (grabbable != null && grabbable.IsGrabbed)
                {
                    continue;
                }

                if (isNetworked && (Object == null || !Object.HasStateAuthority))
                {
                    continue;
                }

                bool needsReset = false;

                if (transform.position.y < worldBoundsFloor)
                {
                    Debug.LogWarning($"U3DKickable: Object '{name}' fell below world bounds (Y: {transform.position.y})");
                    needsReset = true;
                }
                else if (Vector3.Distance(Vector3.zero, transform.position) > worldBoundsRadius)
                {
                    Debug.LogWarning($"U3DKickable: Object '{name}' went beyond world radius ({Vector3.Distance(Vector3.zero, transform.position):F1}m)");
                    needsReset = true;
                }

                if (needsReset)
                {
                    ResetToSpawnPosition();
                }
            }
        }

        private void ResetToSpawnPosition()
        {
            if (isNetworked && !Object.HasStateAuthority) return;

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
                NetworkIsKicked = false;
                NetworkIsPhysicsActive = false;
            }

            if (labelUI != null) labelUI.gameObject.SetActive(true);
            OnWorldBoundsReset?.Invoke();
        }

        private void OnCollisionEnter(Collision collision)
        {
            bool wasKicked = isNetworked ? NetworkIsKicked : (currentPhysicsState == PhysicsState.Active);

            if (wasKicked && collision.relativeVelocity.magnitude > 1.5f)
            {
                OnImpact?.Invoke();
            }
        }

        // Public method to manually put object to sleep
        public void PutToSleep()
        {
            ReturnToKickableSleepState();
        }

        // Public method to update spawn position (useful for dynamic spawn points)
        public void UpdateSpawnPosition(Vector3 newPosition, Quaternion newRotation)
        {
            originalPosition = newPosition;
            originalRotation = newRotation;
        }

        // IU3DInteractable implementation
        public void OnInteract()
        {
            if (CanAttemptKick())
            {
                Kick();
            }
            else
            {
                OnKickFailed?.Invoke();
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
            return CanAttemptKick();
        }

        public int GetInteractionPriority()
        {
            return 30;
        }

        public string GetInteractionPrompt()
        {
            if (grabbable != null && grabbable.IsGrabbed)
            {
                return "Cannot kick while grabbed";
            }
            if (isRequestingAuthority) return "Requesting...";
            return $"Kick ({kickKey})";
        }

        // Public properties for inspection
        public bool HasBeenKicked => isNetworked ? NetworkIsKicked : (currentPhysicsState == PhysicsState.Active);
        public bool IsInKickRange => isInKickRange;
        public bool IsNetworked => isNetworked;
        public PhysicsState CurrentPhysicsState => currentPhysicsState;
        public Vector3 OriginalPosition => originalPosition;
        public Quaternion OriginalRotation => originalRotation;
        public bool HasNetworkRigidbody => networkRigidbody != null;
        public bool IsPhysicsActive => isNetworked ? NetworkIsPhysicsActive : (currentPhysicsState == PhysicsState.Active);
        public KeyCode KickKey { get => kickKey; set => kickKey = value; }
        public bool IsRequestingAuthority => isRequestingAuthority;

        private void OnDestroy()
        {
            if (boundsCheckCoroutine != null)
            {
                StopCoroutine(boundsCheckCoroutine);
            }
        }

        private void OnValidate()
        {
            if (kickForce <= 0f)
            {
                Debug.LogWarning("U3DKickable: Kick force should be greater than 0");
            }

            if (maxKickVelocity < kickForce)
            {
                Debug.LogWarning("U3DKickable: Max kick velocity is less than kick force - kicks will be clamped");
            }

            if (sleepVelocityThreshold < 0f)
            {
                Debug.LogWarning("U3DKickable: Sleep velocity threshold should be positive");
            }

            if (maxKickDistance <= 0f)
            {
                Debug.LogWarning("U3DKickable: Max kick distance should be positive");
            }

            if (kickDetectionRadius <= 0f)
            {
                Debug.LogWarning("U3DKickable: Kick detection radius should be positive");
            }
        }
    }
}