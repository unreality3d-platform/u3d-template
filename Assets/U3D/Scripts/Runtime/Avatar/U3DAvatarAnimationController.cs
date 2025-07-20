using UnityEngine;
using Fusion;
using System.Collections.Generic;

/// <summary>
/// FIXED: Unity 6 + WebGL production-ready animation controller using AnimatorOverrideController only
/// Supports all 8 core animation states with optimized Fusion 2 networking
/// WebGL compatible - NO runtime AnimatorController generation
/// </summary>
[System.Serializable]
public class U3DAvatarAnimationController : NetworkBehaviour
{
    [Header("Animation Configuration")]
    [SerializeField] private AvatarAnimationSet animationSet;
    [SerializeField] private RuntimeAnimatorController baseController; // REQUIRED for WebGL
    [SerializeField] private bool enableParameterValidation = true;

    [Header("Network Animation")]
    [SerializeField] private bool smoothNetworkAnimations = true;
    [SerializeField] private float networkSmoothTime = 0.1f;
    [SerializeField] private float parameterSyncThreshold = 0.01f;

    [Header("Performance")]
    [SerializeField] private bool useCachedParameterIds = true;
    [SerializeField] private bool optimizeInactiveStates = true;

    // FIXED: Single networked state struct for ALL 8 core animation states (optimized)
    [Networked] public U3DAnimationState NetworkAnimationState { get; set; }

    // Core Components
    private Animator targetAnimator;
    private U3DAvatarManager avatarManager;
    private U3DPlayerController playerController;
    private AnimatorOverrideController overrideController;

    // Animation State Tracking
    private Dictionary<string, int> cachedParameterIds = new Dictionary<string, int>();
    private Dictionary<string, object> lastParameterValues = new Dictionary<string, object>();
    private bool isInitialized = false;

    // Animation Events
    public System.Action<string> OnAnimationStateChanged;
    public System.Action<string, string> OnAnimationTransition;
    public System.Action<string> OnCustomAnimationTriggered;

    // Public Properties
    public bool IsInitialized => isInitialized;
    public RuntimeAnimatorController GeneratedController => overrideController;
    public AvatarAnimationSet AnimationSet => animationSet;

    /// <summary>
    /// FIXED: Networked animation state struct containing all 8 core states
    /// </summary>
    [System.Serializable]
    public struct U3DAnimationState : INetworkStruct
    {
        // Core movement states
        [Networked] public bool IsMoving { get; set; }
        [Networked] public bool IsCrouching { get; set; }
        [Networked] public bool IsFlying { get; set; }
        [Networked] public bool IsSwimming { get; set; }
        [Networked] public bool IsGrounded { get; set; }
        [Networked] public bool IsClimbing { get; set; }  // ADDED: Missing core state
        [Networked] public bool IsJumping { get; set; }

        // Movement parameters
        [Networked] public float MoveSpeed { get; set; }
        [Networked] public Vector2 MoveDirection { get; set; }

        // Animation triggers
        [Networked] public int AnimationTriggerHash { get; set; }
        [Networked] public float StateTransitionTime { get; set; }
    }

    /// <summary>
    /// Initialize with WebGL-safe AnimatorOverrideController approach only
    /// </summary>
    public bool Initialize(Animator animator, U3DAvatarManager manager, U3DPlayerController controller)
    {
        if (isInitialized)
        {
            Debug.LogWarning("⚠️ U3DAvatarAnimationController already initialized");
            return true;
        }

        targetAnimator = animator;
        avatarManager = manager;
        playerController = controller;

        if (targetAnimator == null)
        {
            Debug.LogError("❌ Target Animator is null! Cannot initialize animation controller");
            return false;
        }

        if (animationSet == null)
        {
            Debug.LogError("❌ Animation Set is null! Please assign an AvatarAnimationSet");
            return false;
        }

        // CRITICAL: ANimator controller is REQUIRED for WebGL builds
        if (baseController == null)
        {
            Debug.LogError("❌ ANimator Controller is REQUIRED.\n" +
                          "Please assign an AnimatorController in the inspector.\n" +
                          "This controller will be used as template for AnimatorOverrideController.");
            return false;
        }

        // Validate animation set
        if (enableParameterValidation)
        {
            ValidateAnimationSet();
        }

        // Create AnimatorOverrideController (WebGL safe)
        if (!CreateOverrideController())
        {
            Debug.LogError("❌ Failed to create AnimatorOverrideController");
            return false;
        }

        // Cache parameter IDs for Unity 6+ performance
        if (useCachedParameterIds)
        {
            CacheParameterIds();
        }

        // Initialize parameter tracking
        InitializeParameterTracking();

        isInitialized = true;
        Debug.Log("✅ U3DAvatarAnimationController initialized with WebGL-safe approach");
        return true;
    }

    /// <summary>
    /// Create AnimatorOverrideController - WebGL compatible approach
    /// </summary>
    private bool CreateOverrideController()
    {
        try
        {
            // Create override controller from base
            overrideController = new AnimatorOverrideController(baseController);

            if (overrideController == null)
            {
                Debug.LogError("❌ Failed to create AnimatorOverrideController");
                return false;
            }

            // Override animation clips with our animation set
            var clipOverrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            overrideController.GetOverrides(clipOverrides);

            var animationClips = animationSet.GetAllAnimationClips();
            int successfulOverrides = 0;

            // Override clips by intelligent name matching
            for (int i = 0; i < clipOverrides.Count; i++)
            {
                var originalClip = clipOverrides[i].Key;
                if (originalClip == null) continue;

                string originalName = originalClip.name.ToLower();
                AnimationClip replacementClip = null;

                // Direct name matching first
                foreach (var kvp in animationClips)
                {
                    if (DoesClipNameMatch(originalName, kvp.Key.ToLower()))
                    {
                        replacementClip = kvp.Value;
                        break;
                    }
                }

                if (replacementClip != null)
                {
                    clipOverrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(originalClip, replacementClip);
                    successfulOverrides++;
                    Debug.Log($"✅ Overrode '{originalClip.name}' with '{replacementClip.name}'");
                }
            }

            // Apply overrides
            overrideController.ApplyOverrides(clipOverrides);

            // Assign to animator
            targetAnimator.runtimeAnimatorController = overrideController;

            Debug.Log($"✅ Created AnimatorOverrideController with {successfulOverrides} overrides");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Exception creating override controller: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// FIXED: Intelligent clip name matching for all 8 core states
    /// </summary>
    private bool DoesClipNameMatch(string originalName, string setClipKey)
    {
        // Direct match
        if (originalName == setClipKey) return true;

        // FIXED: All 8 core animation name patterns
        var patterns = new Dictionary<string, string[]>
        {
            { "idle", new[] { "idle", "standing", "default" } },
            { "walking", new[] { "walk", "walking", "move" } },
            { "running", new[] { "run", "running", "sprint", "jog" } },
            { "crouching", new[] { "crouch", "crouching", "duck" } },
            { "jumping", new[] { "jump", "jumping", "leap", "hop" } },
            { "flying", new[] { "fly", "flying", "float", "hover" } },
            { "swimming", new[] { "swim", "swimming", "water" } },
            { "climbing", new[] { "climb", "climbing", "ladder", "wall" } }  // ADDED
        };

        foreach (var pattern in patterns)
        {
            if (setClipKey == pattern.Key)
            {
                foreach (string variant in pattern.Value)
                {
                    if (originalName.Contains(variant)) return true;
                }
            }
        }

        // Partial matching as fallback
        return originalName.Contains(setClipKey) || setClipKey.Contains(originalName);
    }

    /// <summary>
    /// FIXED: Validate the assigned animation set for all 8 core states
    /// </summary>
    private void ValidateAnimationSet()
    {
        if (animationSet.ValidateAnimations(out List<string> warnings))
        {
            Debug.Log($"✅ Animation set validation passed ({animationSet.GetAssignedCoreAnimationCount()}/8 core states assigned)");
        }
        else
        {
            Debug.LogWarning("⚠️ Animation set validation found issues:");
            foreach (string warning in warnings)
            {
                Debug.LogWarning(warning);
            }
        }

        // ADDED: Check for complete core animation coverage
        if (!animationSet.HasAllCoreAnimations())
        {
            Debug.LogWarning($"⚠️ Incomplete core animation set: {animationSet.GetAssignedCoreAnimationCount()}/8 states assigned\n" +
                           "Missing states may result in broken animations for some player actions.");
        }
    }

    /// <summary>
    /// FIXED: Cache parameter IDs for Unity 6+ performance optimization
    /// </summary>
    private void CacheParameterIds()
    {
        cachedParameterIds.Clear();

        if (targetAnimator == null || targetAnimator.runtimeAnimatorController == null) return;

        // Use Unity 6+ approach: Get parameters from Animator component
        foreach (var parameter in targetAnimator.parameters)
        {
            cachedParameterIds[parameter.name] = Animator.StringToHash(parameter.name);
        }

        Debug.Log($"✅ Cached {cachedParameterIds.Count} animator parameter IDs (Unity 6+)");
    }

    /// <summary>
    /// FIXED: Initialize parameter value tracking for all 8 core states
    /// </summary>
    private void InitializeParameterTracking()
    {
        lastParameterValues.Clear();

        // Initialize with default values for ALL 8 core states
        lastParameterValues["IsMoving"] = false;
        lastParameterValues["IsCrouching"] = false;
        lastParameterValues["IsFlying"] = false;
        lastParameterValues["IsSwimming"] = false;
        lastParameterValues["IsGrounded"] = true;
        lastParameterValues["IsClimbing"] = false;  // ADDED
        lastParameterValues["IsJumping"] = false;
        lastParameterValues["MoveSpeed"] = 0f;
        lastParameterValues["MoveX"] = 0f;
        lastParameterValues["MoveY"] = 0f;
    }

    /// <summary>
    /// Fusion 2 Network Update - Sync animation state across network
    /// </summary>
    public override void FixedUpdateNetwork()
    {
        if (!isInitialized || playerController == null) return;

        // Only State Authority updates networked animation state
        if (Object.HasStateAuthority)
        {
            UpdateNetworkedAnimationState();
        }
    }

    /// <summary>
    /// FIXED: Update networked animation parameters for all 8 core states
    /// </summary>
    private void UpdateNetworkedAnimationState()
    {
        var newState = NetworkAnimationState;

        // FIXED: Sync with all available player controller states
        newState.IsCrouching = playerController.NetworkIsCrouching;
        newState.IsFlying = playerController.NetworkIsFlying;
        newState.IsGrounded = playerController.IsGrounded;
        newState.IsJumping = playerController.NetworkIsJumping;

        // ADDED: New core states (add these to PlayerController if not present)
        newState.IsSwimming = false; // Add this to player controller if swimming is implemented
        newState.IsClimbing = false; // Add this to player controller if climbing is implemented

        Vector3 actualVelocity = playerController.Velocity;
        float actualSpeed = new Vector2(actualVelocity.x, actualVelocity.z).magnitude;
        bool isActuallyMoving = actualSpeed > 0.1f && playerController.IsGrounded && !playerController.NetworkIsJumping;

        newState.IsMoving = isActuallyMoving;
        newState.MoveSpeed = isActuallyMoving ? actualSpeed : 0f;

        // Calculate movement direction
        Vector3 velocity = playerController.Velocity;
        Vector3 localVelocity = transform.InverseTransformDirection(velocity);
        newState.MoveDirection = new Vector2(localVelocity.x, localVelocity.z);

        NetworkAnimationState = newState;
    }

    /// <summary>
    /// Fusion 2 Render Update - Apply animation parameters to animator
    /// </summary>
    public override void Render()
    {
        if (!isInitialized || targetAnimator == null) return;

        // Apply networked parameters to local animator
        UpdateAnimatorParameters();

        // Handle smooth parameter interpolation if enabled
        if (smoothNetworkAnimations && !Object.HasStateAuthority)
        {
            SmoothNetworkParameters();
        }
    }

    /// <summary>
    /// FIXED: Update animator parameters with priority logic to prevent animation conflicts
    /// </summary>
    private void UpdateAnimatorParameters()
    {
        var state = NetworkAnimationState;

        // Animation priority logic to prevent conflicts
        if (state.IsJumping)
        {
            // PRIORITY 1: Jump takes highest priority - override movement
            if (useCachedParameterIds && cachedParameterIds.Count > 0)
            {
                UpdateParameterWithCache("IsJumping", true);
                UpdateParameterWithCache("IsMoving", false);
                UpdateParameterWithCache("IsCrouching", false);
                UpdateParameterWithCache("IsFlying", state.IsFlying);
                UpdateParameterWithCache("IsSwimming", false);
                UpdateParameterWithCache("IsGrounded", state.IsGrounded);
                UpdateParameterWithCache("IsClimbing", false);
                UpdateParameterWithCache("MoveSpeed", 0f);
                UpdateParameterWithCache("MoveX", 0f);
                UpdateParameterWithCache("MoveY", 0f);
            }
            else
            {
                UpdateParameterDirect("IsJumping", true);
                UpdateParameterDirect("IsMoving", false);
                UpdateParameterDirect("IsCrouching", false);
                UpdateParameterDirect("IsFlying", state.IsFlying);
                UpdateParameterDirect("IsSwimming", false);
                UpdateParameterDirect("IsGrounded", state.IsGrounded);
                UpdateParameterDirect("IsClimbing", false);
                UpdateParameterDirect("MoveSpeed", 0f);
                UpdateParameterDirect("MoveX", 0f);
                UpdateParameterDirect("MoveY", 0f);
            }
        }
        else if (state.IsFlying)
        {
            // PRIORITY 2: Flying state - can have movement
            if (useCachedParameterIds && cachedParameterIds.Count > 0)
            {
                UpdateParameterWithCache("IsFlying", true);
                UpdateParameterWithCache("IsJumping", false);
                UpdateParameterWithCache("IsCrouching", false);
                UpdateParameterWithCache("IsMoving", state.IsMoving);
                UpdateParameterWithCache("IsSwimming", false);
                UpdateParameterWithCache("IsGrounded", state.IsGrounded);
                UpdateParameterWithCache("IsClimbing", false);
                UpdateParameterWithCache("MoveSpeed", state.MoveSpeed);
                UpdateParameterWithCache("MoveX", state.MoveDirection.x);
                UpdateParameterWithCache("MoveY", state.MoveDirection.y);
            }
            else
            {
                UpdateParameterDirect("IsFlying", true);
                UpdateParameterDirect("IsJumping", false);
                UpdateParameterDirect("IsCrouching", false);
                UpdateParameterDirect("IsMoving", state.IsMoving);
                UpdateParameterDirect("IsSwimming", false);
                UpdateParameterDirect("IsGrounded", state.IsGrounded);
                UpdateParameterDirect("IsClimbing", false);
                UpdateParameterDirect("MoveSpeed", state.MoveSpeed);
                UpdateParameterDirect("MoveX", state.MoveDirection.x);
                UpdateParameterDirect("MoveY", state.MoveDirection.y);
            }
        }
        else if (state.IsSwimming)
        {
            // PRIORITY 3: Swimming state - can have movement
            if (useCachedParameterIds && cachedParameterIds.Count > 0)
            {
                UpdateParameterWithCache("IsSwimming", true);
                UpdateParameterWithCache("IsJumping", false);
                UpdateParameterWithCache("IsCrouching", false);
                UpdateParameterWithCache("IsFlying", false);
                UpdateParameterWithCache("IsMoving", state.IsMoving);
                UpdateParameterWithCache("IsGrounded", state.IsGrounded);
                UpdateParameterWithCache("IsClimbing", false);
                UpdateParameterWithCache("MoveSpeed", state.MoveSpeed);
                UpdateParameterWithCache("MoveX", state.MoveDirection.x);
                UpdateParameterWithCache("MoveY", state.MoveDirection.y);
            }
            else
            {
                UpdateParameterDirect("IsSwimming", true);
                UpdateParameterDirect("IsJumping", false);
                UpdateParameterDirect("IsCrouching", false);
                UpdateParameterDirect("IsFlying", false);
                UpdateParameterDirect("IsMoving", state.IsMoving);
                UpdateParameterDirect("IsGrounded", state.IsGrounded);
                UpdateParameterDirect("IsClimbing", false);
                UpdateParameterDirect("MoveSpeed", state.MoveSpeed);
                UpdateParameterDirect("MoveX", state.MoveDirection.x);
                UpdateParameterDirect("MoveY", state.MoveDirection.y);
            }
        }
        else if (state.IsClimbing)
        {
            // PRIORITY 4: Climbing state - can have movement
            if (useCachedParameterIds && cachedParameterIds.Count > 0)
            {
                UpdateParameterWithCache("IsClimbing", true);
                UpdateParameterWithCache("IsJumping", false);
                UpdateParameterWithCache("IsCrouching", false);
                UpdateParameterWithCache("IsFlying", false);
                UpdateParameterWithCache("IsSwimming", false);
                UpdateParameterWithCache("IsMoving", state.IsMoving);
                UpdateParameterWithCache("IsGrounded", state.IsGrounded);
                UpdateParameterWithCache("MoveSpeed", state.MoveSpeed);
                UpdateParameterWithCache("MoveX", state.MoveDirection.x);
                UpdateParameterWithCache("MoveY", state.MoveDirection.y);
            }
            else
            {
                UpdateParameterDirect("IsClimbing", true);
                UpdateParameterDirect("IsJumping", false);
                UpdateParameterDirect("IsCrouching", false);
                UpdateParameterDirect("IsFlying", false);
                UpdateParameterDirect("IsSwimming", false);
                UpdateParameterDirect("IsMoving", state.IsMoving);
                UpdateParameterDirect("IsGrounded", state.IsGrounded);
                UpdateParameterDirect("MoveSpeed", state.MoveSpeed);
                UpdateParameterDirect("MoveX", state.MoveDirection.x);
                UpdateParameterDirect("MoveY", state.MoveDirection.y);
            }
        }
        else if (state.IsCrouching)
        {
            // PRIORITY 5: Crouch state - override movement when crouching
            if (useCachedParameterIds && cachedParameterIds.Count > 0)
            {
                UpdateParameterWithCache("IsCrouching", true);
                UpdateParameterWithCache("IsJumping", false);
                UpdateParameterWithCache("IsFlying", false);
                UpdateParameterWithCache("IsSwimming", false);
                UpdateParameterWithCache("IsClimbing", false);
                UpdateParameterWithCache("IsGrounded", state.IsGrounded);
                // Allow crouch movement but at reduced speed
                UpdateParameterWithCache("IsMoving", state.IsMoving);
                UpdateParameterWithCache("MoveSpeed", state.IsMoving ? state.MoveSpeed * 0.5f : 0f);
                UpdateParameterWithCache("MoveX", state.MoveDirection.x);
                UpdateParameterWithCache("MoveY", state.MoveDirection.y);
            }
            else
            {
                UpdateParameterDirect("IsCrouching", true);
                UpdateParameterDirect("IsJumping", false);
                UpdateParameterDirect("IsFlying", false);
                UpdateParameterDirect("IsSwimming", false);
                UpdateParameterDirect("IsClimbing", false);
                UpdateParameterDirect("IsGrounded", state.IsGrounded);
                // Allow crouch movement but at reduced speed
                UpdateParameterDirect("IsMoving", state.IsMoving);
                UpdateParameterDirect("MoveSpeed", state.IsMoving ? state.MoveSpeed * 0.5f : 0f);
                UpdateParameterDirect("MoveX", state.MoveDirection.x);
                UpdateParameterDirect("MoveY", state.MoveDirection.y);
            }
        }
        else
        {
            // PRIORITY 6: Normal ground movement (lowest priority)
            if (useCachedParameterIds && cachedParameterIds.Count > 0)
            {
                UpdateParameterWithCache("IsMoving", state.IsMoving);
                UpdateParameterWithCache("IsCrouching", false);
                UpdateParameterWithCache("IsFlying", false);
                UpdateParameterWithCache("IsSwimming", false);
                UpdateParameterWithCache("IsGrounded", state.IsGrounded);
                UpdateParameterWithCache("IsClimbing", false);
                UpdateParameterWithCache("IsJumping", false);
                UpdateParameterWithCache("MoveSpeed", state.MoveSpeed);
                UpdateParameterWithCache("MoveX", state.MoveDirection.x);
                UpdateParameterWithCache("MoveY", state.MoveDirection.y);
            }
            else
            {
                UpdateParameterDirect("IsMoving", state.IsMoving);
                UpdateParameterDirect("IsCrouching", false);
                UpdateParameterDirect("IsFlying", false);
                UpdateParameterDirect("IsSwimming", false);
                UpdateParameterDirect("IsGrounded", state.IsGrounded);
                UpdateParameterDirect("IsClimbing", false);
                UpdateParameterDirect("IsJumping", false);
                UpdateParameterDirect("MoveSpeed", state.MoveSpeed);
                UpdateParameterDirect("MoveX", state.MoveDirection.x);
                UpdateParameterDirect("MoveY", state.MoveDirection.y);
            }
        }
    }

    /// <summary>
    /// Update parameter using cached ID for performance
    /// </summary>
    private void UpdateParameterWithCache<T>(string paramName, T value)
    {
        if (!cachedParameterIds.TryGetValue(paramName, out int paramId)) return;

        // Only update if value changed (Unity 6+ optimization)
        if (HasParameterChanged(paramName, value))
        {
            switch (value)
            {
                case bool boolValue:
                    targetAnimator.SetBool(paramId, boolValue);
                    break;
                case float floatValue:
                    targetAnimator.SetFloat(paramId, floatValue);
                    break;
                case int intValue:
                    targetAnimator.SetInteger(paramId, intValue);
                    break;
            }

            lastParameterValues[paramName] = value;
        }
    }

    /// <summary>
    /// Update parameter using string name (fallback)
    /// </summary>
    private void UpdateParameterDirect<T>(string paramName, T value)
    {
        if (HasParameterChanged(paramName, value))
        {
            switch (value)
            {
                case bool boolValue:
                    targetAnimator.SetBool(paramName, boolValue);
                    break;
                case float floatValue:
                    targetAnimator.SetFloat(paramName, floatValue);
                    break;
                case int intValue:
                    targetAnimator.SetInteger(paramName, intValue);
                    break;
            }

            lastParameterValues[paramName] = value;
        }
    }

    /// <summary>
    /// Check if parameter value has changed significantly
    /// </summary>
    private bool HasParameterChanged<T>(string paramName, T newValue)
    {
        if (!lastParameterValues.TryGetValue(paramName, out object lastValue))
        {
            return true; // First time setting this parameter
        }

        // For float values, use threshold comparison
        if (newValue is float newFloat && lastValue is float lastFloat)
        {
            return Mathf.Abs(newFloat - lastFloat) > parameterSyncThreshold;
        }

        // For other types, use direct comparison
        return !newValue.Equals(lastValue);
    }

    /// <summary>
    /// FIXED: Smooth network parameter interpolation for remote players (all 8 states)
    /// </summary>
    private void SmoothNetworkParameters()
    {
        if (Object.HasStateAuthority) return; // Only for remote players

        float deltaTime = Time.deltaTime;
        float smoothFactor = deltaTime / networkSmoothTime;

        // Smooth float parameters
        string[] floatParams = { "MoveSpeed", "MoveX", "MoveY" };
        foreach (string param in floatParams)
        {
            if (cachedParameterIds.TryGetValue(param, out int paramId))
            {
                float currentValue = targetAnimator.GetFloat(paramId);
                float targetValue = GetNetworkParameterValue(param);
                float smoothedValue = Mathf.Lerp(currentValue, targetValue, smoothFactor);
                targetAnimator.SetFloat(paramId, smoothedValue);
            }
        }
    }

    /// <summary>
    /// Get network parameter value by name
    /// </summary>
    private float GetNetworkParameterValue(string paramName)
    {
        var state = NetworkAnimationState;
        switch (paramName)
        {
            case "MoveSpeed": return state.MoveSpeed;
            case "MoveX": return state.MoveDirection.x;
            case "MoveY": return state.MoveDirection.y;
            default: return 0f;
        }
    }

    /// <summary>
    /// Trigger custom animation across network (WebGL safe)
    /// </summary>
    public void TriggerNetworkAnimation(string animationName)
    {
        if (!Object.HasStateAuthority)
        {
            Debug.LogWarning("⚠️ Only State Authority can trigger network animations");
            return;
        }

        // Find custom animation
        var customAnim = animationSet.CustomAnimations.Find(a => a.name == animationName);
        if (customAnim == null)
        {
            Debug.LogWarning($"⚠️ Custom animation '{animationName}' not found in animation set");
            return;
        }

        if (customAnim.networkSynchronized)
        {
            // Use trigger parameter for network sync
            var state = NetworkAnimationState;
            state.AnimationTriggerHash = animationName.GetHashCode();
            NetworkAnimationState = state;

            RPC_TriggerCustomAnimation(animationName);
        }
        else
        {
            // Local animation only
            TriggerLocalAnimation(animationName);
        }
    }

    /// <summary>
    /// RPC to trigger custom animation on all clients
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_TriggerCustomAnimation(string animationName)
    {
        TriggerLocalAnimation(animationName);
        OnCustomAnimationTriggered?.Invoke(animationName);
    }

    /// <summary>
    /// Trigger animation locally
    /// </summary>
    private void TriggerLocalAnimation(string animationName)
    {
        if (targetAnimator == null) return;

        // Try trigger parameter first
        if (useCachedParameterIds && cachedParameterIds.TryGetValue(animationName, out int paramId))
        {
            targetAnimator.SetTrigger(paramId);
        }
        else
        {
            targetAnimator.SetTrigger(animationName);
        }

        Debug.Log($"🎬 Triggered animation: {animationName}");
    }

    /// <summary>
    /// Set animation set at runtime (WebGL safe)
    /// </summary>
    public void SetAnimationSet(AvatarAnimationSet newAnimationSet)
    {
        if (newAnimationSet == null)
        {
            Debug.LogError("❌ Cannot set null animation set");
            return;
        }

        animationSet = newAnimationSet;

        if (isInitialized && baseController != null)
        {
            // Recreate override controller with new animation set
            CreateOverrideController();
            Debug.Log("✅ Updated animation set and recreated override controller");
        }
    }

    /// <summary>
    /// Get current animation state info
    /// </summary>
    public AnimatorStateInfo GetCurrentStateInfo(int layerIndex = 0)
    {
        if (targetAnimator == null)
        {
            return new AnimatorStateInfo();
        }

        return targetAnimator.GetCurrentAnimatorStateInfo(layerIndex);
    }

    /// <summary>
    /// Check if animation is currently playing
    /// </summary>
    public bool IsAnimationPlaying(string stateName, int layerIndex = 0)
    {
        if (targetAnimator == null) return false;

        var stateInfo = targetAnimator.GetCurrentAnimatorStateInfo(layerIndex);
        return stateInfo.IsName(stateName);
    }

    /// <summary>
    /// Get animation completion percentage
    /// </summary>
    public float GetAnimationProgress(int layerIndex = 0)
    {
        if (targetAnimator == null) return 0f;

        var stateInfo = targetAnimator.GetCurrentAnimatorStateInfo(layerIndex);
        return stateInfo.normalizedTime;
    }

    /// <summary>
    /// Force parameter update (bypass change detection)
    /// </summary>
    public void ForceParameterUpdate()
    {
        lastParameterValues.Clear();
        UpdateAnimatorParameters();
    }

    /// <summary>
    /// FIXED: Debug info for all 8 core states
    /// </summary>
    public Dictionary<string, object> GetCurrentParameterValues()
    {
        var currentValues = new Dictionary<string, object>();

        if (targetAnimator == null || targetAnimator.runtimeAnimatorController == null) return currentValues;

        // Get parameters from the Animator component (Unity 6+ approach)
        foreach (var parameter in targetAnimator.parameters)
        {
            try
            {
                switch (parameter.type)
                {
                    case AnimatorControllerParameterType.Bool:
                        currentValues[parameter.name] = targetAnimator.GetBool(parameter.name);
                        break;
                    case AnimatorControllerParameterType.Float:
                        currentValues[parameter.name] = targetAnimator.GetFloat(parameter.name);
                        break;
                    case AnimatorControllerParameterType.Int:
                        currentValues[parameter.name] = targetAnimator.GetInteger(parameter.name);
                        break;
                    case AnimatorControllerParameterType.Trigger:
                        currentValues[parameter.name] = "Trigger";
                        break;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"⚠️ Failed to get parameter '{parameter.name}': {e.Message}");
                currentValues[parameter.name] = "Error";
            }
        }

        return currentValues;
    }

    /// <summary>
    /// ADDED: Get current network animation state for debugging
    /// </summary>
    public U3DAnimationState GetCurrentNetworkState()
    {
        return NetworkAnimationState;
    }

    /// <summary>
    /// ADDED: Check if specific core animation state is active
    /// </summary>
    public bool IsStateActive(string stateName)
    {
        var state = NetworkAnimationState;
        switch (stateName.ToLower())
        {
            case "moving": return state.IsMoving;
            case "crouching": return state.IsCrouching;
            case "flying": return state.IsFlying;
            case "swimming": return state.IsSwimming;
            case "grounded": return state.IsGrounded;
            case "climbing": return state.IsClimbing;
            case "jumping": return state.IsJumping;
            default: return false;
        }
    }

    /// <summary>
    /// Cleanup on destroy
    /// </summary>
    void OnDestroy()
    {
        // Clear events
        OnAnimationStateChanged = null;
        OnAnimationTransition = null;
        OnCustomAnimationTriggered = null;

        // Clear caches
        cachedParameterIds.Clear();
        lastParameterValues.Clear();
    }

    /// <summary>
    /// Editor validation
    /// </summary>
    void OnValidate()
    {
        // Ensure valid network smooth time
        if (networkSmoothTime <= 0f) networkSmoothTime = 0.1f;

        // Ensure valid sync threshold
        if (parameterSyncThreshold < 0f) parameterSyncThreshold = 0.01f;

        // Validate animator controller requirement
        if (baseController == null)
        {
            Debug.LogWarning("⚠️ Animator Controller is REQUIRED.\n" +
                           "Please assign an Animator Controller to enable animation override functionality.");
        }
    }
}