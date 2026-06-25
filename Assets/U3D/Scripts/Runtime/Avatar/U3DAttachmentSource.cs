using Fusion;
using UnityEngine;
using UnityEngine.Events;

namespace U3D
{
    /// <summary>
    /// A scene station that hands a cosmetic accessory — or a whole costume — to whoever interacts
    /// with it. Place it on a persistent scene object (a dummy visual or an empty) and assign ONE
    /// accessory prefab. The pieces of that prefab are defined by its attachment-point markers: each
    /// U3DAttachmentPoint marks one piece — the piece is the object the marker is parented to. A
    /// single accessory has one marker on the prefab root (worn as one piece); a costume has a marker
    /// inside each child (each child rides its own bone). Children with no marker are ignored. The
    /// whole prefab is worn and removed as one unit.
    ///
    /// Interacting toggles the costume on the local player: once to wear it, again to take it off,
    /// like a wardrobe stand. The accessory is never networked or spawned — U3DPlayerAttachments
    /// instantiates it locally on every client and parents each piece to its bone, the same way a
    /// steerable costume rides the player. This component carries no networked state; it only needs a
    /// NetworkObject so it has a stable id every client can resolve when rebuilding the visuals.
    ///
    /// On Wear and On Remove fire on the local player who performs the action, once per action, the
    /// same wearer-only timing as a steerable's driver-seat enter/exit events. They are for the
    /// wearer's own feedback (a sound, a message, a score) — they do NOT fire on other clients
    /// watching the costume appear, and they do not fire when re-wearing something already on (a
    /// no-op) or when an add is refused at capacity.
    ///
    /// Apply via the Creator Dashboard "Make Attachment" tool.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class U3DAttachmentSource : NetworkBehaviour, IU3DInteractable
    {
        [Tooltip("The cosmetic prefab handed to the player. Each piece is marked by a U3DAttachmentPoint (added with Add Attachment Point): a single accessory has one point on the prefab itself; a costume has a point inside each child piece, each riding its own bone. Assign a Project prefab asset, not a scene object, with no NetworkObject anywhere in it — the pieces are local cosmetics, not networked. The whole prefab is worn and removed as one unit.")]
        [SerializeField] private GameObject accessoryPrefab;

        [Tooltip("Fires on the player who wears this, the moment they put it on. Wearer-only and once per action — not when re-wearing something already on. Use for the wearer's own feedback: an equip sound, a message, a score change.")]
        [SerializeField] private UnityEvent onWear;

        [Tooltip("Fires on the player who removes this, the moment they take it off. Wearer-only and once per action. Use for the wearer's own feedback: an unequip sound, a message, a score change.")]
        [SerializeField] private UnityEvent onRemove;

        public GameObject AccessoryPrefab => accessoryPrefab;

        private U3DPlayerController _localPlayer;

        public override void Spawned()
        {
            _localPlayer = U3DPlayerController.FindLocalPlayer();
        }

        public bool CanInteract() => HasPrefab();

        private bool HasPrefab()
        {
            return accessoryPrefab != null;
        }

        public void OnInteract()
        {
            if (!HasPrefab()) return;

            if (_localPlayer == null)
                _localPlayer = U3DPlayerController.FindLocalPlayer();
            if (_localPlayer == null) return;

            U3DPlayerAttachments attachments = _localPlayer.GetComponent<U3DPlayerAttachments>();
            if (attachments == null)
            {
                Debug.LogWarning("U3DAttachmentSource: the player prefab has no U3DPlayerAttachments component. Add it to the player prefab to use attachments.");
                return;
            }

            attachments.Wear(this);
        }

        /// <summary>
        /// Fires the On Wear event. Called by U3DPlayerAttachments on the local player only, right
        /// after this source is actually added to the worn list — never on a re-wear no-op or a
        /// capacity refusal, and never on remote clients rebuilding the visual.
        /// </summary>
        public void InvokeOnWear()
        {
            onWear?.Invoke();
        }

        /// <summary>
        /// Fires the On Remove event. Called by U3DPlayerAttachments on the local player only, right
        /// after this source is actually removed from the worn list.
        /// </summary>
        public void InvokeOnRemove()
        {
            onRemove?.Invoke();
        }

        public void OnPlayerEnterRange() { }
        public void OnPlayerExitRange() { }
        public string GetInteractionPrompt() => "Wear / Remove";

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.85f);
            Gizmos.DrawWireCube(transform.position, Vector3.one * 0.3f);
        }

        private void OnValidate()
        {
            if (accessoryPrefab == null) return;

            if (accessoryPrefab.scene.IsValid())
                Debug.LogWarning($"{name}: accessory prefab '{accessoryPrefab.name}' is a scene object. Assign a Project prefab asset instead.", this);

            if (accessoryPrefab.GetComponent<NetworkObject>() != null)
                Debug.LogWarning($"{name}: accessory prefab '{accessoryPrefab.name}' has a NetworkObject. The accessory is instantiated locally as a cosmetic and must not be networked — remove the NetworkObject from the prefab.", this);

            if (accessoryPrefab.GetComponentInChildren<U3DAttachmentPoint>(true) == null)
                Debug.LogWarning($"{name}: accessory prefab '{accessoryPrefab.name}' has no attachment point. Use Add Attachment Point to mark where it sits on the avatar — without one, it won't attach.", this);
        }
    }
}