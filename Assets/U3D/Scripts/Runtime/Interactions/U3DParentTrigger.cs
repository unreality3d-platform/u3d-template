using UnityEngine;
using UnityEngine.Events;
using Fusion;
using System.Collections;

namespace U3D
{
    /// <summary>
    /// Enhanced parent trigger for moving platforms and vehicles
    /// Automatically parents/unparents player when entering/exiting trigger
    /// Supports both networked and non-networked modes with proper U3D player detection
    /// Based on Unity 6.1+ standards with Fusion 2 compatibility
    /// ENHANCED: Includes retry logic and proper player detection
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class U3DParentTrigger : NetworkBehaviour
    {
        [Header("Player Detection")]
        [Tooltip("Detect U3D player specifically")]
        [SerializeField] private bool detectU3DPlayer = true;

        [Tooltip("Also detect objects with Player tag")]
        [SerializeField] private bool detectPlayerTag = true;

        [Tooltip("Retry finding player if not found initially")]
        [SerializeField] private bool retryPlayerDetection = true;

        [Tooltip("How often to retry finding player (seconds)")]
        [SerializeField] private float retryInterval = 0.1f;

        [Header("Parenting Configuration")]
        [Tooltip("Keep world position when parenting")]
        [SerializeField] private bool keepWorldPosition = true;

        [Tooltip("Smooth transition when parenting/unparenting")]
        [SerializeField] private bool useSmoothTransition = false;

        [Tooltip("Transition speed for smooth parenting")]
        [SerializeField] private float transitionSpeed = 5f;

        [Header("Events")]
        [Tooltip("Called when player enters and gets parented")]
        public UnityEvent OnPlayerParented;

        [Tooltip("Called when player exits and gets unparented")]
        public UnityEvent OnPlayerUnparented;

        [Tooltip("Called when any object enters trigger")]
        public UnityEvent OnObjectEnter;

        [Tooltip("Called when any object exits trigger")]
        public UnityEvent OnObjectExit;

        // Network state
        [Networked] public bool NetworkPlayerIsParented { get; set; }
        [Networked] public PlayerRef NetworkParentedPlayer { get; set; }

        // Components and state
        private GameObject player;
        private bool playerFound = false;
        private bool isNetworked = false;
        private Collider triggerCollider;
        private Transform originalParent;
        private Coroutine playerSearchCoroutine;
        private bool isPlayerParented = false;

        private void Awake()
        {
            triggerCollider = GetComponent<Collider>();
            triggerCollider.isTrigger = true;

            // Check if networked
            isNetworked = GetComponent<NetworkObject>() != null;
        }

        private void Start()
        {
            if (retryPlayerDetection)
            {
                StartCoroutine(FindPlayerWithRetry());
            }
            else
            {
                FindPlayer();
            }
        }

        private IEnumerator FindPlayerWithRetry()
        {
            while (!playerFound)
            {
                FindPlayer();
                if (playerFound) break;

                yield return new WaitForSeconds(retryInterval);
            }
        }

        private void FindPlayer()
        {
            // Look for U3D_PlayerController specifically
            if (detectU3DPlayer)
            {
                var playerController = FindAnyObjectByType<U3DPlayerController>();
                if (playerController != null)
                {
                    player = playerController.gameObject;
                    playerFound = true;
                    Debug.Log($"Found U3D Player: {player.name}");
                    return;
                }
            }

            // Also check by tag as backup
            if (detectPlayerTag)
            {
                var playerByTag = GameObject.FindWithTag("Player");
                if (playerByTag != null)
                {
                    player = playerByTag;
                    playerFound = true;
                    Debug.Log($"Found Player by tag: {player.name}");
                    return;
                }
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            // Authority check for networked objects
            if (isNetworked && !Object.HasStateAuthority)
            {
                return;
            }

            bool isValidPlayer = IsValidPlayer(other.gameObject);

            if (isValidPlayer && playerFound && other.gameObject == player)
            {
                ParentPlayer(other.transform);
            }

            // Fire general enter event
            OnObjectEnter?.Invoke();
        }

        private void OnTriggerExit(Collider other)
        {
            // Authority check for networked objects
            if (isNetworked && !Object.HasStateAuthority)
            {
                return;
            }

            bool isValidPlayer = IsValidPlayer(other.gameObject);

            if (isValidPlayer && playerFound && other.gameObject == player)
            {
                UnparentPlayer(other.transform);
            }

            // Fire general exit event
            OnObjectExit?.Invoke();
        }

        private bool IsValidPlayer(GameObject obj)
        {
            // Check for U3D player
            if (detectU3DPlayer && obj.GetComponent<U3DPlayerController>() != null)
            {
                return true;
            }

            // Check for player tag
            if (detectPlayerTag && obj.CompareTag("Player"))
            {
                return true;
            }

            return false;
        }

        private void ParentPlayer(Transform playerTransform)
        {
            if (isPlayerParented) return;

            // Store original parent
            originalParent = playerTransform.parent;

            // Parent the player
            if (keepWorldPosition)
            {
                playerTransform.SetParent(transform, true);
            }
            else
            {
                playerTransform.SetParent(transform, false);
            }

            // Update state
            isPlayerParented = true;
            if (isNetworked)
            {
                NetworkPlayerIsParented = true;
                if (Runner != null && Runner.LocalPlayer != null)
                {
                    NetworkParentedPlayer = Runner.LocalPlayer;
                }
            }

            OnPlayerParented?.Invoke();
            Debug.Log($"Player parented to: {transform.name}");
        }

        private void UnparentPlayer(Transform playerTransform)
        {
            if (!isPlayerParented) return;

            // Unparent the player
            if (keepWorldPosition)
            {
                playerTransform.SetParent(originalParent, true);
            }
            else
            {
                playerTransform.SetParent(originalParent, false);
            }

            // Update state
            isPlayerParented = false;
            if (isNetworked)
            {
                NetworkPlayerIsParented = false;
                NetworkParentedPlayer = default(PlayerRef);
            }

            OnPlayerUnparented?.Invoke();
            Debug.Log($"Player unparented from: {transform.name}");
        }

        // Public methods for external control
        public void ForceUnparentPlayer()
        {
            if (playerFound && isPlayerParented)
            {
                UnparentPlayer(player.transform);
            }
        }

        public void RefreshPlayerSearch()
        {
            playerFound = false;
            if (playerSearchCoroutine != null)
            {
                StopCoroutine(playerSearchCoroutine);
            }

            if (retryPlayerDetection)
            {
                playerSearchCoroutine = StartCoroutine(FindPlayerWithRetry());
            }
            else
            {
                FindPlayer();
            }
        }

        public void SetKeepWorldPosition(bool keepWorld)
        {
            keepWorldPosition = keepWorld;
        }

        // Public properties
        public bool PlayerFound => playerFound;
        public bool IsPlayerParented => isNetworked ? NetworkPlayerIsParented : isPlayerParented;
        public GameObject Player => player;
        public bool IsNetworked => isNetworked;

        // Override for non-networked compatibility
        public override void Spawned()
        {
            if (!isNetworked) return;
            // Network initialization if needed
        }

        private void OnDestroy()
        {
            // Clean up coroutines
            if (playerSearchCoroutine != null)
            {
                StopCoroutine(playerSearchCoroutine);
            }

            // Ensure player is unparented if this object is destroyed
            if (playerFound && isPlayerParented && player != null)
            {
                UnparentPlayer(player.transform);
            }
        }

        private void OnValidate()
        {
            if (retryInterval <= 0f)
            {
                retryInterval = 0.1f;
            }

            if (transitionSpeed <= 0f)
            {
                transitionSpeed = 5f;
            }

            // Ensure we have a trigger collider
            if (Application.isPlaying)
            {
                Collider col = GetComponent<Collider>();
                if (col != null && !col.isTrigger)
                {
                    Debug.LogWarning($"U3DParentTrigger on {gameObject.name} requires a trigger collider");
                }
            }
        }
    }
}