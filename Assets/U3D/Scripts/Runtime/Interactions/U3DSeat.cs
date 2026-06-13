using Fusion;
using UnityEngine;

namespace U3D
{
    /// <summary>
    /// A sittable object. The player presses Interact to sit; pushing any movement
    /// direction stands them up. Only one occupant at a time.
    ///
    /// This component's own transform IS the seat point — position and rotate the Seat
    /// object the Add Seat tool creates to set where the player sits and which way they
    /// face (the blue arrow gizmo shows the facing). While seated, the player's hips are
    /// pulled onto this point every frame, so the seated pose rests on the seat no matter
    /// how the sit animation is authored — no per-seat offsets.
    ///
    /// On stand, the player steps slightly forward so they don't immediately re-trigger.
    ///
    /// Apply via the Creator Dashboard "Add Seat" tool, which creates the seat object and
    /// configures the NetworkObject.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class U3DSeat : NetworkBehaviour, IU3DInteractable
    {
        [Header("Seat Configuration")]
        [Tooltip("How far in front of the seat the player is placed when standing up.")]
        [SerializeField] private float standOffsetForward = 0.6f;

        [Networked] public PlayerRef NetworkOccupant { get; set; }

        public static U3DSeat CurrentlyOccupied { get; private set; }

        private U3DPlayerController _localPlayer;
        private Transform _seatedHips;

        public override void Spawned()
        {
            _localPlayer = U3DPlayerController.FindLocalPlayer();
        }

        // ==================== IU3DInteractable ====================

        public bool CanInteract()
        {
            if (NetworkOccupant != PlayerRef.None) return false;
            if (CurrentlyOccupied != null) return false;
            return true;
        }

        public void OnInteract()
        {
            if (!CanInteract()) return;

            if (_localPlayer == null)
                _localPlayer = U3DPlayerController.FindLocalPlayer();
            if (_localPlayer == null) return;

            Sit(_localPlayer);
        }

        public void OnPlayerEnterRange() { }
        public void OnPlayerExitRange() { }
        public string GetInteractionPrompt() => "Sit";

        // ==================== Sit / Stand ====================

        private void Sit(U3DPlayerController player)
        {
            // Claim occupancy. RequestStateAuthority pattern mirrors U3DGrabbable.
            if (!Object.HasStateAuthority)
                Object.RequestStateAuthority();

            NetworkOccupant = player.Object.StateAuthority;
            CurrentlyOccupied = this;

            _seatedHips = ResolveHipsBone(player);

            // Disable the controller's collider so it stops driving the body; LateUpdate
            // holds the player at the seat from here.
            player.CharacterController.enabled = false;

            // Face the seat's forward, flattened so the player stays upright even if the
            // seat is tilted. SetRotation also keeps the controller's camera yaw in sync.
            Vector3 flatForward = SeatFlatForward();
            player.SetRotation(Quaternion.LookRotation(flatForward, Vector3.up).eulerAngles.y);

            player.NetworkIsSeated = true;

            // Fallback for non-humanoid avatars with no hips bone: snap the root to the seat.
            if (_seatedHips == null)
            {
                player.transform.position = transform.position;
                player.NetworkPosition = transform.position;
            }
        }

        public void Stand()
        {
            if (_localPlayer == null) return;
            if (CurrentlyOccupied != this) return;

            Vector3 flatForward = SeatFlatForward();

            // Step forward from the player's current spot so they clear the seat trigger,
            // keeping their current height so they don't pop up to the seat's elevation.
            Vector3 standPos = _localPlayer.transform.position + flatForward * standOffsetForward;

            _localPlayer.CharacterController.enabled = false;
            _localPlayer.transform.position = standPos;
            _localPlayer.CharacterController.enabled = true;

            _localPlayer.SetRotation(Quaternion.LookRotation(flatForward, Vector3.up).eulerAngles.y);

            _localPlayer.NetworkIsSeated = false;
            _localPlayer.NetworkPosition = standPos;

            NetworkOccupant = PlayerRef.None;
            CurrentlyOccupied = null;
            _seatedHips = null;
        }

        // ==================== Hips anchoring ====================

        private Transform ResolveHipsBone(U3DPlayerController player)
        {
            var avatarManager = player.GetComponent<U3DAvatarManager>();
            if (avatarManager == null) return null;

            Animator animator = avatarManager.GetAvatarAnimator();
            if (animator == null || !animator.isHuman) return null;

            return animator.GetBoneTransform(HumanBodyBones.Hips);
        }

        private Vector3 SeatFlatForward()
        {
            Vector3 f = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (f.sqrMagnitude < 0.0001f)
                f = transform.right;
            return f.normalized;
        }

        private void LateUpdate()
        {
            if (CurrentlyOccupied != this) return;
            if (_localPlayer == null || _seatedHips == null) return;

            // Move the root so the live posed hips land on the seat point, re-derived each
            // frame so it tracks the sit animation as it settles and loops.
            Vector3 delta = transform.position - _seatedHips.position;
            if (delta.sqrMagnitude < 1e-10f) return;

            _localPlayer.transform.position += delta;
            _localPlayer.NetworkPosition = _localPlayer.transform.position;
        }

        // ==================== Movement-input stand detection ====================

        private void Update()
        {
            if (CurrentlyOccupied != this) return;
            if (_localPlayer == null) return;

            if (_localPlayer.MoveInput.magnitude > 0.1f)
                Stand();
        }

        // ==================== Gizmo ====================

        private void OnDrawGizmos()
        {
            Vector3 origin = transform.position;
            Vector3 forward = transform.forward;

            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.9f);
            Gizmos.DrawSphere(origin, 0.06f);

            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.8f);
            Gizmos.DrawLine(origin, origin + forward * 0.5f);

            Vector3 tip = origin + forward * 0.5f;
            Vector3 right = transform.right;
            Gizmos.DrawLine(tip, tip - forward * 0.15f + right * 0.1f);
            Gizmos.DrawLine(tip, tip - forward * 0.15f - right * 0.1f);

            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.5f);
            Gizmos.DrawSphere(tip, 0.03f);
        }
    }
}