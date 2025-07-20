using UnityEngine;
using System.Collections.Generic;
using System;

/// <summary>
/// FIXED: Complete ScriptableObject for all 8 core avatar animation states
/// Unity 6+ optimized with performance caching and network-ready parameter mapping
/// Core States: Idle, Walking, Running, Crouching, Jumping, Flying, Swimming, Climbing
/// </summary>
[CreateAssetMenu(fileName = "New Avatar Animation Set", menuName = "U3D/Avatar Animation Set")]
public class AvatarAnimationSet : ScriptableObject
{
    [Header("8 Core Animation States - ALL REQUIRED")]
    [SerializeField] private AnimationClip idleLoop;
    [SerializeField] private AnimationClip walkingLoop;
    [SerializeField] private AnimationClip runningLoop;
    [SerializeField] private AnimationClip crouchingLoop;
    [SerializeField] private AnimationClip jumpingLoop;
    [SerializeField] private AnimationClip flyingLoop;
    [SerializeField] private AnimationClip swimmingLoop;
    [SerializeField] private AnimationClip climbingLoop;  // ADDED: Missing core state

    [Header("Animation Transitions")]
    [SerializeField] private float standardTransitionDuration = 0.15f;
    [SerializeField] private float quickTransitionDuration = 0.05f;
    [SerializeField] private float slowTransitionDuration = 0.3f;

    [Header("Custom Animations")]
    [SerializeField] private List<CustomAnimationClip> customAnimations = new List<CustomAnimationClip>();

    [Header("Animation Parameters")]
    [SerializeField] private List<AnimatorParameterDefinition> customParameters = new List<AnimatorParameterDefinition>();

    // Cached parameter IDs for Unity 6+ performance optimization
    private Dictionary<string, int> _cachedParameterIds = new Dictionary<string, int>();
    private bool _parametersCached = false;

    // Animation clip validation cache
    private HashSet<AnimationClip> _validatedClips = new HashSet<AnimationClip>();

    // FIXED: All 8 core states for Fusion 2 networking
    public static readonly string[] StandardParameters = {
        "IsMoving", "IsCrouching", "IsFlying", "IsSwimming", "IsGrounded", "IsClimbing",
        "MoveSpeed", "MoveX", "MoveY", "JumpTrigger"
    };

    /// <summary>
    /// Custom animation clip definition with metadata
    /// </summary>
    [System.Serializable]
    public class CustomAnimationClip
    {
        public string name;
        public AnimationClip clip;
        public AnimationTriggerType triggerType = AnimationTriggerType.Manual;
        public string triggerParameter = "";
        public bool looping = false;
        public float transitionDuration = 0.15f;

        [Header("Networking")]
        public bool networkSynchronized = false;
        public AnimationPriority priority = AnimationPriority.Normal;
    }

    /// <summary>
    /// Custom animator parameter definition
    /// </summary>
    [System.Serializable]
    public class AnimatorParameterDefinition
    {
        public string parameterName;
        public AnimatorControllerParameterType parameterType;
        public float defaultFloat = 0f;
        public int defaultInt = 0;
        public bool defaultBool = false;
        public bool networkSynchronized = false;
    }

    public enum AnimationTriggerType
    {
        Manual,
        OnGroundContact,
        OnJump,
        OnStateChange,
        OnInput
    }

    public enum AnimationPriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Critical = 3
    }

    // FIXED: All 8 Core Properties (maintaining compatibility)
    public AnimationClip IdleAnimation => idleLoop;
    public AnimationClip WalkAnimation => walkingLoop;
    public AnimationClip RunAnimation => runningLoop;
    public AnimationClip SprintAnimation => runningLoop; // Running serves as sprint
    public AnimationClip CrouchIdleAnimation => crouchingLoop;
    public AnimationClip JumpStartAnimation => jumpingLoop;
    public AnimationClip JumpLoopAnimation => jumpingLoop;
    public AnimationClip LandAnimation => jumpingLoop; // Jumping loop handles landing
    public AnimationClip FlyIdleAnimation => flyingLoop;
    public AnimationClip FlyForwardAnimation => flyingLoop;
    public AnimationClip FlyUpAnimation => flyingLoop;
    public AnimationClip FlyDownAnimation => flyingLoop;

    // FIXED: New core state properties
    public AnimationClip SwimIdleAnimation => swimmingLoop;
    public AnimationClip SwimForwardAnimation => swimmingLoop;
    public AnimationClip ClimbIdleAnimation => climbingLoop;
    public AnimationClip ClimbUpAnimation => climbingLoop;
    public AnimationClip ClimbDownAnimation => climbingLoop;

    // Simplified property names for clarity
    public AnimationClip IdleLoop => idleLoop;
    public AnimationClip WalkingLoop => walkingLoop;
    public AnimationClip RunningLoop => runningLoop;
    public AnimationClip CrouchingLoop => crouchingLoop;
    public AnimationClip JumpingLoop => jumpingLoop;
    public AnimationClip FlyingLoop => flyingLoop;
    public AnimationClip SwimmingLoop => swimmingLoop;
    public AnimationClip ClimbingLoop => climbingLoop;  

    public float StandardTransitionDuration => standardTransitionDuration;
    public float QuickTransitionDuration => quickTransitionDuration;
    public float SlowTransitionDuration => slowTransitionDuration;

    public List<CustomAnimationClip> CustomAnimations => customAnimations;
    public List<AnimatorParameterDefinition> CustomParameters => customParameters;

    /// <summary>
    /// Get cached parameter ID for Unity 6+ performance optimization
    /// </summary>
    public int GetParameterID(string parameterName)
    {
        if (!_parametersCached)
        {
            CacheParameterIDs();
        }

        return _cachedParameterIds.TryGetValue(parameterName, out int id) ? id : -1;
    }

    /// <summary>
    /// Cache all parameter IDs for performance
    /// </summary>
    private void CacheParameterIDs()
    {
        _cachedParameterIds.Clear();

        // Cache standard parameters
        foreach (string paramName in StandardParameters)
        {
            _cachedParameterIds[paramName] = Animator.StringToHash(paramName);
        }

        // Cache custom parameters
        foreach (var customParam in customParameters)
        {
            if (!string.IsNullOrEmpty(customParam.parameterName))
            {
                _cachedParameterIds[customParam.parameterName] = Animator.StringToHash(customParam.parameterName);
            }
        }

        _parametersCached = true;
        Debug.Log($"✅ Cached {_cachedParameterIds.Count} animation parameter IDs for performance");
    }

    /// <summary>
    /// FIXED: Validate all 8 core animation clips for humanoid compatibility
    /// </summary>
    public bool ValidateAnimations(out List<string> warnings)
    {
        warnings = new List<string>();
        bool allValid = true;

        // Check ALL 8 core animations (required)
        if (!ValidateClip(idleLoop, "Idle Loop", warnings)) allValid = false;
        if (!ValidateClip(walkingLoop, "Walking Loop", warnings)) allValid = false;
        if (!ValidateClip(runningLoop, "Running Loop", warnings)) allValid = false;
        if (!ValidateClip(crouchingLoop, "Crouching Loop", warnings)) allValid = false;
        if (!ValidateClip(jumpingLoop, "Jumping Loop", warnings)) allValid = false;
        if (!ValidateClip(flyingLoop, "Flying Loop", warnings)) allValid = false;
        if (!ValidateClip(swimmingLoop, "Swimming Loop", warnings)) allValid = false;
        if (!ValidateClip(climbingLoop, "Climbing Loop", warnings)) allValid = false;

        // Check custom animations
        foreach (var customAnim in customAnimations)
        {
            ValidateClip(customAnim.clip, $"Custom: {customAnim.name}", warnings, false);
        }

        return allValid;
    }

    /// <summary>
    /// Validate individual animation clip
    /// </summary>
    private bool ValidateClip(AnimationClip clip, string clipName, List<string> warnings, bool required = true)
    {
        if (clip == null)
        {
            if (required)
            {
                warnings.Add($"❌ Required core animation '{clipName}' is missing");
                return false;
            }
            else
            {
                warnings.Add($"⚠️ Optional animation '{clipName}' is missing");
                return true;
            }
        }

        // Check if already validated
        if (_validatedClips.Contains(clip))
        {
            return true;
        }

        // Validate clip properties
        if (clip.length <= 0f)
        {
            warnings.Add($"⚠️ Animation '{clipName}' has zero length");
        }

        if (clip.frameRate <= 0f)
        {
            warnings.Add($"⚠️ Animation '{clipName}' has invalid frame rate");
        }

        // Check for humanoid compatibility if possible
        if (clip.humanMotion)
        {
            Debug.Log($"✅ '{clipName}' is humanoid-compatible");
        }
        else
        {
            warnings.Add($"⚠️ Animation '{clipName}' may not be humanoid-compatible");
        }

        _validatedClips.Add(clip);
        return true;
    }

    /// <summary>
    /// FIXED: Get animation clip by state name (supports all 8 core states)
    /// </summary>
    public AnimationClip GetAnimationClip(string stateName)
    {
        switch (stateName.ToLower())
        {
            case "idle": return idleLoop;
            case "walking": return walkingLoop;
            case "running": return runningLoop;
            case "crouching": return crouchingLoop;
            case "jumping": return jumpingLoop;
            case "flying": return flyingLoop;
            case "swimming": return swimmingLoop;
            case "climbing": return climbingLoop;  // ADDED
            default:
                // Search custom animations
                var customAnim = customAnimations.Find(c => c.name.ToLower() == stateName.ToLower());
                return customAnim?.clip;
        }
    }

    /// <summary>
    /// FIXED: Get all 8 core animation clips for runtime controller generation
    /// </summary>
    public Dictionary<string, AnimationClip> GetAllAnimationClips()
    {
        var clips = new Dictionary<string, AnimationClip>();

        // Add ALL 8 core animations with legacy naming support
        if (idleLoop != null) clips["Idle"] = idleLoop;
        if (walkingLoop != null)
        {
            clips["Walk"] = walkingLoop;
            clips["Walking"] = walkingLoop;
        }
        if (runningLoop != null)
        {
            clips["Run"] = runningLoop;
            clips["Running"] = runningLoop;
            clips["Sprint"] = runningLoop; // Running serves as sprint
        }
        if (crouchingLoop != null)
        {
            clips["CrouchIdle"] = crouchingLoop;
            clips["Crouching"] = crouchingLoop;
        }
        if (jumpingLoop != null)
        {
            clips["JumpStart"] = jumpingLoop;
            clips["JumpLoop"] = jumpingLoop;
            clips["Land"] = jumpingLoop;
            clips["Jumping"] = jumpingLoop;
        }
        if (flyingLoop != null)
        {
            clips["FlyIdle"] = flyingLoop;
            clips["FlyForward"] = flyingLoop;
            clips["FlyUp"] = flyingLoop;
            clips["FlyDown"] = flyingLoop;
            clips["Flying"] = flyingLoop;
        }
        if (swimmingLoop != null)
        {
            clips["Swimming"] = swimmingLoop;
            clips["SwimIdle"] = swimmingLoop;
            clips["SwimForward"] = swimmingLoop;
        }
        if (climbingLoop != null)  // ADDED
        {
            clips["Climbing"] = climbingLoop;
            clips["ClimbIdle"] = climbingLoop;
            clips["ClimbUp"] = climbingLoop;
            clips["ClimbDown"] = climbingLoop;
        }

        // Add custom animations
        foreach (var customAnim in customAnimations)
        {
            if (customAnim.clip != null && !string.IsNullOrEmpty(customAnim.name))
            {
                clips[customAnim.name] = customAnim.clip;
            }
        }

        return clips;
    }

    /// <summary>
    /// ADDED: Check if all 8 core animations are assigned
    /// </summary>
    public bool HasAllCoreAnimations()
    {
        return idleLoop != null && walkingLoop != null && runningLoop != null &&
               crouchingLoop != null && jumpingLoop != null && flyingLoop != null &&
               swimmingLoop != null && climbingLoop != null;
    }

    /// <summary>
    /// ADDED: Get count of assigned core animations
    /// </summary>
    public int GetAssignedCoreAnimationCount()
    {
        int count = 0;
        if (idleLoop != null) count++;
        if (walkingLoop != null) count++;
        if (runningLoop != null) count++;
        if (crouchingLoop != null) count++;
        if (jumpingLoop != null) count++;
        if (flyingLoop != null) count++;
        if (swimmingLoop != null) count++;
        if (climbingLoop != null) count++;
        return count;
    }

    /// <summary>
    /// Editor validation
    /// </summary>
    void OnValidate()
    {
        // Ensure valid transition durations
        if (standardTransitionDuration < 0f) standardTransitionDuration = 0.15f;
        if (quickTransitionDuration < 0f) quickTransitionDuration = 0.05f;
        if (slowTransitionDuration < 0f) slowTransitionDuration = 0.3f;

        // Clear caches on validation
        _parametersCached = false;
        _validatedClips.Clear();
    }

    /// <summary>
    /// ADDED: Flexible validation - allows partial animation sets for creators
    /// </summary>
    public bool HasMinimumRequiredAnimations()
    {
        // Require only the 3 most essential animations
        return idleLoop != null && walkingLoop != null && runningLoop != null;
    }

    /// <summary>
    /// ADDED: Get validation status for UI feedback
    /// </summary>
    public string GetValidationStatus()
    {
        int assigned = GetAssignedCoreAnimationCount();

        if (assigned >= 8) return "✅ Complete (8/8 core animations)";
        if (assigned >= 6) return "🟡 Good (6/8 core animations)";
        if (assigned >= 3) return "⚠️ Basic (3/8 core animations)";
        return "❌ Incomplete (needs Idle, Walking, Running)";
    }
}