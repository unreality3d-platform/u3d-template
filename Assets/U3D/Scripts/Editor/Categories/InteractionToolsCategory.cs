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

        // Networking preferences
        private static bool addNetworkObjectToGrabbable = true;
        private static bool addNetworkObjectToThrowable = true;
        private static bool addNetworkObjectToKickable = true;
        private static bool addNetworkObjectToEnterTrigger = true;
        private static bool addNetworkObjectToExitTrigger = true;
        private static bool addNetworkObjectToParentTrigger = true;

        public InteractionToolsCategory()
        {
            tools = new List<CreatorTool>
            {
                new CreatorTool("🟢 Make Grabbable", "Objects can be picked up from an adjustable distance", ApplyGrabbable, true),
                new CreatorTool("🟢 Make Throwable", "Objects can be thrown around", ApplyThrowable, true),
                new CreatorTool("🟢 Make Kickable", "Objects can be moved with avatar feet", ApplyKickable, true),
                new CreatorTool("🟢 Make Enter Trigger", "Execute actions when player enters trigger area", ApplyEnterTrigger, true),
                new CreatorTool("🟢 Make Exit Trigger", "Execute actions when player exits trigger area", ApplyExitTrigger, true),
                new CreatorTool("🟢 Make Parent Trigger", "Player follows this object when inside trigger area (moving platforms, vehicles)", ApplyParentTrigger, true),
                new CreatorTool("🚧 Make Swimmable", "Create water volumes players can swim through", () => Debug.Log("Applied Swimmable"), true),
                new CreatorTool("🚧 Make Climbable", "Surfaces players can climb on", () => Debug.Log("Applied Climbable"), true),
                new CreatorTool("🚧 Add Seat", "Triggers avatar sit animation players can exit by resuming movement", () => Debug.Log("Applied Seat"), true),
                new CreatorTool("🚧 Make Rideable", "Players can stand on top and will be moved with the object", () => Debug.Log("Applied Rideable"), true),
                new CreatorTool("🚧 Make Steerable", "Lets player controller movement steer the visual object while W and D smoothly accelerate and decelerate (wheel animations can be added manually)", () => Debug.Log("Applied Steerable"), true),
                new CreatorTool("🚧 Make 1x Trigger", "Trigger that fires once", () => Debug.Log("Applied 1x Trigger"), true),
                new CreatorTool("🚧 Make Toggle", "Switch between two states", () => Debug.Log("Applied Toggle"), true),
                new CreatorTool("🚧 Make Random", "Add component with list of GameObjects (audio, particles, etc.) that randomizes between them on trigger or continuously", () => Debug.Log("Applied Random"), true),
                new CreatorTool("🚧 Make Mutually Exclusive", "Only one can be selected at a time", () => Debug.Log("Applied Mutually Exclusive"), true),
                new CreatorTool("🚧 Make Object Destroy Trigger", "Removes objects when triggered", () => Debug.Log("Applied Object Destroy Trigger"), true),
                new CreatorTool("🚧 Make Object Reset Trigger", "Returns objects to starting position", () => Debug.Log("Applied Object Reset Trigger"), true),
                new CreatorTool("🚧 Add Player Reset Trigger", "Reset player position and state to spawn point", () => Debug.Log("Applied Player Reset Trigger"), true)
            };
        }

        public List<CreatorTool> GetTools() => tools;

        public void DrawCategory()
        {
            EditorGUILayout.LabelField("Interaction Tools", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Add interactive behaviors to your objects. Select an object first, then click Apply.", MessageType.Info);
            EditorGUILayout.Space(10);

            // Update the description for Make Throwable based on selection
            UpdateThrowableDescription();

            foreach (var tool in tools)
            {
                DrawToolWithNetworkingOption(tool);
            }
        }

        private void DrawToolWithNetworkingOption(CreatorTool tool)
        {
            // Draw the main tool UI
            ProjectToolsTab.DrawCategoryTool(tool);

            // Add networking checkbox for tools that support networking
            if (tool.title == "🟢 Make Grabbable")
            {
                EditorGUI.indentLevel++;
                addNetworkObjectToGrabbable = EditorGUILayout.Toggle("NetworkObject for multiplayer", addNetworkObjectToGrabbable);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(5);
            }
            else if (tool.title == "🟢 Make Throwable")
            {
                EditorGUI.indentLevel++;
                addNetworkObjectToThrowable = EditorGUILayout.Toggle("NetworkObject for multiplayer", addNetworkObjectToThrowable);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(5);
            }
            else if (tool.title == "🟢 Make Kickable")
            {
                EditorGUI.indentLevel++;
                addNetworkObjectToKickable = EditorGUILayout.Toggle("NetworkObject for multiplayer", addNetworkObjectToKickable);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(5);
            }
            else if (tool.title == "🟢 Make Enter Trigger")
            {
                EditorGUI.indentLevel++;
                addNetworkObjectToEnterTrigger = EditorGUILayout.Toggle("NetworkObject for multiplayer", addNetworkObjectToEnterTrigger);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(5);
            }
            else if (tool.title == "🟢 Make Exit Trigger")
            {
                EditorGUI.indentLevel++;
                addNetworkObjectToExitTrigger = EditorGUILayout.Toggle("NetworkObject for multiplayer", addNetworkObjectToExitTrigger);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(5);
            }
            else if (tool.title == "🟢 Make Parent Trigger")
            {
                EditorGUI.indentLevel++;
                addNetworkObjectToParentTrigger = EditorGUILayout.Toggle("NetworkObject for multiplayer", addNetworkObjectToParentTrigger);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(5);
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

                    if (!hasGrabbable)
                    {
                        throwableTool.description = "Select a Grabbable object";
                        throwableTool.requiresSelection = true;
                    }
                    else
                    {
                        throwableTool.description = "Objects can be thrown around";
                        throwableTool.requiresSelection = true;
                    }
                }
                else
                {
                    throwableTool.description = "Select a Grabbable object";
                    throwableTool.requiresSelection = true;
                }
            }
        }

        // FIXED: Unified NetworkObject configuration for Shared Mode
        private static void ConfigureNetworkObjectForSharedMode(NetworkObject networkObject)
        {
            var so = new SerializedObject(networkObject);

            // CRITICAL: Enable Allow State Authority Override for authority transfer
            var allowOverrideProp = so.FindProperty("_allowStateAuthorityOverride");
            if (allowOverrideProp != null)
            {
                allowOverrideProp.boolValue = true;
            }

            // CRITICAL: Disable Destroy When State Authority Leaves (objects persist)
            var destroyOnLeaveProp = so.FindProperty("_destroyWhenStateAuthorityLeaves");
            if (destroyOnLeaveProp != null)
            {
                destroyOnLeaveProp.boolValue = false;
            }

            // CRITICAL: Disable Is Master Client Object (any player can grab)
            var isMasterClientProp = so.FindProperty("_isMasterClientObject");
            if (isMasterClientProp != null)
            {
                isMasterClientProp.boolValue = false;
            }

            so.ApplyModifiedProperties();

            Debug.Log($"Configured NetworkObject for Shared Mode: AllowOverride=true, DestroyOnLeave=false, MasterClient=false");
        }

        private static void ApplyGrabbable()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("Please select an object first");
                return;
            }

            // Only add collider - no Rigidbody for grabbables
            if (!selected.GetComponent<Collider>())
            {
                selected.AddComponent<BoxCollider>();
            }

            // Add and configure NetworkObject if requested and not already present
            if (addNetworkObjectToGrabbable && !selected.GetComponent<NetworkObject>())
            {
                var networkObject = selected.AddComponent<NetworkObject>();
                ConfigureNetworkObjectForSharedMode(networkObject);
            }

            // Add grabbable component
            U3DGrabbable grabbable = selected.GetComponent<U3DGrabbable>();
            if (grabbable == null)
            {
                grabbable = selected.AddComponent<U3DGrabbable>();
            }

            EditorUtility.SetDirty(selected);
            Debug.Log($"Applied Grabbable to {selected.name}");
        }

        private static void ApplyThrowable()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("Please select a Grabbable object first");
                return;
            }

            // Check for grabbable components
            bool hasGrabbable = selected.GetComponent<U3DGrabbable>() != null;

            if (!hasGrabbable)
            {
                Debug.LogWarning("Object must have U3DGrabbable component first!");
                return;
            }

            // Add and configure NetworkObject if requested and not already present
            if (addNetworkObjectToThrowable && !selected.GetComponent<NetworkObject>())
            {
                var networkObject = selected.AddComponent<NetworkObject>();
                ConfigureNetworkObjectForSharedMode(networkObject);
            }

            // Add Rigidbody (required for throwable physics)
            if (!selected.GetComponent<Rigidbody>())
            {
                Rigidbody rb = selected.AddComponent<Rigidbody>();
                rb.isKinematic = true; // Start kinematic/sleeping
                rb.useGravity = false; // Don't fall until thrown
                rb.mass = 1f;
            }

            // Add NetworkRigidbody3D for proper Fusion 2 physics networking
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
                    // Fallback: reflection in case addon not installed
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

            // Add throwable component
            U3DThrowable throwable = selected.GetComponent<U3DThrowable>();
            if (throwable == null)
            {
                throwable = selected.AddComponent<U3DThrowable>();
            }

            EditorUtility.SetDirty(selected);
            Debug.Log($"Applied Throwable to {selected.name}");
        }

        private static void ApplyKickable()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("Please select an object first");
                return;
            }

            // Ensure object has a collider
            if (!selected.GetComponent<Collider>())
            {
                selected.AddComponent<BoxCollider>();
            }

            // Add and configure NetworkObject if requested and not already present
            if (addNetworkObjectToKickable && !selected.GetComponent<NetworkObject>())
            {
                var networkObject = selected.AddComponent<NetworkObject>();
                ConfigureNetworkObjectForSharedMode(networkObject);
            }

            // Add Rigidbody (required for kickable physics)
            if (!selected.GetComponent<Rigidbody>())
            {
                Rigidbody rb = selected.AddComponent<Rigidbody>();
                rb.isKinematic = true; // Start kinematic/sleeping
                rb.useGravity = false; // Don't fall until kicked
                rb.mass = 1f;
            }

            // Add NetworkRigidbody3D for proper Fusion 2 physics networking
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
                    // Fallback: reflection in case addon not installed
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

            // Add kickable component
            U3DKickable kickable = selected.GetComponent<U3DKickable>();
            if (kickable == null)
            {
                kickable = selected.AddComponent<U3DKickable>();
            }

            EditorUtility.SetDirty(selected);
            Debug.Log($"Applied Kickable to {selected.name}");
        }

        // FIXED: Consistent NetworkRigidbody3D configuration for Shared Mode
        private static void ConfigureNetworkRigidbody3DForSharedMode(NetworkRigidbody3D networkRigidbody)
        {
            var so = new SerializedObject(networkRigidbody);

            // CRITICAL: Disable SyncParent to prevent conflicts with grab parenting
            var syncParentProp = so.FindProperty("_syncParent");
            if (syncParentProp != null)
            {
                syncParentProp.boolValue = false;
            }

            // Set sync mode for multiplayer physics
            var syncModeProp = so.FindProperty("_syncMode");
            if (syncModeProp != null)
            {
                syncModeProp.intValue = 1; // SyncRigidbody mode for physics objects
            }

            // Enable scale synchronization for consistency
            var syncScaleProp = so.FindProperty("_syncScale");
            if (syncScaleProp != null)
            {
                syncScaleProp.boolValue = true;
            }

            // CRITICAL: Leave InterpolationTarget as null (let Fusion 2 handle)
            var interpolationTargetProp = so.FindProperty("_interpolationTarget");
            if (interpolationTargetProp != null)
            {
                interpolationTargetProp.objectReferenceValue = null;
            }

            so.ApplyModifiedProperties();

            Debug.Log("Configured NetworkRigidbody3D for Shared Mode: SyncParent=false, SyncMode=SyncRigidbody, InterpolationTarget=null");
        }

        private static void ConfigureNetworkRigidbody3DViaReflection(Component networkRigidbody)
        {
            if (networkRigidbody == null) return;

            var so = new SerializedObject(networkRigidbody);

            // CRITICAL: Disable SyncParent via reflection
            var syncParentProp = so.FindProperty("_syncParent");
            if (syncParentProp != null)
            {
                syncParentProp.boolValue = false;
            }

            var syncModeProp = so.FindProperty("_syncMode");
            if (syncModeProp != null)
            {
                syncModeProp.intValue = 1; // SyncRigidbody mode
            }

            var syncScaleProp = so.FindProperty("_syncScale");
            if (syncScaleProp != null)
            {
                syncScaleProp.boolValue = true;
            }

            var interpolationTargetProp = so.FindProperty("_interpolationTarget");
            if (interpolationTargetProp != null)
            {
                interpolationTargetProp.objectReferenceValue = null;
            }

            so.ApplyModifiedProperties();

            Debug.Log("Configured NetworkRigidbody3D via reflection for Shared Mode");
        }

        private static void ApplyEnterTrigger()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("Please select an object first");
                return;
            }

            // Ensure object has a trigger collider
            Collider collider = selected.GetComponent<Collider>();
            if (collider == null)
            {
                collider = selected.AddComponent<BoxCollider>();
            }
            collider.isTrigger = true;

            // Add and configure NetworkObject if requested and not already present
            if (addNetworkObjectToEnterTrigger && !selected.GetComponent<NetworkObject>())
            {
                var networkObject = selected.AddComponent<NetworkObject>();
                ConfigureNetworkObjectForSharedMode(networkObject);
            }

            // Add enter trigger component
            U3DEnterTrigger enterTrigger = selected.GetComponent<U3DEnterTrigger>();
            if (enterTrigger == null)
            {
                enterTrigger = selected.AddComponent<U3DEnterTrigger>();
            }

            EditorUtility.SetDirty(selected);
            Debug.Log($"Applied Enter Trigger to {selected.name}");
        }

        private static void ApplyExitTrigger()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("Please select an object first");
                return;
            }

            // Ensure object has a trigger collider
            Collider collider = selected.GetComponent<Collider>();
            if (collider == null)
            {
                collider = selected.AddComponent<BoxCollider>();
            }
            collider.isTrigger = true;

            // Add and configure NetworkObject if requested and not already present
            if (addNetworkObjectToExitTrigger && !selected.GetComponent<NetworkObject>())
            {
                var networkObject = selected.AddComponent<NetworkObject>();
                ConfigureNetworkObjectForSharedMode(networkObject);
            }

            // Add exit trigger component
            U3DExitTrigger exitTrigger = selected.GetComponent<U3DExitTrigger>();
            if (exitTrigger == null)
            {
                exitTrigger = selected.AddComponent<U3DExitTrigger>();
            }

            EditorUtility.SetDirty(selected);
            Debug.Log($"Applied Exit Trigger to {selected.name}");
        }

        private static void ApplyParentTrigger()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("Please select an object first");
                return;
            }

            // Ensure object has a trigger collider
            Collider collider = selected.GetComponent<Collider>();
            if (collider == null)
            {
                collider = selected.AddComponent<BoxCollider>();
            }
            collider.isTrigger = true;

            // Add and configure NetworkObject if requested and not already present
            if (addNetworkObjectToParentTrigger && !selected.GetComponent<NetworkObject>())
            {
                var networkObject = selected.AddComponent<NetworkObject>();
                ConfigureNetworkObjectForSharedMode(networkObject);
            }

            // Add parent trigger component
            U3DParentTrigger parentTrigger = selected.GetComponent<U3DParentTrigger>();
            if (parentTrigger == null)
            {
                parentTrigger = selected.AddComponent<U3DParentTrigger>();
            }

            EditorUtility.SetDirty(selected);
            Debug.Log($"Applied Parent Trigger to {selected.name}");
        }
    }
}