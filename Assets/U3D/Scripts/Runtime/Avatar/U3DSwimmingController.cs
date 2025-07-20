// ===== SWIMMING COMPONENT =====
using UnityEngine;
using Fusion;

/// <summary>
/// Swimming detection and state management for U3D Player Controller
/// Integrates with the 8-core animation system
/// </summary>
public class U3DSwimmingController : NetworkBehaviour
{
    [Header("Swimming Detection")]
    [SerializeField] private LayerMask waterLayerMask = 1 << 4; // Water layer
    [SerializeField] private float waterCheckRadius = 0.5f;
    [SerializeField] private Vector3 waterCheckOffset = Vector3.zero;
    [SerializeField] private float swimDepthThreshold = 1.5f; // How deep to start swimming

    [Header("Swimming Physics")]
    [SerializeField] private float swimSpeed = 3f;
    [SerializeField] private float swimUpSpeed = 2f;
    [SerializeField] private float waterDrag = 5f;
    [SerializeField] private float buoyancyForce = 10f;

    // Networked state
    [Networked] public bool NetworkIsSwimming { get; set; }
    [Networked] public bool NetworkIsInWater { get; set; }

    // Components
    private U3DPlayerController playerController;
    private CharacterController characterController;
    private Collider waterCollider;

    // State
    private bool isInWater = false;
    private bool isSwimming = false;
    private float waterSurfaceY = 0f;
    private Vector3 lastSwimVelocity = Vector3.zero;

    public bool IsSwimming => isSwimming;
    public bool IsInWater => isInWater;

    void Awake()
    {
        playerController = GetComponent<U3DPlayerController>();
        characterController = GetComponent<CharacterController>();
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return;

        CheckWaterState();
        HandleSwimmingPhysics();
        UpdateNetworkState();
    }

    void CheckWaterState()
    {
        Vector3 checkPosition = transform.position + waterCheckOffset;

        // Check for water collision
        Collider[] waterColliders = Physics.OverlapSphere(checkPosition, waterCheckRadius, waterLayerMask);

        bool wasInWater = isInWater;
        isInWater = waterColliders.Length > 0;

        if (isInWater && waterColliders.Length > 0)
        {
            waterCollider = waterColliders[0];
            waterSurfaceY = waterCollider.bounds.max.y;

            // Start swimming if player is deep enough in water
            float depthInWater = waterSurfaceY - (transform.position.y + characterController.height * 0.5f);
            isSwimming = depthInWater > swimDepthThreshold;
        }
        else
        {
            isSwimming = false;
            waterCollider = null;
        }

        // Log state changes
        if (wasInWater != isInWater)
        {
            Debug.Log($"🏊 Water state changed: InWater={isInWater}, Swimming={isSwimming}");
        }
    }

    void HandleSwimmingPhysics()
    {
        if (!isSwimming || playerController == null) return;

        // Swimming movement handled in player controller
        // This component just provides state information
    }

    void UpdateNetworkState()
    {
        NetworkIsSwimming = isSwimming;
        NetworkIsInWater = isInWater;
    }

    // Public methods for PlayerController integration
    public float GetSwimSpeed() => swimSpeed;
    public float GetSwimUpSpeed() => swimUpSpeed;
    public float GetWaterSurfaceY() => waterSurfaceY;

    void OnDrawGizmosSelected()
    {
        // Draw water check sphere
        Gizmos.color = isInWater ? Color.blue : Color.cyan;
        Gizmos.DrawWireSphere(transform.position + waterCheckOffset, waterCheckRadius);

        if (isInWater && waterCollider != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(transform.position, new Vector3(transform.position.x, waterSurfaceY, transform.position.z));
        }
    }
}