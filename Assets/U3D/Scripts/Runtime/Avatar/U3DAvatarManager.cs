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
    [Header("VR Hand IK")]
    [Tooltip("XR input actions asset. Used by the auto-attached U3DAvatarHandIK to read VR controller poses. Should reference the same U3DInputActions asset used by the player controller.")]
    [SerializeField] private UnityEngine.InputSystem.InputActionAsset xrInputActions;

    // Core Components
    private U3DPlayerController playerController;
    private GameObject avatarInstance;
    private Animator avatarAnimator;
    private Avatar avatarAsset;
    private SkinnedMeshRenderer[] avatarRenderers;
    private U3DAvatarHandIK avatarHandIK;

    // CLEAN: Simple animation system
    private U3DNetworkedAnimator networkedAnimator;
    private bool isInitialized = false;

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

            // Auto-attach VR hand IK. Works for any humanoid avatar (default and creator-supplied).
            // If the avatar isn't humanoid, U3DAvatarHandIK logs a warning and disables itself.
            avatarHandIK = avatarInstance.GetComponent<U3DAvatarHandIK>();
            if (avatarHandIK == null)
                avatarHandIK = avatarInstance.AddComponent<U3DAvatarHandIK>();
            avatarHandIK.Initialize(playerController, xrInputActions);

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
        bool shouldShow = (avatarHandIK != null)
            ? avatarHandIK.ShouldRender(hideInFirstPerson)
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