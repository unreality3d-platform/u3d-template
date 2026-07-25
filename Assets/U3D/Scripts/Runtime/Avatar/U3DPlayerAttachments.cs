using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace U3D
{
    /// <summary>
    /// Per-player cosmetic attachment manager. Sibling NetworkBehaviour on the player prefab,
    /// alongside U3DPlayerController and U3DAvatarManager. Holds the networked set of attachment
    /// sources this player is wearing and rebuilds the cosmetic visuals locally on every client.
    ///
    /// Pattern mirrors U3DSteerable: nothing networked is spawned. The accessory is a plain,
    /// non-networked prefab instantiated locally on each client and parented to the avatar's bones,
    /// so it rides the player's already-networked position and animation with no extra sync. The only
    /// networked state is which sources are active (a list of scene-object references). Each client
    /// reads this player's list in Render and reconciles its local instances to match.
    ///
    /// A source carries ONE prefab. The pieces of that prefab are defined by its attachment-point
    /// markers: each U3DAttachmentPoint marks one piece — the piece is the object the marker is
    /// parented to. A single accessory (a helmet) has one marker on the prefab root, so the whole
    /// prefab is one piece on one bone. A costume has a marker inside each child (hat, glove, glove),
    /// so each child rides its own bone and the now-empty container root is discarded. A child with
    /// no marker is not a piece — it's ignored. The whole source still occupies ONE entry in the worn
    /// list, so its pieces share one on/off state and can't drift apart.
    ///
    /// Toggle is driven from U3DAttachmentSource.OnInteract on the local player: interacting once
    /// adds the source to the list (worn), interacting again removes it (taken off). A source with
    /// a non-empty Slot replaces anything worn from another source sharing that slot; unslotted
    /// sources stack freely. Only the local player writes its own list, so there is no authority
    /// contention.
    ///
    /// Visibility follows the avatar's own rules: each built instance's renderers are registered with
    /// U3DAvatarManager, which toggles them in lockstep with the body — no special-casing.
    /// </summary>
    [RequireComponent(typeof(U3DAvatarManager))]
    public class U3DPlayerAttachments : NetworkBehaviour
    {
        // Maximum simultaneous sources per player. Fixed network cost regardless of usage, so
        // keep it to the smallest count that covers expected wear. Each source may itself be a
        // multi-piece costume, so this caps sources, not individual accessory pieces.
        public const int MAX_ATTACHMENTS = 8;

        [Networked, Capacity(MAX_ATTACHMENTS)]
        private NetworkLinkedList<NetworkBehaviourId> ActiveAttachments => default;

        // Per-source record. One source carries one prefab whose markers define its pieces, so a
        // source builds N local piece instances but stays ONE entry in the networked worn list.
        // Renderers are aggregated across the pieces so the whole costume registers with the avatar's
        // visibility rules as a single unit. HeadRenderers is the subset riding the head bone (or a
        // socket under it); the avatar manager renders those shadow-only for the wearer in VR first
        // person so a face-covering piece can't blind them.
        private struct Built
        {
            public NetworkBehaviourId Id;
            public GameObject[] Instances;
            public Renderer[] Renderers;
            public Renderer[] HeadRenderers;
        }

        private readonly List<Built> _built = new List<Built>();
        private U3DAvatarManager _avatarManager;

        public override void Spawned()
        {
            _avatarManager = GetComponent<U3DAvatarManager>();
        }

        /// <summary>
        /// Toggles the given source on this player. Interacting with a station whose item is
        /// already worn takes it off (firing its On Remove); otherwise the item goes on (firing
        /// its On Wear). If the source has a non-empty Slot, anything worn from another station
        /// with the same slot is removed first — same-role items replace each other, while
        /// unslotted items stack freely. Removals preserve the order of what remains, so
        /// RemoveLast still takes items off newest-first. Call only on the local player (it has
        /// state authority over its own object); a stray remote call is ignored. At capacity,
        /// additions are refused silently and no event fires.
        /// </summary>
        public void Wear(U3DAttachmentSource source)
        {
            if (source == null) return;
            if (Object == null || !Object.HasStateAuthority) return;

            NetworkBehaviourId id = source.Id;
            var list = ActiveAttachments;

            // Toggle off: the wardrobe-stand behavior. Interacting again with something
            // you're wearing takes it off.
            if (list.Contains(id))
            {
                list.Remove(id);
                source.InvokeOnRemove();
                return;
            }

            // Slot replacement: remove anything worn that shares this source's non-empty
            // slot. Walked by index from the end so removals don't disturb unvisited
            // entries; removes every match, which also converges any duplicates worn
            // before slot rules existed. A worn entry whose station can't be resolved is
            // left alone.
            string slot = NormalizeSlot(source.Slot);
            if (slot != null && Runner != null)
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    NetworkBehaviourId wornId = list[i];
                    if (wornId == default) continue;
                    if (!Runner.TryFindBehaviour(wornId, out U3DAttachmentSource worn) || worn == null) continue;

                    if (NormalizeSlot(worn.Slot) == slot)
                    {
                        list.Remove(wornId);
                        worn.InvokeOnRemove();
                    }
                }
            }

            if (list.Count < MAX_ATTACHMENTS)
            {
                list.Add(id);
                source.InvokeOnWear();
            }
        }

        /// <summary>
        /// Takes off the most recently worn source. Reads the last entry in the worn list (kept in
        /// wear order, newest last) and removes it, firing that source's On Remove; the next call
        /// takes off the new last entry, and so on, like undo. Local-authority only, like Wear.
        /// No-op when nothing is worn. Works from anywhere — it does not require returning to a
        /// source.
        /// </summary>
        public void RemoveLast()
        {
            if (Object == null || !Object.HasStateAuthority) return;

            var list = ActiveAttachments;
            if (list.Count == 0) return;

            NetworkBehaviourId last = list[list.Count - 1];
            list.Remove(last);

            if (Runner != null && Runner.TryFindBehaviour(last, out U3DAttachmentSource source) && source != null)
                source.InvokeOnRemove();
        }

        /// <summary>
        /// Canonical form of a slot name for comparison: null when the slot is empty or
        /// whitespace (meaning "stacks with everything"), otherwise trimmed and lowercased so
        /// creator typing differences ("Head" vs "head ") don't split a slot in two.
        /// </summary>
        private static string NormalizeSlot(string slot)
        {
            if (string.IsNullOrWhiteSpace(slot)) return null;
            return slot.Trim().ToLowerInvariant();
        }

        public override void Render()
        {
            ReconcileAttachments();
        }

        /// <summary>
        /// Brings the local cosmetic instances in line with the networked worn set. Runs on every
        /// client for both the local player and remote players — the build path is identical, only
        /// the source of the list differs (the local player wrote its own; remotes read theirs).
        /// Destroys instances whose source left the set, builds instances for sources newly in it.
        /// A source whose pieces can't be built yet (avatar still initializing) is simply retried
        /// next frame — nothing is instantiated until the rig is ready, so there is no churn and no
        /// partial costume.
        /// </summary>
        private void ReconcileAttachments()
        {
            for (int i = _built.Count - 1; i >= 0; i--)
            {
                if (!ActiveAttachments.Contains(_built[i].Id))
                {
                    DestroyBuilt(_built[i]);
                    _built.RemoveAt(i);
                }
            }

            foreach (NetworkBehaviourId id in ActiveAttachments)
            {
                if (id == default) continue;
                if (IsBuilt(id)) continue;

                if (TryBuildAttachment(id, out Built built))
                    _built.Add(built);
            }
        }

        private bool IsBuilt(NetworkBehaviourId id)
        {
            for (int i = 0; i < _built.Count; i++)
                if (_built[i].Id.Equals(id)) return true;
            return false;
        }

        /// <summary>
        /// Builds a source's accessory. The source carries ONE prefab whose attachment-point markers
        /// define its pieces: each marker marks one piece — the piece is the object the marker is
        /// parented to. A single accessory has one marker on the prefab root (the whole prefab is one
        /// piece); a costume has a marker inside each child (each child is a piece). Children with no
        /// marker are ignored.
        ///
        /// Two failure modes are kept apart on purpose. While the avatar's rig is still initializing,
        /// the build waits — it instantiates nothing and returns false so the caller retries next
        /// frame, so a costume never appears half-on during load. Once the rig is ready, any marker
        /// that still can't resolve its bone (a bone the rig lacks, an unset bone, a missing override
        /// name) is skipped for good; the pieces that resolve are built and the build is committed so
        /// it never retries forever. A prefab with no markers builds nothing and commits, so an
        /// unconfigured accessory simply doesn't appear rather than looping.
        ///
        /// Pieces whose bone is the head bone or anything under it are additionally collected as head
        /// renderers and registered separately: the avatar manager renders them shadow-only for the
        /// wearer in VR first person, so a face-covering piece never blocks their own view while
        /// everyone else still sees it.
        /// </summary>
        private bool TryBuildAttachment(NetworkBehaviourId id, out Built built)
        {
            built = default;

            if (Runner == null) return false;
            if (!Runner.TryFindBehaviour(id, out U3DAttachmentSource source) || source == null) return false;

            GameObject prefab = source.AccessoryPrefab;
            if (prefab == null) return false;

            // Markers are static in the prefab, so read them from the asset without instantiating.
            U3DAttachmentPoint[] prefabMarkers = prefab.GetComponentsInChildren<U3DAttachmentPoint>(true);

            // No markers anywhere → nothing is a piece. Commit an empty build so we don't retry
            // forever waiting for pieces that will never come.
            if (prefabMarkers.Length == 0)
            {
                built = new Built { Id = id, Instances = new GameObject[0], Renderers = new Renderer[0], HeadRenderers = new Renderer[0] };
                return true;
            }

            // Readiness gate (transient). The avatar must be instantiated. If any marker targets a
            // Humanoid bone (no name override), the rig must also be reporting humanoid — otherwise
            // it may still be initializing. Until ready, build nothing and retry. (A genuinely
            // non-humanoid avatar never reports humanoid, so a humanoid-only accessory keeps retrying
            // on it; that pairing is unusual and the accessory has no bone to land on regardless.)
            GameObject avatarInstance = _avatarManager != null ? _avatarManager.GetAvatarInstance() : null;
            if (avatarInstance == null) return false;

            Animator animator = _avatarManager.GetAvatarAnimator();

            bool anyHumanoidMarker = false;
            for (int i = 0; i < prefabMarkers.Length; i++)
            {
                U3DAttachmentPoint m = prefabMarkers[i];
                if (string.IsNullOrEmpty(m.BoneNameOverride) && m.TargetBone != HumanBodyBones.LastBone)
                {
                    anyHumanoidMarker = true;
                    break;
                }
            }
            if (anyHumanoidMarker && (animator == null || !animator.isHuman)) return false;

            // Rig is ready. Resolve each marker's bone. A null result now is permanent (a bone the
            // rig lacks, an unset bone, a missing override name) — that piece is skipped silently.
            var resolvedBones = new Transform[prefabMarkers.Length];
            var resolvedByRole = new bool[prefabMarkers.Length];
            bool anyResolved = false;
            for (int i = 0; i < prefabMarkers.Length; i++)
            {
                Transform bone = ResolveBone(prefabMarkers[i], out resolvedByRole[i]);
                resolvedBones[i] = bone;
                if (bone != null) anyResolved = true;
            }

            // Every marker failed to resolve — nothing to attach. Commit empty so we don't loop.
            if (!anyResolved)
            {
                built = new Built { Id = id, Instances = new GameObject[0], Renderers = new Renderer[0], HeadRenderers = new Renderer[0] };
                return true;
            }

            // At least one piece resolves. Instantiate the prefab and map each prefab marker to its
            // counterpart on the instance. GetComponentsInChildren walks an identical hierarchy in
            // the same order, so instance marker i matches prefab marker i.
            GameObject instance = Instantiate(prefab);
            Transform instRoot = instance.transform;
            U3DAttachmentPoint[] instMarkers = instance.GetComponentsInChildren<U3DAttachmentPoint>(true);

            // Defensive: an order/count mismatch should never happen for an identical hierarchy, but
            // if it did, attaching to the wrong bones would be worse than nothing. Commit empty.
            if (instMarkers.Length != prefabMarkers.Length)
            {
                Destroy(instance);
                built = new Built { Id = id, Instances = new GameObject[0], Renderers = new Renderer[0], HeadRenderers = new Renderer[0] };
                return true;
            }

            // Fallback reference when no neutral-pose data exists for a bone: the avatar's current
            // facing, read once (the pre-neutral-capture behavior).
            Quaternion avatarFacing = avatarInstance.transform.rotation;

            // Reference for the head-piece check below. Null on a non-humanoid rig — override-socket
            // pieces on such rigs can't be classified, so none count as head pieces there.
            Transform headBone = (animator != null && animator.isHuman)
                ? animator.GetBoneTransform(HumanBodyBones.Head)
                : null;

            var handledPieces = new HashSet<Transform>();
            var instances = new List<GameObject>();
            var allRenderers = new List<Renderer>();
            var headRenderers = new List<Renderer>();
            bool rootIsPiece = false;

            for (int i = 0; i < instMarkers.Length; i++)
            {
                Transform bone = resolvedBones[i];
                if (bone == null) continue; // skipped marker

                U3DAttachmentPoint marker = instMarkers[i];
                Transform piece = marker.transform.parent;
                if (piece == null) continue; // marker with no parent — not a valid piece

                // Two markers under one piece would otherwise reparent and re-align it twice; the
                // first marker wins, later ones for the same piece are ignored.
                if (handledPieces.Contains(piece)) continue;
                handledPieces.Add(piece);

                if (piece == instRoot) rootIsPiece = true;

                Transform m = marker.transform;

                // Parent with worldPositionStays = false so the piece inherits the bone's scale (a
                // hat on a scaled-up avatar scales with it). Then align: orientation from the
                // neutral-stance frame, position from the bone.
                piece.SetParent(bone, false);

                // Orient against the bone's NEUTRAL-STANCE frame, not its animated rotation at
                // this instant. bone.rotation * Inverse(neutralRel) is the world frame in which
                // the marker's authored forward faces the avatar's forward whenever the bone is
                // in the neutral pose — so the baked local orientation is a constant, and wearing
                // the accessory mid-fly, mid-jump, or mid-swing lands identically to wearing it
                // at idle. Never the bone's own axes, which point in arbitrary per-rig directions.
                // Falls back to the avatar's current facing when no neutral data exists (override
                // sockets, non-humanoid rigs, capture failure) — the previous behavior.
                Quaternion targetMarkerWorld = avatarFacing;
                if (resolvedByRole[i]
                    && _avatarManager.TryGetNeutralBoneRotation(prefabMarkers[i].TargetBone, out Quaternion neutralRel))
                {
                    targetMarkerWorld = bone.rotation * Quaternion.Inverse(neutralRel);
                }

                piece.rotation = (targetMarkerWorld * Quaternion.Inverse(m.rotation)) * piece.rotation;

                // Position read after the rotation, because rotating the piece carries its marker
                // with it.
                piece.position += bone.position - m.position;

                instances.Add(piece.gameObject);

                Renderer[] pieceRenderers = piece.GetComponentsInChildren<Renderer>(true);
                allRenderers.AddRange(pieceRenderers);

                // IsChildOf is true for the head bone itself and anything nested under it, so this
                // covers pieces on the head and on custom sockets inside the head.
                if (headBone != null && bone.IsChildOf(headBone))
                    headRenderers.AddRange(pieceRenderers);
            }

            // If the prefab root was itself a piece (a single accessory, marker on the root), it has
            // been reparented onto a bone and is in our instance list — keep it. Otherwise the root is
            // just an empty container now that its child pieces have been handed to their bones, so
            // discard it; this also drops any unmarked children, which are not pieces.
            if (!rootIsPiece)
                Destroy(instance);

            Renderer[] renderers = allRenderers.ToArray();
            Renderer[] headRendererArray = headRenderers.ToArray();
            if (_avatarManager != null)
            {
                _avatarManager.RegisterAttachmentRenderers(renderers);
                if (headRendererArray.Length > 0)
                    _avatarManager.RegisterHeadAttachmentRenderers(headRendererArray);
            }

            built = new Built { Id = id, Instances = instances.ToArray(), Renderers = renderers, HeadRenderers = headRendererArray };
            return true;
        }

        /// <summary>
        /// Resolves the target bone on this player's equipped avatar. Honors an optional exact bone-
        /// name override first (for non-humanoid rigs or custom sockets), then the Humanoid role.
        /// Returns null when the avatar isn't ready, isn't humanoid, or the rig has no such bone.
        /// resolvedByRole reports whether the Humanoid role produced the result — only role-resolved
        /// bones have neutral-pose data, so override-named sockets keep the pose-dependent alignment.
        /// </summary>
        private Transform ResolveBone(U3DAttachmentPoint marker, out bool resolvedByRole)
        {
            resolvedByRole = false;
            if (_avatarManager == null) return null;

            if (!string.IsNullOrEmpty(marker.BoneNameOverride))
            {
                GameObject avatarInstance = _avatarManager.GetAvatarInstance();
                if (avatarInstance != null)
                {
                    Transform[] all = avatarInstance.GetComponentsInChildren<Transform>(true);
                    for (int i = 0; i < all.Length; i++)
                        if (all[i].name == marker.BoneNameOverride)
                            return all[i];
                }
            }

            Animator animator = _avatarManager.GetAvatarAnimator();
            if (animator == null || !animator.isHuman) return null;
            if (marker.TargetBone == HumanBodyBones.LastBone) return null;

            Transform roleBone = animator.GetBoneTransform(marker.TargetBone);
            resolvedByRole = roleBone != null;
            return roleBone;
        }

        private void DestroyBuilt(Built built)
        {
            if (_avatarManager != null)
            {
                if (built.Renderers != null)
                    _avatarManager.UnregisterAttachmentRenderers(built.Renderers);
                if (built.HeadRenderers != null && built.HeadRenderers.Length > 0)
                    _avatarManager.UnregisterHeadAttachmentRenderers(built.HeadRenderers);
            }

            if (built.Instances != null)
            {
                for (int i = 0; i < built.Instances.Length; i++)
                    if (built.Instances[i] != null)
                        Destroy(built.Instances[i]);
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            for (int i = 0; i < _built.Count; i++)
                DestroyBuilt(_built[i]);
            _built.Clear();

            base.Despawned(runner, hasState);
        }
    }
}