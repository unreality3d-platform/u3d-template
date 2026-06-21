using Fusion;
using UnityEngine;
using UnityEngine.Serialization;

namespace U3D
{
    /// <summary>
    /// Cosmetic vehicle component. The player controller continues driving all movement
    /// normally — U3DSteerable adds no physics or input interception. On entry, a visual
    /// and optional replacement visual are instantiated as children of the player transform
    /// so they ride the player's already-networked position. Other players see the visual
    /// via NetworkSteerableRef on U3DPlayerController, resolved in Render() the same way
    /// NetworkRideableRef works for rideables.
    ///
    /// The visual's position, rotation, and scale come straight from how the creator built
    /// the prefab — nothing is overridden in code, so the driver and everyone watching see
    /// it placed identically. Position it by editing the prefab. If the visual (or the
    /// replacement) has an Animator on its root using IsMoving, MoveSpeed, MoveX, and MoveY,
    /// those are driven from the player's movement so the visual can react — spin wheels,
    /// walk a gait, and so on. An Animator without those parameters just plays on its own.
    ///
    /// SteerableAvatarMode is the single source for how the driver appears:
    ///   HiddenAvatar   — humanoid renderers hidden; replacement visual (if assigned) shown.
    ///   SeatedAvatar   — NetworkIsSeated = true; humanoid plays the sit animation.
    ///   StandingAvatar — humanoid stays visible and holds a standing idle; locomotion
    ///                    animations are suppressed.
    ///
    /// Driver seat: if the assigned visual prefab contains a U3DDriverPose marker (added via
    /// the "Add Seat" tool on this steerable), the driver avatar is anchored to that marker
    /// every frame while steering — the CharacterController stays enabled, so the player keeps
    /// driving. The marker only sets position; avatarMode still decides the pose and
    /// visibility. Which point lands on the marker follows avatarMode: hips when Seated, the
    /// feet/floor plane when Standing. HiddenAvatar ignores the marker (nothing visible to
    /// anchor), so a seat needs Seated or Standing.
    ///
    /// Entry: Interact key while in range.
    /// Exit: Interact key again (routed via U3DInteractionManager direct-route, same
    ///       pattern as CurrentlyGrabbed and CurrentlyOccupied).
    ///
    /// Movement input steers (moves the player and therefore the child visual) — it never
    /// dismounts. The movement-input dismount used by U3DSeat is intentionally absent here.
    ///
    /// Apply via the Creator Dashboard "Make Steerable" tool.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class U3DSteerable : NetworkBehaviour, IU3DInteractable
    {
        // ==================== Inspector fields ====================

        [Tooltip("What the player rides — a vehicle, board, or mount (even a creature) that appears with them while steering. With the Steerable selected, Add Seat places a driver seat inside this prefab to pose the avatar on it. Optional; leave empty for an invisible steerable. Placed exactly as built in the prefab; an Animator using IsMoving/MoveSpeed/MoveX/MoveY is driven by the player's movement, e.g. to spin wheels.")]
        [FormerlySerializedAs("vehicleMeshPrefab")]
        [SerializeField] private GameObject vehicleVisualPrefab;

        [Tooltip("What the player looks like instead of their own avatar. Assign this and set Avatar Mode to Hidden Avatar to appear as something else, like a creature or character. Combine with a Vehicle Visual for a custom rider on a mount. Optional; placed as built in the prefab and movement-animated the same way as the Vehicle Visual.")]
        [FormerlySerializedAs("replacementMeshPrefab")]
        [SerializeField] private GameObject replacementVisualPrefab;

        [Tooltip("How the driver avatar appears while steering. If the Vehicle Visual prefab has a driver seat (U3DDriverPose), the avatar anchors to it — hips when Seated, feet when Standing — so use Seated or Standing, not Hidden.")]
        [SerializeField] private SteerableAvatarMode avatarMode = SteerableAvatarMode.HiddenAvatar;

        // ==================== Static direct-route ====================

        /// <summary>
        /// The steerable the local player is currently steering, or null.
        /// Checked by U3DInteractionManager before the OverlapSphere so the Interact
        /// key exits the active steerable immediately without needing proximity.
        /// Never set on remote players — local-only, same pattern as CurrentlyGrabbed.
        /// </summary>
        public static U3DSteerable CurrentlySteering { get; private set; }

        /// <summary>Read by U3DAvatarIK.ShouldRender to decide whether to hide the humanoid.</summary>
        public SteerableAvatarMode AvatarMode => avatarMode;
        public GameObject VehicleVisualPrefab => vehicleVisualPrefab;
        public GameObject ReplacementVisualPrefab => replacementVisualPrefab;

        // ==================== Private state ====================

        private U3DPlayerController _localPlayer;
        private GameObject _vehicleVisualInstance;
        private GameObject _replacementVisualInstance;
        private Animator _vehicleAnimator;
        private Animator _replacementAnimator;

        // Driver seat (set at Enter if the costume contains a U3DDriverPose marker).
        private U3DDriverPose _driverPose;
        private GameObject _driverAvatarInstance;
        private Transform _driverHips;            // resolved only for the Seated anchor
        private Vector3 _driverAvatarOriginalLocalPos;
        private bool _driverAnchorActive;
        private bool _driverAnchorToHips;         // true = hips (Seated), false = feet/base (Standing)

        // Cached animator parameter hashes for the visual animators.
        // Same parameters as U3DNetworkedAnimator uses for the humanoid, so visual
        // animators can share the same animator controller if desired.
        private static readonly int hashIsMoving = Animator.StringToHash("IsMoving");
        private static readonly int hashMoveSpeed = Animator.StringToHash("MoveSpeed");
        private static readonly int hashMoveX = Animator.StringToHash("MoveX");
        private static readonly int hashMoveY = Animator.StringToHash("MoveY");

        // ==================== Lifecycle ====================

        public override void Spawned()
        {
            _localPlayer = U3DPlayerController.FindLocalPlayer();
        }

        private void Update()
        {
            if (CurrentlySteering != this) return;
            if (_localPlayer == null) return;

            DriveVisualAnimators();
        }

        private void LateUpdate()
        {
            if (CurrentlySteering != this) return;
            if (!_driverAnchorActive) return;
            if (_driverAvatarInstance == null || _driverPose == null) return;

            // The point we want to land on the marker: the hips when Seated (rests on the
            // seat, like U3DSeat), or the avatar's base when Standing (the rig origin / floor
            // plane, so the feet land on the marker surface). Re-derived each frame so it
            // tracks the animation as it settles and loops. The marker rides the costume which
            // rides the player, so the root's motion cancels in the delta and there is no
            // feedback. The CharacterController stays enabled — the player root is still
            // steering. Runs at LateUpdate timing so the Animator has already posed the bones.
            Vector3 reference = (_driverAnchorToHips && _driverHips != null)
                ? _driverHips.position
                : _driverAvatarInstance.transform.position;

            Vector3 delta = _driverPose.transform.position - reference;
            if (delta.sqrMagnitude < 1e-10f) return;
            _driverAvatarInstance.transform.position += delta;
        }

        // ==================== IU3DInteractable ====================

        public bool CanInteract()
        {
            // Only one player can steer this steerable at a time.
            // If this client is already steering something else, block entry.
            if (CurrentlySteering != null) return false;
            return true;
        }

        public void OnInteract()
        {
            if (!CanInteract()) return;

            if (_localPlayer == null)
                _localPlayer = U3DPlayerController.FindLocalPlayer();
            if (_localPlayer == null) return;

            Enter(_localPlayer);
        }

        public void OnPlayerEnterRange() { }
        public void OnPlayerExitRange() { }
        public string GetInteractionPrompt() => "Enter";

        // ==================== Visual placement ====================

        /// <summary>
        /// Instantiates a steerable visual as a child of the given parent, preserving the
        /// prefab's authored local position, rotation, and scale exactly. Both the local
        /// entry path and the remote rebuild path (U3DPlayerController.Render) call this,
        /// so the driver and everyone watching place the visual identically — wherever and
        /// however the creator built it in the prefab.
        /// </summary>
        public static GameObject InstantiateVisual(GameObject prefab, Transform parent)
        {
            // instantiateInWorldSpace: false applies the prefab's authored local transform
            // relative to the parent, so position, rotation, and scale all come straight
            // from the prefab. Nothing is overridden afterward.
            return Instantiate(prefab, parent, false);
        }

        // ==================== Enter / Exit ====================

        private void Enter(U3DPlayerController player)
        {
            CurrentlySteering = this;

            if (!Object.HasStateAuthority)
                Object.RequestStateAuthority();

            if (vehicleVisualPrefab != null)
            {
                _vehicleVisualInstance = InstantiateVisual(vehicleVisualPrefab, player.transform);
                _vehicleAnimator = _vehicleVisualInstance.GetComponent<Animator>();
            }

            // Find the driver seat marker inside the instantiated costume, if any. Its presence
            // adds per-frame anchoring and entry/exit events; avatarMode still sets the pose.
            _driverPose = (_vehicleVisualInstance != null)
                ? _vehicleVisualInstance.GetComponentInChildren<U3DDriverPose>(true)
                : null;

            if (replacementVisualPrefab != null)
            {
                _replacementVisualInstance = InstantiateVisual(replacementVisualPrefab, player.transform);
                _replacementAnimator = _replacementVisualInstance.GetComponent<Animator>();
            }

            player.TakeControlOfSteerable(this);

            ApplyDriverPose(player, entering: true);
        }

        public void Exit()
        {
            if (CurrentlySteering != this) return;
            if (_localPlayer == null) return;

            // Undo the pose before destroying visuals so U3DAvatarManager's next Render()
            // sees the cleared steerable ref and restores the humanoid normally. Uses
            // _driverPose while the costume still exists.
            ApplyDriverPose(_localPlayer, entering: false);

            _localPlayer.ExitSteerable();

            if (_vehicleVisualInstance != null)
            {
                Destroy(_vehicleVisualInstance);
                _vehicleVisualInstance = null;
                _vehicleAnimator = null;
            }

            if (_replacementVisualInstance != null)
            {
                Destroy(_replacementVisualInstance);
                _replacementVisualInstance = null;
                _replacementAnimator = null;
            }

            _driverPose = null;
            CurrentlySteering = null;
        }

        // ==================== Pose ====================

        /// <summary>
        /// Applies or clears the driver pose. avatarMode is the single source for the pose
        /// flag and visibility. If a U3DDriverPose marker is present in the costume, it adds
        /// per-frame anchoring (hips when Seated, feet/base when Standing) and fires its
        /// entry/exit events.
        /// </summary>
        private void ApplyDriverPose(U3DPlayerController player, bool entering)
        {
            switch (avatarMode)
            {
                case SteerableAvatarMode.SeatedAvatar:
                    player.NetworkIsSeated = entering;
                    break;

                case SteerableAvatarMode.StandingAvatar:
                    // Hold a standing idle instead of the walk/run blend while steering.
                    player.NetworkSuppressLocomotion = entering;
                    break;

                case SteerableAvatarMode.HiddenAvatar:
                    // Humanoid is hidden by U3DAvatarManager; its animation state is irrelevant.
                    break;
            }

            if (_driverPose == null) return;

            if (entering)
            {
                // Nothing visible to anchor when the avatar is hidden.
                if (avatarMode != SteerableAvatarMode.HiddenAvatar)
                    CaptureAvatarAnchorTargets(player);

                _driverPose.OnDriverEnter?.Invoke();
            }
            else
            {
                ReleaseAvatarAnchor();
                _driverPose.OnDriverExit?.Invoke();
            }
        }

        // ==================== Driver avatar anchoring ====================

        /// <summary>
        /// Caches the local avatar instance and the point that should land on the marker.
        /// Seated anchors the hips bone (needs a humanoid rig); Standing anchors the avatar's
        /// base (rig origin / floor plane), which works for any rig. If the needed reference
        /// can't be resolved, the anchor stays inactive and the avatar rides at its default
        /// offset.
        /// </summary>
        private void CaptureAvatarAnchorTargets(U3DPlayerController player)
        {
            _driverAnchorActive = false;
            _driverAvatarInstance = null;
            _driverHips = null;
            _driverAnchorToHips = false;

            var avatarManager = player.GetComponent<U3DAvatarManager>();
            if (avatarManager == null) return;

            GameObject avatarInstance = avatarManager.GetAvatarInstance();
            if (avatarInstance == null) return;

            if (avatarMode == SteerableAvatarMode.SeatedAvatar)
            {
                Animator animator = avatarManager.GetAvatarAnimator();
                if (animator == null || !animator.isHuman) return;

                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (hips == null) return;

                _driverHips = hips;
                _driverAnchorToHips = true;
            }
            // Standing: no bone needed — the avatar instance's own transform (rig base) is the
            // reference, so the feet/floor plane lands on the marker surface.

            _driverAvatarInstance = avatarInstance;
            _driverAvatarOriginalLocalPos = avatarInstance.transform.localPosition;
            _driverAnchorActive = true;
        }

        private void ReleaseAvatarAnchor()
        {
            if (_driverAvatarInstance != null)
                _driverAvatarInstance.transform.localPosition = _driverAvatarOriginalLocalPos;

            _driverAnchorActive = false;
            _driverAvatarInstance = null;
            _driverHips = null;
            _driverAnchorToHips = false;
        }

        // ==================== Visual animators ====================

        /// <summary>
        /// Drives the costume and replacement visual animators from the player's already-
        /// networked movement state. Runs locally per-client — no separate network sync is
        /// needed because the source values (NetworkIsMoving, NetworkIsSprinting, MoveInput)
        /// are already replicated on the player controller. An animator without these
        /// parameters is unaffected and plays on its own.
        /// </summary>
        private void DriveVisualAnimators()
        {
            if (_localPlayer == null) return;
            if (_vehicleAnimator == null && _replacementAnimator == null) return;

            bool isMoving = _localPlayer.NetworkIsMoving;
            float speed = _localPlayer.NetworkIsSprinting ? 2f : (isMoving ? 1f : 0f);
            Vector2 moveInput = _localPlayer.MoveInput;

            ApplyMovementToAnimator(_vehicleAnimator, isMoving, speed, moveInput);
            ApplyMovementToAnimator(_replacementAnimator, isMoving, speed, moveInput);
        }

        private static void ApplyMovementToAnimator(Animator animator, bool isMoving, float speed, Vector2 moveInput)
        {
            if (animator == null) return;

            animator.SetBool(hashIsMoving, isMoving);
            animator.SetFloat(hashMoveSpeed, speed);
            animator.SetFloat(hashMoveX, moveInput.x);
            animator.SetFloat(hashMoveY, moveInput.y);
        }

        // ==================== Gizmo ====================

        private void OnDrawGizmos()
        {
            // Marks the steerable's interaction point. The visual itself is placed from
            // the prefab onto the player at runtime, not from this object's transform.
            Gizmos.color = new Color(0.4f, 0.9f, 0.4f, 0.85f);
            Gizmos.DrawWireCube(transform.position, Vector3.one * 0.3f);
        }

        // ==================== Validation ====================

        private void OnValidate()
        {
            ValidateVisual(vehicleVisualPrefab, "Vehicle Visual Prefab");
            ValidateVisual(replacementVisualPrefab, "Replacement Visual Prefab");

            if (vehicleVisualPrefab != null
                && avatarMode == SteerableAvatarMode.HiddenAvatar
                && vehicleVisualPrefab.GetComponentInChildren<U3DDriverPose>(true) != null)
            {
                Debug.LogWarning($"{name}: the visual prefab has a driver seat (U3DDriverPose), but Avatar Mode is HiddenAvatar — the driver would be invisible. Set Avatar Mode to Seated or Standing.", this);
            }
        }

        private void ValidateVisual(GameObject prefab, string fieldLabel)
        {
            if (prefab == null) return;

            if (prefab.scene.IsValid())
                Debug.LogWarning($"{name}: '{fieldLabel}' is a scene object. Assign a Project prefab asset instead — a scene object's transform places the visual incorrectly on the player.", this);

            if (prefab.GetComponent<NetworkObject>() != null)
                Debug.LogWarning($"{name}: '{fieldLabel}' has a NetworkObject. The steerable visual is instantiated locally as a costume and must not be networked — remove the NetworkObject from the prefab.", this);
        }
    }

    /// <summary>
    /// Controls how the humanoid avatar is presented while the player is steering.
    /// </summary>
    public enum SteerableAvatarMode
    {
        /// <summary>Humanoid renderers are hidden. A replacement visual (if assigned) is shown instead.</summary>
        HiddenAvatar,

        /// <summary>Humanoid plays the sit animation (NetworkIsSeated = true).</summary>
        SeatedAvatar,

        /// <summary>Humanoid stays visible and holds a standing idle; locomotion animations are suppressed (NetworkSuppressLocomotion = true).</summary>
        StandingAvatar
    }
}