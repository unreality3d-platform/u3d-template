using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.XR;
#if WEBXR_ENABLED
using WebXR;
#endif

namespace U3D
{
    /// <summary>
    /// Drives a humanoid avatar's pose from VR input. Owns arm IK (shoulder/upper/lower/hand)
    /// and head bone rotation. Renamed from U3DAvatarHandIK in May 2026 when head bone
    /// driving was added — the original name covered only the arm solve.
    ///
    /// Local VR player: reads controller poses directly from De-Panther's WebXRController
    /// components, solves two-bone IK on shoulder/upperArm/lowerArm/hand, applies camera
    /// rotation to the head bone, and writes rig-local pose to the owning U3DPlayerController's
    /// [Networked] slots so remote viewers can replicate.
    ///
    /// Remote viewer of any player: reads NetworkLeftHandPos/Rot, NetworkRightHandPos/Rot,
    /// NetworkHeadRotation, NetworkIsInVR from the owning U3DPlayerController. If owner is
    /// in VR, applies same IK solve to that avatar's arms and same rotation to its head
    /// bone. If owner is not in VR, IK weight lerps to zero and the Animator's locomotion
    /// clip drives arms and head unmodified.
    ///
    /// Runs in LateUpdate after the Animator evaluates so it can override the animated
    /// arm and head pose. Per-frame IK weight blends between animator output and IK output
    /// for smooth VR mode transitions.
    ///
    /// Auto-attached to the avatar instance by U3DAvatarManager. Creators do not need
    /// to add this component manually.
    ///
    /// Note on input source: this component originally read XR controller poses through
    /// the new Input System using <XRController>{LeftHand}/devicePosition bindings, then
    /// tried Unity's XR Input subsystem (InputDevices.GetDevicesAtXRNode), but neither
    /// approach exposes left and right hand poses independently in De-Panther's WebXR
    /// runtime. We use De-Panther's own WebXRController components: each one has a hand
    /// property (LEFT/RIGHT/NONE) and its transform is updated every frame with the
    /// controller pose. If the scene doesn't already have WebXRController instances
    /// (the U3D rig doesn't include them by default), this component creates them at
    /// runtime as children of the player root when VR mode begins.
    /// </summary>
    [MovedFrom(false, "U3D", null, "U3DAvatarHandIK")]
    public class U3DAvatarIK : MonoBehaviour
    {
        [Header("Input (set by U3DAvatarManager)")]
        [Tooltip("Reference to the XR input actions asset. Retained for backward compatibility; pose data is now read directly from WebXRController components and this field is unused at runtime.")]
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

        [SerializeField] private Vector3 leftHandRotationOffset = new Vector3(0f, -90f, -90f);
        [SerializeField] private Vector3 rightHandRotationOffset = new Vector3(0f, 90f, 90f);
        [Tooltip("Fixed Euler offset applied to the avatar head bone in VR. Mecanim head bone bind orientation rarely matches the camera/HMD orientation; this corrects the difference. Tune empirically for each avatar rig.")]
        [SerializeField] private Vector3 headRotationOffset = new Vector3(0f, -90f, 0f);

        [Header("Debug")]
        [Tooltip("Show on-screen pose data overlay in VR. For diagnostic use only — disable for production.")]
        [SerializeField] private bool showDebugOverlay = false;

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

#if WEBXR_ENABLED
        // De-Panther WebXRController references. Either found in the scene if the creator
        // placed them there, or created at runtime if not. De-Panther writes the controller
        // pose to each component's transform every frame, and exposes a hand property
        // (LEFT/RIGHT/NONE) for handedness. This is the canonical De-Panther API for
        // per-hand controller data and works when Unity's XR Input subsystem returns
        // nothing on WebGL/WebXR.
        private WebXRController _leftHandController;
        private WebXRController _rightHandController;
#endif
        private bool _xrActionsBound;  // True when at least one controller has been resolved.

        // IK weight state (lerped each frame)
        private float _currentIKWeight;
        private float _targetIKWeight;

        // Head-chop state
        private bool _headChopActive;

        // Debug overlay state
        private GameObject _debugPanel;
        private TextMesh _debugText;
        private int _debugFrameCounter;

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
            // WebXRController references are resolved per-frame in LateUpdate via BindXRActions.
            // No setup needed at initialization time.
        }

        void CacheBones()
        {
            _animator = GetComponent<Animator>();
            if (_animator == null)
            {
                Debug.LogWarning("[U3DAvatarIK] No Animator on avatar instance. IK disabled.");
                return;
            }

            if (_animator.avatar == null || !_animator.avatar.isHuman)
            {
                Debug.LogWarning("[U3DAvatarIK] Avatar is not humanoid. IK disabled.");
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
        }

        /// <summary>
        /// Locate or create De-Panther WebXRController instances for left and right hands.
        /// First tries to find existing ones in the scene (if a creator placed them);
        /// if none found, creates them as children of the player root. Once created,
        /// they're cached and reused.
        /// </summary>
        void BindXRActions()
        {
#if WEBXR_ENABLED
            // Skip if both refs are still alive.
            bool needScan = (_leftHandController == null) || (_rightHandController == null);
            if (!needScan)
            {
                _xrActionsBound = true;
                return;
            }

            // First try to find existing WebXRController instances in the scene.
            var existing = Object.FindObjectsByType<WebXRController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < existing.Length; i++)
            {
                var c = existing[i];
                if (c == null) continue;

                if (c.hand == WebXRControllerHand.LEFT && _leftHandController == null)
                {
                    _leftHandController = c;
                }
                else if (c.hand == WebXRControllerHand.RIGHT && _rightHandController == null)
                {
                    _rightHandController = c;
                }
            }

            // Create any controllers that weren't found in the scene. They live as
            // children of the player root, which is the WebXR tracking origin in U3D's
            // rig (the camera, which De-Panther drives, is also parented to the player
            // root). De-Panther's WebXRController will write world-space pose to the
            // GameObject's transform every frame once the WebXR session is active.
            if (_leftHandController == null && _playerController != null)
            {
                _leftHandController = CreateRuntimeController(WebXRControllerHand.LEFT);
            }
            if (_rightHandController == null && _playerController != null)
            {
                _rightHandController = CreateRuntimeController(WebXRControllerHand.RIGHT);
            }

            _xrActionsBound = (_leftHandController != null) || (_rightHandController != null);
#else
            _xrActionsBound = false;
#endif
        }

#if WEBXR_ENABLED
        WebXRController CreateRuntimeController(WebXRControllerHand handAssignment)
        {
            string label = handAssignment == WebXRControllerHand.LEFT ? "U3D_WebXRController_L" : "U3D_WebXRController_R";
            GameObject go = new GameObject(label);
            go.transform.SetParent(_playerController.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            var ctrl = go.AddComponent<WebXRController>();
            ctrl.hand = handAssignment;
            return ctrl;
        }
#endif

        void LateUpdate()
        {
            if (!IsReady || _playerController == null) return;

            bool isLocalPlayer = _playerController.IsLocalPlayer;
            bool ownerInVR = _playerController.NetworkIsInVR;

            // Refresh WebXRController references each frame (cheap when refs are alive).
            if (isLocalPlayer && ownerInVR)
            {
                BindXRActions();
            }

            if (showDebugOverlay && isLocalPlayer && ownerInVR)
            {
                EnsureDebugPanel();
                UpdateDebugOverlay();
            }
            else if (_debugPanel != null)
            {
                _debugPanel.SetActive(false);
            }

            // Decide IK target weight based on owner state. Local VR player has authoritative
            // controller pose data. Remote viewer of a VR player reads networked rig-local pose.
            // Owner not in VR: IK off, animator drives arms and head.
            _targetIKWeight = ownerInVR ? 1f : 0f;

            float lerpStep = (ikTransitionTime > 0.001f)
                ? Time.deltaTime / ikTransitionTime
                : 1f;
            _currentIKWeight = Mathf.MoveTowards(_currentIKWeight, _targetIKWeight, lerpStep);

            // For local VR player, read controller poses every frame and write to networked
            // slots regardless of weight (so remote viewers see fresh data even during fade-out).
            if (isLocalPlayer && ownerInVR)
            {
                ReadAndPublishLocalControllerPoses();
            }

            // Skip IK math entirely if weight is effectively zero.
            if (_currentIKWeight < 0.001f) return;

            // Drive the head bone from NetworkHeadRotation. Done before arm IK so the
            // arm solver's target reconstruction (which doesn't use the head) is unaffected,
            // and so the head pose is set in the same frame the arms are.
            ApplyHeadRotation();

            // Resolve target wrist pose in world space for both arms. Source depends on
            // whether this is the local player (read from input directly) or a remote
            // viewer (reconstruct from networked rig-local pose).
            Vector3 leftTargetWorldPos, rightTargetWorldPos;
            Quaternion leftTargetWorldRot, rightTargetWorldRot;

            if (isLocalPlayer && ownerInVR)
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

        void EnsureDebugPanel()
        {
            if (_debugPanel != null)
            {
                if (!_debugPanel.activeSelf) _debugPanel.SetActive(true);
                return;
            }
            if (_playerController == null || _playerController.CameraTransform == null) return;

            _debugPanel = new GameObject("U3DAvatarIK_DebugPanel");
            _debugPanel.transform.SetParent(_playerController.CameraTransform, false);
            _debugPanel.transform.localPosition = new Vector3(0f, 0f, 0.75f);
            _debugPanel.transform.localRotation = Quaternion.identity;
            _debugPanel.transform.localScale = Vector3.one;

            _debugText = _debugPanel.AddComponent<TextMesh>();
            _debugText.fontSize = 64;
            _debugText.characterSize = 0.012f;
            _debugText.anchor = TextAnchor.MiddleCenter;
            _debugText.alignment = TextAlignment.Left;
            _debugText.color = Color.yellow;

            // Render on top of geometry
            _debugText.GetComponent<MeshRenderer>().material.renderQueue = 4000;
        }

        void UpdateDebugOverlay()
        {
            if (_debugPanel == null) return;

            _debugFrameCounter++;

#if WEBXR_ENABLED
            string lFound = _leftHandController != null ? "FOUND" : "NULL";
            string rFound = _rightHandController != null ? "FOUND" : "NULL";

            Vector3 lp = Vector3.zero;
            Quaternion lr = Quaternion.identity;
            if (_leftHandController != null)
            {
                lp = _leftHandController.transform.position;
                lr = _leftHandController.transform.rotation;
            }

            Vector3 rp = Vector3.zero;
            Quaternion rr = Quaternion.identity;
            if (_rightHandController != null)
            {
                rp = _rightHandController.transform.position;
                rr = _rightHandController.transform.rotation;
            }

            string lName = _leftHandController != null ? _leftHandController.gameObject.name : "(none)";
            string rName = _rightHandController != null ? _rightHandController.gameObject.name : "(none)";
#else
            string lFound = "WEBXR_DISABLED";
            string rFound = "WEBXR_DISABLED";
            Vector3 lp = Vector3.zero;
            Quaternion lr = Quaternion.identity;
            Vector3 rp = Vector3.zero;
            Quaternion rr = Quaternion.identity;
            string lName = "(disabled)";
            string rName = "(disabled)";
#endif

            // TPD diagnostic: read the TPD state directly from the camera so we can
            // see whether the headset rotation write is landing.
            string tpdDiag = "(no camera)";
            if (_playerController != null && _playerController.CameraTransform != null)
            {
                Transform camT = _playerController.CameraTransform;
                var tpd = camT.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
                Vector3 camLocalEuler = camT.localEulerAngles;
                Vector3 camWorldEuler = camT.eulerAngles;

                if (tpd != null)
                {
                    string tpdEnabled = tpd.enabled ? "ON" : "OFF";
                    string tpdType = tpd.trackingType.ToString();
                    string posEnabled = (tpd.positionInput.action != null && tpd.positionInput.action.enabled) ? "Y" : "N";
                    string rotEnabled = (tpd.rotationInput.action != null && tpd.rotationInput.action.enabled) ? "Y" : "N";
                    tpdDiag = $"TPD {tpdEnabled} {tpdType} pos:{posEnabled} rot:{rotEnabled}";
                }
                else
                {
                    tpdDiag = "TPD MISSING";
                }
                tpdDiag += $"\nCamLocal: ({camLocalEuler.x:F0},{camLocalEuler.y:F0},{camLocalEuler.z:F0})";
                tpdDiag += $"\nCamWorld: ({camWorldEuler.x:F0},{camWorldEuler.y:F0},{camWorldEuler.z:F0})";

                if (_playerController.transform != null)
                {
                    float bodyYaw = _playerController.transform.eulerAngles.y;
                    tpdDiag += $"\nBodyYaw: {bodyYaw:F0}";
                }
            }

            // Geometry diagnostic: world positions of camera, avatar head bone, and
            // player root, plus the offsets between them. Tells us where the camera
            // actually sits on the avatar rig (no guessing — measured values).
            string geomDiag = "(no geom)";
            if (_playerController != null && _playerController.CameraTransform != null)
            {
                Transform camT = _playerController.CameraTransform;
                Transform rootT = _playerController.transform;
                Vector3 camPos = camT.position;
                Vector3 rootPos = rootT.position;
                Vector3 headPos = (_head != null) ? _head.position : Vector3.zero;

                geomDiag = $"Cam:  ({camPos.x:F2},{camPos.y:F2},{camPos.z:F2})";
                geomDiag += $"\nHead: ({headPos.x:F2},{headPos.y:F2},{headPos.z:F2})";
                geomDiag += $"\nRoot: ({rootPos.x:F2},{rootPos.y:F2},{rootPos.z:F2})";
                geomDiag += $"\nC-H:  ({(camPos.x - headPos.x):F2},{(camPos.y - headPos.y):F2},{(camPos.z - headPos.z):F2})";
                geomDiag += $"\nH-R:  ({(headPos.x - rootPos.x):F2},{(headPos.y - rootPos.y):F2},{(headPos.z - rootPos.z):F2})";
            }

            _debugText.text =
                $"Frame: {_debugFrameCounter}\n" +
                $"L Ctrl {lFound}: {lName}\n" +
                $"R Ctrl {rFound}: {rName}\n" +
                $"L-Pos: ({lp.x:F2}, {lp.y:F2}, {lp.z:F2})\n" +
                $"R-Pos: ({rp.x:F2}, {rp.y:F2}, {rp.z:F2})\n" +
                $"---\n" +
                $"{tpdDiag}\n" +
                $"---\n" +
                $"{geomDiag}";
        }

        void ReadAndPublishLocalControllerPoses()
        {
            // De-Panther's WebXRController writes pose to its transform every frame in
            // WebXR reference space. We compute the IRL head-to-hand vector by
            // subtracting the IRL HMD's pose (read from the player controller's raw
            // HMD reference, which is a position-only TPD-driven transform) from the
            // controller's pose. Both are in WebXR space, so the subtraction yields a
            // clean IRL vector. Then we anchor that vector to the avatar's head bone
            // world position to place hands relative to the avatar rig.
            //
            // The locked-to-avatar player camera CANNOT be used as the IRL HMD reference
            // because U3DPlayerController.LateUpdate overrides the camera's world position
            // to follow the avatar head bone — it no longer reflects where the IRL HMD
            // actually is. The raw HMD reference exists specifically as the unmoved
            // De-Panther TPD output for this purpose.
            //
            // If a controller reference is null this frame, leave its networked slot
            // untouched so transient dropouts don't cause arm collapse.
            Transform playerRoot = _playerController.transform;
            Transform cam = _playerController.CameraTransform;
            Transform rawHmd = _playerController.RawHmdReference;
            if (cam == null) return;

            // Fallback: if the raw HMD reference doesn't exist yet (e.g. first frame
            // of VR mode before EnterVRMode finished), fall back to the camera transform.
            // The hand placement will be slightly off for that one frame, which is
            // imperceptible.
            Vector3 irlHmdPos = (rawHmd != null) ? rawHmd.position : cam.position;
            Vector3 headBoneWorld = (_head != null) ? _head.position : cam.position;

#if WEBXR_ENABLED
            if (_leftHandController != null)
            {
                Transform t = _leftHandController.transform;
                Vector3 irlHeadToHand = t.position - irlHmdPos;
                Vector3 handWorld = headBoneWorld + irlHeadToHand;
                _playerController.NetworkLeftHandPos = playerRoot.InverseTransformPoint(handWorld);
                _playerController.NetworkLeftHandRot = Quaternion.Inverse(playerRoot.rotation) * t.rotation;
            }

            if (_rightHandController != null)
            {
                Transform t = _rightHandController.transform;
                Vector3 irlHeadToHand = t.position - irlHmdPos;
                Vector3 handWorld = headBoneWorld + irlHeadToHand;
                _playerController.NetworkRightHandPos = playerRoot.InverseTransformPoint(handWorld);
                _playerController.NetworkRightHandRot = Quaternion.Inverse(playerRoot.rotation) * t.rotation;
            }
#endif

            // Head pose is driven by the camera transform (the WebXRCamera component
            // applies the headset pose to the camera every frame).
            _playerController.NetworkHeadPosition = playerRoot.InverseTransformPoint(cam.position);
            _playerController.NetworkHeadRotation = Quaternion.Inverse(playerRoot.rotation) * cam.rotation;
        }

        void ResolveLocalTargets(
            out Vector3 leftPos, out Quaternion leftRot,
            out Vector3 rightPos, out Quaternion rightRot)
        {
            // Local targets read from the networked slots that ReadAndPublishLocalControllerPoses
            // just wrote to. This makes the local player's IK use the same data path as remote
            // viewers, so any networking-side transformation (compression, interpolation) is
            // applied identically and the local view matches what others see.
            Transform playerRoot = _playerController.transform;
            leftPos = playerRoot.TransformPoint(_playerController.NetworkLeftHandPos);
            leftRot = playerRoot.rotation * _playerController.NetworkLeftHandRot;
            rightPos = playerRoot.TransformPoint(_playerController.NetworkRightHandPos);
            rightRot = playerRoot.rotation * _playerController.NetworkRightHandRot;
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
        /// Drives the avatar's humanoid head bone from NetworkHeadRotation, which the
        /// owning controller publishes every VR frame as the camera's rotation in
        /// player-root-local space. Local and remote viewers both read from the same
        /// networked slot so the local avatar's head bone tracks the HMD identically
        /// to how remote viewers see it — no divergence between viewpoints.
        ///
        /// Runs in LateUpdate after the Animator evaluates, so the write overrides
        /// any animator-authored head pose for the duration of the VR session. When
        /// the owner exits VR, the IK weight lerps to zero and the animator's head
        /// channel resumes uncontested.
        ///
        /// headRotationOffset corrects for the Mecanim head bone's bind orientation
        /// not matching the camera/HMD orientation. Same pattern as the per-hand
        /// rotation offsets.
        /// </summary>
        void ApplyHeadRotation()
        {
            if (_head == null) return;

            Transform playerRoot = _playerController.transform;
            Quaternion headRotLocal = _playerController.NetworkHeadRotation;

            // Sanity: the slot is initialized to identity and may briefly be identity
            // during the spawn frame before the local player writes a real value.
            // Identity is harmless to apply (head sits in bind orientation aligned
            // with body yaw) so no early-return needed here.

            Quaternion headOffset = Quaternion.Euler(headRotationOffset);
            Quaternion targetWorldRot = playerRoot.rotation * headRotLocal * headOffset;

            // Cache animator output so the IK weight lerp blends smoothly during the
            // VR-on/VR-off transition. Same pattern the arms use above.
            Quaternion animHead = _head.rotation;
            _head.rotation = Quaternion.Slerp(animHead, targetWorldRot, _currentIKWeight);
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

            // Apply per-hand rotation offset. WebXR controller pose and Mecanim humanoid
            // hand bone orientation use different axis conventions; the offset is a fixed
            // correction. The two offsets are mirror images of each other so both hands
            // rotate symmetrically about the avatar's centerline.
            Vector3 offsetEuler = isLeftSide ? leftHandRotationOffset : rightHandRotationOffset;
            Quaternion handOffset = Quaternion.Euler(offsetEuler);
            handRotation = targetRot * handOffset;
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
            // Delay the hide until the scroll transition completes so the avatar doesn't
            // vanish while the camera is still pulling in from third person.
            if (hideInFirstPersonPref && isFirstPerson && !_playerController.IsCameraTransitioning) return false;

            return true;
        }
    }
}