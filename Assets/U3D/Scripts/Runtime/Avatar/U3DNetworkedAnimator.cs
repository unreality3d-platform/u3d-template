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
    [SerializeField] private RuntimeAnimatorController animatorController;

    [Header("🔧 Settings")]
    [SerializeField] private bool debugAnimationStates = false;

    private NetworkMecanimAnimator networkAnimator;
    private Animator targetAnimator;
    private U3DPlayerController playerController;

    private Animator pendingAvatarAnimator;

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

    private bool lastIsJumping;

    public bool IsInitialized { get; private set; }

    public override void Spawned()
    {
        InitializeComponents();
    }

    void InitializeComponents()
    {
        networkAnimator = GetComponent<NetworkMecanimAnimator>();
        playerController = GetComponent<U3DPlayerController>();
        targetAnimator = GetComponent<Animator>();

        if (networkAnimator == null || targetAnimator == null || playerController == null)
        {
            Debug.LogError("❌ Missing required components for U3DNetworkedAnimator");
            return;
        }

        if (animatorController != null)
        {
            targetAnimator.runtimeAnimatorController = animatorController;
        }
        else
        {
            Debug.LogError("❌ No Animator Controller assigned! Please assign your U3DAnimatorController.");
            return;
        }

        networkAnimator.Animator = targetAnimator;

        CacheParameterIDs();

        IsInitialized = true;

        if (pendingAvatarAnimator != null)
        {
            SetAvatarAnimator(pendingAvatarAnimator);
            pendingAvatarAnimator = null;
        }
    }

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

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return;
        if (!IsInitialized) return;
        if (targetAnimator == null) return;

        UpdateAnimationParameters();
    }

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
    /// Fires a one-shot Trigger parameter through NetworkMecanimAnimator so it syncs
    /// to all clients. Only valid on the local player (State Authority). Use this for
    /// action animations like kick and throw that originate from interaction components.
    /// The trigger name must exist as a Trigger parameter in U3DAnimatorController.
    /// </summary>
    public void TriggerAnimation(string triggerName)
    {
        if (!IsInitialized) return;
        if (!Object.HasStateAuthority) return;
        networkAnimator.SetTrigger(triggerName);
    }

    /// <summary>
    /// Sets a Bool parameter on the Animator and syncs it to all clients via
    /// NetworkMecanimAnimator. Only valid on the local player (State Authority).
    /// Use this for sustained states like pushing and pulling that remain active
    /// across multiple frames. The parameter name must exist as a Bool parameter
    /// in U3DAnimatorController.
    /// </summary>
    public void SetAnimationBool(string paramName, bool value)
    {
        if (!IsInitialized) return;
        if (!Object.HasStateAuthority) return;
        networkAnimator.Animator.SetBool(paramName, value);
    }

    public void SetAvatarAnimator(Animator avatarAnimator)
    {
        if (avatarAnimator == null) return;

        if (!IsInitialized)
        {
            pendingAvatarAnimator = avatarAnimator;
            return;
        }

        if (animatorController != null)
            avatarAnimator.runtimeAnimatorController = animatorController;

        Animator tempAnimator = GetComponent<Animator>();
        if (tempAnimator != null)
            DestroyImmediate(tempAnimator);

        networkAnimator.Animator = avatarAnimator;

        if (networkAnimator.Animator != avatarAnimator)
            Debug.LogError($"❌ NetworkMecanimAnimator connection failed! Expected: {avatarAnimator.name}, Got: {(networkAnimator.Animator?.name ?? "NULL")}");

        targetAnimator = avatarAnimator;
    }

    void OnValidate()
    {
        if (animatorController == null)
            Debug.LogWarning("⚠️ No Animator Controller assigned! Please assign your U3DAnimatorController.");
    }
}