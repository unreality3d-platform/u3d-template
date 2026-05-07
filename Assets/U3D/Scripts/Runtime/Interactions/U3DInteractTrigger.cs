using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Fusion;

namespace U3D
{
    [RequireComponent(typeof(Collider))]
    public class U3DInteractTrigger : NetworkBehaviour, IU3DInteractable
    {
        [Header("Trigger Configuration")]
        [Tooltip("Only fire for players/objects with a specific tag")]
        [SerializeField] private bool requireTag = false;

        [Tooltip("Tag required to activate this trigger")]
        [SerializeField] private string requiredTag = "Player";

        [Tooltip("Should this trigger only work once?")]
        [SerializeField] private bool triggerOnce = false;

        [Tooltip("Delay before trigger can fire again (seconds)")]
        [SerializeField] private float cooldownTime = 0f;

        [Header("Interaction Settings")]
        [Tooltip("Key shown in interaction prompt (remappable)")]
        [SerializeField] private KeyCode interactKey = KeyCode.R;

        [Tooltip("Maximum distance for proximity-based interaction (Interact key)")]
        [SerializeField] private float maxInteractDistance = 3f;

        [Tooltip("Maximum distance for mouse click raycast (0 = unlimited)")]
        [SerializeField] private float maxClickDistance = 10f;

        [Tooltip("Allow mouse click as an additional way to trigger (camera raycast)")]
        [SerializeField] private bool allowMouseClick = true;

        [Header("Optional Label")]
        [Tooltip("Assign a U3DWorldspaceUI in your scene to show a label near this object. Edit the text on that object directly.")]
        public U3DWorldspaceUI labelUI;

        [Header("Events")]
        public UnityEvent OnInteractTriggered;
        public UnityEvent OnInteractFailed;
        public UnityEvent OnPlayerEnterRangeEvent;
        public UnityEvent OnPlayerExitRangeEvent;

        [Networked] public bool NetworkHasTriggered { get; set; }
        [Networked] public float NetworkLastTriggerTime { get; set; }

        private bool hasTriggered = false;
        private float lastTriggerTime = 0f;
        private bool isNetworked = false;
        private Collider triggerCollider;
        private Transform playerTransform;
        private bool isInRange = false;

        private void Awake()
        {
            triggerCollider = GetComponent<Collider>();
            isNetworked = GetComponent<NetworkObject>() != null;
        }

        private void Update()
        {
            UpdatePlayerProximity();

            if (allowMouseClick)
                CheckMouseClick();
        }

        private void UpdatePlayerProximity()
        {
            if (playerTransform == null)
            {
                FindPlayer();
                if (playerTransform == null) return;
            }

            float distance = Vector3.Distance(transform.position, playerTransform.position);
            bool wasInRange = isInRange;
            isInRange = distance <= maxInteractDistance;

            if (isInRange && !wasInRange)
                OnPlayerEnterRangeEvent?.Invoke();
            else if (!isInRange && wasInRange)
                OnPlayerExitRangeEvent?.Invoke();
        }

        private void FindPlayer()
        {
            U3DPlayerController[] allPlayers = FindObjectsByType<U3DPlayerController>(FindObjectsSortMode.None);
            foreach (U3DPlayerController player in allPlayers)
            {
                if (player.IsLocalPlayer)
                {
                    playerTransform = player.transform;
                    return;
                }
            }
        }

        private bool PassesTagCheck()
        {
            if (!requireTag) return true;
            if (playerTransform == null) return false;
            return playerTransform.CompareTag(requiredTag);
        }

        private void CheckMouseClick()
        {
            if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
                return;

            if (Camera.main == null)
                return;

            Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
            float rayDistance = maxClickDistance > 0f ? maxClickDistance : Mathf.Infinity;

            if (!Physics.Raycast(ray, out RaycastHit hit, rayDistance))
                return;

            if (hit.collider != triggerCollider)
                return;

            if (!PassesTagCheck())
            {
                OnInteractFailed?.Invoke();
                return;
            }

            AttemptTrigger();
        }

        private bool AttemptTrigger()
        {
            float currentTime = Time.time;
            float timeSinceLastTrigger = isNetworked
                ? currentTime - NetworkLastTriggerTime
                : currentTime - lastTriggerTime;

            if (cooldownTime > 0f && timeSinceLastTrigger < cooldownTime)
            {
                OnInteractFailed?.Invoke();
                return false;
            }

            bool alreadyTriggered = isNetworked ? NetworkHasTriggered : hasTriggered;
            if (triggerOnce && alreadyTriggered)
            {
                OnInteractFailed?.Invoke();
                return false;
            }

            ExecuteTrigger();
            return true;
        }

        private void ExecuteTrigger()
        {
            if (isNetworked)
            {
                if (triggerOnce) NetworkHasTriggered = true;
                NetworkLastTriggerTime = Time.time;
            }
            else
            {
                if (triggerOnce) hasTriggered = true;
                lastTriggerTime = Time.time;
            }

            OnInteractTriggered?.Invoke();
        }

        // IU3DInteractable implementation
        public void OnInteract()
        {
            if (!PassesTagCheck())
            {
                OnInteractFailed?.Invoke();
                return;
            }

            AttemptTrigger();
        }

        public void OnPlayerEnterRange() { }

        public void OnPlayerExitRange() { }

        public bool CanInteract()
        {
            if (!PassesTagCheck()) return false;

            if (triggerOnce && (isNetworked ? NetworkHasTriggered : hasTriggered))
                return false;

            if (cooldownTime > 0f)
            {
                float timeSinceLastTrigger = isNetworked
                    ? Time.time - NetworkLastTriggerTime
                    : Time.time - lastTriggerTime;
                if (timeSinceLastTrigger < cooldownTime)
                    return false;
            }

            return isInRange;
        }

        public string GetInteractionPrompt()
        {
            return $"Interact ({interactKey})";
        }

        // Public API
        public void ResetTrigger()
        {
            if (isNetworked && Object != null && Object.HasStateAuthority)
            {
                NetworkHasTriggered = false;
                NetworkLastTriggerTime = 0f;
            }
            else if (!isNetworked)
            {
                hasTriggered = false;
                lastTriggerTime = 0f;
            }
        }

        public void SetCooldownTime(float newCooldownTime) => cooldownTime = Mathf.Max(0f, newCooldownTime);
        public void SetTriggerOnce(bool value) => triggerOnce = value;
        public bool HasTriggered => isNetworked ? NetworkHasTriggered : hasTriggered;
        public float LastTriggerTime => isNetworked ? NetworkLastTriggerTime : lastTriggerTime;
        public bool IsOnCooldown => Time.time - LastTriggerTime < cooldownTime;
        public bool IsNetworked => isNetworked;
        public bool IsInRange => isInRange;
        public KeyCode InteractKey { get => interactKey; set => interactKey = value; }

        public override void Spawned()
        {
            if (!isNetworked) return;
        }

        private void OnValidate()
        {
            if (cooldownTime < 0f) cooldownTime = 0f;
            if (maxInteractDistance < 0f) maxInteractDistance = 0f;
            if (maxClickDistance < 0f) maxClickDistance = 0f;
        }
    }
}