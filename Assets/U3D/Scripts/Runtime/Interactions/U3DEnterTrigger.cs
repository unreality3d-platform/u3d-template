using UnityEngine;
using UnityEngine.Events;
using Fusion;

namespace U3D
{
    [RequireComponent(typeof(Collider))]
    public class U3DEnterTrigger : NetworkBehaviour
    {
        [Header("Trigger Configuration")]
        [Tooltip("Only fire for objects with a specific tag")]
        [SerializeField] private bool requireTag = false;

        [Tooltip("Tag required to fire this trigger")]
        [SerializeField] private string requiredTag = "Player";

        [Tooltip("Should this trigger only work once?")]
        [SerializeField] private bool triggerOnce = false;

        [Tooltip("Delay before trigger can fire again (seconds)")]
        [SerializeField] private float cooldownTime = 0f;

        [Header("Events")]
        public UnityEvent OnEnterTrigger;

        [Networked] public bool NetworkHasTriggered { get; set; }
        [Networked] public float NetworkLastTriggerTime { get; set; }

        private bool hasTriggered = false;
        private float lastTriggerTime = 0f;
        private bool isNetworked = false;

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
            isNetworked = GetComponent<NetworkObject>() != null;
        }

        private void OnTriggerEnter(Collider other)
        {
            float currentTime = Time.time;
            float timeSinceLastTrigger = isNetworked
                ? currentTime - NetworkLastTriggerTime
                : currentTime - lastTriggerTime;

            if (cooldownTime > 0f && timeSinceLastTrigger < cooldownTime)
                return;

            bool alreadyTriggered = isNetworked ? NetworkHasTriggered : hasTriggered;
            if (triggerOnce && alreadyTriggered)
                return;

            if (isNetworked && !Object.HasStateAuthority)
                return;

            if (requireTag && !other.CompareTag(requiredTag))
                return;

            ExecuteTrigger();
        }

        private void ExecuteTrigger()
        {
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

            OnEnterTrigger?.Invoke();
        }

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
        }

        public void SetCooldownTime(float newCooldownTime) => cooldownTime = Mathf.Max(0f, newCooldownTime);
        public void SetTriggerOnce(bool value) => triggerOnce = value;

        public bool HasTriggered => isNetworked ? NetworkHasTriggered : hasTriggered;
        public float LastTriggerTime => isNetworked ? NetworkLastTriggerTime : lastTriggerTime;
        public bool IsOnCooldown => Time.time - LastTriggerTime < cooldownTime;
        public bool IsNetworked => isNetworked;

        public override void Spawned()
        {
            if (!isNetworked) return;
        }

        private void OnValidate()
        {
            if (cooldownTime < 0f) cooldownTime = 0f;
        }
    }
}