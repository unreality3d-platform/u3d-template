using UnityEngine;
using System.Collections.Generic;
using System;

/// <summary>
/// ScriptableObject for organizing avatar animation clips and runtime AnimatorController generation
/// Unity 6+ optimized with performance caching and network-ready parameter mapping
/// </summary>
[CreateAssetMenu(fileName = "New Avatar Animation Set", menuName = "U3D/Avatar Animation Set")]
public class AvatarAnimationSet : ScriptableObject
{
    [Header("Core Locomotion Animations")]
    [SerializeField] private AnimationClip idleAnimation;
    [SerializeField] private AnimationClip walkAnimation;
    [SerializeField] private AnimationClip runAnimation;
    [SerializeField] private AnimationClip sprintAnimation;

    [Header("Movement States")]
    [SerializeField] private AnimationClip crouchIdleAnimation;
    [SerializeField] private AnimationClip crouchWalkAnimation;
    [SerializeField] private AnimationClip jumpStartAnimation;
    [SerializeField] private AnimationClip jumpLoopAnimation;
    [SerializeField] private AnimationClip landAnimation;

    [Header("Flying/Swimming States")]
    [SerializeField] private AnimationClip flyIdleAnimation;
    [SerializeField] private AnimationClip flyForwardAnimation;
    [SerializeField] private AnimationClip flyUpAnimation;
    [SerializeField] private AnimationClip flyDownAnimation;

    [Header("Blend Tree Configuration")]
    [SerializeField] private bool useBlendTrees = true;
    [SerializeField] private float walkToRunThreshold = 2.5f;
    [SerializeField] private float runToSprintThreshold = 5.0f;
    [SerializeField] private Vector2 blendTreeRange = new Vector2(0f, 8f);

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

    // Standard parameter names for U3D networking integration
    public static readonly string[] StandardParameters = {
        "IsMoving", "IsSprinting", "IsCrouching", "IsFlying", "IsGrounded",
        "MoveSpeed", "MoveX", "MoveY", "JumpTrigger", "LandTrigger"
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

    // Public Properties
    public AnimationClip IdleAnimation => idleAnimation;
    public AnimationClip WalkAnimation => walkAnimation;
    public AnimationClip RunAnimation => runAnimation;
    public AnimationClip SprintAnimation => sprintAnimation;
    public AnimationClip CrouchIdleAnimation => crouchIdleAnimation;
    public AnimationClip CrouchWalkAnimation => crouchWalkAnimation;
    public AnimationClip JumpStartAnimation => jumpStartAnimation;
    public AnimationClip JumpLoopAnimation => jumpLoopAnimation;
    public AnimationClip LandAnimation => landAnimation;
    public AnimationClip FlyIdleAnimation => flyIdleAnimation;
    public AnimationClip FlyForwardAnimation => flyForwardAnimation;
    public AnimationClip FlyUpAnimation => flyUpAnimation;
    public AnimationClip FlyDownAnimation => flyDownAnimation;

    public bool UseBlendTrees => useBlendTrees;
    public float WalkToRunThreshold => walkToRunThreshold;
    public float RunToSprintThreshold => runToSprintThreshold;
    public Vector2 BlendTreeRange => blendTreeRange;

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
    /// Validate all animation clips for humanoid compatibility
    /// </summary>
    public bool ValidateAnimations(out List<string> warnings)
    {
        warnings = new List<string>();
        bool allValid = true;

        // Check core animations
        if (!ValidateClip(idleAnimation, "Idle", warnings)) allValid = false;
        if (!ValidateClip(walkAnimation, "Walk", warnings)) allValid = false;
        if (!ValidateClip(runAnimation, "Run", warnings)) allValid = false;

        // Check optional animations
        ValidateClip(sprintAnimation, "Sprint", warnings, false);
        ValidateClip(crouchIdleAnimation, "Crouch Idle", warnings, false);
        ValidateClip(crouchWalkAnimation, "Crouch Walk", warnings, false);
        ValidateClip(jumpStartAnimation, "Jump Start", warnings, false);
        ValidateClip(jumpLoopAnimation, "Jump Loop", warnings, false);
        ValidateClip(landAnimation, "Land", warnings, false);
        ValidateClip(flyIdleAnimation, "Fly Idle", warnings, false);
        ValidateClip(flyForwardAnimation, "Fly Forward", warnings, false);
        ValidateClip(flyUpAnimation, "Fly Up", warnings, false);
        ValidateClip(flyDownAnimation, "Fly Down", warnings, false);

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
                warnings.Add($"❌ Required animation '{clipName}' is missing");
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
    /// Get animation clip by state name
    /// </summary>
    public AnimationClip GetAnimationClip(string stateName)
    {
        switch (stateName.ToLower())
        {
            case "idle": return idleAnimation;
            case "walk": return walkAnimation;
            case "run": return runAnimation;
            case "sprint": return sprintAnimation;
            case "crouchidle": return crouchIdleAnimation;
            case "crouchwalk": return crouchWalkAnimation;
            case "jumpstart": return jumpStartAnimation;
            case "jumploop": return jumpLoopAnimation;
            case "land": return landAnimation;
            case "flyidle": return flyIdleAnimation;
            case "flyforward": return flyForwardAnimation;
            case "flyup": return flyUpAnimation;
            case "flydown": return flyDownAnimation;
            default:
                // Search custom animations
                var customAnim = customAnimations.Find(c => c.name.ToLower() == stateName.ToLower());
                return customAnim?.clip;
        }
    }

    /// <summary>
    /// Get all required animation clips for runtime controller generation
    /// </summary>
    public Dictionary<string, AnimationClip> GetAllAnimationClips()
    {
        var clips = new Dictionary<string, AnimationClip>();

        // Add core animations
        if (idleAnimation != null) clips["Idle"] = idleAnimation;
        if (walkAnimation != null) clips["Walk"] = walkAnimation;
        if (runAnimation != null) clips["Run"] = runAnimation;
        if (sprintAnimation != null) clips["Sprint"] = sprintAnimation;

        // Add movement state animations
        if (crouchIdleAnimation != null) clips["CrouchIdle"] = crouchIdleAnimation;
        if (crouchWalkAnimation != null) clips["CrouchWalk"] = crouchWalkAnimation;
        if (jumpStartAnimation != null) clips["JumpStart"] = jumpStartAnimation;
        if (jumpLoopAnimation != null) clips["JumpLoop"] = jumpLoopAnimation;
        if (landAnimation != null) clips["Land"] = landAnimation;

        // Add flying animations
        if (flyIdleAnimation != null) clips["FlyIdle"] = flyIdleAnimation;
        if (flyForwardAnimation != null) clips["FlyForward"] = flyForwardAnimation;
        if (flyUpAnimation != null) clips["FlyUp"] = flyUpAnimation;
        if (flyDownAnimation != null) clips["FlyDown"] = flyDownAnimation;

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
    /// Editor validation
    /// </summary>
    void OnValidate()
    {
        // Ensure valid threshold values
        if (walkToRunThreshold <= 0f) walkToRunThreshold = 2.5f;
        if (runToSprintThreshold <= walkToRunThreshold) runToSprintThreshold = walkToRunThreshold + 2.5f;

        // Ensure valid transition durations
        if (standardTransitionDuration < 0f) standardTransitionDuration = 0.15f;
        if (quickTransitionDuration < 0f) quickTransitionDuration = 0.05f;
        if (slowTransitionDuration < 0f) slowTransitionDuration = 0.3f;

        // Ensure valid blend tree range
        if (blendTreeRange.y <= blendTreeRange.x) blendTreeRange.y = blendTreeRange.x + 8f;

        // Clear caches on validation
        _parametersCached = false;
        _validatedClips.Clear();
    }
}