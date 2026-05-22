using UnityEngine;
using UnityEngine.Events;
using Fusion;

namespace U3D
{
    /// <summary>
    /// Self-contained swimmable water volume. Add via Creator Dashboard "Make Swimmable" button.
    /// Place a trigger collider of any shape (Box, Sphere, Mesh — including concave meshes)
    /// sized to where you want swimming to engage. The local player swims while inside the
    /// trigger and stops swimming when leaving.
    ///
    /// Swimming reuses the player's flying locomotion model: full 3D camera-aligned movement,
    /// gravity disabled, Space/Crouch for vertical. The IsSwimming animator flag drives swim
    /// animations instead of fly animations.
    ///
    /// Two responsibilities, handled separately:
    ///  - Locomotion: each client engages swimming for its OWN local player when that player
    ///    enters this volume. No authority involvement — every client runs this independently.
    ///  - Events: OnEnterWater / OnExitWater fire on the state-authority client, matching the
    ///    U3DEnterTrigger / U3DExitTrigger pattern. Use these for splashes, audio, score, etc.
    ///    Effects wired to these events must use network-aware mechanisms (RPC, networked
    ///    state) to be visible to other players.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class U3DSwimmable : NetworkBehaviour
    {
        [Header("Trigger Configuration")]
        [Tooltip("Only fire events for objects with a specific tag")]
        [SerializeField] private bool requireTag = false;

        [Tooltip("Tag required to fire this trigger's events")]
        [SerializeField] private string requiredTag = "Player";

        [Header("Events")]
        [Tooltip("Fires on the authority client when a qualifying object enters the water")]
        public UnityEvent OnEnterWater;

        [Tooltip("Fires on the authority client when a qualifying object exits the water")]
        public UnityEvent OnExitWater;

        private bool isNetworked = false;
        private U3DPlayerController engagedLocalPlayer;

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
            isNetworked = GetComponent<NetworkObject>() != null;
        }

        private void OnTriggerEnter(Collider other)
        {
            // Locomotion side: each client engages swimming for its own local player.
            // Runs on every client independently, no authority check.
            U3DPlayerController player = other.GetComponentInParent<U3DPlayerController>();
            if (player != null && player.IsLocalPlayer && engagedLocalPlayer != player)
            {
                engagedLocalPlayer = player;
                player.SetSwimmingState(true);
            }

            // Events side: authority guard MUST come first, before any [Networked]
            // property access (none here today, but matches the trigger family pattern
            // and keeps the guard placement consistent if networked state is added later).
            if (isNetworked && (Object == null || !Object.HasStateAuthority))
                return;

            if (requireTag && !other.CompareTag(requiredTag))
                return;

            OnEnterWater?.Invoke();
        }

        private void OnTriggerExit(Collider other)
        {
            // Locomotion side: each client disengages swimming for its own local player.
            U3DPlayerController player = other.GetComponentInParent<U3DPlayerController>();
            if (player != null && player.IsLocalPlayer && engagedLocalPlayer == player)
            {
                engagedLocalPlayer = null;
                player.SetSwimmingState(false);
            }

            // Events side: authority guard first, then tag filter.
            if (isNetworked && (Object == null || !Object.HasStateAuthority))
                return;

            if (requireTag && !other.CompareTag(requiredTag))
                return;

            OnExitWater?.Invoke();
        }

        private void OnDisable()
        {
            // Clear swimming state if the local player is currently engaged. Matches
            // Climbable's Detach-on-disable — a disabled water volume must not leave
            // the player stuck swimming.
            if (engagedLocalPlayer != null && engagedLocalPlayer.IsLocalPlayer)
            {
                engagedLocalPlayer.SetSwimmingState(false);
            }
            engagedLocalPlayer = null;
        }

        public override void Spawned()
        {
            if (!isNetworked) return;
        }
    }
}