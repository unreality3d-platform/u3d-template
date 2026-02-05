using UnityEngine;
using Fusion;

/// <summary>
/// Climbing detection and movement for U3D Player Controller.
/// Integrates with the 8-core animation system (IsClimbing state).
///
/// When the player faces a surface on the Climbable layer and moves forward,
/// climbing begins automatically. Movement remaps to surface traversal:
///   W = climb up, S = climb down, A/D = lateral movement along surface.
/// Jump (Space) detaches from the surface.
///
/// This component lives on the player prefab alongside U3DPlayerController.
/// Climbable surfaces use the U3DClimbable component (added via Creator Dashboard).
/// </summary>
public class U3DClimbingController : NetworkBehaviour
{
    [Header("Climbing Detection")]
    [Tooltip("Layer mask for climbable surfaces (must match U3DClimbable.CLIMBABLE_LAYER)")]
    [SerializeField] private LayerMask climbableLayerMask = 1 << 6;

    [Tooltip("How far forward to check for climbable surfaces")]
    [SerializeField] private float climbCheckDistance = 0.8f;

    [Tooltip("Radius of the detection sphere cast")]
    [SerializeField] private float climbCheckRadius = 0.4f;

    [Tooltip("Height offset for the detection origin (chest height works best)")]
    [SerializeField] private Vector3 climbCheckOffset = new Vector3(0, 0.8f, 0);

    [Header("Climbing Movement")]
    [Tooltip("Vertical climb speed (up)")]
    [SerializeField] private float climbUpSpeed = 3f;

    [Tooltip("Vertical climb speed (down)")]
    [SerializeField] private float climbDownSpeed = 3f;

    [Tooltip("Lateral movement speed along the surface")]
    [SerializeField] private float climbLateralSpeed = 2.5f;

    [Tooltip("How close the player stays to the climbable surface")]
    [SerializeField] private float surfaceStickDistance = 0.05f;

    [Tooltip("Brief cooldown after detaching before re-attach is allowed")]
    [SerializeField] private float reattachCooldown = 0.3f;

    // Networked state
    [Networked] public bool NetworkIsClimbing { get; set; }

    // Components (cached)
    private U3DPlayerController playerController;
    private CharacterController characterController;

    // Climbing state
    private bool isClimbing;
    private bool canClimb;
    private Vector3 climbSurfaceNormal;
    private Vector3 lastSurfacePoint;
    private float detachTime;
    private float surfaceSpeedMultiplier = 1f;

    // Cached input (set each tick from PlayerController's network input)
    private Vector2 currentMoveInput;
    private bool jumpPressed;

    public bool IsClimbing => isClimbing;
    public bool CanClimb => canClimb;

    void Awake()
    {
        playerController = GetComponent<U3DPlayerController>();
        characterController = GetComponent<CharacterController>();
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return;

        // Read input from the same Fusion input pipeline the PlayerController uses
        if (GetInput<U3DNetworkInputData>(out var input))
        {
            currentMoveInput = input.MovementInput;
            jumpPressed = input.Buttons.GetPressed(default).IsSet(U3DInputButtons.Jump);

            // We need proper press detection using previous buttons
            // Since PlayerController handles button tracking, check the raw state
            // and use a simpler approach: jump is pressed this tick if the button is set
            jumpPressed = input.Buttons.IsSet(U3DInputButtons.Jump);
        }

        DetectClimbableSurface();

        if (isClimbing)
        {
            HandleClimbingMovement();
            CheckDetach();
        }
        else
        {
            CheckAttach();
        }

        UpdateNetworkState();
    }

    /// <summary>
    /// SphereCast forward from chest height to find climbable surfaces.
    /// </summary>
    void DetectClimbableSurface()
    {
        Vector3 origin = transform.position + climbCheckOffset;
        Vector3 direction = transform.forward;

        bool hitSurface = Physics.SphereCast(
            origin,
            climbCheckRadius,
            direction,
            out RaycastHit hit,
            climbCheckDistance,
            climbableLayerMask
        );

        bool wasCan = canClimb;
        canClimb = hitSurface;

        if (canClimb)
        {
            climbSurfaceNormal = hit.normal;
            lastSurfacePoint = hit.point;

            // Check for per-surface speed multiplier
            var climbable = hit.collider.GetComponent<U3D.U3DClimbable>();
            surfaceSpeedMultiplier = climbable != null ? climbable.SpeedMultiplier : 1f;
        }

        // If climbing and surface lost, detach
        if (isClimbing && !canClimb)
        {
            Detach();
        }
    }

    /// <summary>
    /// Start climbing when player walks into a climbable surface.
    /// Auto-attach: moving forward into the surface triggers climbing.
    /// </summary>
    void CheckAttach()
    {
        if (!canClimb) return;
        if (Time.time - detachTime < reattachCooldown) return;

        // Attach when moving forward into the surface
        if (currentMoveInput.y > 0.1f)
        {
            Attach();
        }
    }

    /// <summary>
    /// Detach conditions: jump, or move backward when near ground.
    /// </summary>
    void CheckDetach()
    {
        // Jump always detaches
        if (jumpPressed)
        {
            Detach();
            // Give a small upward boost so the player doesn't immediately re-attach
            playerController.SetClimbDetachVelocity(new Vector3(0, 2f, 0));
            return;
        }

        // Moving backward (S key) while near the ground detaches
        if (currentMoveInput.y < -0.1f && characterController.isGrounded)
        {
            Detach();
        }
    }

    /// <summary>
    /// Core climbing movement. Remaps WASD to surface-relative directions:
    ///   W (forward input) = climb up along the surface
    ///   S (backward input) = climb down along the surface
    ///   A/D (lateral input) = move left/right along the surface
    /// Gravity is suppressed by the PlayerController when NetworkIsClimbing is true.
    /// </summary>
    void HandleClimbingMovement()
    {
        if (characterController == null) return;

        // Build a coordinate frame on the climbable surface
        // "up" along the surface = perpendicular to the surface normal, biased toward world up
        Vector3 surfaceUp = Vector3.Cross(climbSurfaceNormal, Vector3.Cross(Vector3.up, climbSurfaceNormal)).normalized;

        // If surface is perfectly horizontal (a ceiling), fall back
        if (surfaceUp.sqrMagnitude < 0.01f)
        {
            surfaceUp = Vector3.up;
        }

        // "right" along the surface = perpendicular to both surface normal and surface up
        Vector3 surfaceRight = Vector3.Cross(surfaceUp, climbSurfaceNormal).normalized;

        // Calculate climb velocity from input
        float verticalSpeed = 0f;
        if (currentMoveInput.y > 0.1f)
            verticalSpeed = climbUpSpeed;
        else if (currentMoveInput.y < -0.1f)
            verticalSpeed = -climbDownSpeed;

        float lateralSpeed = currentMoveInput.x * climbLateralSpeed;

        Vector3 climbVelocity = (surfaceUp * verticalSpeed + surfaceRight * lateralSpeed) * surfaceSpeedMultiplier;

        // Stick to surface: nudge toward the surface so the player doesn't drift away
        Vector3 toSurface = -climbSurfaceNormal * surfaceStickDistance;

        Vector3 totalMovement = (climbVelocity + toSurface) * Runner.DeltaTime;

        characterController.Move(totalMovement);

        // Update network position so remote players see the climb
        playerController.NetworkPosition = transform.position;
        playerController.NetworkIsMoving = climbVelocity.sqrMagnitude > 0.01f;
    }

    void Attach()
    {
        if (isClimbing) return;

        isClimbing = true;
        playerController.SetClimbingState(true);
    }

    void Detach()
    {
        if (!isClimbing) return;

        isClimbing = false;
        detachTime = Time.time;
        playerController.SetClimbingState(false);
    }

    void UpdateNetworkState()
    {
        NetworkIsClimbing = isClimbing;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = canClimb ? Color.green : Color.yellow;
        Vector3 origin = transform.position + climbCheckOffset;
        Gizmos.DrawWireSphere(origin, climbCheckRadius);
        Gizmos.DrawRay(origin, transform.forward * climbCheckDistance);

        if (isClimbing)
        {
            // Show surface normal
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(lastSurfacePoint, climbSurfaceNormal);
        }
    }
}
