using UnityEngine;
using UnityEngine.Events;

namespace U3D
{
    /// <summary>
    /// Self-contained climbable surface. Add via Creator Dashboard "Make Climbable" button.
    /// Detects player proximity and facing direction, then manages climbing movement
    /// directly on the player's CharacterController.
    ///
    /// W = climb up, S = climb down, A/D = lateral movement, Space = detach.
    /// Works in both first-person and third-person camera modes.
    ///
    /// Follows the same self-contained pattern as U3DKickable/U3DPushable:
    /// all logic lives on the target object, not on the player prefab.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class U3DClimbable : MonoBehaviour
    {
        [Header("Detection")]
        [Tooltip("Maximum distance the player can be from this surface to begin climbing")]
        [SerializeField] private float maxClimbDistance = 1.5f;

        [Tooltip("Height offset from player origin for surface detection raycast (chest height)")]
        [SerializeField] private Vector3 detectionOffset = new Vector3(0, 0.8f, 0);

        [Tooltip("Radius of the detection sphere cast")]
        [SerializeField] private float detectionRadius = 0.4f;

        [Header("Climbing Movement")]
        [Tooltip("Vertical climb speed (up)")]
        [SerializeField] private float climbUpSpeed = 3f;

        [Tooltip("Vertical climb speed (down)")]
        [SerializeField] private float climbDownSpeed = 3f;

        [Tooltip("Lateral movement speed along the surface")]
        [SerializeField] private float climbLateralSpeed = 2.5f;

        [Tooltip("Movement speed multiplier for this specific surface")]
        [SerializeField] private float speedMultiplier = 1.0f;

        [Tooltip("How close the player stays to the climbable surface")]
        [SerializeField] private float surfaceStickDistance = 0.05f;

        [Tooltip("Brief cooldown after detaching before re-attach is allowed")]
        [SerializeField] private float reattachCooldown = 0.3f;

        [Header("Ledge Transition")]
        [Tooltip("When the surface normal is within this many degrees of straight up, the player automatically mantles over the edge. Lower values require the surface to be more horizontal before triggering.")]
        [SerializeField] private float ledgeAngleThreshold = 45f;

        [Tooltip("How far forward the player is nudged when mantling over a ledge, so they clear the edge geometry")]
        [SerializeField] private float ledgeForwardNudge = 0.4f;

        [Tooltip("How far upward the player is nudged when mantling over a ledge")]
        [SerializeField] private float ledgeUpwardNudge = 0.2f;

        [Header("Optional Label")]
        [Tooltip("Assign a U3DBillboardUI in your scene to show a label near this surface")]
        public U3DBillboardUI labelUI;

        [Header("Events")]
        [Tooltip("Fires when the player attaches to this surface and begins climbing")]
        public UnityEvent OnClimbStart;

        [Tooltip("Fires when the player detaches from this surface for any reason")]
        public UnityEvent OnClimbEnd;

        [Tooltip("Fires when the player jumps off this surface (before OnClimbEnd)")]
        public UnityEvent OnJumpOff;

        [Tooltip("Fires when the player mantles over the top edge of this surface (before OnClimbEnd)")]
        public UnityEvent OnLedgeTransition;

        [Tooltip("Fires when the player enters climbing range of this surface")]
        public UnityEvent OnPlayerEnterRange;

        [Tooltip("Fires when the player leaves climbing range of this surface")]
        public UnityEvent OnPlayerExitRange;

        public float SpeedMultiplier => speedMultiplier;
        public bool IsClimbing => isClimbing;
        public bool IsPlayerInRange => playerInRange;

        public const int CLIMBABLE_LAYER = 6;
        public const string CLIMBABLE_LAYER_NAME = "Climbable";

        private U3DPlayerController playerController;
        private CharacterController playerCharacterController;
        private Transform playerTransform;

        private bool isClimbing;
        private bool playerInRange;
        private Vector3 climbSurfaceNormal;
        private Vector3 lastSurfacePoint;
        private float detachTime;

        private Collider surfaceCollider;

        void Start()
        {
            surfaceCollider = GetComponent<Collider>();
            FindPlayer();
        }

        void Update()
        {
            if (playerController == null || !playerController.IsLocalPlayer)
            {
                FindPlayer();
                return;
            }

            UpdatePlayerProximity();

            if (isClimbing)
            {
                HandleClimbingMovement();
                CheckDetach();
            }
            else
            {
                CheckAttach();
            }
        }

        void FindPlayer()
        {
            U3DPlayerController controller = FindAnyObjectByType<U3DPlayerController>();
            if (controller != null && controller.IsLocalPlayer)
            {
                playerController = controller;
                playerTransform = controller.transform;
                playerCharacterController = controller.GetComponent<CharacterController>();
            }
            else
            {
                playerController = null;
                playerTransform = null;
                playerCharacterController = null;
            }
        }

        void UpdatePlayerProximity()
        {
            if (playerTransform == null) return;

            float distance = Vector3.Distance(
                surfaceCollider.ClosestPoint(playerTransform.position),
                playerTransform.position
            );

            bool wasInRange = playerInRange;
            playerInRange = distance <= maxClimbDistance;

            if (playerInRange && !wasInRange)
                OnPlayerEnterRange?.Invoke();
            else if (!playerInRange && wasInRange)
                OnPlayerExitRange?.Invoke();

            if (isClimbing && !playerInRange)
                Detach();
        }

        /// <summary>
        /// SphereCast from the player's chest toward their forward direction.
        /// Only returns true if the hit collider belongs to this climbable surface.
        /// </summary>
        bool DetectSurface(out RaycastHit hit)
        {
            Vector3 origin = playerTransform.position + detectionOffset;
            Vector3 direction = playerTransform.forward;

            if (Physics.SphereCast(origin, detectionRadius, direction, out hit, maxClimbDistance))
            {
                if (hit.collider == surfaceCollider || hit.collider.transform.IsChildOf(transform))
                {
                    climbSurfaceNormal = hit.normal;
                    lastSurfacePoint = hit.point;
                    return true;
                }
            }

            hit = default;
            return false;
        }

        /// <summary>
        /// Attach when the player is in range, pressing forward, and facing this surface.
        /// Won't attach if another climbable already has the player climbing.
        /// </summary>
        void CheckAttach()
        {
            if (!playerInRange) return;
            if (Time.time - detachTime < reattachCooldown) return;
            if (playerController.NetworkIsClimbing) return;

            Vector2 input = playerController.MoveInput;
            if (input.y <= 0.1f) return;

            if (DetectSurface(out _))
                Attach();
        }

        /// <summary>
        /// Detach on jump press, ledge transition, moving backward while grounded,
        /// or losing surface contact.
        /// </summary>
        void CheckDetach()
        {
            if (playerController.JumpPressedThisFrame)
            {
                playerController.ConsumeJumpPress();
                OnJumpOff?.Invoke();
                Detach();
                playerController.SetClimbDetachVelocity(new Vector3(0, 2f, 0));
                return;
            }

            if (CheckLedgeTransition())
                return;

            Vector2 input = playerController.MoveInput;
            if (input.y < -0.1f && playerCharacterController.isGrounded)
            {
                Detach();
                return;
            }

            if (!DetectSurface(out _))
                Detach();
        }

        /// <summary>
        /// Checks whether the surface normal indicates the player has crested the top
        /// of the wall. If the angle between the surface normal and Vector3.up is less
        /// than the threshold, the surface is near-horizontal and the player mantles over.
        /// Returns true if a ledge transition occurred.
        /// </summary>
        bool CheckLedgeTransition()
        {
            float angleFromUp = Vector3.Angle(climbSurfaceNormal, Vector3.up);
            if (angleFromUp >= ledgeAngleThreshold)
                return false;

            Vector3 nudge = playerTransform.forward * ledgeForwardNudge + Vector3.up * ledgeUpwardNudge;

            OnLedgeTransition?.Invoke();
            Detach();

            playerCharacterController.enabled = false;
            playerTransform.position += nudge;
            playerCharacterController.enabled = true;

            playerController.NetworkPosition = playerTransform.position;

            return true;
        }

        /// <summary>
        /// Remaps WASD to surface-relative directions:
        ///   W (forward) = climb up along surface
        ///   S (backward) = climb down along surface
        ///   A/D (lateral) = move left/right along surface
        /// Gravity is suppressed by the PlayerController when NetworkIsClimbing is true.
        /// </summary>
        void HandleClimbingMovement()
        {
            if (playerCharacterController == null) return;

            Vector3 surfaceUp = Vector3.Cross(
                climbSurfaceNormal,
                Vector3.Cross(Vector3.up, climbSurfaceNormal)
            ).normalized;

            if (surfaceUp.sqrMagnitude < 0.01f)
                surfaceUp = Vector3.up;

            Vector3 surfaceRight = Vector3.Cross(climbSurfaceNormal, surfaceUp).normalized;

            Vector2 input = playerController.MoveInput;

            float verticalSpeed = 0f;
            if (input.y > 0.1f)
                verticalSpeed = climbUpSpeed;
            else if (input.y < -0.1f)
                verticalSpeed = -climbDownSpeed;

            float lateralSpeed = input.x * climbLateralSpeed;

            Vector3 climbVelocity = (surfaceUp * verticalSpeed + surfaceRight * lateralSpeed) * speedMultiplier;
            Vector3 toSurface = -climbSurfaceNormal * surfaceStickDistance;

            Vector3 totalMovement = (climbVelocity + toSurface) * Time.deltaTime;

            playerCharacterController.Move(totalMovement);

            playerController.NetworkPosition = playerTransform.position;
            playerController.NetworkIsMoving = climbVelocity.sqrMagnitude > 0.01f;
        }

        void Attach()
        {
            if (isClimbing) return;

            isClimbing = true;
            playerController.SetClimbingState(true);
            OnClimbStart?.Invoke();
        }

        void Detach()
        {
            if (!isClimbing) return;

            isClimbing = false;
            detachTime = Time.time;

            if (playerController != null)
                playerController.SetClimbingState(false);

            OnClimbEnd?.Invoke();
        }

        void OnDisable()
        {
            if (isClimbing)
                Detach();
        }

        void OnValidate()
        {
            if (gameObject.layer != CLIMBABLE_LAYER)
            {
                Debug.LogWarning($"U3DClimbable: '{name}' is not on the Climbable layer ({CLIMBABLE_LAYER}). " +
                    "Use the Creator Dashboard 'Make Climbable' button to set this up correctly.");
            }

            if (maxClimbDistance <= 0f)
                Debug.LogWarning("U3DClimbable: Max climb distance should be positive");

            if (climbUpSpeed <= 0f)
                Debug.LogWarning("U3DClimbable: Climb up speed should be positive");

            if (ledgeAngleThreshold < 10f || ledgeAngleThreshold > 80f)
                Debug.LogWarning("U3DClimbable: Ledge angle threshold outside practical range (10-80 degrees)");
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = isClimbing ? Color.green : (playerInRange ? Color.yellow : Color.gray);

            Collider col = surfaceCollider != null ? surfaceCollider : GetComponent<Collider>();
            if (col != null)
                Gizmos.DrawWireSphere(col.bounds.center, maxClimbDistance);
            else
                Gizmos.DrawWireSphere(transform.position, maxClimbDistance);

            if (isClimbing && lastSurfacePoint != Vector3.zero)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawRay(lastSurfacePoint, climbSurfaceNormal);
            }
        }
    }
}