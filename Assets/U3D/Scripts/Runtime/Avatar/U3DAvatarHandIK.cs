using UnityEngine;
using UnityEngine.InputSystem;

namespace U3D
{
    /// <summary>
    /// Drives a humanoid avatar's arm IK from VR controller poses.
    ///
    /// Local VR player: reads controller poses directly from the Input System
    /// (XR action map in U3DInputActions), solves two-bone IK on shoulder/upperArm/
    /// lowerArm/hand, also writes rig-local pose to the owning U3DPlayerController's
    /// [Networked] slots so remote viewers can replicate.
    ///
    /// Remote viewer of any player: reads NetworkLeftHandPos/Rot, NetworkRightHandPos/Rot,
    /// NetworkIsInVR from the owning U3DPlayerController. If owner is in VR, applies same
    /// IK solve to that avatar's arms. If owner is not in VR, IK weight lerps to zero and
    /// the Animator's locomotion clip drives arms unmodified.
    ///
    /// Runs in LateUpdate after the Animator evaluates so it can override the animated
    /// arm pose. Per-frame IK weight blends between animator output and IK output for
    /// smooth VR mode transitions.
    ///
    /// Auto-attached to the avatar instance by U3DAvatarManager. Creators do not need
    /// to add this component manually.
    /// </summary>
    public class U3DAvatarHandIK : MonoBehaviour
    {
        [Header("Input (set by U3DAvatarManager)")]
        [Tooltip("Reference to the XR input actions asset. Auto-assigned by U3DAvatarManager on spawn.")]
        [SerializeField] private InputActionAsset xrInputActions;

        [Header("IK Tuning")]
        [Tooltip("Seconds for the IK weight to lerp from 0 to 1 (or 1 to 0) when VR mode toggles. Higher = more snap, lower = smoother but laggier.")]
        [SerializeField] private float ikTransitionTime = 0.2f;

        [Tooltip("How far the elbow bends out from the body. 0 = no hint (elbow may flip awkwardly), 1 = strong outward bend. 0.5 is natural.")]
        [Range(0f, 1f)]
        [SerializeField] private float elbowOutwardHint = 0.5f;

        [Tooltip("How far the elbow bends down. 0 = elbow points sideways, 1 = elbow points down. 0.3 is natural for arms held in front.")]
        [Range(0f, 1f)]
        [SerializeField] private float elbowDownwardHint = 0.3f;

        // Owning controller (the player this avatar belongs to)
        private U3DPlayerController _playerController;
        private Animator _animator;

        // Cached humanoid bones (resolved at Awake)
        private Transform _leftShoulder;
        private Transform _leftUpperArm;
        private Transform _leftLowerArm;
        private Transform _leftHand;

        private Transform _rightShoulder;
        private Transform _rightUpperArm;
        private Transform _rightLowerArm;
        private Transform _rightHand;

        private Transform _head;

        // Cached arm bone lengths (computed once from initial hierarchy)
        private float _leftUpperArmLength;
        private float _leftLowerArmLength;
        private float _rightUpperArmLength;
        private float _rightLowerArmLength;

        // Cached original head scale for head-chop restoration
        private Vector3 _originalHeadScale;
        private bool _headScaleCached;

        // Input System actions (resolved from xrInputActions on Start)
        private InputAction _leftHandPositionAction;
        private InputAction _leftHandRotationAction;
        private InputAction _rightHandPositionAction;
        private InputAction _rightHandRotationAction;
        private bool _xrActionsBound;

        // IK weight state (lerped each frame)
        private float _currentIKWeight;
        private float _targetIKWeight;

        // Head-chop state
        private bool _headChopActive;

        public bool IsReady => _animator != null
            && _leftHand != null && _rightHand != null
            && _leftUpperArm != null && _leftLowerArm != null
            && _rightUpperArm != null && _rightLowerArm != null;

        /// <summary>
        /// Called by U3DAvatarManager after avatar instantiation. Wires the IK component
        /// to its owning player controller and the XR input asset.
        /// </summary>
        public void Initialize(U3DPlayerController owner, InputActionAsset xrActions)
        {
            _playerController = owner;
            xrInputActions = xrActions;

            CacheBones();
            BindXRActions();
        }

        void CacheBones()
        {
            _animator = GetComponent<Animator>();
            if (_animator == null)
            {
                Debug.LogWarning("[U3DAvatarHandIK] No Animator on avatar instance. Hand IK disabled.");
                return;
            }

            if (_animator.avatar == null || !_animator.avatar.isHuman)
            {
                Debug.LogWarning("[U3DAvatarHandIK] Avatar is not humanoid. Hand IK disabled.");
                return;
            }

            _leftShoulder = _animator.GetBoneTransform(HumanBodyBones.LeftShoulder);
            _leftUpperArm = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _leftLowerArm = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _leftHand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);

            _rightShoulder = _animator.GetBoneTransform(HumanBodyBones.RightShoulder);
            _rightUpperArm = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _rightLowerArm = _animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);

            _head = _animator.GetBoneTransform(HumanBodyBones.Head);

            // Cache bone lengths from initial pose. These reflect the avatar's actual
            // arm proportions and stay constant for the lifetime of this avatar instance.
            if (_leftUpperArm != null && _leftLowerArm != null && _leftHand != null)
            {
                _leftUpperArmLength = Vector3.Distance(_leftUpperArm.position, _leftLowerArm.position);
                _leftLowerArmLength = Vector3.Distance(_leftLowerArm.position, _leftHand.position);
            }

            if (_rightUpperArm != null && _rightLowerArm != null && _rightHand != null)
            {
                _rightUpperArmLength = Vector3.Distance(_rightUpperArm.position, _rightLowerArm.position);
                _rightLowerArmLength = Vector3.Distance(_rightLowerArm.position, _rightHand.position);
            }

            // Cache original head scale once for head-chop restoration
            if (_head != null && !_headScaleCached)
            {
                _originalHeadScale = _head.localScale;
                _headScaleCached = true;
            }
        }

        void BindXRActions()
        {
            if (xrInputActions == null)
            {
                Debug.LogWarning("[U3DAvatarHandIK] No XR InputActionAsset assigned. Local VR pose input disabled.");
                return;
            }

            var xrMap = xrInputActions.FindActionMap("XR", throwIfNotFound: false);
            if (xrMap == null)
            {
                Debug.LogWarning("[U3DAvatarHandIK] XR action map not found in InputActionAsset. Local VR pose input disabled.");
                return;
            }

            _leftHandPositionAction = xrMap.FindAction("LeftHandPosition", throwIfNotFound: false);
            _leftHandRotationAction = xrMap.FindAction("LeftHandRotation", throwIfNotFound: false);
            _rightHandPositionAction = xrMap.FindAction("RightHandPosition", throwIfNotFound: false);
            _rightHandRotationAction = xrMap.FindAction("RightHandRotation", throwIfNotFound: false);

            if (_leftHandPositionAction != null) _leftHandPositionAction.Enable();
            if (_leftHandRotationAction != null) _leftHandRotationAction.Enable();
            if (_rightHandPositionAction != null) _rightHandPositionAction.Enable();
            if (_rightHandRotationAction != null) _rightHandRotationAction.Enable();

            _xrActionsBound = _leftHandPositionAction != null
                && _leftHandRotationAction != null
                && _rightHandPositionAction != null
                && _rightHandRotationAction != null;

            if (!_xrActionsBound)
            {
                Debug.LogWarning("[U3DAvatarHandIK] One or more XR pose actions could not be resolved. Verify the XR map in U3DInputActions has LeftHandPosition / LeftHandRotation / RightHandPosition / RightHandRotation actions.");
            }
        }

        void OnDisable()
        {
            if (_leftHandPositionAction != null) _leftHandPositionAction.Disable();
            if (_leftHandRotationAction != null) _leftHandRotationAction.Disable();
            if (_rightHandPositionAction != null) _rightHandPositionAction.Disable();
            if (_rightHandRotationAction != null) _rightHandRotationAction.Disable();
        }

        void LateUpdate()
        {
            if (!IsReady || _playerController == null) return;

            bool isLocalPlayer = _playerController.IsLocalPlayer;
            bool ownerInVR = _playerController.NetworkIsInVR;

            // Decide IK target weight based on owner state. Local VR player has authoritative
            // controller pose data. Remote viewer of a VR player reads networked rig-local pose.
            // Owner not in VR: IK off, animator drives arms.
            _targetIKWeight = ownerInVR ? 1f : 0f;

            float lerpStep = (ikTransitionTime > 0.001f)
                ? Time.deltaTime / ikTransitionTime
                : 1f;
            _currentIKWeight = Mathf.MoveTowards(_currentIKWeight, _targetIKWeight, lerpStep);

            // For local VR player, read controller poses every frame and write to networked
            // slots regardless of weight (so remote viewers see fresh data even during fade-out).
            if (isLocalPlayer && ownerInVR && _xrActionsBound)
            {
                ReadAndPublishLocalControllerPoses();
            }

            // Head-chop only for the local player in VR. Local-only visual; never networked.
            UpdateHeadChop(isLocalPlayer && ownerInVR);

            // Skip IK math entirely if weight is effectively zero.
            if (_currentIKWeight < 0.001f) return;

            // Resolve target wrist pose in world space for both arms. Source depends on
            // whether this is the local player (read from input directly) or a remote
            // viewer (reconstruct from networked rig-local pose).
            Vector3 leftTargetWorldPos, rightTargetWorldPos;
            Quaternion leftTargetWorldRot, rightTargetWorldRot;

            if (isLocalPlayer && _xrActionsBound)
            {
                ResolveLocalTargets(out leftTargetWorldPos, out leftTargetWorldRot,
                                    out rightTargetWorldPos, out rightTargetWorldRot);
            }
            else
            {
                ResolveRemoteTargets(out leftTargetWorldPos, out leftTargetWorldRot,
                                     out rightTargetWorldPos, out rightTargetWorldRot);
            }

            // Cache animator-output rotations BEFORE we overwrite them, so the lerp
            // can blend smoothly between animator pose and IK pose at partial weights.
            Quaternion animLeftUpper = _leftUpperArm.rotation;
            Quaternion animLeftLower = _leftLowerArm.rotation;
            Quaternion animLeftHand = _leftHand.rotation;
            Quaternion animRightUpper = _rightUpperArm.rotation;
            Quaternion animRightLower = _rightLowerArm.rotation;
            Quaternion animRightHand = _rightHand.rotation;

            // Solve and apply left arm
            SolveTwoBoneIK(
                _leftUpperArm, _leftLowerArm, _leftHand,
                _leftUpperArmLength, _leftLowerArmLength,
                leftTargetWorldPos, leftTargetWorldRot,
                isLeftSide: true,
                out Quaternion ikLeftUpper, out Quaternion ikLeftLower, out Quaternion ikLeftHand);

            _leftUpperArm.rotation = Quaternion.Slerp(animLeftUpper, ikLeftUpper, _currentIKWeight);
            _leftLowerArm.rotation = Quaternion.Slerp(animLeftLower, ikLeftLower, _currentIKWeight);
            _leftHand.rotation = Quaternion.Slerp(animLeftHand, ikLeftHand, _currentIKWeight);

            // Solve and apply right arm
            SolveTwoBoneIK(
                _rightUpperArm, _rightLowerArm, _rightHand,
                _rightUpperArmLength, _rightLowerArmLength,
                rightTargetWorldPos, rightTargetWorldRot,
                isLeftSide: false,
                out Quaternion ikRightUpper, out Quaternion ikRightLower, out Quaternion ikRightHand);

            _rightUpperArm.rotation = Quaternion.Slerp(animRightUpper, ikRightUpper, _currentIKWeight);
            _rightLowerArm.rotation = Quaternion.Slerp(animRightLower, ikRightLower, _currentIKWeight);
            _rightHand.rotation = Quaternion.Slerp(animRightHand, ikRightHand, _currentIKWeight);
        }

        void ReadAndPublishLocalControllerPoses()
        {
            // Controller poses from the Input System are reported in XR-rig local space
            // (i.e., relative to the XR Origin / camera offset). For U3D, the XR origin
            // is effectively the player root, and the offset is firstPersonPosition (the
            // head height). The player camera transform reflects this once VR is active —
            // its local space IS the XR-rig space.
            //
            // We want to network rig-local pose (relative to player root) so remote
            // viewers can reconstruct world-space targets via playerRoot.TransformPoint.
            Transform playerRoot = _playerController.transform;
            Transform cam = _playerController.CameraTransform;
            if (cam == null) return;

            Vector3 leftLocalPos = _leftHandPositionAction.ReadValue<Vector3>();
            Quaternion leftLocalRot = _leftHandRotationAction.ReadValue<Quaternion>();
            Vector3 rightLocalPos = _rightHandPositionAction.ReadValue<Vector3>();
            Quaternion rightLocalRot = _rightHandRotationAction.ReadValue<Quaternion>();

            // Controller poses arrive relative to XR origin (head-tracking local space).
            // Convert to world via the camera's parent (the player root) since the camera's
            // local position IS the XR-origin offset within the player root.
            //
            // Convert pose: world = playerRoot.position + playerRoot.rotation * controllerLocal
            // Then store back as rig-local relative to playerRoot for networking.
            Vector3 leftWorldPos = playerRoot.TransformPoint(leftLocalPos);
            Quaternion leftWorldRot = playerRoot.rotation * leftLocalRot;
            Vector3 rightWorldPos = playerRoot.TransformPoint(rightLocalPos);
            Quaternion rightWorldRot = playerRoot.rotation * rightLocalRot;

            // Re-encode as rig-local for the networked slots. This is identical to the
            // input we just read (round-trip through TransformPoint/InverseTransformPoint),
            // but going through world ensures we're consistent with how remote viewers
            // will reconstruct: any future change to XR-rig handling stays consistent.
            _playerController.NetworkLeftHandPos = playerRoot.InverseTransformPoint(leftWorldPos);
            _playerController.NetworkLeftHandRot = Quaternion.Inverse(playerRoot.rotation) * leftWorldRot;
            _playerController.NetworkRightHandPos = playerRoot.InverseTransformPoint(rightWorldPos);
            _playerController.NetworkRightHandRot = Quaternion.Inverse(playerRoot.rotation) * rightWorldRot;

            _playerController.NetworkHeadPosition = playerRoot.InverseTransformPoint(cam.position);
            _playerController.NetworkHeadRotation = Quaternion.Inverse(playerRoot.rotation) * cam.rotation;
        }

        void ResolveLocalTargets(
            out Vector3 leftPos, out Quaternion leftRot,
            out Vector3 rightPos, out Quaternion rightRot)
        {
            Transform playerRoot = _playerController.transform;
            Vector3 leftLocal = _leftHandPositionAction.ReadValue<Vector3>();
            Quaternion leftLocalRot = _leftHandRotationAction.ReadValue<Quaternion>();
            Vector3 rightLocal = _rightHandPositionAction.ReadValue<Vector3>();
            Quaternion rightLocalRot = _rightHandRotationAction.ReadValue<Quaternion>();

            leftPos = playerRoot.TransformPoint(leftLocal);
            leftRot = playerRoot.rotation * leftLocalRot;
            rightPos = playerRoot.TransformPoint(rightLocal);
            rightRot = playerRoot.rotation * rightLocalRot;
        }

        void ResolveRemoteTargets(
            out Vector3 leftPos, out Quaternion leftRot,
            out Vector3 rightPos, out Quaternion rightRot)
        {
            Transform playerRoot = _playerController.transform;

            leftPos = playerRoot.TransformPoint(_playerController.NetworkLeftHandPos);
            leftRot = playerRoot.rotation * _playerController.NetworkLeftHandRot;
            rightPos = playerRoot.TransformPoint(_playerController.NetworkRightHandPos);
            rightRot = playerRoot.rotation * _playerController.NetworkRightHandRot;
        }

        /// <summary>
        /// Two-bone IK solve. Given a fixed shoulder/upper-arm root and a target wrist
        /// pose, computes upper-arm and lower-arm rotations that place the hand at the
        /// target with the elbow bent in a natural direction.
        ///
        /// Math: law of cosines for the elbow bend angle, then construct the elbow
        /// position using a bend-direction hint (lateral away from torso, slightly down).
        /// </summary>
        void SolveTwoBoneIK(
            Transform upperArm, Transform lowerArm, Transform hand,
            float upperLen, float lowerLen,
            Vector3 targetPos, Quaternion targetRot,
            bool isLeftSide,
            out Quaternion upperRotation, out Quaternion lowerRotation, out Quaternion handRotation)
        {
            Vector3 shoulderPos = upperArm.position;
            Vector3 toTarget = targetPos - shoulderPos;
            float chord = toTarget.magnitude;

            // Clamp chord so the law of cosines stays valid even when reaching beyond arm extent.
            float armExtent = upperLen + lowerLen;
            float clampedChord = Mathf.Clamp(chord, 0.01f, armExtent - 0.01f);

            // Law of cosines: angle at shoulder between upper arm and chord.
            float cosShoulder = (upperLen * upperLen + clampedChord * clampedChord - lowerLen * lowerLen)
                                / (2f * upperLen * clampedChord);
            cosShoulder = Mathf.Clamp(cosShoulder, -1f, 1f);
            float shoulderAngle = Mathf.Acos(cosShoulder);

            // Bend direction hint: laterally outward from the torso, slightly down.
            // In avatar root space, "outward" is +X for right arm, -X for left arm; "down" is -Y.
            Transform playerRoot = _playerController.transform;
            float lateralSign = isLeftSide ? -1f : 1f;
            Vector3 outwardWorld = playerRoot.right * lateralSign;
            Vector3 downWorld = -playerRoot.up;
            Vector3 bendHint = (outwardWorld * elbowOutwardHint + downWorld * elbowDownwardHint).normalized;

            Vector3 chordDir = toTarget.normalized;

            // Build elbow position. Project bendHint onto the plane perpendicular to chord
            // to get the actual bend direction; then offset from the chord by the IK geometry.
            Vector3 bendPerp = (bendHint - Vector3.Dot(bendHint, chordDir) * chordDir).normalized;
            if (bendPerp.sqrMagnitude < 0.001f)
            {
                // Degenerate: bendHint parallel to chord. Pick a fallback perpendicular.
                bendPerp = Vector3.Cross(chordDir, playerRoot.forward).normalized;
                if (bendPerp.sqrMagnitude < 0.001f)
                    bendPerp = Vector3.Cross(chordDir, Vector3.up).normalized;
            }

            float alongChord = Mathf.Cos(shoulderAngle) * upperLen;
            float perpFromChord = Mathf.Sin(shoulderAngle) * upperLen;
            Vector3 elbowPos = shoulderPos + chordDir * alongChord + bendPerp * perpFromChord;

            // Build rotations. LookRotation needs forward and up; we use the bone direction
            // as forward. Up is the bend direction so the joint twists naturally.
            Vector3 upperForward = elbowPos - shoulderPos;
            Vector3 lowerForward = targetPos - elbowPos;

            // Compute desired upper/lower rotations from the bone forward vectors.
            // We pre-multiply by the inverse of the upper/lower arm's bind-pose forward
            // direction to get a corrective rotation that aligns the actual bone with
            // the desired direction. For a humanoid Mecanim rig, the bone's forward in
            // world space is (childPos - bonePos), so we use that as the reference.
            Vector3 upperBindForward = lowerArm.position - upperArm.position;
            Vector3 lowerBindForward = hand.position - lowerArm.position;

            Quaternion upperDelta = Quaternion.FromToRotation(upperBindForward, upperForward);
            Quaternion lowerDelta = Quaternion.FromToRotation(lowerBindForward, lowerForward);

            upperRotation = upperDelta * upperArm.rotation;
            // Apply upper rotation to lower arm's reference before computing lower's delta.
            // After the upper arm rotates, the lower arm's bind-forward also rotates with it.
            Vector3 lowerBindForwardAfterUpper = upperDelta * lowerBindForward;
            Quaternion lowerDeltaAdjusted = Quaternion.FromToRotation(lowerBindForwardAfterUpper, lowerForward);
            lowerRotation = lowerDeltaAdjusted * (upperDelta * lowerArm.rotation);

            handRotation = targetRot;
        }

        void UpdateHeadChop(bool shouldChop)
        {
            if (_head == null) return;

            if (shouldChop && !_headChopActive)
            {
                _head.localScale = Vector3.one * 0.0001f;
                _headChopActive = true;
            }
            else if (!shouldChop && _headChopActive)
            {
                _head.localScale = _originalHeadScale;
                _headChopActive = false;
            }
        }

        /// <summary>
        /// Used by U3DAvatarManager to query whether the avatar should be visible
        /// from the local viewpoint. Replaces the per-renderer enable/disable that
        /// used to gate on first-person mode.
        ///
        /// Returns true when this avatar should render (to whichever client is asking).
        /// Returns false only for the local player's own avatar in desktop first-person
        /// when the creator opted into hideInFirstPerson.
        /// </summary>
        public bool ShouldRender(bool hideInFirstPersonPref)
        {
            if (_playerController == null) return true;

            bool isLocal = _playerController.IsLocalPlayer;
            bool inVR = _playerController.NetworkIsInVR;
            bool isFirstPerson = _playerController.NetworkIsFirstPerson;

            // Remote viewer always sees the avatar.
            if (!isLocal) return true;

            // Local VR player: show the body, head will be chopped separately.
            if (inVR) return true;

            // Local desktop player: respect the creator's hideInFirstPerson preference.
            if (hideInFirstPersonPref && isFirstPerson) return false;

            return true;
        }
    }
}