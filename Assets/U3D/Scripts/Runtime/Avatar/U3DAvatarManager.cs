using Fusion;
using System.Collections.Generic;
using U3D;
using UnityEngine;

[RequireComponent(typeof(U3DPlayerController))]
public class U3DAvatarManager : NetworkBehaviour
{
    [Header("Avatar Configuration")]
    [SerializeField] private GameObject avatarFBX;
    [SerializeField] private bool autoConfigureHumanoid = true;
    [SerializeField] private float avatarScaleMultiplier = 1f;

    [Header("Avatar Positioning")]
    [SerializeField] private Vector3 avatarOffset = Vector3.zero;
    [SerializeField] private bool followPlayerRotation = true;
    [SerializeField] private bool hideInFirstPerson = true;

    [Header("VR IK")]
    [Tooltip("XR input actions asset. Used by the auto-attached U3DAvatarIK to read VR controller poses. Should reference the same U3DInputActions asset used by the player controller.")]
    [SerializeField] private UnityEngine.InputSystem.InputActionAsset xrInputActions;

    // Core Components
    private U3DPlayerController playerController;
    private GameObject avatarInstance;
    private Animator avatarAnimator;
    private Avatar avatarAsset;
    private SkinnedMeshRenderer[] avatarRenderers;
    private U3DAvatarIK avatarIK;
    private Transform _handAnchor;

    // Neutral-pose reference data, captured once at avatar initialization via
    // HumanPoseHandler. The avatar is silently posed to Unity's muscle-neutral stance
    // (the same normalized pose on every humanoid rig, regardless of whether the file
    // was exported in T-pose or A-pose), each bone's rotation relative to the avatar
    // root is recorded, and the original pose is restored — all within one frame, never
    // visible. Attachments and grabs bake their orientation against this fixed frame
    // instead of the bone's animated rotation at the attach moment, so attaching
    // mid-fly, mid-jump, or mid-swing produces the identical result as attaching at
    // idle. Also caches the elbow→wrist direction in each hand bone's local frame,
    // used to place held objects a fixed distance out from the wrist joint.
    private Quaternion[] _neutralBoneRotations;
    private bool[] _neutralBoneValid;
    private Vector3 _leftHandAnchorDir;
    private Vector3 _rightHandAnchorDir;
    private bool _leftHandAnchorValid;
    private bool _rightHandAnchorValid;

    // Renderers of cosmetic attachments riding this avatar's bones, registered by
    // U3DPlayerAttachments. Toggled alongside the body in UpdateAvatarVisibility so attachments
    // follow the avatar's own first-person / VR / third-person visibility with no special rules.
    private readonly List<Renderer> _attachmentRenderers = new List<Renderer>();

    // Subset of attachment renderers riding the head bone, registered separately by
    // U3DPlayerAttachments. For the local wearer in VR first person these render shadow-only, so a
    // face-covering piece (a costume head) can't block their own view — other players, VR third
    // person, and desktop all see it normally, and the wearer keeps its shadow. The authored shadow
    // mode is captured at registration and restored whenever suppression doesn't apply, so a piece
    // the creator shipped with shadows off stays that way.
    private struct HeadAttachmentEntry
    {
        public Renderer Renderer;
        public UnityEngine.Rendering.ShadowCastingMode OriginalMode;
    }
    private readonly List<HeadAttachmentEntry> _headAttachmentEntries = new List<HeadAttachmentEntry>();

    // Simple animation system
    private U3DNetworkedAnimator networkedAnimator;
    private bool isInitialized = false;

    // VR idle suppression: when the local player is in VR and the player controller's
    // movement state indicates idle, the avatar Animator's speed is set to 0 to freeze
    // all animation playback. This suppresses the breathing, weight-shift, and finger
    // motion baked into the idle clip, which would otherwise transfer to the camera
    // and hands in VR. When the player starts moving (walk, run, jump, crouch, fly,
    // swim, climb), Animator speed is restored to 1 so those animations play normally.
    private bool vrIdleSuppressionActive = false;
    private float freezeScheduledTime = -1f;
    private bool _prevSeated;
    private bool _prevSuppressLocomotion;

    public override void Spawned()
    {
        // Initialize components
        playerController = GetComponent<U3DPlayerController>();
        if (playerController == null)
        {
            Debug.LogError("U3DAvatarManager: U3DPlayerController not found!");
            return;
        }

        // Get the clean animation system
        networkedAnimator = GetComponent<U3DNetworkedAnimator>();
        if (networkedAnimator == null)
        {
            Debug.LogError("❌ U3DNetworkedAnimator not found! Please add it to the prefab.");
            return;
        }

        // Initialize avatar if FBX is assigned
        if (avatarFBX != null)
        {
            InitializeAvatar();
        }
        else
        {
            Debug.LogWarning("⚠️ No avatar FBX assigned - using default setup");
        }
    }

    void InitializeAvatar()
    {
        try
        {
            // Instantiate avatar FBX
            avatarInstance = Instantiate(avatarFBX, transform);
            avatarInstance.transform.localPosition = avatarOffset;
            avatarInstance.transform.localRotation = Quaternion.identity;
            avatarInstance.transform.localScale = Vector3.one * avatarScaleMultiplier;

            // Configure humanoid Avatar if auto-configuration is enabled
            if (autoConfigureHumanoid)
            {
                ConfigureHumanoidAvatar();
            }

            // Get or add Animator component
            avatarAnimator = avatarInstance.GetComponent<Animator>();
            if (avatarAnimator == null)
            {
                avatarAnimator = avatarInstance.AddComponent<Animator>();
            }

            // Root motion must always be off — the player controller owns all positional movement.
            // Any avatar prefab with Apply Root Motion enabled will otherwise drift away from the capsule.
            avatarAnimator.applyRootMotion = false;

            CaptureNeutralPoseData();

            // Connect to animation system
            ConnectToAnimationSystem();

            // Get all SkinnedMeshRenderers for visibility control
            avatarRenderers = avatarInstance.GetComponentsInChildren<SkinnedMeshRenderer>();

            // Auto-attach VR IK. Works for any humanoid avatar (default and creator-supplied).
            // If the avatar isn't humanoid, U3DAvatarIK logs a warning and disables itself.
            avatarIK = avatarInstance.GetComponent<U3DAvatarIK>();
            if (avatarIK == null)
                avatarIK = avatarInstance.AddComponent<U3DAvatarIK>();
            avatarIK.Initialize(playerController, xrInputActions);

            isInitialized = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Failed to initialize avatar: {e.Message}");
        }
    }

    /// <summary>
    /// Captures the neutral-pose reference frame for every mapped Humanoid bone. Poses the
    /// avatar to the muscle-neutral stance with HumanPoseHandler, records each bone's
    /// rotation relative to the avatar root plus the elbow→wrist direction in each hand's
    /// local frame, then restores the original pose. Fully synchronous — the neutral pose
    /// is never rendered. No-op on non-humanoid avatars; consumers fall back to
    /// pose-dependent behavior when no data was captured.
    /// </summary>
    private void CaptureNeutralPoseData()
    {
        _neutralBoneRotations = null;
        _neutralBoneValid = null;
        _leftHandAnchorValid = false;
        _rightHandAnchorValid = false;

        if (avatarInstance == null || avatarAnimator == null) return;
        if (!avatarAnimator.isHuman || avatarAnimator.avatar == null) return;

        HumanPoseHandler poseHandler = null;
        try
        {
            poseHandler = new HumanPoseHandler(avatarAnimator.avatar, avatarInstance.transform);

            HumanPose pose = new HumanPose();
            poseHandler.GetHumanPose(ref pose);

            Vector3 originalBodyPosition = pose.bodyPosition;
            Quaternion originalBodyRotation = pose.bodyRotation;
            float[] originalMuscles = (float[])pose.muscles.Clone();

            // Muscle-neutral stance, hips aligned to the avatar root — the same
            // normalized pose on every humanoid rig.
            for (int i = 0; i < pose.muscles.Length; i++)
                pose.muscles[i] = 0f;
            pose.bodyRotation = Quaternion.identity;
            poseHandler.SetHumanPose(ref pose);

            Quaternion invRoot = Quaternion.Inverse(avatarInstance.transform.rotation);
            int boneCount = (int)HumanBodyBones.LastBone;
            _neutralBoneRotations = new Quaternion[boneCount];
            _neutralBoneValid = new bool[boneCount];

            for (int i = 0; i < boneCount; i++)
            {
                Transform bone = avatarAnimator.GetBoneTransform((HumanBodyBones)i);
                if (bone == null) continue;
                _neutralBoneRotations[i] = invRoot * bone.rotation;
                _neutralBoneValid[i] = true;
            }

            CaptureHandAnchorDirection(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                out _leftHandAnchorDir, out _leftHandAnchorValid);
            CaptureHandAnchorDirection(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                out _rightHandAnchorDir, out _rightHandAnchorValid);

            // Restore the pose exactly as it was.
            pose.bodyPosition = originalBodyPosition;
            pose.bodyRotation = originalBodyRotation;
            System.Array.Copy(originalMuscles, pose.muscles, originalMuscles.Length);
            poseHandler.SetHumanPose(ref pose);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"U3DAvatarManager: neutral pose capture failed ({e.Message}) — attachments and grabs will use pose-dependent fallback.");
            _neutralBoneRotations = null;
            _neutralBoneValid = null;
            _leftHandAnchorValid = false;
            _rightHandAnchorValid = false;
        }
        finally
        {
            poseHandler?.Dispose();
        }
    }

    /// <summary>
    /// Records the elbow→wrist direction expressed in the hand bone's local frame, read
    /// while the avatar is posed neutral. Both directions are rotation-only (unaffected by
    /// scale), so the result is a constant unit direction that rides the hand at runtime.
    /// </summary>
    private void CaptureHandAnchorDirection(HumanBodyBones lowerArmBone, HumanBodyBones handBone,
        out Vector3 handLocalDirection, out bool valid)
    {
        handLocalDirection = Vector3.zero;
        valid = false;

        Transform lowerArm = avatarAnimator.GetBoneTransform(lowerArmBone);
        Transform hand = avatarAnimator.GetBoneTransform(handBone);
        if (lowerArm == null || hand == null) return;

        Vector3 worldDir = hand.position - lowerArm.position;
        if (worldDir.sqrMagnitude < 0.0001f) return;

        handLocalDirection = hand.InverseTransformDirection(worldDir.normalized);
        valid = true;
    }

    /// <summary>
    /// Returns the given Humanoid bone's neutral-pose rotation relative to the avatar
    /// root, captured at avatar initialization. At runtime,
    /// bone.rotation * Quaternion.Inverse(result) gives the world frame a marker should
    /// align to so that its authored forward faces the avatar's forward whenever the bone
    /// is in the neutral stance — making baked orientations independent of the animated
    /// pose at the attach moment. False when no data exists (non-humanoid avatar, unmapped
    /// bone, or capture failure); callers fall back to pose-dependent behavior.
    /// </summary>
    public bool TryGetNeutralBoneRotation(HumanBodyBones bone, out Quaternion neutralRelRotation)
    {
        neutralRelRotation = Quaternion.identity;
        if (_neutralBoneRotations == null || _neutralBoneValid == null) return false;

        int index = (int)bone;
        if (index < 0 || index >= _neutralBoneRotations.Length) return false;
        if (!_neutralBoneValid[index]) return false;

        neutralRelRotation = _neutralBoneRotations[index];
        return true;
    }

    /// <summary>
    /// Provides the grab frame for a resolved hand: the hand bone's neutral-pose rotation
    /// (for pose-independent orientation baking) and the elbow→wrist direction in hand-local
    /// space (for placing held objects a fixed distance out from the wrist joint, along the
    /// forearm line). Returns false when the resolved transform is not this avatar's actual
    /// Humanoid hand bone — a creator-typed custom socket is treated as the exact intended
    /// attach point and gets neither adjustment — or when no neutral data was captured.
    /// </summary>
    public bool TryGetHandGrabFrame(Transform resolvedHand, bool leftHand,
        out Quaternion neutralRelRotation, out Vector3 anchorLocalDirection)
    {
        neutralRelRotation = Quaternion.identity;
        anchorLocalDirection = Vector3.zero;

        if (resolvedHand == null) return false;
        if (avatarAnimator == null || !avatarAnimator.isHuman) return false;

        HumanBodyBones handBone = leftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
        if (avatarAnimator.GetBoneTransform(handBone) != resolvedHand) return false;

        if (!TryGetNeutralBoneRotation(handBone, out neutralRelRotation)) return false;

        bool anchorValid = leftHand ? _leftHandAnchorValid : _rightHandAnchorValid;
        if (!anchorValid) return false;

        anchorLocalDirection = leftHand ? _leftHandAnchorDir : _rightHandAnchorDir;
        return true;
    }

    /// <summary>
    /// CLEAN: Simple connection to animation system
    /// </summary>
    void ConnectToAnimationSystem()
    {
        if (networkedAnimator == null || avatarAnimator == null)
        {
            Debug.LogError("❌ Cannot connect animation system - missing components");
            return;
        }

        // Tell the animation system about the new avatar
        networkedAnimator.SetAvatarAnimator(avatarAnimator);
    }

    void ConfigureHumanoidAvatar()
    {
        if (avatarInstance == null) return;

        // Try to find existing Avatar asset from the FBX
        var avatarAssetFromFBX = avatarFBX.GetComponent<Animator>()?.avatar;

        if (avatarAssetFromFBX != null && avatarAssetFromFBX.isHuman)
        {
            avatarAsset = avatarAssetFromFBX;
        }
        else
        {
            Debug.LogWarning("⚠️ No Humanoid Avatar found in FBX. Please configure Avatar in Import Settings.");
        }
    }

    /// <summary>
    /// Engages or disengages VR idle suppression. Called by U3DPlayerController when
    /// the local player enters or exits VR. While engaged, LateUpdate freezes the
    /// Animator (speed = 0) whenever the player is idle, and resumes it (speed = 1)
    /// whenever the player is moving in any way. This suppresses idle-clip motion
    /// across the entire avatar — head, neck, spine, arms, hands, fingers, hips,
    /// everything — without needing to know which bones the clip animates.
    /// On disengage, Animator speed is unconditionally restored to 1.
    /// Safe to call before initialization and from non-local players (no-ops).
    /// </summary>
    public void SetVRMode(bool enabled)
    {
        if (!isInitialized || avatarAnimator == null) return;

        vrIdleSuppressionActive = enabled;

        if (enabled)
        {
            _prevSeated = playerController != null && playerController.NetworkIsSeated;
            _prevSuppressLocomotion = playerController != null && playerController.NetworkSuppressLocomotion;
        }
        else
        {
            avatarAnimator.speed = 1f;
            freezeScheduledTime = -1f;
        }
    }
    /// <summary>
    /// While VR idle suppression is active, drives the avatar Animator's speed based
    /// on the player controller's movement state. Unfreezing is instant; freezing is
    /// delayed slightly to let any in-progress animation transition complete cleanly,
    /// preventing the avatar from getting stuck mid-blend when exiting states like
    /// Flying. If the player starts moving again during the delay, the pending freeze
    /// is cancelled.
    /// </summary>
    void LateUpdate()
    {
        if (!vrIdleSuppressionActive) return;
        if (avatarAnimator == null || playerController == null) return;

        bool seated = playerController.NetworkIsSeated;
        bool suppressLocomotion = playerController.NetworkSuppressLocomotion;

        // When a pose-defining flag changes (sitting down/up, entering/leaving a standing
        // hold), unfreeze briefly so the transition into the new pose plays, then let the
        // delayed-freeze path below re-freeze on the new static pose. Runs before the
        // early-outs so it works even when the animator is already frozen at speed 0 —
        // which is the case when you sit from a standstill.
        if (seated != _prevSeated || suppressLocomotion != _prevSuppressLocomotion)
        {
            avatarAnimator.speed = 1f;
            freezeScheduledTime = Time.time + 0.3f;
            _prevSeated = seated;
            _prevSuppressLocomotion = suppressLocomotion;
        }

        bool movementFlagsClear = !playerController.NetworkIsMoving
                               && !playerController.NetworkIsCrouching
                               && !playerController.NetworkIsFlying
                               && !playerController.NetworkIsSwimming
                               && !playerController.NetworkIsClimbing
                               && !playerController.NetworkIsJumping;

        if (!movementFlagsClear)
        {
            if (avatarAnimator.speed != 1f) avatarAnimator.speed = 1f;
            freezeScheduledTime = -1f;
            return;
        }

        if (avatarAnimator.speed == 0f) return;

        if (freezeScheduledTime < 0f)
        {
            freezeScheduledTime = Time.time + 0.3f;
            return;
        }

        if (Time.time >= freezeScheduledTime)
        {
            avatarAnimator.speed = 0f;
            freezeScheduledTime = -1f;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!isInitialized || playerController == null) return;
        // Clean animation system handles its own network updates automatically
    }

    public override void Render()
    {
        if (!isInitialized) return;

        // Update avatar visibility based on perspective
        UpdateAvatarVisibility();

        // Ensure avatar follows player rotation
        if (followPlayerRotation && avatarInstance != null)
        {
            avatarInstance.transform.localRotation = Quaternion.identity;
        }
    }

    void UpdateAvatarVisibility()
    {
        if (avatarRenderers == null) return;

        // Resolve the steerable this player is currently controlling so ShouldRender can check its
        // avatar mode. Done here because UpdateAvatarVisibility runs on a NetworkBehaviour (has
        // Runner access); U3DAvatarIK does not.
        // Local player: CurrentlySteering is authoritative, no Runner lookup needed.
        // Remote player: resolve from NetworkSteerableRef via Runner.
        if (avatarIK != null)
        {
            U3D.U3DSteerable resolvedSteerable = null;

            if (playerController.IsLocalPlayer)
            {
                resolvedSteerable = U3D.U3DSteerable.CurrentlySteering;
            }
            else if (Runner != null && playerController.NetworkSteerableRef != default)
            {
                Runner.TryFindBehaviour(playerController.NetworkSteerableRef, out resolvedSteerable);
            }

            avatarIK.SetResolvedSteerable(resolvedSteerable);
        }

        bool shouldShow = (avatarIK != null)
            ? avatarIK.ShouldRender(hideInFirstPerson)
            : ResolveVisibilityFallback();

        foreach (var renderer in avatarRenderers)
        {
            if (renderer != null && renderer.enabled != shouldShow)
                renderer.enabled = shouldShow;
        }

        // Cosmetic attachments follow the avatar's own visibility — registered by
        // U3DPlayerAttachments when each accessory is built, toggled here in lockstep with the body
        // so a worn hat hides in first-person desktop and shows in VR and third-person exactly as
        // the avatar does.
        for (int i = 0; i < _attachmentRenderers.Count; i++)
        {
            Renderer r = _attachmentRenderers[i];
            if (r != null && r.enabled != shouldShow)
                r.enabled = shouldShow;
        }

        // Head-attachment view protection. For the local wearer in VR first person, pieces riding
        // the head render shadow-only so a face-covering costume head can't blind them — they keep
        // the piece's shadow as a grounding cue, everyone else sees it normally. In VR third person
        // the camera is behind the body, so the piece shows in full. Applied per frame against the
        // current state so entering/exiting VR and perspective switches restore the authored mode
        // with no separate transition handling.
        bool suppressHeadPieces = playerController != null
            && playerController.IsLocalPlayer
            && playerController.IsInVRMode
            && playerController.IsFirstPerson;

        for (int i = 0; i < _headAttachmentEntries.Count; i++)
        {
            HeadAttachmentEntry entry = _headAttachmentEntries[i];
            if (entry.Renderer == null) continue;

            UnityEngine.Rendering.ShadowCastingMode desired = suppressHeadPieces
                ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                : entry.OriginalMode;

            if (entry.Renderer.shadowCastingMode != desired)
                entry.Renderer.shadowCastingMode = desired;
        }
    }

    /// <summary>
    /// Visibility resolution used when the IK component isn't available (e.g. non-humanoid
    /// avatar). Mirrors the IK component's logic so behavior stays consistent.
    /// </summary>
    bool ResolveVisibilityFallback()
    {
        if (playerController == null) return true;

        bool isLocal = playerController.IsLocalPlayer;
        bool inVR = playerController.NetworkIsInVR;
        bool isFirstPerson = playerController.NetworkIsFirstPerson;

        if (!isLocal)
        {
            // Non-humanoid remote avatar: check networked steerable ref directly
            // since there's no IK component to delegate to.
            if (Runner != null && playerController.NetworkSteerableRef != default)
            {
                U3D.U3DSteerable resolvedSteerable;
                if (Runner.TryFindBehaviour(playerController.NetworkSteerableRef, out resolvedSteerable)
                    && resolvedSteerable != null
                    && resolvedSteerable.AvatarMode == U3D.SteerableAvatarMode.HiddenAvatar)
                    return false;
            }
            return true;
        }

        if (inVR) return true;
        if (hideInFirstPerson && isFirstPerson) return false;
        return true;
    }

    /// <summary>
    /// Registers a cosmetic attachment's renderers so they follow this avatar's visibility.
    /// Called by U3DPlayerAttachments when an accessory is built. Skips nulls and duplicates.
    /// </summary>
    public void RegisterAttachmentRenderers(Renderer[] renderers)
    {
        if (renderers == null) return;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && !_attachmentRenderers.Contains(renderers[i]))
                _attachmentRenderers.Add(renderers[i]);
        }
    }

    /// <summary>
    /// Removes a cosmetic attachment's renderers from visibility tracking. Called by
    /// U3DPlayerAttachments before the accessory instance is destroyed.
    /// </summary>
    public void UnregisterAttachmentRenderers(Renderer[] renderers)
    {
        if (renderers == null) return;
        for (int i = 0; i < renderers.Length; i++)
            _attachmentRenderers.Remove(renderers[i]);
    }

    /// <summary>
    /// Registers head-riding attachment renderers for VR view protection, in addition to the
    /// general registration. Called by U3DPlayerAttachments for pieces attached to the head bone
    /// or a socket under it. Captures each renderer's authored shadow mode so it can be restored
    /// exactly. Skips nulls and duplicates.
    /// </summary>
    public void RegisterHeadAttachmentRenderers(Renderer[] renderers)
    {
        if (renderers == null) return;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;

            bool exists = false;
            for (int j = 0; j < _headAttachmentEntries.Count; j++)
            {
                if (_headAttachmentEntries[j].Renderer == r) { exists = true; break; }
            }
            if (exists) continue;

            _headAttachmentEntries.Add(new HeadAttachmentEntry
            {
                Renderer = r,
                OriginalMode = r.shadowCastingMode
            });
        }
    }

    /// <summary>
    /// Removes head-riding attachment renderers from VR view protection, restoring each one's
    /// authored shadow mode. Called by U3DPlayerAttachments before the accessory is destroyed.
    /// </summary>
    public void UnregisterHeadAttachmentRenderers(Renderer[] renderers)
    {
        if (renderers == null) return;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;

            for (int j = _headAttachmentEntries.Count - 1; j >= 0; j--)
            {
                if (_headAttachmentEntries[j].Renderer == r)
                {
                    if (r != null)
                        r.shadowCastingMode = _headAttachmentEntries[j].OriginalMode;
                    _headAttachmentEntries.RemoveAt(j);
                }
            }
        }
    }

    /// <summary>
    /// Resolves the hand Transform for attaching held or summoned objects, using this
    /// player's equipped avatar. Both U3DGrabbable and U3DInventory call this so hand
    /// resolution lives in one place and can't drift between them. Resolution order:
    ///   1. Exact transform name match anywhere under the player — preserves the shipped
    ///      rig (bone literally named "RightHand") and any custom socket a creator typed
    ///      into the hand bone name field.
    ///   2. Humanoid hand bone by role via the avatar's Animator, so any humanoid rig
    ///      resolves regardless of bone naming (e.g. Mixamo's "mixamorig:" prefix) with
    ///      no per-rig field editing.
    ///   3. Last resort, only when createAnchorIfMissing is true: a persistent synthetic
    ///      anchor in front of and above the player, for non-humanoid avatars with no
    ///      matching bone. When false, returns null so best-effort callers (like remote-
    ///      viewer interpolation) can skip cleanly instead of spawning an anchor.
    /// Handedness comes from handBoneName: containing "Left" targets the left hand,
    /// otherwise the right. An empty handBoneName means no hand attachment — tiers 1 and
    /// 2 are skipped and the result depends solely on createAnchorIfMissing.
    /// </summary>
    public Transform ResolveHandBone(string handBoneName, bool createAnchorIfMissing)
    {
        Transform playerTransform = transform;

        if (!string.IsNullOrEmpty(handBoneName))
        {
            Transform[] allTransforms = playerTransform.GetComponentsInChildren<Transform>();
            foreach (Transform t in allTransforms)
            {
                if (t.name == handBoneName && !t.name.Contains("Camera"))
                    return t;
            }

            Transform humanoidHand = ResolveHumanoidHand(handBoneName);
            if (humanoidHand != null)
                return humanoidHand;
        }

        if (!createAnchorIfMissing)
            return null;

        if (_handAnchor == null)
        {
            GameObject anchor = new GameObject($"{playerTransform.name}_HandAnchor");
            anchor.transform.SetParent(playerTransform);
            anchor.transform.localPosition = Vector3.forward * 0.5f + Vector3.up * 1.2f;
            anchor.transform.localRotation = Quaternion.identity;
            _handAnchor = anchor.transform;
        }
        return _handAnchor;
    }

    /// <summary>
    /// Maps handBoneName to a Humanoid hand transform on this avatar's Animator. Returns
    /// null if the avatar isn't Humanoid, the name is empty, or the rig has no mapped
    /// hand bone. Handedness: name contains "Left" → left hand, otherwise right.
    /// </summary>
    private Transform ResolveHumanoidHand(string handBoneName)
    {
        if (avatarAnimator == null || !avatarAnimator.isHuman) return null;
        if (string.IsNullOrEmpty(handBoneName)) return null;

        bool wantsLeft = handBoneName.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) >= 0;
        HumanBodyBones boneId = wantsLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
        return avatarAnimator.GetBoneTransform(boneId);
    }

    // Utility properties (unchanged)
    public bool IsAvatarInitialized => isInitialized;
    public GameObject GetAvatarInstance() => avatarInstance;
    public Animator GetAvatarAnimator() => avatarAnimator;
    public Avatar GetAvatarAsset() => avatarAsset;
    public U3DNetworkedAnimator GetNetworkedAnimator() => networkedAnimator;

    void OnValidate()
    {
        if (avatarScaleMultiplier <= 0f) avatarScaleMultiplier = 1f;
    }
}