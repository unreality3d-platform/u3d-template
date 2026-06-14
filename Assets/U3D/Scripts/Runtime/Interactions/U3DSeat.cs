using Fusion;
using UnityEngine;
using UnityEngine.Events;

namespace U3D
{
    [RequireComponent(typeof(Collider))]
    public class U3DSeat : NetworkBehaviour, IU3DInteractable
    {
        [Header("Seat Configuration")]
        [Tooltip("How far in front of the seat the player is placed when standing up.")]
        [SerializeField] private float standOffsetForward = 0.6f;

        [Header("Events")]
        [Tooltip("Called when the local player sits down.")]
        public UnityEvent OnSit;

        [Tooltip("Called when the local player stands up.")]
        public UnityEvent OnStand;

        [Tooltip("Called when any player occupies this seat (networked — fires on all clients).")]
        public UnityEvent OnOccupied;

        [Tooltip("Called when any player vacates this seat (networked — fires on all clients).")]
        public UnityEvent OnVacated;

        [Networked] public PlayerRef NetworkOccupant { get; set; }

        public static U3DSeat CurrentlyOccupied { get; private set; }

        private U3DPlayerController _localPlayer;
        private PlayerRef _lastKnownOccupant;
        private Transform _seatedHips;

        public override void Spawned()
        {
            _localPlayer = U3DPlayerController.FindLocalPlayer();
            _lastKnownOccupant = PlayerRef.None;
        }

        // ==================== Networked change detection ====================

        public override void Render()
        {
            if (NetworkOccupant != _lastKnownOccupant)
            {
                if (NetworkOccupant == PlayerRef.None)
                    OnVacated?.Invoke();
                else
                    OnOccupied?.Invoke();

                _lastKnownOccupant = NetworkOccupant;
            }
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
            if (!Object.HasStateAuthority)
                Object.RequestStateAuthority();

            NetworkOccupant = player.Object.StateAuthority;
            CurrentlyOccupied = this;

            _seatedHips = ResolveHipsBone(player);

            player.CharacterController.enabled = false;

            Vector3 flatForward = SeatFlatForward();
            player.SetRotation(Quaternion.LookRotation(flatForward, Vector3.up).eulerAngles.y);

            player.NetworkIsSeated = true;

            if (_seatedHips == null)
            {
                player.transform.position = transform.position;
                player.NetworkPosition = transform.position;
            }

            OnSit?.Invoke();
        }

        public void Stand()
        {
            if (_localPlayer == null) return;
            if (CurrentlyOccupied != this) return;

            Vector3 flatForward = SeatFlatForward();

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

            OnStand?.Invoke();
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