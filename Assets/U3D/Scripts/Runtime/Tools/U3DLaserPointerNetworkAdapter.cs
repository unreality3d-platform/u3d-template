using Fusion;
using UnityEngine;

namespace U3D
{
    /// <summary>
    /// Networks a U3DLaserPointer's on/off state and pulse across a Fusion Shared Mode
    /// session so every player sees the same beam. The visual core stays network-blind;
    /// this adapter is the only Fusion-aware piece.
    ///
    /// Drive the pointer by calling THIS adapter's Activate/Deactivate/Toggle/Pulse
    /// (from a UGUI button, U3D Interact Trigger, etc.) rather than the core's. The
    /// adapter flips the networked state; every client — including the caller — reacts
    /// by driving its own local core. Only the on/off bit and a pulse counter travel the
    /// network; each client draws the beam locally from the prop's already-synced
    /// transform, so the shape never goes over the wire.
    ///
    /// With no NetworkObject present (or before a Runner is running) it drives the core
    /// directly, so the same prefab still works in a non-networked scene.
    /// </summary>
    [RequireComponent(typeof(U3DLaserPointer))]
    public class U3DLaserPointerNetworkAdapter : NetworkBehaviour
    {
        [Networked] public bool NetworkIsOn { get; set; }
        [Networked] public int NetworkPulseCount { get; set; }

        private U3DLaserPointer pointer;
        private NetworkObject networkObject;
        private bool isNetworked;

        private bool requestingAuthority;
        private float authorityRequestTime;
        private const float AUTHORITY_REQUEST_TIMEOUT = 2f;

        // Intent captured while waiting for state authority to arrive.
        private bool pendingOn;
        private bool pendingPulse;

        // Render-based change detection caches, mirroring U3DGrabbable's approach.
        private bool lastAppliedOn;
        private int lastSeenPulseCount;

        private void Awake()
        {
            pointer = GetComponent<U3DLaserPointer>();
            networkObject = GetComponent<NetworkObject>();
            isNetworked = networkObject != null;
        }

        public override void Spawned()
        {
            // Adopt the current networked truth without firing a phantom toggle on late
            // joiners, and override any stray Start Active left on the core — when
            // networked, the adapter owns activation.
            lastAppliedOn = NetworkIsOn;
            lastSeenPulseCount = NetworkPulseCount;
            ApplyOnState(NetworkIsOn);
        }

        // Public surface mirrors the core so wiring is identical to what you'd have
        // pointed at the core directly.
        public void Activate() => SetOn(true);
        public void Deactivate() => SetOn(false);
        public void Toggle() => SetOn(!IsOn());
        public void Pulse() => RequestPulse();

        private bool IsOn()
        {
            if (!isNetworked || Object == null || !Object.IsValid) return pointer.IsActive;
            return NetworkIsOn;
        }

        private void SetOn(bool on)
        {
            if (!isNetworked || Object == null || !Object.IsValid)
            {
                ApplyOnState(on);
                return;
            }

            if (Object.HasStateAuthority)
            {
                NetworkIsOn = on;
            }
            else
            {
                pendingOn = on;
                RequestAuthority();
            }
        }

        private void RequestPulse()
        {
            if (!isNetworked || Object == null || !Object.IsValid)
            {
                pointer.Pulse();
                return;
            }

            if (Object.HasStateAuthority)
            {
                NetworkPulseCount++;
            }
            else
            {
                pendingPulse = true;
                RequestAuthority();
            }
        }

        private void RequestAuthority()
        {
            if (requestingAuthority) return;
            requestingAuthority = true;
            authorityRequestTime = Time.time;
            Object.RequestStateAuthority();
        }

        // Fusion invokes this on every client when this object's state authority changes,
        // the same hook U3DGrabbable uses. On the client that just gained authority,
        // commit whatever was requested while the grant was in flight.
        public void OnStateAuthorityChanged()
        {
            if (!requestingAuthority) return;
            if (Object == null || !Object.IsValid || !Object.HasStateAuthority) return;

            requestingAuthority = false;
            NetworkIsOn = pendingOn;
            if (pendingPulse)
            {
                NetworkPulseCount++;
                pendingPulse = false;
            }
        }

        public override void Render()
        {
            base.Render();

            if (!isNetworked || Object == null || !Object.IsValid) return;

            if (NetworkIsOn != lastAppliedOn)
            {
                lastAppliedOn = NetworkIsOn;
                ApplyOnState(NetworkIsOn);
            }

            if (NetworkPulseCount != lastSeenPulseCount)
            {
                lastSeenPulseCount = NetworkPulseCount;
                pointer.Pulse();
            }
        }

        private void Update()
        {
            if (requestingAuthority && Time.time - authorityRequestTime > AUTHORITY_REQUEST_TIMEOUT)
            {
                requestingAuthority = false;
                pendingPulse = false;
            }
        }

        private void ApplyOnState(bool on)
        {
            if (on) pointer.Activate();
            else pointer.Deactivate();
        }
    }
}