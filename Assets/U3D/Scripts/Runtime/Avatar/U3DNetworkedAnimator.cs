using UnityEngine;
using Fusion;

/// <summary>
/// FUSION 2 COMPLIANT: Unity 6 + Fusion 2 + WebGL networked animation system
/// CRITICAL: Only State Authority sets parameters, NetworkMecanimAnimator syncs to proxies
/// Creator-friendly: Assigns controller and updates parameters - proper authority handling
/// </summary>
[RequireComponent(typeof(NetworkMecanimAnimator))]
public class U3DNetworkedAnimator : NetworkBehaviour
{
    [Header("🎬 Animation Controller")]
    [SerializeField] private RuntimeAnimatorController animatorController; // Your U3DAnimatorController

    [Header("🔧 Settings")]
    [SerializeField] private bool debugAnimationStates = false;

    // Core Components
    private NetworkMecanimAnimator networkAnimator;
    private Animator targetAnimator;
    private U3DPlayerController playerController;

    // Avatar animator handling
    private Animator pendingAvatarAnimator; // Store avatar animator until initialization complete

    // Cached parameter IDs for Unity 6+ performance
    private int hashIsMoving;
    private int hashIsCrouching;
    private int hashIsFlying;
    private int hashIsSwimming;
    private int hashIsGrounded;
    private int hashIsClimbing;
    private int hashIsJumping;
    private int hashMoveSpeed;
    private int hashMoveX;
    private int hashMoveY;
    private int hashJumpTrigger;

    // State tracking for jump trigger
    private bool lastIsJumping;

    public bool IsInitialized { get; private set; }

    public override void Spawned()
    {
        InitializeComponents();
    }

    /// <summary>
    /// Initialize all required components
    /// </summary>
    void InitializeComponents()
    {
        // Get required components
        networkAnimator = GetComponent<NetworkMecanimAnimator>();
        playerController = GetComponent<U3DPlayerController>();

        // CRITICAL: Get the TEMPORARY Animator component from the prefab
        targetAnimator = GetComponent<Animator>();

        if (networkAnimator == null || targetAnimator == null || playerController == null)
        {
            Debug.LogError("❌ Missing required components for U3DNetworkedAnimator");
            return;
        }

        // Apply the animation controller to the temporary animator
        if (animatorController != null)
        {
            targetAnimator.runtimeAnimatorController = animatorController;
            Debug.Log($"✅ Animation controller applied: {animatorController.name}");
        }
        else
        {
            Debug.LogError("❌ No Animator Controller assigned! Please assign your U3DAnimatorController.");
            return;
        }

        // Connect NetworkMecanimAnimator to our temporary Animator
        networkAnimator.Animator = targetAnimator;

        // Cache parameter IDs for performance
        CacheParameterIDs();

        IsInitialized = true;
        Debug.Log("✅ U3DNetworkedAnimator initialized successfully");

        // CRITICAL: Apply pending avatar animator if one was set before initialization
        if (pendingAvatarAnimator != null)
        {
            Debug.Log("⚡ Applying pending avatar animator after initialization");
            SetAvatarAnimator(pendingAvatarAnimator);
            pendingAvatarAnimator = null;
        }
    }

    /// <summary>
    /// Cache parameter IDs for Unity 6+ performance
    /// </summary>
    void CacheParameterIDs()
    {
        hashIsMoving = Animator.StringToHash("IsMoving");
        hashIsCrouching = Animator.StringToHash("IsCrouching");
        hashIsFlying = Animator.StringToHash("IsFlying");
        hashIsSwimming = Animator.StringToHash("IsSwimming");
        hashIsGrounded = Animator.StringToHash("IsGrounded");
        hashIsClimbing = Animator.StringToHash("IsClimbing");
        hashIsJumping = Animator.StringToHash("IsJumping");
        hashMoveSpeed = Animator.StringToHash("MoveSpeed");
        hashMoveX = Animator.StringToHash("MoveX");
        hashMoveY = Animator.StringToHash("MoveY");
        hashJumpTrigger = Animator.StringToHash("JumpTrigger");

        Debug.Log("✅ Animation parameter IDs cached");
    }

    /// <summary>
    /// FUSION 2 CRITICAL: Only State Authority updates animation parameters
    /// </summary>
    public override void FixedUpdateNetwork()
    {
        // FUSION 2 REQUIREMENT: Only State Authority can modify animator parameters
        if (!Object.HasStateAuthority) 
        {
            if (debugAnimationStates) Debug.Log("⚠️ No State Authority - skipping animation updates");
            return;
        }
        
        if (!IsInitialized) 
        {
            if (debugAnimationStates) Debug.Log("⚠️ Not initialized - skipping animation updates");
            return;
        }

        if (targetAnimator == null)
        {
            if (debugAnimationStates) Debug.Log("⚠️ No target animator - skipping animation updates");
            return;
        }

        if (debugAnimationStates) Debug.Log("🎯 FixedUpdateNetwork running - updating parameters");
        UpdateAnimationParameters();
    }

    /// <summary>
    /// Update animation parameters based on PlayerController state
    /// CRITICAL: Only called by State Authority
    /// </summary>
    void UpdateAnimationParameters()
    {
        // Read PlayerController states (NEVER modify them)
        bool isMoving = playerController.NetworkIsMoving;
        bool isCrouching = playerController.NetworkIsCrouching;
        bool isFlying = playerController.NetworkIsFlying;
        bool isGrounded = playerController.IsGrounded;
        bool isJumping = playerController.NetworkIsJumping;
        bool isSwimming = playerController.NetworkIsSwimming;
        bool isClimbing = playerController.NetworkIsClimbing;

        // Calculate movement values using PlayerController's actual speed logic
        Vector3 velocity = playerController.Velocity;
        
        // CORRECT: Use PlayerController's intended speed, not CharacterController velocity
        float moveSpeed = 0f;
        if (isMoving)
        {
            // Get the actual speed the PlayerController is using
            moveSpeed = playerController.CurrentSpeed;
        }

        Vector3 localVelocity = playerController.transform.InverseTransformDirection(velocity);
        Vector2 moveDirection = new Vector2(localVelocity.x, localVelocity.z);
        if (moveDirection.magnitude > 0.1f) moveDirection.Normalize();

        // FUSION 2 CRITICAL: Set parameters on NetworkMecanimAnimator's actual Animator
        // This ensures parameters and network sync target the same animator
        Animator activeAnimator = networkAnimator.Animator;
        
        activeAnimator.SetBool(hashIsMoving, isMoving);
        activeAnimator.SetBool(hashIsCrouching, isCrouching);
        activeAnimator.SetBool(hashIsFlying, isFlying);
        activeAnimator.SetBool(hashIsSwimming, isSwimming);
        activeAnimator.SetBool(hashIsGrounded, isGrounded);
        activeAnimator.SetBool(hashIsClimbing, isClimbing);
        activeAnimator.SetBool(hashIsJumping, isJumping);

        activeAnimator.SetFloat(hashMoveSpeed, moveSpeed);
        activeAnimator.SetFloat(hashMoveX, moveDirection.x);
        activeAnimator.SetFloat(hashMoveY, moveDirection.y);

        // Handle jump trigger using NetworkMecanimAnimator (FUSION 2 WAY)
        if (isJumping && !lastIsJumping)
        {
            // CRITICAL: Use NetworkMecanimAnimator.SetTrigger for proper network sync
            networkAnimator.SetTrigger("JumpTrigger");
        }

        // Debug output
        if (debugAnimationStates)
        {
            Debug.Log($"🎬 AUTHORITY Animation - Moving:{isMoving} Speed:{moveSpeed:F2} Crouch:{isCrouching} Jump:{isJumping} Flying:{isFlying} Ground:{isGrounded}");
            Debug.Log($"📊 Active Animator: {(networkAnimator.Animator?.name ?? "NULL")} - Controller: {(networkAnimator.Animator?.runtimeAnimatorController?.name ?? "NULL")}");
        }

        // Store for next frame
        lastIsJumping = isJumping;
    }

    /// <summary>
    /// Called by U3DAvatarManager when avatar changes
    /// CRITICAL: Must handle both temporary animator and avatar animator
    /// </summary>
    public void SetAvatarAnimator(Animator avatarAnimator)
    {
        if (avatarAnimator == null) return;

        if (!IsInitialized)
        {
            Debug.LogWarning("⏳ Avatar animator set before initialization - storing for later");
            pendingAvatarAnimator = avatarAnimator;
            return;
        }

        // Apply controller to avatar animator
        if (animatorController != null)
        {
            avatarAnimator.runtimeAnimatorController = animatorController;
            Debug.Log($"✅ Controller applied to avatar animator: {animatorController.name}");
        }

        // IMPORTANT: Remove the temporary animator FIRST
        Animator tempAnimator = GetComponent<Animator>();
        if (tempAnimator != null)
        {
            Debug.Log("🗑️ Removing temporary animator before connecting avatar");
            DestroyImmediate(tempAnimator);
        }

        // CRITICAL: NOW connect NetworkMecanimAnimator to avatar animator
        networkAnimator.Animator = avatarAnimator;
        
        // VERIFY the connection worked
        if (networkAnimator.Animator == avatarAnimator)
        {
            Debug.Log($"✅ NetworkMecanimAnimator successfully connected to: {avatarAnimator.name}");
        }
        else
        {
            Debug.LogError($"❌ NetworkMecanimAnimator connection failed! Expected: {avatarAnimator.name}, Got: {(networkAnimator.Animator?.name ?? "NULL")}");
        }
        
        // Update our reference
        targetAnimator = avatarAnimator;

        Debug.Log("✅ Avatar animator connected to NetworkMecanimAnimator");
    }

    /// <summary>
    /// Validate setup in editor
    /// </summary>
    void OnValidate()
    {
        if (animatorController == null)
        {
            Debug.LogWarning("⚠️ No Animator Controller assigned! Please assign your U3DAnimatorController.");
        }
    }
}