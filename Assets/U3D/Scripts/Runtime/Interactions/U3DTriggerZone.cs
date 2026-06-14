using UnityEngine;
using UnityEngine.Events;
using Fusion;
using System.Collections.Generic;

namespace U3D
{
    [RequireComponent(typeof(Collider))]
    public class U3DTriggerZone : NetworkBehaviour
    {
        [Header("Zone Configuration")]
        [Tooltip("Only respond to objects with a specific tag")]
        [SerializeField] private bool requireTag = false;

        [Tooltip("Tag required to count as an occupant")]
        [SerializeField] private string requiredTag = "Player";

        [Tooltip("Should this zone only fire once ever (first occupy/clear cycle)")]
        [SerializeField] private bool triggerOnce = false;

        [Header("Events")]
        [Tooltip("Fired when the zone goes from empty to occupied")]
        public UnityEvent OnZoneOccupied;

        [Tooltip("Fired when the zone goes from occupied to empty")]
        public UnityEvent OnZoneCleared;

        [Networked] public bool NetworkHasTriggered { get; set; }

        private readonly List<Collider> _occupants = new List<Collider>();
        private bool hasTriggered = false;
        private bool isNetworked = false;

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
            isNetworked = GetComponent<NetworkObject>() != null;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (requireTag && !other.CompareTag(requiredTag)) return;

            bool alreadyTriggered = isNetworked && Object != null ? NetworkHasTriggered : hasTriggered;
            if (triggerOnce && alreadyTriggered) return;

            if (!_occupants.Contains(other))
                _occupants.Add(other);

            if (_occupants.Count == 1)
            {
                OnZoneOccupied?.Invoke();
                // Tell authority the zone is now occupied (for triggerOnce tracking).
                if (isNetworked && Object != null && !Object.HasStateAuthority)
                    RPC_NotifyOccupied();
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (requireTag && !other.CompareTag(requiredTag)) return;

            _occupants.Remove(other);

            if (_occupants.Count == 0)
            {
                bool alreadyTriggered = isNetworked && Object != null ? NetworkHasTriggered : hasTriggered;
                if (triggerOnce && alreadyTriggered) return;

                OnZoneCleared?.Invoke();

                if (triggerOnce)
                {
                    if (isNetworked && Object != null)
                    {
                        if (Object.HasStateAuthority) NetworkHasTriggered = true;
                        else RPC_MarkTriggered();
                    }
                    else hasTriggered = true;
                }
            }
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_NotifyOccupied() { }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_MarkTriggered() => NetworkHasTriggered = true;

        public void ResetZone()
        {
            _occupants.Clear();
            if (isNetworked && Object != null && Object.HasStateAuthority)
                NetworkHasTriggered = false;
            else if (!isNetworked)
                hasTriggered = false;
        }

        public bool IsOccupied => _occupants.Count > 0;
        public int OccupantCount => _occupants.Count;

        private void OnDisable() => _occupants.Clear();

        public override void Spawned()
        {
            if (!isNetworked) return;
        }
    }
}