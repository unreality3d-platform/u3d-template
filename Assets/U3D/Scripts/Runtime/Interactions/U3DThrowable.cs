using UnityEngine;
using UnityEngine.Events;
using Fusion;
using Fusion.Addons.Physics;
using System.Collections;

namespace U3D
{
    /// <summary>
    /// Proper physics state management that works with NetworkRigidbody3D
    /// Eliminates conflicts with Fusion 2's automatic interpolation system
    /// Handles authority-based physics control for Shared Mode
    /// Throw is triggered on release from U3DGrabbable using the configured grab/release key
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class U3DThrowable : NetworkBehaviour
    {
        [Header("Throw Configuration")]
        [Tooltip("Base throw force multiplier")]
        [SerializeField] private float throwForce = 10f;

        [Tooltip("Additional upward force when throwing")]
        [SerializeField] private float upwardThrowBoost = 2f;

        [Tooltip("Maximum throw velocity")]
        [SerializeField] private float maxThrowVelocity = 20f;

        [Tooltip("Minimum velocity required to trigger throw events")]
        [SerializeField] private float minThrowVelocity = 1f;

        [Tooltip("Drop straight down on release instead of throwing. Ignores Throw Configuration.")]
        [SerializeField] private bool dropOnRelease = false;

        [Header("Starting State")]
        [Tooltip("When enabled, object spawns with gravity active and falls to the ground before becoming throwable. Use this for objects spawned above ground level.")]
        [SerializeField] private bool startActive = false;

        [Header("Impact Filter")]
        [Tooltip("Only fire OnImpact for collisions with a specific tag")]
        [SerializeField] private bool requireTag = false;

        [Tooltip("Tag required to fire OnImpact")]
        [SerializeField] private string requiredTag = "Player";

        [Header("Events")]
        [Tooltip("Called when object is thrown")]
        public UnityEvent OnThrown;

        [Tooltip("Called when thrown object hits something")]
        public UnityEvent OnImpact;

        [Tooltip("Called when object goes to sleep")]
        public UnityEvent OnSleep;

        [Tooltip("Called when object is reset due to world bounds violation")]
        public UnityEvent OnWorldBoundsReset;

        // HIDDEN PHYSICS MANAGEMENT - Optimal defaults
        [HideInInspector]
        [SerializeField] private float sleepVelocityThreshold = 0.5f;
        [HideInInspector]
        [SerializeField] private float maxActiveTime = 10f;

        // HIDDEN WORLD BOUNDS SAFETY
        [HideInInspector]
        [SerializeField] private float worldBoundsFloor = -50f;
        [HideInInspector]
        [SerializeField] private float worldBoundsRadius = 1000f;
        [HideInInspector]
        [SerializeField] private float boundsCheckInterval = 1f;

        // Network state for physics management

        [Networked] public bool NetworkAwaitingMotion { get; set; }
        [Networked] public bool NetworkIsThrown { get; set; }
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
        private U3DPlayerController playerController;

        // State tracking
        private bool isNetworked = false;
        private Coroutine boundsCheckCoroutine;
        private Coroutine throwVelocityCoroutine;

        // Physics state management
        private PhysicsState currentPhysicsState = PhysicsState.Sleeping;
        private PhysicsState lastNetworkPhysicsState = PhysicsState.Sleeping;

        // Original position and rotation for reset purposes
        private Vector3 originalPosition;
        private Quaternion originalRotation;
        private bool hasRecordedOriginalTransform = false;

        public enum PhysicsState
        {
            Sleeping,      // Kinematic, no gravity - grabbable state
            Grabbed,       // Kinematic, no gravity - held in hand
            Active,        // Non-kinematic, gravity - physics simulation
            Resetting      // Temporarily kinematic while resetting position
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            grabbable = GetComponent<U3DGrabbable>();
            networkRigidbody = GetComponent<NetworkRigidbody3D>();
            hasNetworkRb3D = networkRigidbody != null;

            networkObject = GetComponent<NetworkObject>();
            isNetworked = networkObject != null;

            if (grabbable == null)
            {
                Debug.LogError("U3DThrowable requires U3DGrabbable component!");
                enabled = false;
                return;
            }
        }

        public override void Spawned()
        {
            if (!isNetworked) return;

            NetworkIsThrown = false;
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

            if (grabbable != null)
            {
                grabbable.OnReleased.AddListener(OnObjectReleased);
                grabbable.OnGrabbed.AddListener(OnObjectGrabbed);
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

        public void OnStateAuthorityChanged()
        {
            if (!isNetworked) return;

            if (Object.HasStateAuthority)
            {
                SyncNetworkPhysicsState();
            }
            else
            {
                SyncLocalPhysicsState();
            }
        }

        public override void Render()
        {
            if (!isNetworked) return;

            PhysicsState networkState = NetworkIsPhysicsActive ? PhysicsState.Active : PhysicsState.Sleeping;

            if (grabbable != null && grabbable.IsGrabbed)
            {
                networkState = PhysicsState.Grabbed;
            }

            if (networkState != lastNetworkPhysicsState)
            {
                if (!Object.HasStateAuthority)
                {
                    ApplyPhysicsStateFromNetwork(networkState);
                }
                lastNetworkPhysicsState = networkState;
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
                case PhysicsState.Grabbed:
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
            if (grabbable != null && grabbable.IsGrabbed)
            {
                networkState = PhysicsState.Grabbed;
            }

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

        private void StartBoundsMonitoring()
        {
            if (boundsCheckCoroutine == null)
            {
                boundsCheckCoroutine = StartCoroutine(MonitorWorldBounds());
            }
        }

        private void FindPlayerComponents()
        {
            playerController = U3DPlayerController.FindLocalPlayer();
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

        private void OnObjectGrabbed()
        {
            if (throwVelocityCoroutine != null)
            {
                StopCoroutine(throwVelocityCoroutine);
                throwVelocityCoroutine = null;
            }

            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkIsThrown = false;
                NetworkAwaitingMotion = false;
            }

            if (hasNetworkRb3D && networkRigidbody != null)
            {
                networkRigidbody.enabled = false;
            }

            SetPhysicsState(PhysicsState.Grabbed);

            if (playerCamera == null || playerTransform == null)
            {
                FindPlayerComponents();
            }
        }

        private void OnObjectReleased()
        {
            if (isNetworked && !Object.HasStateAuthority) return;

            if (dropOnRelease)
            {
                if (hasNetworkRb3D && networkRigidbody != null)
                {
                    networkRigidbody.enabled = true;
                    networkRigidbody.Teleport(transform.position, transform.rotation);
                }

                rb.isKinematic = false;
                rb.useGravity = true;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;

                StartCoroutine(ExtendPlayerCollisionIgnore());

                currentPhysicsState = PhysicsState.Active;

                if (isNetworked && Object.HasStateAuthority)
                {
                    NetworkIsPhysicsActive = true;
                    NetworkIsThrown = false;
                    NetworkAwaitingMotion = true;
                    NetworkSleepTimer = TickTimer.CreateFromSeconds(Runner, maxActiveTime);
                }
                return;
            }

            // Throw path — unchanged except add the awaiting-motion flag
            if (hasNetworkRb3D && networkRigidbody != null)
            {
                networkRigidbody.enabled = true;
                networkRigidbody.Teleport(transform.position, transform.rotation);
            }

            if (playerCamera == null)
            {
                FindPlayerComponents();
                if (playerCamera == null)
                {
                    Debug.LogWarning("U3DThrowable: No player camera found - cannot determine throw direction");
                    SetPhysicsState(PhysicsState.Sleeping);
                    return;
                }
            }

            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
            }

            SetPhysicsState(PhysicsState.Active);

            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkAwaitingMotion = true;
            }

            throwVelocityCoroutine = StartCoroutine(ApplyThrowVelocityAfterPhysicsActivation());
        }

        private IEnumerator ExtendPlayerCollisionIgnore()
        {
            U3DPlayerController player = U3DPlayerController.FindLocalPlayer();
            if (player == null) yield break;

            CharacterController cc = player.GetComponent<CharacterController>();
            Collider myCollider = GetComponent<Collider>();
            if (cc == null || myCollider == null) yield break;

            Physics.IgnoreCollision(myCollider, cc, true);
            yield return new WaitForSeconds(0.5f);
            if (myCollider != null && cc != null)
                Physics.IgnoreCollision(myCollider, cc, false);
        }

        private IEnumerator ApplyThrowVelocityAfterPhysicsActivation()
        {
            yield return null;

            float useForce = throwForce;
            Vector3 throwDirection = GetThrowDirection();
            throwDirection.y += upwardThrowBoost / Mathf.Max(0.01f, useForce);
            throwDirection.Normalize();

            Vector3 throwVelocity = throwDirection * useForce;
            if (throwVelocity.magnitude > maxThrowVelocity)
                throwVelocity = throwVelocity.normalized * maxThrowVelocity;

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
                rb.linearVelocity = throwVelocity;
            }
            else
            {
                Debug.LogWarning("U3DThrowable: Could not apply throw velocity (Rigidbody still kinematic or null).");
                SetPhysicsState(PhysicsState.Sleeping);
                throwVelocityCoroutine = null;
                yield break;
            }

            if (throwVelocity.magnitude >= minThrowVelocity)
            {
                if (isNetworked && Object.HasStateAuthority)
                {
                    NetworkIsThrown = true;
                    NetworkSleepTimer = TickTimer.CreateFromSeconds(Runner, maxActiveTime);
                }
                OnThrown?.Invoke();
            }
            else
            {
                SetPhysicsState(PhysicsState.Sleeping);
            }

            throwVelocityCoroutine = null;
        }

        private Vector3 GetThrowDirection()
        {
            if (playerCamera != null)
                return playerCamera.transform.forward;

            if (playerTransform != null)
                return playerTransform.forward;

            return Vector3.forward;
        }

        public override void FixedUpdateNetwork()
        {
            if (!isNetworked || !Object.HasStateAuthority) return;

            if (grabbable != null && grabbable.IsGrabbed) return;

            if (NetworkIsPhysicsActive)
            {
                if (NetworkAwaitingMotion)
                {
                    // Don't check sleep yet. Wait until the object has actually moved
                    // (gravity has accelerated it past threshold) OR it's made contact
                    // with something (collision callback clears this flag too).
                    if (rb.linearVelocity.magnitude > sleepVelocityThreshold ||
                        rb.angularVelocity.magnitude > sleepVelocityThreshold)
                    {
                        NetworkAwaitingMotion = false;
                    }
                    else if (NetworkSleepTimer.Expired(Runner))
                    {
                        // Safety: maxActiveTime elapsed and it never moved — force sleep
                        NetworkAwaitingMotion = false;
                        ReturnToGrabbableSleepState();
                    }
                    return;
                }

                // Object has proven it's in motion. Now check for settle.
                bool shouldSleep = false;

                if (NetworkSleepTimer.Expired(Runner))
                {
                    shouldSleep = true;
                }
                else if (NetworkSleepTimer.IsRunning)
                {
                    if (rb.linearVelocity.magnitude < sleepVelocityThreshold &&
                        rb.angularVelocity.magnitude < sleepVelocityThreshold)
                    {
                        shouldSleep = true;
                    }
                }

                if (shouldSleep)
                {
                    ReturnToGrabbableSleepState();
                }
            }
        }

        private void ReturnToGrabbableSleepState()
        {
            if (hasNetworkRb3D && networkRigidbody != null)
            {
                if (!networkRigidbody.enabled)
                {
                    networkRigidbody.Teleport(transform.position, transform.rotation);
                    networkRigidbody.enabled = true;
                }
                networkRigidbody.SyncParent = true;
            }

            SetPhysicsState(PhysicsState.Sleeping);

            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkIsThrown = false;
                NetworkIsPhysicsActive = false;
                NetworkAwaitingMotion = false;
            }

            OnSleep?.Invoke();
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
                    Debug.LogWarning($"U3DThrowable: Object '{name}' fell below world bounds (Y: {transform.position.y})");
                    needsReset = true;
                }
                else if (Vector3.Distance(Vector3.zero, transform.position) > worldBoundsRadius)
                {
                    Debug.LogWarning($"U3DThrowable: Object '{name}' went beyond world radius ({Vector3.Distance(Vector3.zero, transform.position):F1}m)");
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
            if (isNetworked && (Object == null || !Object.HasStateAuthority)) return;

            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

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
                NetworkIsThrown = false;
                NetworkIsPhysicsActive = false;
            }

            OnWorldBoundsReset?.Invoke();
        }

        private void OnCollisionEnter(Collision collision)
        {
            // Don't count player body contact as proof of landing
            if (collision.gameObject.CompareTag("Player")) return;

            if (isNetworked && Object.HasStateAuthority && NetworkAwaitingMotion)
            {
                NetworkAwaitingMotion = false;
            }

            bool wasThrown = isNetworked ? NetworkIsThrown : (currentPhysicsState == PhysicsState.Active);

            if (wasThrown && collision.relativeVelocity.magnitude > 2f)
            {
                if (requireTag && !collision.gameObject.CompareTag(requiredTag))
                    return;

                OnImpact?.Invoke();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!requireTag) return;
            if (!other.CompareTag(requiredTag)) return;

            bool wasThrown = isNetworked ? NetworkIsThrown : (currentPhysicsState == PhysicsState.Active);

            if (wasThrown)
            {
                OnImpact?.Invoke();
            }
        }

        public void ThrowInDirection(Vector3 direction, float force)
        {
            if (isNetworked && !Object.HasStateAuthority) return;

            if (grabbable != null && grabbable.IsGrabbed)
            {
                grabbable.Release();
            }

            SetPhysicsState(PhysicsState.Active);

            Vector3 throwVelocity = direction.normalized * force;

            if (throwVelocity.magnitude > maxThrowVelocity)
            {
                throwVelocity = throwVelocity.normalized * maxThrowVelocity;
            }

            rb.linearVelocity = throwVelocity;

            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkIsThrown = true;
                NetworkSleepTimer = TickTimer.CreateFromSeconds(Runner, maxActiveTime);
            }

            OnThrown?.Invoke();
        }

        public void ThrowInCameraDirection(float customForce = -1f)
        {
            if (playerCamera == null || playerTransform == null)
            {
                FindPlayerComponents();
                if (playerCamera == null && playerTransform == null)
                {
                    Debug.LogWarning("U3DThrowable: No player found for ThrowInCameraDirection");
                    return;
                }
            }

            float useForce = customForce > 0f ? customForce : throwForce;
            Vector3 throwDirection = GetThrowDirection();
            throwDirection.y += upwardThrowBoost / useForce;
            throwDirection.Normalize();

            ThrowInDirection(throwDirection, useForce);
        }

        public void PutToSleep()
        {
            ReturnToGrabbableSleepState();
        }

        public void WakeUp()
        {
            if (grabbable == null || !grabbable.IsGrabbed)
            {
                SetPhysicsState(PhysicsState.Active);
            }
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

        public bool HasBeenThrown => isNetworked ? NetworkIsThrown : (currentPhysicsState == PhysicsState.Active);
        public bool IsCurrentlyGrabbed => grabbable != null && grabbable.IsGrabbed;
        public bool IsNetworked => isNetworked;
        public PhysicsState CurrentPhysicsState => currentPhysicsState;
        public Vector3 OriginalPosition => originalPosition;
        public Quaternion OriginalRotation => originalRotation;
        public bool HasNetworkRigidbody => networkRigidbody != null;
        public bool IsPhysicsActive => isNetworked ? NetworkIsPhysicsActive : (currentPhysicsState == PhysicsState.Active);

        private void OnDestroy()
        {
            if (boundsCheckCoroutine != null)
            {
                StopCoroutine(boundsCheckCoroutine);
            }

            if (grabbable != null)
            {
                grabbable.OnReleased.RemoveListener(OnObjectReleased);
                grabbable.OnGrabbed.RemoveListener(OnObjectGrabbed);
            }
        }

        private void OnValidate()
        {
            if (throwForce <= 0f)
            {
                Debug.LogWarning("U3DThrowable: Throw force should be greater than 0");
            }

            if (maxThrowVelocity < throwForce)
            {
                Debug.LogWarning("U3DThrowable: Max throw velocity is less than throw force - throws will be clamped");
            }

            if (sleepVelocityThreshold < 0f)
            {
                Debug.LogWarning("U3DThrowable: Sleep velocity threshold should be positive");
            }

            if (worldBoundsFloor > 0f)
            {
                Debug.LogWarning("U3DThrowable: World bounds floor should typically be negative (below ground level)");
            }

            if (worldBoundsRadius <= 0f)
            {
                Debug.LogWarning("U3DThrowable: World bounds radius should be positive");
            }
        }
    }
}