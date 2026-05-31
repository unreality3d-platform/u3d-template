using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Fusion;
using U3D.Networking;

namespace U3D
{
    /// <summary>
    /// Trigger zone that destroys or respawns objects that enter it.
    /// Place on any trigger collider — typically an invisible zone under the world
    /// to catch fallen objects, or anywhere a creator needs a kill/reset plane.
    ///
    /// Target scope:
    ///   - Empty reference list: acts on any Rigidbody that enters (including creator custom objects)
    ///   - Populated reference list: acts only on the listed GameObjects
    ///
    /// Mode:
    ///   - Destroy: calls RequestDestroy() on U3DDestroyable if present. Otherwise, a
    ///     networked object is despawned via Runner.Despawn() when this client owns it
    ///     (non-owners skip — the owning client despawns it in its own simulation), and a
    ///     non-networked object is removed with Destroy().
    ///   - Respawn: returns U3D interactables to their original position via their
    ///     internal reset method; teleports the player to the scene spawn point;
    ///     teleports plain Rigidbody objects to their position at scene start.
    ///
    /// Note: For plain Rigidbody objects (no U3D interactable component), the original
    /// position is captured at Start(). If the object was placed above ground and
    /// dropped via gravity before entering this zone, Respawn will return it to the
    /// authored editor position, not where it landed.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class U3DTrashHandler : MonoBehaviour
    {
        public enum TrashMode
        {
            Destroy,
            Respawn
        }

        [Header("Trash Handler Configuration")]
        [Tooltip("Destroy: removes objects permanently. Respawn: returns them to their original position.")]
        [SerializeField] private TrashMode mode = TrashMode.Destroy;

        [Tooltip("Leave empty to act on any Rigidbody that enters this zone. Add specific GameObjects to restrict to those objects only.")]
        [SerializeField] private List<GameObject> targetObjects = new List<GameObject>();

        [Header("Filtering (Optional)")]
        [Tooltip("When enabled, this zone only acts on objects that carry the tag below. Set the tag to Player to make a player-only respawn zone that ignores physics objects.")]
        [SerializeField] private bool requireTag = false;

        [Tooltip("The tag an object must have for this zone to act on it. Only used when Require Tag is enabled.")]
        [SerializeField] private string requiredTag = "Player";

        [Header("Events")]
        [Tooltip("Called when an object is destroyed by this zone. Not called for player respawns.")]
        public UnityEvent OnObjectDestroyed;

        [Tooltip("Called when an object or the player is respawned by this zone.")]
        public UnityEvent OnObjectRespawned;

        // Cached original transforms for plain Rigidbody objects (no U3D interactable).
        // Populated lazily on first trigger enter for each object.
        private readonly Dictionary<GameObject, (Vector3 position, Quaternion rotation)> cachedOriginalTransforms
            = new Dictionary<GameObject, (Vector3, Quaternion)>();

        private bool isNetworked = false;

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            Rigidbody rb = other.attachedRigidbody;

            // The player moves via a CharacterController and has no Rigidbody. Anything
            // carrying a Rigidbody is a physics object — even a grabbed object parented
            // to the player — so the player check only runs when there is no Rigidbody.
            U3DPlayerController player = (rb == null) ? other.GetComponentInParent<U3DPlayerController>() : null;

            // Nothing actionable entered (no Rigidbody and not the player).
            if (rb == null && player == null) return;

            GameObject obj = player != null ? player.gameObject : rb.gameObject;

            // Optional tag filter: when enabled, act only on objects carrying the required tag.
            if (requireTag && !obj.CompareTag(requiredTag)) return;

            // Reference list filter: if populated, only act on listed objects.
            if (targetObjects.Count > 0 && !targetObjects.Contains(obj)) return;

            // Player: respawn only. A trash zone never destroys the player.
            if (player != null)
            {
                if (mode == TrashMode.Respawn)
                {
                    RespawnPlayer(player);
                }
                return;
            }

            if (mode == TrashMode.Destroy)
            {
                HandleDestroy(obj);
            }
            else
            {
                HandleRespawn(obj, rb);
            }
        }

        private void HandleDestroy(GameObject obj)
        {
            // Preferred path: U3DDestroyable owns the networked authority handshake and
            // fires its own OnDestroyed event. Works whether or not this client owns the object.
            U3DDestroyable destroyable = obj.GetComponent<U3DDestroyable>();
            if (destroyable != null)
            {
                destroyable.RequestDestroy();
                OnObjectDestroyed?.Invoke();
                return;
            }

            // Networked object without U3DDestroyable: Unity's Destroy() would corrupt
            // Fusion's state, and only the state authority may despawn it. If this client
            // owns it, despawn directly; otherwise skip — the owning client runs this same
            // trigger in its own simulation and despawns it there, replicating to everyone.
            NetworkObject netObj = obj.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                if (netObj.HasStateAuthority && netObj.Runner != null && netObj.Runner.IsRunning)
                {
                    netObj.Runner.Despawn(netObj);
                    OnObjectDestroyed?.Invoke();
                }
                return;
            }

            // Plain non-networked object.
            Destroy(obj);
            OnObjectDestroyed?.Invoke();
        }

        private void HandleRespawn(GameObject obj, Rigidbody rb)
        {
            // A held object is in the player's hand, not lost. Teleporting it here while
            // it's still parented to the hand bone bakes a fixed offset into its local
            // position, so it floats off the hand instead of staying held. Leave held
            // objects alone — the player throws or drops it to send it through respawn
            // as a loose object.
            U3DGrabbable heldCheck = obj.GetComponent<U3DGrabbable>();
            if (heldCheck != null && heldCheck.IsGrabbed) return;

            // U3D interactables: delegate to their internal reset methods.
            // Check each type — an object can have multiple (e.g. Grabbable + Throwable).
            bool handledByU3D = false;

            U3DThrowable throwable = obj.GetComponent<U3DThrowable>();
            if (throwable != null)
            {
                throwable.ResetToSpawn();
                handledByU3D = true;
            }

            // Throwable owns the physics reset when present — skip Kickable/Pushable
            // physics reset on the same object to avoid double-reset conflicts.
            if (!handledByU3D)
            {
                U3DKickable kickable = obj.GetComponent<U3DKickable>();
                if (kickable != null)
                {
                    kickable.PutToSleep();
                    kickable.transform.position = kickable.OriginalPosition;
                    kickable.transform.rotation = kickable.OriginalRotation;
                    handledByU3D = true;
                }

                U3DPushable pushable = obj.GetComponent<U3DPushable>();
                if (pushable != null)
                {
                    pushable.PutToSleep();
                    pushable.transform.position = pushable.OriginalPosition;
                    pushable.transform.rotation = pushable.OriginalRotation;
                    handledByU3D = true;
                }
            }

            // Grabbable: release if grabbed, then reset. Only runs if no Throwable
            // handled it (Throwable owns the post-release lifecycle when both are present).
            if (!handledByU3D)
            {
                U3DGrabbable grabbable = obj.GetComponent<U3DGrabbable>();
                if (grabbable != null)
                {
                    grabbable.ResetToSpawn();
                    handledByU3D = true;
                }
            }

            if (handledByU3D)
            {
                OnObjectRespawned?.Invoke();
                return;
            }

            // Plain Rigidbody — teleport to cached original transform.
            if (!cachedOriginalTransforms.ContainsKey(obj))
            {
                // First time we've seen this object — cache its current position now.
                // This will be where it was when it first entered this zone, which for
                // most static objects equals their authored editor position.
                cachedOriginalTransforms[obj] = (obj.transform.position, obj.transform.rotation);
                Debug.LogWarning($"U3DTrashHandler: '{obj.name}' has no U3D interactable component. Original position was not captured at scene start — caching current position as fallback.");
            }

            var original = cachedOriginalTransforms[obj];
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            obj.transform.position = original.position;
            obj.transform.rotation = original.rotation;
            rb.isKinematic = false;

            OnObjectRespawned?.Invoke();
        }

        private void RespawnPlayer(U3DPlayerController player)
        {
            // In Shared Mode every client runs this trigger locally. Only the client
            // that owns this player may move it — otherwise a non-owning client fights
            // the network sync on a player it doesn't control.
            NetworkObject netObj = player.GetComponent<NetworkObject>();
            if (netObj != null && !netObj.HasStateAuthority) return;

            if (U3DPlayerSpawner.Instance == null)
            {
                Debug.LogWarning("U3DTrashHandler: Cannot respawn player — U3DPlayerSpawner instance not found.");
                return;
            }

            (Vector3 spawnPosition, Quaternion spawnRotation) = U3DPlayerSpawner.Instance.GetSpawnData();

            // Route through the controller's own move/rotate API rather than writing the
            // transform directly. SetPosition handles the CharacterController toggle, detaches
            // from any rideable, resets velocity, and syncs NetworkPosition; SetRotation syncs
            // NetworkRotation and the internal cameraYaw so the view doesn't swing to the old
            // heading on the first step after respawn.
            player.SetPosition(spawnPosition);
            player.SetRotation(spawnRotation.eulerAngles.y);

            OnObjectRespawned?.Invoke();
        }

        /// <summary>
        /// Pre-register an object's original transform so Respawn mode has an
        /// accurate starting position even for objects that drop under gravity
        /// before they could enter this zone. Call this from scene setup code
        /// if needed for plain Rigidbody objects with startActive-style behaviour.
        /// </summary>
        public void RegisterOriginalTransform(GameObject obj)
        {
            if (obj == null) return;
            if (!cachedOriginalTransforms.ContainsKey(obj))
            {
                cachedOriginalTransforms[obj] = (obj.transform.position, obj.transform.rotation);
            }
        }

        public TrashMode Mode => mode;
        public int TargetCount => targetObjects.Count;
    }
}