using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Fusion;
using Fusion.Addons.Physics;

namespace U3D.Editor
{
    public class InteractionToolsCategory : IToolCategory
    {
        public string CategoryName => "Interactions";
        public System.Action<int> OnRequestTabSwitch { get; set; }
        private List<CreatorTool> tools;

        public InteractionToolsCategory()
        {
            tools = new List<CreatorTool>
            {
                new CreatorTool("🟢 Make Object Spawner", "Spawns a prefab at this location. Enable 'Networked Spawn' on the component to have all players see the spawned object — your prefab must have a NetworkObject for that to work.", ApplyObjectSpawner, true),
                new CreatorTool("🟢 Make Grabbable", "Objects can be picked up from an adjustable distance. Released objects float in place — use Make Throwable with 'Drop On Release' for gravity drop.", ApplyGrabbable, true),
                new CreatorTool("🟢 Make Throwable", "Objects can be picked up and thrown", ApplyThrowable, true),
                new CreatorTool("🟢 Make Kickable", "Objects can be moved with avatar feet", ApplyKickable, true),
                new CreatorTool("🟢 Make Pushable", "Objects can be pushed along surfaces by walking into them", ApplyPushable, true),
                new CreatorTool("🟢 Make Climbable", "Surfaces players can climb (W=up, S=down, A/D=lateral, Space=detach)", ApplyClimbable, true),
                new CreatorTool("🟢 Make Swimmable", "Players swim through this trigger volume — full 3D movement, no gravity. Add a Box, Sphere, or Mesh collider sized to your water.", ApplySwimmable, true),
                new CreatorTool("🟢 Make Enter Trigger", "Execute actions when player enters trigger area", ApplyEnterTrigger, true),
                new CreatorTool("🟢 Make Exit Trigger", "Execute actions when player exits trigger area", ApplyExitTrigger, true),
                new CreatorTool("🟢 Make Interact Trigger", "Execute actions when player interacts with this object (Interact key or mouse click)", ApplyInteractTrigger, true),
                new CreatorTool("🟢 Make On Interact Collectable", "Players press the Interact key nearby to pick this up into their Inventory. Stays solid, so it can still block the player. Pairs with the Inventory in Game Systems.", U3DInventoryTools.ApplyInteractCollectable, true),
                new CreatorTool("🟢 Make On Enter Collectable", "Players pick this up by walking into it — for pass-through items like coins or gems. Becomes a trigger, so it won't block the player. Pairs with the Inventory in Game Systems.", U3DInventoryTools.ApplyEnterCollectable, true),new CreatorTool("🟢 Make Trigger Zone", "Fire events when zone goes from empty to occupied, and when it clears", ApplyTriggerZone, true),
                new CreatorTool("🟢 Make Delayed Trigger Activation", "Disables a trigger's collider briefly at scene start so OnTriggerEnter only fires on real entries, not on scene-load overlap. Use on triggers that start with an animated object already inside.", ApplyDelayedTriggerActivation, true),
                new CreatorTool("🚧 Make Random", "Add component with list of GameObjects (audio, particles, etc.) that randomizes between them on trigger or continuously", () => { }, true),
                new CreatorTool("🚧 Make Mutually Exclusive", "Only one can be selected at a time", () => { }, true),
                new CreatorTool("🚧 Add Player Reset Trigger", "Reset player position and state to spawn point", () => { }, true),
                // ── Movement ──
                new CreatorTool("🚧 Add Seat", "Triggers avatar sit animation players can exit by resuming movement", () => { }, true),
                new CreatorTool("🟢 Make Rideable", "Players can stand on top and will be moved with the object", ApplyMakeRideable, true),
                new CreatorTool("🚧 Make Steerable", "Lets player controller movement steer the visual object while W and D smoothly accelerate and decelerate (wheel animations can be added manually)", () => { }, true),
                new CreatorTool("🚧 Add Scene Portal", "Portal to load a different scene", () => { }, true),
                new CreatorTool("🚧 Add 1-Way Portal", "Portal for one-direction travel within scene", () => { }, true),
                new CreatorTool("🚧 Add 2-Way Portal", "Portal for bi-directional travel within scene", () => { }, true),
            };
        }

        public List<CreatorTool> GetTools() => tools;

        public void DrawCategory()
        {
            EditorGUILayout.LabelField("Interaction Tools", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Add interactive behaviors to your objects. Select an object first, then click Apply.", MessageType.Info);
            EditorGUILayout.Space(10);

            bool inMovementSection = false;

            foreach (var tool in tools)
            {
                if (!inMovementSection && IsMovementTool(tool.title))
                {
                    inMovementSection = true;
                    EditorGUILayout.Space(6);
                    EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
                    GUIStyle movementHeaderStyle = new GUIStyle(EditorStyles.label)
                    {
                        fontStyle = FontStyle.BoldAndItalic,
                        alignment = TextAnchor.MiddleCenter
                    };
                    EditorGUILayout.LabelField("Movement", movementHeaderStyle);
                    EditorGUILayout.Space(4);
                }

                ProjectToolsTab.DrawCategoryTool(tool);
            }
        }

        private static bool IsMovementTool(string title)
        {
            return title == "🚧 Add Seat"
                || title == "🟢 Make Rideable"
                || title == "🚧 Make Steerable"
                || title == "🚧 Add Scene Portal"
                || title == "🚧 Add 1-Way Portal"
                || title == "🚧 Add 2-Way Portal";
        }

        private static void ApplyObjectSpawner()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("Please select an object first");
                return;
            }

            if (!selected.GetComponent<NetworkObject>())
            {
                var networkObject = selected.AddComponent<NetworkObject>();
                ConfigureNetworkObjectForSharedMode(networkObject);
            }

            if (selected.GetComponent<U3DObjectSpawner>() == null)
                selected.AddComponent<U3DObjectSpawner>();

            EditorUtility.SetDirty(selected);
        }

        private static void ApplyGrabbable()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("Please select an object first");
                return;
            }

            if (!selected.GetComponent<Collider>())
                selected.AddComponent<BoxCollider>();

            if (!selected.GetComponent<NetworkObject>())
            {
                var networkObject = selected.AddComponent<NetworkObject>();
                ConfigureNetworkObjectForSharedMode(networkObject);
            }

            if (selected.GetComponent<U3DGrabbable>() == null)
                selected.AddComponent<U3DGrabbable>();

            EditorUtility.SetDirty(selected);
        }

        private static void ApplyThrowable()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("Please select an object first");
                return;
            }

            if (!selected.GetComponent<Collider>())
                selected.AddComponent<BoxCollider>();

            if (selected.GetComponent<U3DGrabbable>() == null)
                selected.AddComponent<U3DGrabbable>();

            if (!selected.GetComponent<NetworkObject>())
            {
                var networkObject = selected.AddComponent<NetworkObject>();
                ConfigureNetworkObjectForSharedMode(networkObject);
            }

            Rigidbody rb = selected.GetComponent<Rigidbody>();
            if (rb == null)
                rb = selected.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.mass = 1f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            if (selected.GetComponent<NetworkObject>() && selected.GetComponent<Rigidbody>())
            {
                try
                {
#if FUSION_ADDONS_PHYSICS
                    if (!selected.GetComponent<NetworkRigidbody3D>())
                    {
                        var networkRigidbody = selected.AddComponent<NetworkRigidbody3D>();
                        ConfigureNetworkRigidbody3DForSharedMode(networkRigidbody);
                    }
#else
                    var networkRigidbody3DType = System.Type.GetType(
                        "Fusion.Addons.Physics.NetworkRigidbody3D, Fusion.Addons.Physics"
                    );
                    if (networkRigidbody3DType != null && selected.GetComponent(networkRigidbody3DType) == null)
                    {
                        var networkRigidbody = selected.AddComponent(networkRigidbody3DType) as Component;
                        ConfigureNetworkRigidbody3DViaReflection(networkRigidbody);
                    }
#endif
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Error adding NetworkRigidbody3D: {ex.Message}");
                }
            }

            if (selected.GetComponent<U3DThrowable>() == null)
                selected.AddComponent<U3DThrowable>();

            EditorUtility.SetDirty(selected);
        }

        private static void ApplyKickable()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("Please select an object first");
                return;
            }

            if (!selected.GetComponent<Collider>())
                selected.AddComponent<BoxCollider>();

            if (!selected.GetComponent<NetworkObject>())
            {
                var networkObject = selected.AddComponent<NetworkObject>();
                ConfigureNetworkObjectForSharedMode(networkObject);
            }

            Rigidbody rb = selected.GetComponent<Rigidbody>();
            if (rb == null)
                rb = selected.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.mass = 1f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            if (selected.GetComponent<NetworkObject>() && selected.GetComponent<Rigidbody>())
            {
                try
                {
#if FUSION_ADDONS_PHYSICS
                    if (!selected.GetComponent<NetworkRigidbody3D>())
                    {
                        var networkRigidbody = selected.AddComponent<NetworkRigidbody3D>();
                        ConfigureNetworkRigidbody3DForSharedMode(networkRigidbody);
                    }
#else
                    var networkRigidbody3DType = System.Type.GetType(
                        "Fusion.Addons.Physics.NetworkRigidbody3D, Fusion.Addons.Physics"
                    );
                    if (networkRigidbody3DType != null && selected.GetComponent(networkRigidbody3DType) == null)
                    {
                        var networkRigidbody = selected.AddComponent(networkRigidbody3DType) as Component;
                        ConfigureNetworkRigidbody3DViaReflection(networkRigidbody);
                    }
#endif
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Error adding NetworkRigidbody3D: {ex.Message}");
                }
            }

            if (selected.GetComponent<U3DKickable>() == null)
                selected.AddComponent<U3DKickable>();

            EditorUtility.SetDirty(selected);
        }

        private static void ApplyPushable()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("Please select an object first");
                return;
            }

            if (!selected.GetComponent<Collider>())
                selected.AddComponent<BoxCollider>();

            if (!selected.GetComponent<NetworkObject>())
            {
                var networkObject = selected.AddComponent<NetworkObject>();
                ConfigureNetworkObjectForSharedMode(networkObject);
            }

            Rigidbody rb = selected.GetComponent<Rigidbody>();
            if (rb == null)
                rb = selected.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.mass = 5f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            if (selected.GetComponent<NetworkObject>() && selected.GetComponent<Rigidbody>())
            {
                try
                {
#if FUSION_ADDONS_PHYSICS
                    if (!selected.GetComponent<NetworkRigidbody3D>())
                    {
                        var networkRigidbody = selected.AddComponent<NetworkRigidbody3D>();
                        ConfigureNetworkRigidbody3DForSharedMode(networkRigidbody);
                    }
#else
                    var networkRigidbody3DType = System.Type.GetType(
                        "Fusion.Addons.Physics.NetworkRigidbody3D, Fusion.Addons.Physics"
                    );
                    if (networkRigidbody3DType != null && selected.GetComponent(networkRigidbody3DType) == null)
                    {
                        var networkRigidbody = selected.AddComponent(networkRigidbody3DType) as Component;
                        ConfigureNetworkRigidbody3DViaReflection(networkRigidbody);
                    }
#endif
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Error adding NetworkRigidbody3D: {ex.Message}");
                }
            }

            if (selected.GetComponent<U3DPushable>() == null)
                selected.AddComponent<U3DPushable>();

            EditorUtility.SetDirty(selected);
        }

        private static void AddTrashHandler()
        {
            GameObject go = new GameObject("Trash Handler");

            BoxCollider col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(10f, 1f, 10f);

            go.AddComponent<U3DTrashHandler>();

            Undo.RegisterCreatedObjectUndo(go, "Add Trash Handler");
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
        }

        private static void ApplyEnterTrigger()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("Please select an object first");
                return;
            }

            Collider collider = selected.GetComponent<Collider>();
            if (collider == null)
                collider = selected.AddComponent<BoxCollider>();
            collider.isTrigger = true;

            if (!selected.GetComponent<NetworkObject>())
            {
                var networkObject = selected.AddComponent<NetworkObject>();
                ConfigureNetworkObjectForSharedMode(networkObject);
            }

            if (selected.GetComponent<U3DEnterTrigger>() == null)
            {
                selected.AddComponent<U3DEnterTrigger>();
            }
            else
            {
                Debug.Log(
                    $"'{selected.name}' already has a U3D Enter Trigger. " +
                    $"To add a second trigger that fires on a different tag, use the Inspector's Add Component button " +
                    $"and search for 'U3D Enter Trigger'. Each trigger can have its own Required Tag and Events."
                );
            }

            EditorUtility.SetDirty(selected);
        }

        private static void ApplyExitTrigger()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("Please select an object first");
                return;
            }

            Collider collider = selected.GetComponent<Collider>();
            if (collider == null)
                collider = selected.AddComponent<BoxCollider>();
            collider.isTrigger = true;

            if (!selected.GetComponent<NetworkObject>())
            {
                var networkObject = selected.AddComponent<NetworkObject>();
                ConfigureNetworkObjectForSharedMode(networkObject);
            }

            if (selected.GetComponent<U3DExitTrigger>() == null)
            {
                selected.AddComponent<U3DExitTrigger>();
            }
            else
            {
                Debug.Log(
                    $"'{selected.name}' already has a U3D Exit Trigger. " +
                    $"To add a second trigger that fires on a different tag, use the Inspector's Add Component button " +
                    $"and search for 'U3D Exit Trigger'. Each trigger can have its own Required Tag and Events."
                );
            }

            EditorUtility.SetDirty(selected);
        }

        private static void ApplyInteractTrigger()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("Please select an object first");
                return;
            }

            Collider collider = selected.GetComponent<Collider>();
            if (collider == null)
                selected.AddComponent<BoxCollider>();

            if (!selected.GetComponent<NetworkObject>())
            {
                var networkObject = selected.AddComponent<NetworkObject>();
                ConfigureNetworkObjectForSharedMode(networkObject);
            }

            if (selected.GetComponent<U3DInteractTrigger>() == null)
            {
                selected.AddComponent<U3DInteractTrigger>();
            }
            else
            {
                Debug.Log(
                    $"'{selected.name}' already has a U3D Interact Trigger. " +
                    $"To add a second trigger that fires on a different tag, use the Inspector's Add Component button " +
                    $"and search for 'U3D Interact Trigger'. Each trigger can have its own Required Tag and Events."
                );
            }

            EditorUtility.SetDirty(selected);
        }

        private static void ApplyTriggerZone()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("Please select an object first");
                return;
            }

            Collider collider = selected.GetComponent<Collider>();
            if (collider == null)
                collider = selected.AddComponent<BoxCollider>();
            collider.isTrigger = true;

            if (!selected.GetComponent<NetworkObject>())
            {
                var networkObject = selected.AddComponent<NetworkObject>();
                ConfigureNetworkObjectForSharedMode(networkObject);
            }

            if (selected.GetComponent<U3DTriggerZone>() == null)
            {
                selected.AddComponent<U3DTriggerZone>();
            }
            else
            {
                Debug.Log(
                    $"'{selected.name}' already has a U3D Trigger Zone. " +
                    $"To add a second zone that fires on a different tag, use the Inspector's Add Component button " +
                    $"and search for 'U3D Trigger Zone'. Each zone can have its own Required Tag and Events."
                );
            }

            EditorUtility.SetDirty(selected);
        }

        private static void ApplyDelayedTriggerActivation()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("Please select an object first");
                return;
            }

            Collider collider = selected.GetComponent<Collider>();
            if (collider == null)
            {
                Debug.LogWarning("Make Delayed Trigger Activation: selected object has no Collider. Add a trigger first (Make Enter Trigger, Make Exit Trigger, or Make Trigger Zone) before applying this.");
                return;
            }

            if (selected.GetComponent<U3DDelayedTriggerActivation>() == null)
                selected.AddComponent<U3DDelayedTriggerActivation>();

            EditorUtility.SetDirty(selected);
        }

        private static void ApplyClimbable()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("Please select an object first");
                return;
            }

            EnsureClimbableLayerExists();

            if (!selected.GetComponent<Collider>())
                selected.AddComponent<BoxCollider>();

            SetLayerRecursive(selected, U3DClimbable.CLIMBABLE_LAYER);

            if (selected.GetComponent<U3DClimbable>() == null)
                selected.AddComponent<U3DClimbable>();

            EditorUtility.SetDirty(selected);
        }

        private static void ApplySwimmable()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("Please select an object first");
                return;
            }

            Collider collider = selected.GetComponent<Collider>();
            if (collider == null)
                collider = selected.AddComponent<BoxCollider>();
            collider.isTrigger = true;

            if (!selected.GetComponent<NetworkObject>())
            {
                var networkObject = selected.AddComponent<NetworkObject>();
                ConfigureNetworkObjectForSharedMode(networkObject);
            }

            if (selected.GetComponent<U3DSwimmable>() == null)
            {
                selected.AddComponent<U3DSwimmable>();
            }
            else
            {
                Debug.Log(
                    $"'{selected.name}' already has a U3D Swimmable. " +
                    $"To add a second swimmable that fires on a different tag, use the Inspector's Add Component button " +
                    $"and search for 'U3D Swimmable'. Each swimmable can have its own Required Tag and Events."
                );
            }

            EditorUtility.SetDirty(selected);
        }

        private static void ApplyMakeRideable()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("Please select an object first");
                return;
            }

            MakeRideableSetup(selected);
        }

        private static void MakeRideableSetup(GameObject selected)
        {
            Undo.RecordObject(selected, "Make Rideable");

            if (selected.GetComponent<U3DRideableController>() == null)
                selected.AddComponent<U3DRideableController>();

            NetworkObject networkObject = selected.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                networkObject = selected.AddComponent<NetworkObject>();
            }
            // Always reconfigure flags so re-running on an existing rideable upgrades
            // it from the old shared-mode flag to the rideable-specific flag.
            ConfigureNetworkObjectForRideable(networkObject);

            // NetworkTransform replicates the platform's transform from the master client
            // to all other clients, so every player sees the platform at the same world
            // position each tick. Without this, each client runs the platform's movement
            // on its own clock and players see the platform out of sync.
            NetworkTransform networkTransform = selected.GetComponent<NetworkTransform>();
            if (networkTransform == null)
            {
                networkTransform = selected.AddComponent<NetworkTransform>();
            }
            ConfigureNetworkTransformForRideable(networkTransform);

            // Idempotent trigger child — find by name if it already exists, otherwise create.
            Transform existingTrigger = selected.transform.Find("RideableTrigger");
            if (existingTrigger == null)
            {
                GameObject triggerZoneGO = new GameObject("RideableTrigger");
                Undo.RegisterCreatedObjectUndo(triggerZoneGO, "Make Rideable");
                triggerZoneGO.transform.SetParent(selected.transform, false);
                triggerZoneGO.transform.localPosition = Vector3.zero;

                var triggerCollider = triggerZoneGO.AddComponent<BoxCollider>();
                triggerCollider.isTrigger = true;
                triggerCollider.center = new Vector3(0f, 1f, 0f);
                triggerCollider.size = new Vector3(1f, 3f, 1f);

                triggerZoneGO.AddComponent<U3DRideableTrigger>();
            }

            // Idempotent first waypoint — only create if no Waypoint_0 already exists in
            // the scene. Avoids duplicating the creator's existing waypoints when they
            // re-run the tool to upgrade an existing rideable.
            GameObject existingWaypoint = GameObject.Find("Waypoint_0");
            if (existingWaypoint == null)
            {
                GameObject waypointGO = new GameObject("Waypoint_0");
                Undo.RegisterCreatedObjectUndo(waypointGO, "Make Rideable");
                waypointGO.transform.position = selected.transform.position;
            }

            EditorUtility.SetDirty(selected);
            Selection.activeGameObject = selected;
        }

        // ========== SHARED HELPERS ==========

        internal static void ConfigureNetworkObjectForSharedMode(NetworkObject networkObject)
        {
            var so = new SerializedObject(networkObject);
            var flagsProp = so.FindProperty("Flags");
            if (flagsProp != null)
            {
                flagsProp.intValue = (int)(
                    NetworkObjectFlags.AllowStateAuthorityOverride
                );
                so.ApplyModifiedProperties();
            }
            else
            {
                Debug.LogWarning("Could not find Flags property on NetworkObject — Shared Mode flags not configured");
            }
        }

        /// <summary>
        /// Configures a NetworkObject for the Rideable use case in Shared Mode:
        /// MasterClientObject so the master client owns the platform's authority
        /// deterministically, plus AllowStateAuthorityOverride so authority can
        /// transfer cleanly when the master client changes.
        /// </summary>
        /// <summary>
        /// Configures a NetworkObject for the Rideable use case in Shared Mode:
        /// MasterClientObject so the master client owns the platform's authority
        /// for the entire session, with no per-interaction authority churn.
        /// Authority auto-transfers to the new master client if the current one leaves.
        /// Different from physics objects (balls, kickables) which use
        /// AllowStateAuthorityOverride so authority can transfer to whoever interacts
        /// with them. The rideable is environment, not a player-claimed object.
        /// </summary>
        private static void ConfigureNetworkObjectForRideable(NetworkObject networkObject)
        {
            var so = new SerializedObject(networkObject);
            var flagsProp = so.FindProperty("Flags");
            if (flagsProp != null)
            {
                flagsProp.intValue = (int)NetworkObjectFlags.MasterClientObject;
                so.ApplyModifiedProperties();
            }
            else
            {
                Debug.LogWarning("Could not find Flags property on NetworkObject — Rideable flags not configured");
            }
        }

        /// <summary>
        /// Configures a NetworkTransform for the Rideable use case:
        /// SyncParent disabled (the platform has no networked parent — riders parent
        /// to it locally, which is a one-way relationship handled by the player
        /// controller, not by the platform's NetworkTransform).
        /// SyncScale disabled (we don't change rideable scale at runtime).
        /// Position and rotation are always synced — that's NetworkTransform's
        /// fundamental purpose and isn't toggled.
        /// Follows the same SerializedObject + FindProperty pattern as
        /// ConfigureNetworkRigidbody3DForSharedMode for consistency.
        /// If field names don't match, FindProperty silently returns null and the
        /// configurator is a no-op — which is safe because Fusion 2's defaults for
        /// these properties are already what we want (off/off).
        /// </summary>
        private static void ConfigureNetworkTransformForRideable(NetworkTransform networkTransform)
        {
            var so = new SerializedObject(networkTransform);

            var syncParentProp = so.FindProperty("_syncParent");
            if (syncParentProp != null)
                syncParentProp.boolValue = false;

            var syncScaleProp = so.FindProperty("_syncScale");
            if (syncScaleProp != null)
                syncScaleProp.boolValue = false;

            so.ApplyModifiedProperties();
        }

#if FUSION_ADDONS_PHYSICS
        private static void ConfigureNetworkRigidbody3DForSharedMode(NetworkRigidbody3D networkRigidbody)
        {
            var so = new SerializedObject(networkRigidbody);

            var syncParentProp = so.FindProperty("_syncParent");
            if (syncParentProp != null)
                syncParentProp.boolValue = false;

            var syncModeProp = so.FindProperty("_syncMode");
            if (syncModeProp != null)
                syncModeProp.intValue = 0;

            so.ApplyModifiedProperties();
        }
#endif

        private static void ConfigureNetworkRigidbody3DViaReflection(Component networkRigidbody)
        {
            if (networkRigidbody == null) return;
            var so = new SerializedObject(networkRigidbody);

            var syncParentProp = so.FindProperty("_syncParent");
            if (syncParentProp != null)
                syncParentProp.boolValue = false;

            var syncModeProp = so.FindProperty("_syncMode");
            if (syncModeProp != null)
                syncModeProp.intValue = 0;

            so.ApplyModifiedProperties();
        }

        private static void EnsureClimbableLayerExists()
        {
            int layer = LayerMask.NameToLayer(U3DClimbable.CLIMBABLE_LAYER_NAME);
            if (layer == -1)
            {
                Debug.LogWarning(
                    $"Layer '{U3DClimbable.CLIMBABLE_LAYER_NAME}' not found in project settings. " +
                    $"U3DClimbable uses layer {U3DClimbable.CLIMBABLE_LAYER} ('{U3DClimbable.CLIMBABLE_LAYER_NAME}') for organization. " +
                    $"Add this layer in Edit > Project Settings > Tags and Layers."
                );
            }
        }

        private static void SetLayerRecursive(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
                SetLayerRecursive(child.gameObject, layer);
        }
    }
}