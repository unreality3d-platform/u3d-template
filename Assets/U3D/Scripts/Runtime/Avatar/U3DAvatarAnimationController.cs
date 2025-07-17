using UnityEngine;
using Fusion;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
#endif

/// <summary>
/// Runtime AnimatorController generator and animation system for U3DAvatarManager
/// Unity 6+ optimized with Fusion 2 networking integration
/// Handles automatic parameter binding and network synchronization
/// </summary>
[System.Serializable]
public class U3DAvatarAnimationController : NetworkBehaviour
{
    [Header("Animation Configuration")]
    [SerializeField] private AvatarAnimationSet animationSet;
    [SerializeField] private bool autoGenerateController = true;
    [SerializeField] private bool enableParameterValidation = true;

    [Header("Network Animation")]
    [SerializeField] private bool smoothNetworkAnimations = true;
    [SerializeField] private float networkSmoothTime = 0.1f;
    [SerializeField] private float parameterSyncThreshold = 0.01f;

    [Header("Performance")]
    [SerializeField] private bool useCachedParameterIds = true;
    [SerializeField] private int maxAnimationLayers = 4;
    [SerializeField] private bool optimizeInactiveStates = true;

    // Networked Animation Parameters (Fusion 2)
    [Networked] public bool NetworkIsMoving { get; set; }
    [Networked] public bool NetworkIsSprinting { get; set; }
    [Networked] public bool NetworkIsCrouching { get; set; }
    [Networked] public bool NetworkIsFlying { get; set; }
    [Networked] public bool NetworkIsGrounded { get; set; }
    [Networked] public float NetworkMoveSpeed { get; set; }
    [Networked] public Vector2 NetworkMoveDirection { get; set; }
    [Networked] public int NetworkAnimationTrigger { get; set; }

    // Core Components
    private Animator targetAnimator;
    private U3DAvatarManager avatarManager;
    private U3DPlayerController playerController;
    private RuntimeAnimatorController generatedController;

    // Animation State Tracking
    private Dictionary<string, int> cachedParameterIds = new Dictionary<string, int>();
    private Dictionary<string, object> lastParameterValues = new Dictionary<string, object>();
    private bool isInitialized = false;
    private float lastNetworkUpdate = 0f;

    // Animation Events
    public System.Action<string> OnAnimationStateChanged;
    public System.Action<string, string> OnAnimationTransition;
    public System.Action<string> OnCustomAnimationTriggered;

    // Public Properties
    public bool IsInitialized => isInitialized;
    public RuntimeAnimatorController GeneratedController => generatedController;
    public AvatarAnimationSet AnimationSet => animationSet;

    /// <summary>
    /// Initialize the animation controller with target animator
    /// </summary>
    public void Initialize(Animator animator, U3DAvatarManager manager, U3DPlayerController controller)
    {
        if (isInitialized)
        {
            Debug.LogWarning("U3DAvatarAnimationController already initialized");
            return;
        }

        targetAnimator = animator;
        avatarManager = manager;
        playerController = controller;

        if (targetAnimator == null)
        {
            Debug.LogError("❌ Target Animator is null! Cannot initialize animation controller");
            return;
        }

        if (animationSet == null)
        {
            Debug.LogError("❌ Animation Set is null! Please assign an AvatarAnimationSet");
            return;
        }

        // Validate animation set
        if (enableParameterValidation)
        {
            ValidateAnimationSet();
        }

        // Generate or assign runtime controller
        if (autoGenerateController)
        {
            GenerateRuntimeController();
        }
        else if (targetAnimator.runtimeAnimatorController != null)
        {
            generatedController = targetAnimator.runtimeAnimatorController;
        }

        // Cache parameter IDs for performance
        if (useCachedParameterIds)
        {
            CacheParameterIds();
        }

        // Initialize parameter tracking
        InitializeParameterTracking();

        isInitialized = true;
        Debug.Log("✅ U3DAvatarAnimationController initialized successfully");
    }

    /// <summary>
    /// Validate the assigned animation set
    /// </summary>
    private void ValidateAnimationSet()
    {
        if (animationSet.ValidateAnimations(out List<string> warnings))
        {
            Debug.Log("✅ Animation set validation passed");
        }
        else
        {
            Debug.LogWarning("⚠️ Animation set validation found issues:");
            foreach (string warning in warnings)
            {
                Debug.LogWarning(warning);
            }
        }
    }

    /// <summary>
    /// Generate runtime AnimatorController from animation set
    /// Note: This is primarily for editor use - production should use pre-built controllers
    /// </summary>
    private void GenerateRuntimeController()
    {
#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            // Runtime generation is limited - use pre-built controllers in production
            Debug.LogWarning("⚠️ Runtime controller generation is limited. Use pre-built AnimatorControllers for best performance.");
            CreateBasicRuntimeController();
        }
        else
        {
            CreateEditorAnimatorController();
        }
#else
        CreateBasicRuntimeController();
#endif
    }

    /// <summary>
    /// Create basic runtime controller for production builds
    /// </summary>
    private void CreateBasicRuntimeController()
    {
        // In production, this should ideally use AnimatorOverrideController
        // with a pre-built base controller for best performance

        if (targetAnimator.runtimeAnimatorController == null)
        {
            Debug.LogError("❌ No AnimatorController assigned and runtime generation failed. Please assign a pre-built controller.");
            return;
        }

        generatedController = targetAnimator.runtimeAnimatorController;

        // If using AnimatorOverrideController, override clips here
        var overrideController = generatedController as AnimatorOverrideController;
        if (overrideController != null)
        {
            OverrideAnimationClips(overrideController);
        }
    }

    /// <summary>
    /// Override animation clips in AnimatorOverrideController
    /// </summary>
    private void OverrideAnimationClips(AnimatorOverrideController overrideController)
    {
        var clipOverrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        overrideController.GetOverrides(clipOverrides);

        var animationClips = animationSet.GetAllAnimationClips();

        for (int i = 0; i < clipOverrides.Count; i++)
        {
            var originalClip = clipOverrides[i].Key;

            // Try to find matching clip in animation set
            if (animationClips.TryGetValue(originalClip.name, out AnimationClip newClip))
            {
                clipOverrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(originalClip, newClip);
                Debug.Log($"✅ Overrode animation clip: {originalClip.name}");
            }
        }

        overrideController.ApplyOverrides(clipOverrides);
    }

#if UNITY_EDITOR
    /// <summary>
    /// Create full AnimatorController in editor (development/testing)
    /// </summary>
    private void CreateEditorAnimatorController()
    {
        string controllerPath = $"Assets/Generated/Avatar_Controller_{GetInstanceID()}.controller";

        // Create controller
        var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

        // Add standard parameters
        foreach (string paramName in AvatarAnimationSet.StandardParameters)
        {
            AddParameterToController(controller, paramName);
        }

        // Add custom parameters
        foreach (var customParam in animationSet.CustomParameters)
        {
            controller.AddParameter(customParam.parameterName, customParam.parameterType);
        }

        // Create basic state machine
        CreateBasicStateMachine(controller);

        // Assign to animator
        targetAnimator.runtimeAnimatorController = controller;
        generatedController = controller;

        Debug.Log($"✅ Generated AnimatorController at: {controllerPath}");
    }

    /// <summary>
    /// Add parameter to controller with proper type detection
    /// </summary>
    private void AddParameterToController(AnimatorController controller, string paramName)
    {
        switch (paramName)
        {
            case "IsMoving":
            case "IsSprinting":
            case "IsCrouching":
            case "IsFlying":
            case "IsGrounded":
                controller.AddParameter(paramName, AnimatorControllerParameterType.Bool);
                break;
            case "MoveSpeed":
            case "MoveX":
            case "MoveY":
                controller.AddParameter(paramName, AnimatorControllerParameterType.Float);
                break;
            case "JumpTrigger":
            case "LandTrigger":
                controller.AddParameter(paramName, AnimatorControllerParameterType.Trigger);
                break;
            default:
                controller.AddParameter(paramName, AnimatorControllerParameterType.Float);
                break;
        }
    }

    /// <summary>
    /// Create basic state machine with core states
    /// </summary>
    private void CreateBasicStateMachine(AnimatorController controller)
    {
        var rootStateMachine = controller.layers[0].stateMachine;

        // Create core states
        var idleState = rootStateMachine.AddState("Idle");
        var walkState = rootStateMachine.AddState("Walk");
        var runState = rootStateMachine.AddState("Run");

        // Assign animation clips
        if (animationSet.IdleAnimation != null) idleState.motion = animationSet.IdleAnimation;
        if (animationSet.WalkAnimation != null) walkState.motion = animationSet.WalkAnimation;
        if (animationSet.RunAnimation != null) runState.motion = animationSet.RunAnimation;

        // Set default state
        rootStateMachine.defaultState = idleState;

        // Create basic transitions
        CreateBasicTransitions(idleState, walkState, runState);
    }

    /// <summary>
    /// Create basic transitions between core states
    /// </summary>
    private void CreateBasicTransitions(AnimatorState idleState, AnimatorState walkState, AnimatorState runState)
    {
        // Idle to Walk
        var idleToWalk = idleState.AddTransition(walkState);
        idleToWalk.AddCondition(AnimatorConditionMode.If, 0, "IsMoving");
        idleToWalk.duration = animationSet.StandardTransitionDuration;

        // Walk to Idle
        var walkToIdle = walkState.AddTransition(idleState);
        walkToIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "IsMoving");
        walkToIdle.duration = animationSet.StandardTransitionDuration;

        // Walk to Run (if sprint animation exists)
        if (animationSet.SprintAnimation != null)
        {
            var walkToRun = walkState.AddTransition(runState);
            walkToRun.AddCondition(AnimatorConditionMode.If, 0, "IsSprinting");
            walkToRun.duration = animationSet.QuickTransitionDuration;

            var runToWalk = runState.AddTransition(walkState);
            runToWalk.AddCondition(AnimatorConditionMode.IfNot, 0, "IsSprinting");
            runToWalk.duration = animationSet.QuickTransitionDuration;
        }
    }
#endif

    /// <summary>
    /// Cache parameter IDs for Unity 6+ performance optimization
    /// </summary>
    private void CacheParameterIds()
    {
        cachedParameterIds.Clear();

        if (targetAnimator == null || targetAnimator.runtimeAnimatorController == null) return;

        // Get parameters from the Animator component, not the RuntimeAnimatorController
        foreach (var parameter in targetAnimator.parameters)
        {
            cachedParameterIds[parameter.name] = Animator.StringToHash(parameter.name);
        }

        Debug.Log($"✅ Cached {cachedParameterIds.Count} animator parameter IDs");
    }

    /// <summary>
    /// Initialize parameter value tracking for change detection
    /// </summary>
    private void InitializeParameterTracking()
    {
        lastParameterValues.Clear();

        // Initialize with default values
        lastParameterValues["IsMoving"] = false;
        lastParameterValues["IsSprinting"] = false;
        lastParameterValues["IsCrouching"] = false;
        lastParameterValues["IsFlying"] = false;
        lastParameterValues["IsGrounded"] = true;
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
    /// Update networked animation parameters from player controller
    /// </summary>
    private void UpdateNetworkedAnimationState()
    {
        // Sync with player controller state
        NetworkIsMoving = playerController.NetworkIsMoving;
        NetworkIsSprinting = playerController.NetworkIsSprinting;
        NetworkIsCrouching = playerController.NetworkIsCrouching;
        NetworkIsFlying = playerController.NetworkIsFlying;
        NetworkIsGrounded = playerController.IsGrounded;
        NetworkMoveSpeed = playerController.CurrentSpeed;

        // Calculate movement direction
        Vector3 velocity = playerController.Velocity;
        Vector3 localVelocity = transform.InverseTransformDirection(velocity);
        NetworkMoveDirection = new Vector2(localVelocity.x, localVelocity.z);
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
    /// Update animator parameters with cached IDs for performance
    /// </summary>
    private void UpdateAnimatorParameters()
    {
        // Use cached parameter IDs for Unity 6+ performance
        if (useCachedParameterIds && cachedParameterIds.Count > 0)
        {
            UpdateParameterWithCache("IsMoving", NetworkIsMoving);
            UpdateParameterWithCache("IsSprinting", NetworkIsSprinting);
            UpdateParameterWithCache("IsCrouching", NetworkIsCrouching);
            UpdateParameterWithCache("IsFlying", NetworkIsFlying);
            UpdateParameterWithCache("IsGrounded", NetworkIsGrounded);
            UpdateParameterWithCache("MoveSpeed", NetworkMoveSpeed);
            UpdateParameterWithCache("MoveX", NetworkMoveDirection.x);
            UpdateParameterWithCache("MoveY", NetworkMoveDirection.y);
        }
        else
        {
            // Fallback to string-based parameter setting
            UpdateParameterDirect("IsMoving", NetworkIsMoving);
            UpdateParameterDirect("IsSprinting", NetworkIsSprinting);
            UpdateParameterDirect("IsCrouching", NetworkIsCrouching);
            UpdateParameterDirect("IsFlying", NetworkIsFlying);
            UpdateParameterDirect("IsGrounded", NetworkIsGrounded);
            UpdateParameterDirect("MoveSpeed", NetworkMoveSpeed);
            UpdateParameterDirect("MoveX", NetworkMoveDirection.x);
            UpdateParameterDirect("MoveY", NetworkMoveDirection.y);
        }
    }

    /// <summary>
    /// Update parameter using cached ID for performance
    /// </summary>
    private void UpdateParameterWithCache<T>(string paramName, T value)
    {
        if (!cachedParameterIds.TryGetValue(paramName, out int paramId)) return;

        // Only update if value changed (optimization)
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
    /// Smooth network parameter interpolation for remote players
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
        switch (paramName)
        {
            case "MoveSpeed": return NetworkMoveSpeed;
            case "MoveX": return NetworkMoveDirection.x;
            case "MoveY": return NetworkMoveDirection.y;
            default: return 0f;
        }
    }

    /// <summary>
    /// Trigger custom animation across network
    /// </summary>
    public void TriggerNetworkAnimation(string animationName)
    {
        if (!Object.HasStateAuthority)
        {
            Debug.LogWarning("Only State Authority can trigger network animations");
            return;
        }

        // Find custom animation
        var customAnim = animationSet.CustomAnimations.Find(a => a.name == animationName);
        if (customAnim == null)
        {
            Debug.LogWarning($"Custom animation '{animationName}' not found in animation set");
            return;
        }

        if (customAnim.networkSynchronized)
        {
            // Use trigger parameter for network sync
            NetworkAnimationTrigger = animationName.GetHashCode();
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
    /// Set animation set at runtime
    /// </summary>
    public void SetAnimationSet(AvatarAnimationSet newAnimationSet)
    {
        if (newAnimationSet == null)
        {
            Debug.LogError("Cannot set null animation set");
            return;
        }

        animationSet = newAnimationSet;

        if (isInitialized)
        {
            // Reinitialize with new animation set
            isInitialized = false;
            Initialize(targetAnimator, avatarManager, playerController);
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
    /// Debug: Get all current parameter values
    /// </summary>
    public Dictionary<string, object> GetCurrentParameterValues()
    {
        var currentValues = new Dictionary<string, object>();

        if (targetAnimator == null || targetAnimator.runtimeAnimatorController == null) return currentValues;

        // Get parameters from the Animator component
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
                Debug.LogWarning($"Failed to get parameter '{parameter.name}': {e.Message}");
                currentValues[parameter.name] = "Error";
            }
        }

        return currentValues;
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

        // Ensure valid max layers
        if (maxAnimationLayers < 1) maxAnimationLayers = 4;
    }
}