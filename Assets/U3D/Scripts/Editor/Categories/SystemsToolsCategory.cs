using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace U3D.Editor
{
    public class SystemsToolsCategory : IToolCategory
    {
        public string CategoryName => "Game Systems";
        public System.Action<int> OnRequestTabSwitch { get; set; }
        private List<CreatorTool> tools;

        private const string CORE_PREFAB_PATH = "Assets/U3D/Prefabs/U3D CORE - DO NOT DELETE.prefab";

        public SystemsToolsCategory()
        {
            tools = new List<CreatorTool>
            {
                new CreatorTool("🟢 Add U3D Core Prefab", "Required in every scene. Contains networking, deployment, player spawning, and platform systems. Will not add a duplicate.", AddCorePrefab),
                new CreatorTool("🟢 Add Quest System", "Create single-player missions and objectives for your experience", () => U3DQuestSystemTools.CreateQuestSystem()),
                new CreatorTool("🟢 Add Scorable", "Track and display a score. Add to any object with a TextMeshPro component in its hierarchy.", () => U3DScorableTools.CreateScorable()),
                new CreatorTool("🚧 Add Inventory System", "Essential for collecting, managing, and using items in exploration games", () => { }),
                new CreatorTool("🚧 Add Dialogue System", "Critical for storytelling, NPCs, and guided experiences", () => { }),
                new CreatorTool("🚧 Add Quiz System", "Interactive questions and knowledge tests", () => { }),
                new CreatorTool("🚧 Add Checkpoint System", "Save progress and restart points for complex experiences", () => { }),
                new CreatorTool("🚧 Add Achievement / Award System", "Unlock rewards and track progression", () => { }),
                new CreatorTool("🚧 Add Timer System", "Countdown timers, time limits, scheduled events", () => { }),
                new CreatorTool("🚧 Add Progress Bar", "Visual progress tracking for objectives or loading", () => { }),
            };
        }

        public List<CreatorTool> GetTools() => tools;

        public void DrawCategory()
        {
            EditorGUILayout.LabelField("Game Systems", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Add complete game systems to enhance player engagement.", MessageType.Info);
            EditorGUILayout.Space(10);

            foreach (var tool in tools)
            {
                ProjectToolsTab.DrawCategoryTool(tool);
            }
        }

        private static void AddCorePrefab()
        {
            // Check if U3D CORE already exists in the scene by looking for key components
            var existingManagers = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var mb in existingManagers)
            {
                if (mb != null && mb.gameObject.name.Contains("U3D CORE"))
                {
                    EditorUtility.DisplayDialog("U3D Core",
                        "U3D CORE is already in this scene.\n\nFound: " + mb.gameObject.name,
                        "OK");
                    Selection.activeGameObject = mb.gameObject;
                    return;
                }
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CORE_PREFAB_PATH);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("U3D Core Prefab Not Found",
                    "Could not find the U3D Core prefab at:\n" + CORE_PREFAB_PATH +
                    "\n\nMake sure the U3D template prefab has not been moved or renamed.",
                    "OK");
                return;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = Vector3.zero;
            Undo.RegisterCreatedObjectUndo(instance, "Add U3D Core");

            Selection.activeGameObject = instance;
            EditorGUIUtility.PingObject(instance);
        }
    }
}