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
                new CreatorTool("🟢 Add Object Spawner", "Spawns a prefab at this location. Add NetworkObject to your prefab for all players to see it.", ApplyObjectSpawner, true),
                new CreatorTool("🟢 Make Grabbable", "Objects can be picked up from an adjustable distance", ApplyGrabbable, true),
                new CreatorTool("🟢 Make Throwable", "Objects can be thrown around", ApplyThrowable, true),
                new CreatorTool("🟢 Make Kickable", "Objects can be moved with avatar feet", ApplyKickable, true),
                new CreatorTool("🟢 Make Enter Trigger", "Execute actions when player enters trigger area", ApplyEnterTrigger, true),
                new CreatorTool("🟢 Make Exit Trigger", "Execute actions when player exits trigger area", ApplyExitTrigger, true),
                new CreatorTool("🟢 Make Climbable", "Surfaces players can climb (W=up, S=down, A/D=lateral, Space=detach)", ApplyClimbable, true),
                new CreatorTool("🚧 Make Swimmable", "Create water volumes players can swim through", () => { }, true),
                new CreatorTool("🚧 Add Seat", "Triggers avatar sit animation players can exit by resuming movement", () => { }, true),
                new CreatorTool("🚧 Make Rideable", "Players can stand on top and will be moved with the object", () => { }, true),
                new CreatorTool("🚧 Make Steerable", "Lets player controller movement steer the visual object while W and D smoothly accelerate and decelerate (wheel animations can be added manually)", () => { }, true),
                new CreatorTool("🚧 Make 1x Trigger", "Trigger that fires once", () => { }, true),
                new CreatorTool("🚧 Make Toggle", "Switch between two states", () => { }, true),
                new CreatorTool("🚧 Make Random", "Add component with list of GameObjects (audio, particles, etc.) that randomizes between them on trigger or continuously", () => { }, true),
                new CreatorTool("🚧 Make Mutually Exclusive", "Only one can be selected at a time", () => { }, true),
                new CreatorTool("🚧 Make Object Destroy Trigger", "Removes objects when triggered", () => { }, true),
                new CreatorTool("🚧 Make Object Reset Trigger", "Returns objects to starting position", () => { }, true),
                new CreatorTool("🚧 Add Player Reset Trigger", "Reset player position and state to spawn point", () => { }, true)
            };
        }

        public List<CreatorTool> GetTools() => tools;

        public void DrawCategory()
        {
            EditorGUILayout.LabelField("Interaction Tools", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Add interactive behaviors to your objects. Select an object first, then click Apply.", MessageType.Info);
            EditorGUILayout.Space(10);

            UpdateThrowableDescription();

            foreach (var tool in tools)
            {
                ProjectToolsTab.DrawCategoryTool(tool);
            }
        }

        private void UpdateThrowableDescription()
        {
            var throwableTool = tools.Find(t => t.title == "🟢 Make Throwable");
            if (throwableTool != null)
            {
                GameObject selected = Selection.activeGameObject;
                if (selected != null)
                {
                    bool hasGrabbable = selected.GetComponent<U3DGrabbable>() != null;
                    throwableTool.description = hasGrabbable
                        ? "Objects can be thrown around"
                        : "Select a Grabbable object first";
                    throwableTool.requiresSelection = true;
                }
                else
                {
                    throwableTool.description = "Select a Grabbable object first";
                    throwableTool.requiresSelection = true;
                }
            }
        }

        private static void ApplyObjectSpawner()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                // Create a new empty GameObject at the scene origin when nothing is selected
                selected = new GameObject("Object Spawner");
                Undo.RegisterCreatedObjectUndo(selected, "Add Object Spawner");
                Selection.activeGameObject = selected;
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
                Debug.LogWarning("Please select a Grabbable object first");
                return;
            }

            if (selected.GetComponent<U3DGrabbable>() == null)
            {
                Debug.LogWarning("U3DObjectSpawner: Object must have U3DGrabbable before applying Throwable.");
                return;
            }

            if (!selected.GetComponent<NetworkObject>())
            {
                var networkObject = selected.AddComponent<NetworkObject>();
                ConfigureNetworkObjectForSharedMode(networkObject);
            }

            if (!selected.GetComponent<Rigidbody>())
            {
                Rigidbody rb = selected.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.mass = 1f;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }

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

            if (!selected.GetComponent<Rigidbody>())
            {
                Rigidbody rb = selected.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.mass = 1f;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }

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
                selected.AddComponent<U3DEnterTrigger>();

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
                selected.AddComponent<U3DExitTrigger>();

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

        // ========== SHARED HELPERS ==========

        private static void ConfigureNetworkObjectForSharedMode(NetworkObject networkObject)
        {
            var so = new SerializedObject(networkObject);

            var allowOverrideProp = so.FindProperty("_allowStateAuthorityOverride");
            if (allowOverrideProp != null)
                allowOverrideProp.boolValue = true;

            var destroyOnLeaveProp = so.FindProperty("_destroyWhenStateAuthorityLeaves");
            if (destroyOnLeaveProp != null)
                destroyOnLeaveProp.boolValue = false;

            var isMasterClientProp = so.FindProperty("_isMasterClientObject");
            if (isMasterClientProp != null)
                isMasterClientProp.boolValue = false;

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
                    $"U3DClimbingController expects it to be '{U3DClimbable.CLIMBABLE_LAYER_NAME}'. " +
                    $"Update climbableLayerMask on the player prefab if using a different layer."
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