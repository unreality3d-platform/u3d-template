// ===== CLIMBING COMPONENT =====
using UnityEngine;
using Fusion;


/// <summary>
/// Climbing detection and state management for U3D Player Controller
/// Integrates with the 8-core animation system
/// </summary>
public class U3DClimbingController : NetworkBehaviour
{
    [Header("Climbing Detection")]
    [SerializeField] private LayerMask climbableLayerMask = 1 << 6; // Climbable layer
    [SerializeField] private float climbCheckDistance = 1f;
    [SerializeField] private float climbCheckRadius = 0.3f;
    [SerializeField] private Vector3 climbCheckOffset = new Vector3(0, 0.5f, 0);

    [Header("Climbing Physics")]
    [SerializeField] private float climbSpeed = 2f;
    [SerializeField] private float climbUpSpeed = 3f;
    [SerializeField] private float climbDownSpeed = 4f;
    [SerializeField] private float climbStamina = 100f;
    [SerializeField] private float staminaDrainRate = 10f;

    [Header("Climbing Controls")]
    [SerializeField] private KeyCode climbKey = KeyCode.LeftControl;
    [SerializeField] private bool autoClimb = true; // Auto-climb when touching climbable surface

    // Networked state
    [Networked] public bool NetworkIsClimbing { get; set; }
    [Networked] public bool NetworkCanClimb { get; set; }
    [Networked] public Vector3 NetworkClimbDirection { get; set; }

    // Components
    private U3DPlayerController playerController;
    private CharacterController characterController;

    // State
    private bool isClimbing = false;
    private bool canClimb = false;
    private Vector3 climbSurfaceNormal = Vector3.zero;
    private Vector3 climbDirection = Vector3.zero;
    private float currentStamina = 100f;

    public bool IsClimbing => isClimbing;
    public bool CanClimb => canClimb;
    public float StaminaPercentage => currentStamina / climbStamina;

    void Awake()
    {
        playerController = GetComponent<U3DPlayerController>();
        characterController = GetComponent<CharacterController>();
        currentStamina = climbStamina;
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return;

        CheckClimbableState();
        HandleClimbingInput();
        HandleClimbingPhysics();
        UpdateStamina();
        UpdateNetworkState();
    }

    void CheckClimbableState()
    {
        Vector3 checkPosition = transform.position + climbCheckOffset;
        Vector3 forwardDirection = transform.forward;

        // Raycast forward to detect climbable surfaces
        RaycastHit hit;
        bool hitClimbable = Physics.SphereCast(
            checkPosition,
            climbCheckRadius,
            forwardDirection,
            out hit,
            climbCheckDistance,
            climbableLayerMask
        );

        bool wasCanClimb = canClimb;
        canClimb = hitClimbable && currentStamina > 0;

        if (canClimb)
        {
            climbSurfaceNormal = hit.normal;
        }

        // Auto-stop climbing if no longer touching climbable surface
        if (isClimbing && !canClimb)
        {
            StopClimbing();
        }

        // Log state changes
        if (wasCanClimb != canClimb)
        {
            Debug.Log($"🧗 Climb state changed: CanClimb={canClimb}");
        }
    }

    void HandleClimbingInput()
    {
        // Manual climbing toggle (if not auto-climb)
        if (!autoClimb && Input.GetKeyDown(climbKey) && canClimb && !isClimbing)
        {
            StartClimbing();
        }
        else if (!autoClimb && Input.GetKeyUp(climbKey) && isClimbing)
        {
            StopClimbing();
        }
        // Auto-climb when touching climbable surface
        else if (autoClimb && canClimb && !isClimbing && playerController.NetworkIsMoving)
        {
            StartClimbing();
        }
        else if (autoClimb && isClimbing && (!canClimb || !playerController.NetworkIsMoving))
        {
            StopClimbing();
        }
    }

    void HandleClimbingPhysics()
    {
        if (!isClimbing || playerController == null) return;

        // Calculate climb direction based on input
        Vector2 moveInput = Vector2.zero; // Get from input system

        if (moveInput.magnitude > 0.1f)
        {
            // Calculate climb direction along the surface
            Vector3 right = Vector3.Cross(Vector3.up, climbSurfaceNormal).normalized;
            Vector3 up = Vector3.Cross(climbSurfaceNormal, right).normalized;

            climbDirection = (right * moveInput.x + up * moveInput.y).normalized;
        }
        else
        {
            climbDirection = Vector3.zero;
        }
    }

    void UpdateStamina()
    {
        if (isClimbing)
        {
            // Drain stamina while climbing
            currentStamina -= staminaDrainRate * Runner.DeltaTime;
            currentStamina = Mathf.Max(0, currentStamina);

            // Stop climbing if out of stamina
            if (currentStamina <= 0)
            {
                StopClimbing();
            }
        }
        else
        {
            // Regenerate stamina when not climbing
            currentStamina += (staminaDrainRate * 0.5f) * Runner.DeltaTime;
            currentStamina = Mathf.Min(climbStamina, currentStamina);
        }
    }

    void UpdateNetworkState()
    {
        NetworkIsClimbing = isClimbing;
        NetworkCanClimb = canClimb;
        NetworkClimbDirection = climbDirection;
    }

    public void StartClimbing()
    {
        if (!canClimb || isClimbing) return;

        isClimbing = true;
        Debug.Log("🧗 Started climbing");
    }

    public void StopClimbing()
    {
        if (!isClimbing) return;

        isClimbing = false;
        climbDirection = Vector3.zero;
        Debug.Log("🧗 Stopped climbing");
    }

    // Public methods for PlayerController integration
    public float GetClimbSpeed() => climbSpeed;
    public float GetClimbUpSpeed() => climbUpSpeed;
    public float GetClimbDownSpeed() => climbDownSpeed;
    public Vector3 GetClimbDirection() => climbDirection;
    public Vector3 GetClimbSurfaceNormal() => climbSurfaceNormal;

    void OnDrawGizmosSelected()
    {
        // Draw climb check sphere
        Gizmos.color = canClimb ? Color.green : Color.yellow;
        Vector3 checkPos = transform.position + climbCheckOffset;
        Gizmos.DrawWireSphere(checkPos, climbCheckRadius);

        // Draw forward check ray
        Gizmos.DrawRay(checkPos, transform.forward * climbCheckDistance);

        if (isClimbing && climbDirection != Vector3.zero)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawRay(transform.position, climbDirection * 2f);
        }
    }
}