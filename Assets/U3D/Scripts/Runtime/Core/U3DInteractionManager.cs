using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace U3D
{
    /// <summary>
    /// SIMPLIFIED: Direct integration with U3DPlayerController
    /// Handles interaction detection and processing for the grab/throw system
    /// </summary>
    public class U3DInteractionManager : MonoBehaviour
    {
        [Header("Interaction System Configuration")]
        [Tooltip("Maximum distance to check for interactables")]
        [SerializeField] private float interactionRange = 3f;

        [Tooltip("Layer mask for interaction raycasting")]
        [SerializeField] private LayerMask interactionLayerMask = -1;

        [Tooltip("Show debug information about nearby interactables")]
        [SerializeField] private bool debugMode = false;

        private static U3DInteractionManager instance;
        private List<IU3DInteractable> nearbyInteractables = new List<IU3DInteractable>();
        private IU3DInteractable currentInteractable;
        private U3DPlayerController localPlayerController;
        private Camera playerCamera;

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
                Debug.Log("InteractionManager: Singleton instance created");
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
            // Find the local player controller
            FindLocalPlayer();
        }

        private void FindLocalPlayer()
        {
            U3DPlayerController[] allPlayers = FindObjectsByType<U3DPlayerController>(FindObjectsSortMode.None);

            foreach (U3DPlayerController player in allPlayers)
            {
                if (player.IsLocalPlayer)
                {
                    localPlayerController = player;
                    playerCamera = player.GetComponentInChildren<Camera>();
                    Debug.Log($"InteractionManager: Found local player {player.name}");
                    break;
                }
            }

            if (localPlayerController == null)
            {
                Debug.LogWarning("InteractionManager: No local player found!");
            }
        }

        private void Update()
        {
            if (localPlayerController == null)
            {
                FindLocalPlayer();
                return;
            }

            UpdateNearbyInteractables();
        }

        /// <summary>
        /// Called by PlayerController when interact button is pressed
        /// </summary>
        public void OnPlayerInteract()
        {
            if (localPlayerController == null)
            {
                Debug.LogWarning("InteractionManager: No local player available for interaction");
                return;
            }

            // Find the best interactable to use
            IU3DInteractable targetInteractable = GetBestInteractable();

            if (targetInteractable != null)
            {
                Debug.Log($"Interacted with: {targetInteractable.GetInteractionPrompt()}");
                targetInteractable.OnInteract();
            }
            else
            {
                if (debugMode)
                {
                    Debug.Log("No interactables found in range");
                }
            }
        }

        /// <summary>
        /// Find all interactables within range and update current target
        /// </summary>
        private void UpdateNearbyInteractables()
        {
            nearbyInteractables.Clear();

            if (localPlayerController == null) return;

            Vector3 playerPosition = localPlayerController.transform.position;

            // Use sphere overlap to find all colliders in range
            Collider[] colliders = Physics.OverlapSphere(playerPosition, interactionRange, interactionLayerMask);

            foreach (Collider col in colliders)
            {
                // Get all IU3DInteractable components on this object and its children
                IU3DInteractable[] interactables = col.GetComponentsInChildren<IU3DInteractable>();

                foreach (IU3DInteractable interactable in interactables)
                {
                    if (interactable != null && interactable.CanInteract())
                    {
                        nearbyInteractables.Add(interactable);
                    }
                }
            }

            // Update current primary interactable
            IU3DInteractable newPrimary = GetBestInteractable();

            if (newPrimary != currentInteractable)
            {
                // Exit previous
                if (currentInteractable != null)
                {
                    currentInteractable.OnPlayerExitRange();
                }

                // Enter new
                if (newPrimary != null)
                {
                    newPrimary.OnPlayerEnterRange();
                }

                currentInteractable = newPrimary;
            }
        }

        /// <summary>
        /// Get the best interactable based on priority and distance
        /// </summary>
        private IU3DInteractable GetBestInteractable()
        {
            if (nearbyInteractables.Count == 0) return null;
            if (localPlayerController == null) return null;

            Vector3 playerPosition = localPlayerController.transform.position;

            IU3DInteractable best = null;
            int highestPriority = int.MinValue;
            float closestDistance = Mathf.Infinity;

            foreach (IU3DInteractable interactable in nearbyInteractables)
            {
                if (interactable == null || !interactable.CanInteract()) continue;

                // Get the transform of the interactable
                Transform interactableTransform = ((MonoBehaviour)interactable).transform;
                if (interactableTransform == null) continue;

                int priority = interactable.GetInteractionPriority();
                float distance = Vector3.Distance(playerPosition, interactableTransform.position);

                // Higher priority wins, or closer distance if same priority
                if (priority > highestPriority || (priority == highestPriority && distance < closestDistance))
                {
                    best = interactable;
                    highestPriority = priority;
                    closestDistance = distance;
                }
            }

            return best;
        }

        private void OnDrawGizmosSelected()
        {
            if (localPlayerController != null)
            {
                // Draw interaction range
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(localPlayerController.transform.position, interactionRange);

                // Draw connections to nearby interactables
                if (debugMode && nearbyInteractables != null)
                {
                    Gizmos.color = Color.yellow;
                    foreach (IU3DInteractable interactable in nearbyInteractables)
                    {
                        if (interactable != null)
                        {
                            Transform interactableTransform = ((MonoBehaviour)interactable).transform;
                            if (interactableTransform != null)
                            {
                                Gizmos.DrawLine(localPlayerController.transform.position, interactableTransform.position);
                            }
                        }
                    }

                    // Highlight current primary
                    if (currentInteractable != null)
                    {
                        Gizmos.color = Color.red;
                        Transform primaryTransform = ((MonoBehaviour)currentInteractable).transform;
                        if (primaryTransform != null)
                        {
                            Gizmos.DrawWireSphere(primaryTransform.position, 0.5f);
                        }
                    }
                }
            }
        }

        // Public API for external systems
        public IU3DInteractable GetCurrentInteractable()
        {
            return currentInteractable;
        }

        public int GetNearbyInteractableCount()
        {
            return nearbyInteractables.Count;
        }

        public bool IsPlayerInRange(Vector3 objectPosition, float customRange = -1f)
        {
            if (localPlayerController == null) return false;

            float checkRange = customRange > 0 ? customRange : interactionRange;
            float distance = Vector3.Distance(localPlayerController.transform.position, objectPosition);
            return distance <= checkRange;
        }
    }

    /// <summary>
    /// Interface that all interactable objects must implement
    /// </summary>
    public interface IU3DInteractable
    {
        /// <summary>
        /// Called when player interacts with this object
        /// </summary>
        void OnInteract();

        /// <summary>
        /// Called when player enters interaction range
        /// </summary>
        void OnPlayerEnterRange();

        /// <summary>
        /// Called when player exits interaction range
        /// </summary>
        void OnPlayerExitRange();

        /// <summary>
        /// Check if this object can currently be interacted with
        /// </summary>
        bool CanInteract();

        /// <summary>
        /// Get interaction priority (higher = more important)
        /// </summary>
        int GetInteractionPriority();

        /// <summary>
        /// Get text to show in interaction prompt
        /// </summary>
        string GetInteractionPrompt();
    }
}