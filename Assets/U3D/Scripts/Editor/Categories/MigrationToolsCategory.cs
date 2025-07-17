using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace U3D.Editor
{
    public class MigrationToolsCategory : IToolCategory
    {
        public string CategoryName => "Asset Cleanup";
        public System.Action<int> OnRequestTabSwitch { get; set; }
        private List<CreatorTool> tools;

        public MigrationToolsCategory()
        {
            tools = new List<CreatorTool>
            {
                // Missing Script Tools
                new CreatorTool("🔧 Replace Missing Scripts", "Replace missing script references with placeholder components to prevent errors while retaining visual reminders", AssetCleanupTools.ReplaceMissingScriptsWithPlaceholders),
                new CreatorTool("🧼 Remove Script Placeholders", "Remove placeholder components added by the Replace Missing Scripts tool", AssetCleanupTools.RemovePlaceholderComponents),
                new CreatorTool("🗑️ Clean Missing Scripts from Scene", "Remove missing script components directly from all GameObjects in loaded scenes", AssetCleanupTools.RemoveMissingScriptsFromScene),
                new CreatorTool("🧹 Clean Missing Scripts from Prefabs", "Remove missing script components from prefabs in selected folder", AssetCleanupTools.CleanPrefabsInFolder),
                
                // Missing Reference Tools
                new CreatorTool("🔍 Replace Missing References", "Detect missing object references in components and add placeholder tracking components", AssetCleanupTools.ReplaceMissingReferencesWithPlaceholders),
                new CreatorTool("🎯 Find Reference Placeholders", "Locate and select all GameObjects with missing reference placeholders for easy rewiring", AssetCleanupTools.FindMissingReferencePlaceholders),
                new CreatorTool("🧼 Remove Reference Placeholders", "Remove all missing reference placeholder components from the scene", AssetCleanupTools.RemoveMissingReferencePlaceholders)
            };
        }

        public List<CreatorTool> GetTools() => tools;

        public void DrawCategory()
        {
            EditorGUILayout.LabelField("Asset Cleanup Tools", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Clean up missing script references and broken object references from your project. These tools help maintain a healthy codebase when migrating or updating assets.", MessageType.Info);
            EditorGUILayout.Space(10);

            // Missing Scripts Section
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("🔄 Missing Scripts Workflow:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("1. Replace Missing Scripts → Creates placeholders for safety", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("2. Fix/restore your scripts as needed", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("3. Remove Script Placeholders → Clean up when done", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);

            // Draw missing script tools
            for (int i = 0; i < 4; i++)
            {
                DrawTool(tools[i]);
            }

            EditorGUILayout.Space(10);

            // Missing References Section
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("🔗 Missing References Workflow:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("1. Replace Missing References → Track missing object references", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("2. Find Reference Placeholders → Locate and rewire references", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("3. Remove Reference Placeholders → Clean up when done", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);

            // Draw missing reference tools
            for (int i = 4; i < tools.Count; i++)
            {
                DrawTool(tools[i]);
            }
        }

        private void DrawTool(CreatorTool tool)
        {
            // Use responsive drawing but keep confirmation
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            float windowWidth = EditorGUIUtility.currentViewWidth;

            if (windowWidth < 400f)
            {
                // Vertical layout
                EditorGUILayout.LabelField(tool.title, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(tool.description, EditorStyles.wordWrappedMiniLabel);

                if (GUILayout.Button("Apply", GUILayout.Height(35)))
                {
                    if (EditorUtility.DisplayDialog("Confirm Asset Cleanup",
                        $"This will run: {tool.title}\n\n{tool.description}\n\nThis action can be undone with Ctrl+Z.",
                        "Continue", "Cancel"))
                    {
                        tool.action?.Invoke();
                    }
                }
            }
            else
            {
                // Horizontal layout
                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(tool.title, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(tool.description, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndVertical();

                if (GUILayout.Button("Apply", GUILayout.Width(80), GUILayout.Height(35)))
                {
                    if (EditorUtility.DisplayDialog("Confirm Asset Cleanup",
                        $"This will run: {tool.title}\n\n{tool.description}\n\nThis action can be undone with Ctrl+Z.",
                        "Continue", "Cancel"))
                    {
                        tool.action?.Invoke();
                    }
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);
        }
    }
}