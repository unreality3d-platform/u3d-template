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
    private int hashIsSeated;

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

        // CRITICAL: Apply pending avatar animator if one was set before initialization
        if (pendingAvatarAnimator != null)
        {
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
        hashIsSeated = Animator.StringToHash("IsSeated");
    }

    /// <summary>
    /// FUSION 2 CRITICAL: Only State Authority updates animation parameters
    /// </summary>
    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return;
        if (!IsInitialized) return;
        if (targetAnimator == null) return;

        UpdateAnimationParameters();
    }

    /// <summary>
    /// Update animation parameters based on PlayerController state
    /// CRITICAL: Only called by State Authority
    /// </summary>
    void UpdateAnimationParameters()
    {
        bool isMoving = playerController.NetworkIsMoving;
        bool isCrouching = playerController.NetworkIsCrouching;
        bool isFlying = playerController.NetworkIsFlying;
        bool isGrounded = playerController.IsGrounded;
        bool isJumping = playerController.NetworkIsJumping;
        bool isSwimming = playerController.NetworkIsSwimming;
        bool isClimbing = playerController.NetworkIsClimbing;
        bool isSeated = playerController.NetworkIsSeated;

        Vector3 velocity = playerController.Velocity;

        float moveSpeed = 0f;
        if (isMoving)
            moveSpeed = playerController.CurrentSpeed;

        Vector3 localVelocity = playerController.transform.InverseTransformDirection(velocity);
        Vector2 moveDirection = new Vector2(localVelocity.x, localVelocity.z);
        if (moveDirection.magnitude > 0.1f) moveDirection.Normalize();

        // Pose-lock override: while the player is held in a standing idle (a standing
        // steerable, or later a stand-configured seat), show the locomotion blend at rest
        // instead of walk/run, even though the body is actually moving. Seated and the
        // other pose states are unaffected — they win through their own parameters.
        if (playerController.NetworkSuppressLocomotion)
        {
            isMoving = false;
            moveSpeed = 0f;
            moveDirection = Vector2.zero;
        }

        Animator activeAnimator = networkAnimator.Animator;

        activeAnimator.SetBool(hashIsMoving, isMoving);
        activeAnimator.SetBool(hashIsCrouching, isCrouching);
        activeAnimator.SetBool(hashIsFlying, isFlying);
        activeAnimator.SetBool(hashIsSwimming, isSwimming);
        activeAnimator.SetBool(hashIsGrounded, isGrounded);
        activeAnimator.SetBool(hashIsClimbing, isClimbing);
        activeAnimator.SetBool(hashIsJumping, isJumping);
        activeAnimator.SetBool(hashIsSeated, isSeated);

        activeAnimator.SetFloat(hashMoveSpeed, moveSpeed);
        activeAnimator.SetFloat(hashMoveX, moveDirection.x);
        activeAnimator.SetFloat(hashMoveY, moveDirection.y);

        if (isJumping && !lastIsJumping)
            networkAnimator.SetTrigger("JumpTrigger");

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
            pendingAvatarAnimator = avatarAnimator;
            return;
        }

        // Apply controller to avatar animator
        if (animatorController != null)
        {
            avatarAnimator.runtimeAnimatorController = animatorController;
        }

        // IMPORTANT: Remove the temporary animator FIRST
        Animator tempAnimator = GetComponent<Animator>();
        if (tempAnimator != null)
        {
            DestroyImmediate(tempAnimator);
        }

        // CRITICAL: NOW connect NetworkMecanimAnimator to avatar animator
        networkAnimator.Animator = avatarAnimator;
        
        // VERIFY the connection worked
        if (networkAnimator.Animator == avatarAnimator)
        {
        }
        else
        {
            Debug.LogError($"❌ NetworkMecanimAnimator connection failed! Expected: {avatarAnimator.name}, Got: {(networkAnimator.Animator?.name ?? "NULL")}");
        }
        
        // Update our reference
        targetAnimator = avatarAnimator;
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