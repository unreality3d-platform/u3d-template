using UnityEngine;

namespace U3D
{
    /// <summary>
    /// Handles interaction detection and dispatch for the U3D interaction system.
    /// Selection uses a SphereCast in the direction the player's body is facing —
    /// whatever the cast hits first, if it's an IU3DInteractable that can be interacted
    /// with, receives OnInteract(). No priority scoring. Closest hit wins.
    ///
    /// If the player is currently holding a grabbable object, R is routed directly
    /// to that object so it can handle its own release. The SphereCast is skipped
    /// in that case.
    /// </summary>
    public class U3DInteractionManager : MonoBehaviour
    {
        [Header("Interaction SphereCast")]
        [Tooltip("Radius of the SphereCast used to find interactables in front of the player. Larger = more forgiving aim.")]
        [SerializeField] private float sphereCastRadius = 0.5f;

        [Tooltip("Maximum distance the SphereCast will check for interactables.")]
        [SerializeField] private float sphereCastMaxDistance = 15f;

        [Tooltip("Layers checked by the interaction SphereCast.")]
        [SerializeField] private LayerMask interactionLayerMask = -1;

        [Tooltip("Show the SphereCast in the Scene view when this object is selected.")]
        [SerializeField] private bool debugMode = false;

        private static U3DInteractionManager instance;
        private U3DPlayerController localPlayerController;

        public static U3DInteractionManager Instance
        {
            get
            {
                if (instance == null)
                    instance = FindAnyObjectByType<U3DInteractionManager>();
                return instance;
            }
        }

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
            }
            else if (instance != this)
            {
                Debug.LogWarning("Multiple InteractionManagers found. Destroying duplicate on: " + gameObject.name);
                Destroy(this);
                return;
            }
        }

        private void Start()
        {
            FindLocalPlayer();
        }

        private void Update()
        {
            if (localPlayerController == null)
            {
                FindLocalPlayer();
            }
        }

        private void FindLocalPlayer()
        {
            localPlayerController = U3DPlayerController.FindLocalPlayer();
        }

        /// <summary>
        /// Called by PlayerController when interact button is pressed.
        /// If the player is holding a grabbable, route the press to that object (drop/release).
        /// Otherwise, SphereCast forward to find the closest interactable the player is aiming at.
        /// </summary>
        public void OnPlayerInteract()
        {
            if (localPlayerController == null) return;

            // Hands full? The press goes to the held object so it can handle its own release.
            if (U3DGrabbable.CurrentlyGrabbed != null)
            {
                U3DGrabbable.CurrentlyGrabbed.OnInteract();
                return;
            }

            IU3DInteractable target = GetBestInteractable();
            if (target != null)
            {
                target.OnInteract();
            }
        }

        /// <summary>
        /// SphereCast in the direction the player's body is facing.
        /// Fires two casts: chest height first (covers grab/throw/interact targets at
        /// hand level on tables, shelves, etc.), then floor height (covers kickables
        /// and low-to-the-ground objects). First valid hit wins.
        /// </summary>
        private IU3DInteractable GetBestInteractable()
        {
            if (localPlayerController == null) return null;

            Transform playerTransform = localPlayerController.transform;
            Vector3 direction = playerTransform.forward;

            // Primary: chest-height cast for grab/throw/interact targets
            Vector3 chestOrigin = playerTransform.position + Vector3.up * 1.0f;
            IU3DInteractable chestHit = CastForInteractable(chestOrigin, direction);
            if (chestHit != null) return chestHit;

            // Fallback: floor-height cast for kickables and low objects
            Vector3 floorOrigin = playerTransform.position;
            return CastForInteractable(floorOrigin, direction);
        }

        /// <summary>
        /// Single SphereCast that returns an interactable if one is hit and can be interacted with.
        /// </summary>
        private IU3DInteractable CastForInteractable(Vector3 origin, Vector3 direction)
        {
            if (Physics.SphereCast(origin, sphereCastRadius, direction, out RaycastHit hit,
                sphereCastMaxDistance, interactionLayerMask, QueryTriggerInteraction.Collide))
            {
                // Walk up the hierarchy — covers cases where the collider is on a child
                // of the IU3DInteractable component.
                IU3DInteractable interactable = hit.collider.GetComponentInParent<IU3DInteractable>();
                if (interactable != null && interactable.CanInteract())
                {
                    return interactable;
                }
            }

            return null;
        }

        private void OnDrawGizmosSelected()
        {
            if (!debugMode) return;
            if (localPlayerController == null) return;

            Transform playerTransform = localPlayerController.transform;
            Vector3 direction = playerTransform.forward;

            // Primary cast (chest height) — cyan
            Vector3 chestOrigin = playerTransform.position + Vector3.up * 1.0f;
            Vector3 chestEnd = chestOrigin + direction * sphereCastMaxDistance;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(chestOrigin, sphereCastRadius);
            Gizmos.DrawLine(chestOrigin, chestEnd);
            Gizmos.DrawWireSphere(chestEnd, sphereCastRadius);

            // Fallback cast (floor height) — yellow
            Vector3 floorOrigin = playerTransform.position;
            Vector3 floorEnd = floorOrigin + direction * sphereCastMaxDistance;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(floorOrigin, sphereCastRadius);
            Gizmos.DrawLine(floorOrigin, floorEnd);
            Gizmos.DrawWireSphere(floorEnd, sphereCastRadius);
        }
    }

    /// <summary>
    /// Interface that all interactable objects must implement.
    /// </summary>
    public interface IU3DInteractable
    {
        /// <summary>
        /// Called when player interacts with this object.
        /// </summary>
        void OnInteract();

        /// <summary>
        /// Called when player enters interaction range.
        /// </summary>
        void OnPlayerEnterRange();

        /// <summary>
        /// Called when player exits interaction range.
        /// </summary>
        void OnPlayerExitRange();

        /// <summary>
        /// Check if this object can currently be interacted with.
        /// </summary>
        bool CanInteract();

        /// <summary>
        /// Get text to show in interaction prompt.
        /// </summary>
        string GetInteractionPrompt();
    }
}