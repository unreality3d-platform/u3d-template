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

            // CLEAN: Connect to animation system
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

        if (!enabled)
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

        bool movementFlagsClear = !playerController.NetworkIsMoving
                               && !playerController.NetworkIsCrouching
                               && !playerController.NetworkIsFlying
                               && !playerController.NetworkIsSwimming
                               && !playerController.NetworkIsClimbing
                               && !playerController.NetworkIsJumping;

        if (!movementFlagsClear)
        {
            // Player is moving in some way: unfreeze immediately, cancel any pending freeze.
            if (avatarAnimator.speed != 1f) avatarAnimator.speed = 1f;
            freezeScheduledTime = -1f;
            return;
        }

        // Player is idle. If the freeze isn't already scheduled and we're not already
        // frozen, schedule one to fire after enough time for any current animation
        // transition to complete (transitions in U3DAnimatorController are 0.25s).
        if (avatarAnimator.speed == 0f) return; // already frozen, nothing to do

        if (freezeScheduledTime < 0f)
        {
            freezeScheduledTime = Time.time + 0.3f;
            return;
        }

        // Freeze when the scheduled time arrives.
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

        // Per-client visibility decision: every client decides what to show for every
        // avatar based on the avatar owner's networked state. This replaces the old
        // gate that only let the owning client update visibility, which left remote
        // viewers' renderers stuck in whatever state the FBX shipped with.
        bool shouldShow = (avatarIK != null)
            ? avatarIK.ShouldRender(hideInFirstPerson)
            : ResolveVisibilityFallback();

        foreach (var renderer in avatarRenderers)
        {
            if (renderer != null && renderer.enabled != shouldShow)
                renderer.enabled = shouldShow;
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

        if (!isLocal) return true;
        if (inVR) return true;
        if (hideInFirstPerson && isFirstPerson) return false;
        return true;
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