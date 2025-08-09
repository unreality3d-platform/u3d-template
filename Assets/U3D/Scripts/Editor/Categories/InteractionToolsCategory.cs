using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

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
                new CreatorTool("Make Grabbable Near", "Objects can be picked up when close", ApplyGrabbableNear, true),
                new CreatorTool("Make Grabbable Far", "Objects can be picked up from distance", ApplyGrabbableFar, true),
                new CreatorTool("Make Throwable", "Objects can be thrown around", ApplyThrowable, true),
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
                ProjectToolsTab.DrawCategoryTool(tool);
            }
        }

        private void UpdateThrowableDescription()
        {
            var throwableTool = tools.Find(t => t.title == "Make Throwable");
            if (throwableTool != null)
            {
                GameObject selected = Selection.activeGameObject;
                if (selected != null)
                {
                    bool hasGrabbable = selected.GetComponent<U3DGrabbableNear>() != null ||
                                       selected.GetComponent<U3DGrabbableFar>() != null;

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

        private static void ApplyGrabbableNear()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("Please select an object first");
                return;
            }

            // Ensure required components
            if (!selected.GetComponent<Rigidbody>())
            {
                Rigidbody rb = selected.AddComponent<Rigidbody>();
                rb.useGravity = true;
                rb.mass = 1f;
            }

            if (!selected.GetComponent<Collider>())
            {
                // Add appropriate collider based on object type
                MeshRenderer meshRenderer = selected.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    selected.AddComponent<BoxCollider>();
                }
                else
                {
                    selected.AddComponent<BoxCollider>();
                }
            }

            // Add grabbable component
            U3DGrabbableNear grabbable = selected.GetComponent<U3DGrabbableNear>();
            if (grabbable == null)
            {
                grabbable = selected.AddComponent<U3DGrabbableNear>();
                Debug.Log($"✅ Made '{selected.name}' grabbable when near");
            }
            else
            {
                Debug.Log($"'{selected.name}' is already grabbable near");
            }

            // Mark as dirty for saving
            EditorUtility.SetDirty(selected);
        }

        private static void ApplyGrabbableFar()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("Please select an object first");
                return;
            }

            // Ensure required components
            if (!selected.GetComponent<Rigidbody>())
            {
                Rigidbody rb = selected.AddComponent<Rigidbody>();
                rb.useGravity = true;
                rb.mass = 1f;
            }

            if (!selected.GetComponent<Collider>())
            {
                // Add appropriate collider based on object type
                MeshRenderer meshRenderer = selected.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    selected.AddComponent<BoxCollider>();
                }
                else
                {
                    selected.AddComponent<BoxCollider>();
                }
            }

            // Add grabbable component
            U3DGrabbableFar grabbable = selected.GetComponent<U3DGrabbableFar>();
            if (grabbable == null)
            {
                grabbable = selected.AddComponent<U3DGrabbableFar>();
                Debug.Log($"✅ Made '{selected.name}' grabbable from distance");
            }
            else
            {
                Debug.Log($"'{selected.name}' is already grabbable from distance");
            }

            // Mark as dirty for saving
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
            bool hasGrabbable = selected.GetComponent<U3DGrabbableNear>() != null ||
                               selected.GetComponent<U3DGrabbableFar>() != null;

            if (!hasGrabbable)
            {
                Debug.LogWarning("Object must have U3DGrabbableNear or U3DGrabbableFar component first!");
                return;
            }

            // Ensure Rigidbody exists
            if (!selected.GetComponent<Rigidbody>())
            {
                Rigidbody rb = selected.AddComponent<Rigidbody>();
                rb.useGravity = true;
                rb.mass = 1f;
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

            // Mark as dirty for saving
            EditorUtility.SetDirty(selected);
        }
    }
}