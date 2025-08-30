using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Fusion;

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
        private static bool addNetworkObjectToEnterTrigger = true;
        private static bool addNetworkObjectToExitTrigger = true;
        private static bool addNetworkObjectToParentTrigger = true;

        // NEW: Instance mode preferences
        private static bool enableInstanceModeForGrabbable = true;
        private static bool enableInstanceModeForThrowable = true;

        public InteractionToolsCategory()
        {
            tools = new List<CreatorTool>
            {
                new CreatorTool("🟢 Make Grabbable", "Objects can be picked up from an adjustable distance", ApplyGrabbable, true),
                new CreatorTool("🟢 Make Throwable", "Objects can be thrown around", ApplyThrowable, true),
                new CreatorTool("🟢 Make Enter Trigger", "Execute actions when player enters trigger area", ApplyEnterTrigger, true),
                new CreatorTool("🟢 Make Exit Trigger", "Execute actions when player exits trigger area", ApplyExitTrigger, true),
                new CreatorTool("🟢 Make Parent Trigger", "Player follows this object when inside trigger area (moving platforms, vehicles)", ApplyParentTrigger, true),
                new CreatorTool("🚧 Make Swimmable", "Create water volumes players can swim through", () => Debug.Log("Applied Swimmable"), true),
                new CreatorTool("🚧 Make Climbable", "Surfaces players can climb on", () => Debug.Log("Applied Climbable"), true),
                new CreatorTool("🚧 Make Kickable", "Objects can be moved with avatar feet", () => Debug.Log("Applied Kickable"), true),
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

            // NEW: Instance mode explanation
            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox("Instance Mode: Each visitor gets their own copy for collaborative experiences.\nShared Mode: Single object with authority transfer for competitive interactions.", MessageType.Info);
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

            // Add networking and instance options for tools that support them
            if (tool.title == "🟢 Make Grabbable")
            {
                EditorGUI.indentLevel++;
                addNetworkObjectToGrabbable = EditorGUILayout.Toggle("NetworkObject for multiplayer", addNetworkObjectToGrabbable);
                if (addNetworkObjectToGrabbable)
                {
                    enableInstanceModeForGrabbable = EditorGUILayout.Toggle("Enable Instance Mode", enableInstanceModeForGrabbable);
                    EditorGUILayout.HelpBox(enableInstanceModeForGrabbable ?
                        "Each visitor gets their own grabbable copy" :
                        "Single shared object with authority transfer", MessageType.None);
                }
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(5);
            }
            else if (tool.title == "🟢 Make Throwable")
            {
                EditorGUI.indentLevel++;
                addNetworkObjectToThrowable = EditorGUILayout.Toggle("NetworkObject for multiplayer", addNetworkObjectToThrowable);
                if (addNetworkObjectToThrowable)
                {
                    enableInstanceModeForThrowable = EditorGUILayout.Toggle("Enable Instance Mode", enableInstanceModeForThrowable);
                    EditorGUILayout.HelpBox(enableInstanceModeForThrowable ?
                        "Each visitor gets their own throwable copy" :
                        "Single shared object with authority transfer", MessageType.None);
                }
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

            // Add NetworkObject if requested and not already present
            if (addNetworkObjectToGrabbable && !selected.GetComponent<NetworkObject>())
            {
                var networkObject = selected.AddComponent<NetworkObject>();

                // Configure NetworkObject for the selected mode
                if (enableInstanceModeForGrabbable)
                {
                    // Instance mode: Allow State Authority Override = false (each player owns their instance)
                    // Set via reflection since these properties might not be directly accessible
                    SetNetworkObjectForInstanceMode(networkObject);
                    Debug.Log($"✅ Added NetworkObject to '{selected.name}' configured for Instance Mode");
                }
                else
                {
                    // Shared mode: Allow State Authority Override = true (authority can transfer)
                    SetNetworkObjectForSharedMode(networkObject);
                    Debug.Log($"✅ Added NetworkObject to '{selected.name}' configured for Shared Mode");
                }
            }

            // Add Instance Manager if instance mode is enabled
            if (addNetworkObjectToGrabbable && enableInstanceModeForGrabbable)
            {
                if (!selected.GetComponent<U3D.Networking.U3DInstanceManager>())
                {
                    var instanceManager = selected.AddComponent<U3D.Networking.U3DInstanceManager>();
                    // The Instance Manager will handle creating per-player copies
                    Debug.Log($"✅ Added Instance Manager to '{selected.name}' for per-player instancing");
                }
            }

            // Add grabbable component
            U3DGrabbable grabbable = selected.GetComponent<U3DGrabbable>();
            if (grabbable == null)
            {
                grabbable = selected.AddComponent<U3DGrabbable>();
                Debug.Log($"✅ Made '{selected.name}' grabbable");
            }
            else
            {
                Debug.Log($"'{selected.name}' is already grabbable");
            }

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

            // Check for grabbable components
            bool hasGrabbable = selected.GetComponent<U3DGrabbable>() != null;

            if (!hasGrabbable)
            {
                Debug.LogWarning("Object must have U3DGrabbable component first!");
                return;
            }

            // Add NetworkObject if requested and not already present
            if (addNetworkObjectToThrowable && !selected.GetComponent<NetworkObject>())
            {
                var networkObject = selected.AddComponent<NetworkObject>();

                // Configure NetworkObject for the selected mode
                if (enableInstanceModeForThrowable)
                {
                    SetNetworkObjectForInstanceMode(networkObject);
                    Debug.Log($"✅ Added NetworkObject to '{selected.name}' configured for Instance Mode");
                }
                else
                {
                    SetNetworkObjectForSharedMode(networkObject);
                    Debug.Log($"✅ Added NetworkObject to '{selected.name}' configured for Shared Mode");
                }
            }

            // Add Rigidbody (required for throwable physics) - set to sleep initially
            if (!selected.GetComponent<Rigidbody>())
            {
                Rigidbody rb = selected.AddComponent<Rigidbody>();
                rb.isKinematic = true; // Start kinematic/sleeping
                rb.useGravity = false; // Don't fall until thrown
                rb.mass = 1f;
                Debug.Log($"✅ Added sleeping Rigidbody to '{selected.name}'");
            }

            // CORRECTED: Add NetworkRigidbody3D for proper Fusion 2 physics networking
            if (selected.GetComponent<NetworkObject>() && selected.GetComponent<Rigidbody>())
            {
                // Try to find and add NetworkRigidbody3D from Physics Addon
                var networkRigidbody3DType = System.Type.GetType("Fusion.NetworkRigidbody3D, Fusion.Addons.Physics");
                if (networkRigidbody3DType != null && selected.GetComponent(networkRigidbody3DType) == null)
                {
                    selected.AddComponent(networkRigidbody3DType);
                    Debug.Log($"✅ Added NetworkRigidbody3D to '{selected.name}' for physics networking");
                }
                else if (networkRigidbody3DType == null)
                {
                    Debug.LogWarning("NetworkRigidbody3D type not found - ensure Fusion Physics Addon is installed");
                    Debug.LogWarning("Physics will sync via [Networked] properties in U3DThrowable instead");
                }
                else
                {
                    Debug.Log($"NetworkRigidbody3D already present on '{selected.name}'");
                }
            }

            // Add Instance Manager if instance mode is enabled
            if (addNetworkObjectToThrowable && enableInstanceModeForThrowable)
            {
                if (!selected.GetComponent<U3D.Networking.U3DInstanceManager>())
                {
                    var instanceManager = selected.AddComponent<U3D.Networking.U3DInstanceManager>();
                    Debug.Log($"✅ Added Instance Manager to '{selected.name}' for per-player instancing");
                }
            }

            // Add throwable component
            U3DThrowable throwable = selected.GetComponent<U3DThrowable>();
            if (throwable == null)
            {
                throwable = selected.AddComponent<U3DThrowable>();
                Debug.Log($"✅ Made '{selected.name}' throwable");
            }
            else
            {
                Debug.Log($"'{selected.name}' is already throwable");
            }

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

            // Ensure object has a trigger collider
            Collider collider = selected.GetComponent<Collider>();
            if (collider == null)
            {
                collider = selected.AddComponent<BoxCollider>();
            }
            collider.isTrigger = true;

            // Add NetworkObject if requested and not already present
            if (addNetworkObjectToEnterTrigger && !selected.GetComponent<NetworkObject>())
            {
                var networkObject = selected.AddComponent<NetworkObject>();
                SetNetworkObjectForSharedMode(networkObject); // Triggers typically use shared mode
                Debug.Log($"✅ Added NetworkObject to '{selected.name}' for multiplayer support");
            }

            // Add enter trigger component
            U3DEnterTrigger enterTrigger = selected.GetComponent<U3DEnterTrigger>();
            if (enterTrigger == null)
            {
                enterTrigger = selected.AddComponent<U3DEnterTrigger>();
                Debug.Log($"✅ Made '{selected.name}' an enter trigger - configure actions in Inspector");
            }
            else
            {
                Debug.Log($"'{selected.name}' already has enter trigger");
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

            // Ensure object has a trigger collider
            Collider collider = selected.GetComponent<Collider>();
            if (collider == null)
            {
                collider = selected.AddComponent<BoxCollider>();
            }
            collider.isTrigger = true;

            // Add NetworkObject if requested and not already present
            if (addNetworkObjectToExitTrigger && !selected.GetComponent<NetworkObject>())
            {
                var networkObject = selected.AddComponent<NetworkObject>();
                SetNetworkObjectForSharedMode(networkObject); // Triggers typically use shared mode
                Debug.Log($"✅ Added NetworkObject to '{selected.name}' for multiplayer support");
            }

            // Add exit trigger component
            U3DExitTrigger exitTrigger = selected.GetComponent<U3DExitTrigger>();
            if (exitTrigger == null)
            {
                exitTrigger = selected.AddComponent<U3DExitTrigger>();
                Debug.Log($"✅ Made '{selected.name}' an exit trigger - configure actions in Inspector");
            }
            else
            {
                Debug.Log($"'{selected.name}' already has exit trigger");
            }

            EditorUtility.SetDirty(selected);
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

            // Add NetworkObject if requested and not already present
            if (addNetworkObjectToParentTrigger && !selected.GetComponent<NetworkObject>())
            {
                var networkObject = selected.AddComponent<NetworkObject>();
                SetNetworkObjectForSharedMode(networkObject); // Parent triggers typically use shared mode
                Debug.Log($"✅ Added NetworkObject to '{selected.name}' for multiplayer support");
            }

            // Add parent trigger component
            U3DParentTrigger parentTrigger = selected.GetComponent<U3DParentTrigger>();
            if (parentTrigger == null)
            {
                parentTrigger = selected.AddComponent<U3DParentTrigger>();
                Debug.Log($"✅ Made '{selected.name}' a parent trigger - players will follow this object when inside trigger");
            }
            else
            {
                Debug.Log($"'{selected.name}' already has parent trigger");
            }

            EditorUtility.SetDirty(selected);
        }

        // Helper methods to configure NetworkObject for different modes
        private static void SetNetworkObjectForInstanceMode(NetworkObject networkObject)
        {
            // Instance mode: Each player owns their own copy, no authority transfer needed
            // Use reflection to set properties that may not be publicly accessible
            try
            {
                var allowStateAuthorityOverrideField = networkObject.GetType().GetField("_allowStateAuthorityOverride",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (allowStateAuthorityOverrideField != null)
                {
                    allowStateAuthorityOverrideField.SetValue(networkObject, false);
                }

                var destroyWhenStateAuthorityLeavesField = networkObject.GetType().GetField("_destroyWhenStateAuthorityLeaves",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (destroyWhenStateAuthorityLeavesField != null)
                {
                    destroyWhenStateAuthorityLeavesField.SetValue(networkObject, true); // Instance should be destroyed when owner leaves
                }

                Debug.Log("Configured NetworkObject for Instance Mode");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Could not configure NetworkObject properties via reflection: {e.Message}");
                Debug.LogWarning("You may need to manually configure NetworkObject settings in the Inspector");
            }
        }

        private static void SetNetworkObjectForSharedMode(NetworkObject networkObject)
        {
            // Shared mode: Allow authority transfer between players
            try
            {
                var allowStateAuthorityOverrideField = networkObject.GetType().GetField("_allowStateAuthorityOverride",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (allowStateAuthorityOverrideField != null)
                {
                    allowStateAuthorityOverrideField.SetValue(networkObject, true);
                }

                var destroyWhenStateAuthorityLeavesField = networkObject.GetType().GetField("_destroyWhenStateAuthorityLeaves",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (destroyWhenStateAuthorityLeavesField != null)
                {
                    destroyWhenStateAuthorityLeavesField.SetValue(networkObject, false); // Shared object should persist when authority leaves
                }

                Debug.Log("Configured NetworkObject for Shared Mode");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Could not configure NetworkObject properties via reflection: {e.Message}");
                Debug.LogWarning("You may need to manually configure NetworkObject settings in the Inspector");
            }
        }
    }
}