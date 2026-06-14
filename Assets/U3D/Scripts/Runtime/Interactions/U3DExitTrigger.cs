using UnityEngine;
using UnityEngine.Events;
using Fusion;

namespace U3D
{
    [RequireComponent(typeof(Collider))]
    public class U3DExitTrigger : NetworkBehaviour
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
        public UnityEvent OnExitTrigger;

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

        private void OnTriggerExit(Collider other)
        {
            if (requireTag && !other.CompareTag(requiredTag)) return;

            if (!isNetworked || Object == null)
            {
                float timeSinceLast = Time.time - lastTriggerTime;
                if (cooldownTime > 0f && timeSinceLast < cooldownTime) return;
                if (triggerOnce && hasTriggered) return;
                hasTriggered = triggerOnce || hasTriggered;
                lastTriggerTime = Time.time;
                OnExitTrigger?.Invoke();
                return;
            }

            float localTimeSinceLast = Time.time - NetworkLastTriggerTime;
            if (cooldownTime > 0f && localTimeSinceLast < cooldownTime) return;
            if (triggerOnce && NetworkHasTriggered) return;

            OnExitTrigger?.Invoke();

            if (Object.HasStateAuthority)
                UpdateNetworkState();
            else
                RPC_RequestTrigger();
        }

        private void UpdateNetworkState()
        {
            if (triggerOnce) NetworkHasTriggered = true;
            NetworkLastTriggerTime = Time.time;
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestTrigger()
        {
            float timeSinceLast = Time.time - NetworkLastTriggerTime;
            if (cooldownTime > 0f && timeSinceLast < cooldownTime) return;
            if (triggerOnce && NetworkHasTriggered) return;
            UpdateNetworkState();
        }

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
        public bool HasTriggered => isNetworked && Object != null ? NetworkHasTriggered : hasTriggered;
        public float LastTriggerTime => isNetworked && Object != null ? NetworkLastTriggerTime : lastTriggerTime;
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