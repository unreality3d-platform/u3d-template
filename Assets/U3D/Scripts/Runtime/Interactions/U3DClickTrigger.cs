using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Fusion;

namespace U3D
{
    [RequireComponent(typeof(Collider))]
    public class U3DClickTrigger : NetworkBehaviour
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

        [Header("Optional Label")]
        public U3DBillboardUI labelUI;

        [Header("Events")]
        [Tooltip("Called when this object is clicked")]
        public UnityEvent OnClickTrigger;

        [Tooltip("Called when clicked by the U3D player")]
        public UnityEvent OnPlayerClick;

        [Tooltip("Called when clicked by any valid object")]
        public UnityEvent OnObjectClick;

        [Networked] public bool NetworkHasTriggered { get; set; }
        [Networked] public float NetworkLastTriggerTime { get; set; }

        private bool hasTriggered = false;
        private float lastTriggerTime = 0f;
        private bool isNetworked = false;
        private Collider clickCollider;

        private void Awake()
        {
            clickCollider = GetComponent<Collider>();
            isNetworked = GetComponent<NetworkObject>() != null;
        }

        private void Update()
        {
            if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
                return;

            if (Camera.main == null)
                return;

            Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit))
                return;

            if (hit.collider != clickCollider)
                return;

            float currentTime = Time.time;
            float timeSinceLastTrigger = isNetworked
                ? currentTime - NetworkLastTriggerTime
                : currentTime - lastTriggerTime;

            if (cooldownTime > 0f && timeSinceLastTrigger < cooldownTime)
                return;

            bool alreadyTriggered = isNetworked ? NetworkHasTriggered : hasTriggered;
            if (triggerOnce && alreadyTriggered)
                return;

            bool isPlayer = false;

            if (detectU3DPlayer)
            {
                // Local player check: find local NetworkObject or fall back to tag
                GameObject localPlayer = GameObject.FindWithTag("Player");
                if (localPlayer != null && localPlayer.GetComponent<U3DPlayerController>() != null)
                    isPlayer = true;
            }

            if (!isPlayer && detectTaggedObjects && !string.IsNullOrEmpty(requiredTag))
            {
                GameObject localPlayer = GameObject.FindWithTag(requiredTag);
                if (localPlayer != null)
                    isPlayer = requiredTag == "Player";
            }

            ExecuteTrigger(isPlayer);
        }

        private void ExecuteTrigger(bool isPlayer)
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

            OnClickTrigger?.Invoke();
            OnObjectClick?.Invoke();

            if (isPlayer)
                OnPlayerClick?.Invoke();
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

        public void SetCooldownTime(float newCooldownTime)
        {
            cooldownTime = Mathf.Max(0f, newCooldownTime);
        }

        public void SetTriggerOnce(bool value)
        {
            triggerOnce = value;
        }

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
            if (cooldownTime < 0f)
                cooldownTime = 0f;
        }
    }
}