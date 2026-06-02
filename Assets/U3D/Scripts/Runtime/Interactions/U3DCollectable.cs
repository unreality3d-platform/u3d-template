using UnityEngine;
using UnityEngine.Events;
using Fusion;

namespace U3D
{
    /// <summary>
    /// Add to any object that should be collectable into a player's Inventory.
    /// Pairs with U3DInventory in the scene.
    ///
    /// Collection methods:
    ///   OnEnter — picks up automatically when the player walks into the trigger.
    ///   OnInteract — picks up when the player presses the Interact key in range.
    ///
    /// On collection, the configured prefab is added to the local player's Inventory.
    /// The world GameObject is NOT auto-destroyed by default — wire OnCollected to
    /// SetActive(false) or Destroy() yourself if you want the world object to disappear.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class U3DCollectable : NetworkBehaviour, IU3DInteractable
    {
        public enum CollectionMethod { OnEnter, OnInteract }

        [Header("What Gets Collected")]
        [Tooltip("Prefab to add to the player's Inventory. Defaults to this GameObject for when the collectable IS the prefab being dispensed (gems, coins, items). For scattered pickups where many scene instances should stack together, assign the shared source prefab.")]
        [SerializeField] private GameObject prefabToCollect;

        [Tooltip("How many copies are added per successful collection.")]
        [Min(1)]
        [SerializeField] private int quantity = 1;

        [HideInInspector]
        [SerializeField] private CollectionMethod collectionMethod = CollectionMethod.OnEnter;

        [Header("Collection Trigger")]
        [Tooltip("Maximum distance for OnInteract collection (ignored for OnEnter). The U3DInteractionManager's overall interaction range still applies as a coarse outer filter.")]
        [SerializeField] private float interactDistance = 3f;

        [Header("Trigger Configuration")]
        [Tooltip("If enabled, this collectable can only be picked up once and then becomes inert.")]
        [SerializeField] private bool triggerOnce = false;

        [Tooltip("Seconds the collectable is inert after a successful pickup. 0 = no cooldown.")]
        [SerializeField] private float cooldownTime = 0f;

        [Tooltip("Only collect when the colliding object has a specific tag.")]
        [SerializeField] private bool requireTag = false;

        [Tooltip("Tag required to collect. Only checked when Require Tag is enabled.")]
        [SerializeField] private string requiredTag = "Player";

        [Header("Optional Label")]
        [Tooltip("Assign a U3DWorldspaceUI in your scene to show a label near this object. Edit the text on that object directly.")]
        public U3DWorldspaceUI labelUI;

        [Header("Events")]
        [Tooltip("Fired when the collectable is successfully picked up. The argument is the GameObject of the player who collected it.")]
        public UnityEvent<GameObject> OnCollected;

        [Tooltip("Fired when a collection attempt fails (cooldown, already collected, tag mismatch, inventory full).")]
        public UnityEvent OnCollectFailed;

        [Tooltip("Fired when the local player enters interact range. OnInteract mode only.")]
        public UnityEvent OnPlayerEnterRangeEvent;

        [Tooltip("Fired when the local player exits interact range. OnInteract mode only.")]
        public UnityEvent OnPlayerExitRangeEvent;

        [Networked] public bool NetworkHasBeenCollected { get; set; }
        [Networked] public float NetworkLastCollectTime { get; set; }

        private bool hasBeenCollected = false;
        private float lastCollectTime = 0f;
        private bool isNetworked = false;
        private Collider triggerCollider;
        private Transform playerTransform;
        private bool isInRange = false;
        private U3DInventory cachedInventory;

        private void Reset()
        {
            // Default prefabToCollect to self so the vending-machine pattern just works:
            // attach Collectable to a gem prefab, and it dispenses copies of that gem.
            if (prefabToCollect == null)
                prefabToCollect = gameObject;

            // Intentionally does NOT change the collider's isTrigger state. Forcing a trigger
            // silently removes a creator's intended blocking collider. The dashboard's two
            // "Make ... Collectable" tools set up the correct collider for each intent.
        }

        private void Awake()
        {
            triggerCollider = GetComponent<Collider>();
            isNetworked = GetComponent<NetworkObject>() != null;
        }

        private void Update()
        {
            // Range tracking only matters for OnInteract mode.
            if (collectionMethod == CollectionMethod.OnInteract)
                UpdatePlayerProximity();
        }

        private void UpdatePlayerProximity()
        {
            if (playerTransform == null)
            {
                FindLocalPlayer();
                if (playerTransform == null) return;
            }

            float distance = Vector3.Distance(transform.position, playerTransform.position);
            bool wasInRange = isInRange;
            isInRange = distance <= interactDistance;

            if (isInRange && !wasInRange)
                OnPlayerEnterRangeEvent?.Invoke();
            else if (!isInRange && wasInRange)
                OnPlayerExitRangeEvent?.Invoke();
        }

        private void FindLocalPlayer()
        {
            U3DPlayerController controller = U3DPlayerController.FindLocalPlayer();
            if (controller != null)
                playerTransform = controller.transform;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (collectionMethod != CollectionMethod.OnEnter) return;

            if (!PassesTagCheck(other.transform)) return;

            // Only the local player triggers collection — remote-player overlaps must not
            // dispense items into the local inventory.
            U3DPlayerController controller = other.GetComponentInParent<U3DPlayerController>();
            if (controller == null || !controller.IsLocalPlayer) return;

            AttemptCollect(controller.gameObject);
        }

        // IU3DInteractable implementation
        public void OnInteract()
        {
            if (collectionMethod != CollectionMethod.OnInteract)
            {
                OnCollectFailed?.Invoke();
                return;
            }

            U3DPlayerController controller = U3DPlayerController.FindLocalPlayer();
            if (controller == null)
            {
                OnCollectFailed?.Invoke();
                return;
            }

            if (!PassesTagCheck(controller.transform))
            {
                OnCollectFailed?.Invoke();
                return;
            }

            AttemptCollect(controller.gameObject);
        }

        public void OnPlayerEnterRange() { }
        public void OnPlayerExitRange() { }

        public bool CanInteract()
        {
            if (collectionMethod != CollectionMethod.OnInteract) return false;
            if (IsExhausted()) return false;
            if (IsOnCooldown()) return false;
            return isInRange;
        }

        public string GetInteractionPrompt()
        {
            return prefabToCollect != null ? $"Collect {prefabToCollect.name} (R)" : "Collect (R)";
        }

        private bool PassesTagCheck(Transform candidate)
        {
            if (!requireTag) return true;
            if (candidate == null) return false;
            return candidate.CompareTag(requiredTag);
        }

        private bool IsExhausted()
        {
            if (!triggerOnce) return false;
            return isNetworked ? NetworkHasBeenCollected : hasBeenCollected;
        }

        private bool IsOnCooldown()
        {
            if (cooldownTime <= 0f) return false;
            float last = isNetworked ? NetworkLastCollectTime : lastCollectTime;
            return Time.time - last < cooldownTime;
        }

        /// <summary>
        /// Gates and routes a collection attempt. Called by both OnTriggerEnter (OnEnter mode)
        /// and OnInteract (OnInteract mode). Tag check is performed by the caller.
        /// </summary>
        private void AttemptCollect(GameObject collectingPlayer)
        {
            if (IsExhausted())
            {
                OnCollectFailed?.Invoke();
                return;
            }

            if (IsOnCooldown())
            {
                OnCollectFailed?.Invoke();
                return;
            }

            if (prefabToCollect == null)
            {
                Debug.LogWarning($"U3DCollectable on '{name}': prefabToCollect is not assigned — nothing to add to inventory.", this);
                OnCollectFailed?.Invoke();
                return;
            }

            U3DInventory inventory = FindInventory();
            if (inventory == null)
            {
                Debug.LogWarning($"U3DCollectable on '{name}': No U3DInventory found in scene. Add one via Creator Dashboard → Game Systems → Add Inventory.", this);
                OnCollectFailed?.Invoke();
                return;
            }

            bool added = inventory.AddItem(prefabToCollect, quantity);
            if (!added)
            {
                OnCollectFailed?.Invoke();
                return;
            }

            RecordCollect();
            OnCollected?.Invoke(collectingPlayer);
        }

        private void RecordCollect()
        {
            if (isNetworked && Object != null && Object.IsValid)
            {
                if (triggerOnce) NetworkHasBeenCollected = true;
                NetworkLastCollectTime = Time.time;
            }
            else
            {
                if (triggerOnce) hasBeenCollected = true;
                lastCollectTime = Time.time;
            }
        }

        private U3DInventory FindInventory()
        {
            if (cachedInventory != null) return cachedInventory;
            cachedInventory = UnityEngine.Object.FindAnyObjectByType<U3DInventory>();
            return cachedInventory;
        }

        public override void Spawned()
        {
            if (!isNetworked) return;
        }

        private void OnValidate()
        {
            if (cooldownTime < 0f) cooldownTime = 0f;
            if (interactDistance < 0f) interactDistance = 0f;
            if (quantity < 1) quantity = 1;
        }

        // Public API
        public void ResetCollectable()
        {
            if (isNetworked && Object != null && Object.HasStateAuthority)
            {
                NetworkHasBeenCollected = false;
                NetworkLastCollectTime = 0f;
            }
            else if (!isNetworked)
            {
                hasBeenCollected = false;
                lastCollectTime = 0f;
            }
        }

        public void SetCollectionMethod(CollectionMethod method)
        {
            collectionMethod = method;
        }

        public bool HasBeenCollected => isNetworked ? NetworkHasBeenCollected : hasBeenCollected;
        public bool IsNetworked => isNetworked;
        public bool IsInRange => isInRange;
        public GameObject PrefabToCollect { get => prefabToCollect; set => prefabToCollect = value; }
        public int Quantity { get => quantity; set => quantity = Mathf.Max(1, value); }
    }
}
