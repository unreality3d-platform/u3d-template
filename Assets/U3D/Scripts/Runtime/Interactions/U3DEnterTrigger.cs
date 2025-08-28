using UnityEngine;
using UnityEngine.Events;
using Fusion;

namespace U3D
{
    /// <summary>
    /// Enhanced trigger component for player enter events
    /// Supports both networked and non-networked modes
    /// Integrates with U3D player detection system
    /// Based on Unity 6.1+ standards with Fusion 2 compatibility
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class U3DEnterTrigger : NetworkBehaviour
    {
        [Header("Trigger Configuration")]
        [Tooltip("Only trigger for objects with this tag (leave empty for any object)")]
        [SerializeField] private string requiredTag = "Player";

        [Tooltip("Should this trigger only work once?")]
        [SerializeField] private bool triggerOnce = false;

        [Tooltip("Delay before trigger can fire again (seconds)")]
        [SerializeField] private float cooldownTime = 0f;

        [Header("Player Detection")]
        [Tooltip("Detect U3D player specifically")]
        [SerializeField] private bool detectU3DPlayer = true;

        [Tooltip("Also detect other objects with required tag")]
        [SerializeField] private bool detectTaggedObjects = true;

        [Header("Events")]
        [Tooltip("Called when trigger is entered")]
        public UnityEvent OnEnterTrigger;

        [Tooltip("Called when U3D player specifically enters")]
        public UnityEvent OnPlayerEnter;

        [Tooltip("Called when any valid object enters")]
        public UnityEvent OnObjectEnter;

        // Network state
        [Networked] public bool NetworkHasTriggered { get; set; }
        [Networked] public float NetworkLastTriggerTime { get; set; }

        // Local state
        private bool hasTriggered = false;
        private float lastTriggerTime = 0f;
        private Collider triggerCollider;
        private bool isNetworked = false;

        private void Awake()
        {
            triggerCollider = GetComponent<Collider>();
            triggerCollider.isTrigger = true;

            // Check if networked
            isNetworked = GetComponent<NetworkObject>() != null;
        }

        private void OnTriggerEnter(Collider other)
        {
            // Check cooldown
            float currentTime = Time.time;
            float timeSinceLastTrigger = isNetworked ?
                currentTime - NetworkLastTriggerTime :
                currentTime - lastTriggerTime;

            if (cooldownTime > 0f && timeSinceLastTrigger < cooldownTime)
            {
                return;
            }

            // Check if already triggered and set to trigger once
            bool alreadyTriggered = isNetworked ? NetworkHasTriggered : hasTriggered;
            if (triggerOnce && alreadyTriggered)
            {
                return;
            }

            // Authority check for networked objects
            if (isNetworked && !Object.HasStateAuthority)
            {
                return;
            }

            bool shouldTrigger = false;
            bool isPlayer = false;

            // Check for U3D player
            if (detectU3DPlayer)
            {
                U3DPlayerController playerController = other.GetComponent<U3DPlayerController>();
                if (playerController != null)
                {
                    shouldTrigger = true;
                    isPlayer = true;
                    Debug.Log($"U3D Player entered trigger: {gameObject.name}");
                }
            }

            // Check for tagged objects
            if (!shouldTrigger && detectTaggedObjects && !string.IsNullOrEmpty(requiredTag))
            {
                if (other.CompareTag(requiredTag))
                {
                    shouldTrigger = true;
                    isPlayer = requiredTag == "Player";
                    Debug.Log($"Tagged object ({requiredTag}) entered trigger: {gameObject.name}");
                }
            }

            // Check for any object if no specific requirements
            if (!shouldTrigger && string.IsNullOrEmpty(requiredTag) && !detectU3DPlayer)
            {
                shouldTrigger = true;
                Debug.Log($"Object entered trigger: {gameObject.name}");
            }

            if (shouldTrigger)
            {
                ExecuteTrigger(isPlayer, other.gameObject);
            }
        }

        private void ExecuteTrigger(bool isPlayer, GameObject triggerObject)
        {
            // Update state
            if (isNetworked)
            {
                NetworkHasTriggered = triggerOnce ? true : NetworkHasTriggered;
                NetworkLastTriggerTime = Time.time;
            }
            else
            {
                hasTriggered = triggerOnce ? true : hasTriggered;
                lastTriggerTime = Time.time;
            }

            // Fire events
            OnEnterTrigger?.Invoke();
            OnObjectEnter?.Invoke();

            if (isPlayer)
            {
                OnPlayerEnter?.Invoke();
            }

            Debug.Log($"Enter trigger executed on {gameObject.name} by {triggerObject.name}");
        }

        // Public methods for external control
        public void ResetTrigger()
        {
            if (isNetworked && Object.HasStateAuthority)
            {
                NetworkHasTriggered = false;
                NetworkLastTriggerTime = 0f;
            }
            else if (!isNetworked)
            {
                hasTriggered = false;
                lastTriggerTime = 0f;
            }

            Debug.Log($"Enter trigger reset: {gameObject.name}");
        }

        public void SetCooldownTime(float newCooldownTime)
        {
            cooldownTime = Mathf.Max(0f, newCooldownTime);
        }

        public void SetTriggerOnce(bool triggerOnce)
        {
            this.triggerOnce = triggerOnce;
        }

        // Public properties
        public bool HasTriggered => isNetworked ? NetworkHasTriggered : hasTriggered;
        public float LastTriggerTime => isNetworked ? NetworkLastTriggerTime : lastTriggerTime;
        public bool IsOnCooldown => Time.time - LastTriggerTime < cooldownTime;
        public bool IsNetworked => isNetworked;

        // Override for non-networked compatibility
        public override void Spawned()
        {
            if (!isNetworked) return;
            // Network initialization if needed
        }

        private void OnValidate()
        {
            if (cooldownTime < 0f)
            {
                cooldownTime = 0f;
            }

            // Ensure we have a trigger collider
            if (Application.isPlaying)
            {
                Collider col = GetComponent<Collider>();
                if (col != null && !col.isTrigger)
                {
                    Debug.LogWarning($"U3DEnterTrigger on {gameObject.name} requires a trigger collider");
                }
            }
        }
    }
}