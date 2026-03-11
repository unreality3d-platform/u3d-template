using UnityEngine;
using UnityEngine.Events;
using Fusion;
using System.Collections.Generic;

namespace U3D
{
    /// <summary>
    /// Stateful trigger zone that tracks occupancy and fires events based on whether
    /// anything is currently inside. Use this instead of paired Enter/Exit triggers
    /// when you need "while occupied" logic: pressure plates, safe zones, proximity areas.
    ///
    /// OnZoneOccupied fires when the first qualifying object enters an empty zone.
    /// OnZoneCleared fires when the last qualifying object leaves.
    /// </summary>
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
            if (isNetworked && !Object.HasStateAuthority) return;

            if (!_occupants.Contains(other))
                _occupants.Add(other);

            if (_occupants.Count == 1)
                FireOccupied();
        }

        private void OnTriggerExit(Collider other)
        {
            if (requireTag && !other.CompareTag(requiredTag)) return;
            if (isNetworked && !Object.HasStateAuthority) return;

            _occupants.Remove(other);

            if (_occupants.Count == 0)
                FireCleared();
        }

        private void FireOccupied()
        {
            bool alreadyTriggered = isNetworked ? NetworkHasTriggered : hasTriggered;
            if (triggerOnce && alreadyTriggered) return;

            OnZoneOccupied?.Invoke();
        }

        private void FireCleared()
        {
            bool alreadyTriggered = isNetworked ? NetworkHasTriggered : hasTriggered;
            if (triggerOnce && alreadyTriggered) return;

            if (triggerOnce)
            {
                if (isNetworked) NetworkHasTriggered = true;
                else hasTriggered = true;
            }

            OnZoneCleared?.Invoke();
        }

        public void ResetZone()
        {
            _occupants.Clear();
            if (isNetworked && Object.HasStateAuthority)
                NetworkHasTriggered = false;
            else
                hasTriggered = false;
        }

        public bool IsOccupied => _occupants.Count > 0;
        public int OccupantCount => _occupants.Count;

        private void OnDisable()
        {
            _occupants.Clear();
        }

        public override void Spawned()
        {
            if (!isNetworked) return;
        }
    }
}