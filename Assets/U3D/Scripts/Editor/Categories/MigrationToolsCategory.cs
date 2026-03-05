using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

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
                new CreatorTool("🟢 Replace Missing Scripts", "Replace missing script references with placeholder components to prevent errors while retaining visual reminders", AssetCleanupTools.ReplaceMissingScriptsWithPlaceholders),
                new CreatorTool("🟢 Remove Script Placeholders", "Remove placeholder components added by the Replace Missing Scripts tool", AssetCleanupTools.RemovePlaceholderComponents),
                new CreatorTool("🟢 Clean Missing Scripts from Scene", "Remove missing script components directly from all GameObjects in loaded scenes", AssetCleanupTools.RemoveMissingScriptsFromScene),
                new CreatorTool("🟢 Clean Missing Scripts from Prefabs", "Remove missing script components from prefabs in selected folder", AssetCleanupTools.CleanPrefabsInFolder),

                // Missing Reference Tools
                new CreatorTool("🟢 Replace Missing References", "Detect missing object references in components and add placeholder tracking components", AssetCleanupTools.ReplaceMissingReferencesWithPlaceholders),
                new CreatorTool("🟢 Find Reference Placeholders", "Locate and select all GameObjects with missing reference placeholders for easy rewiring", AssetCleanupTools.FindMissingReferencePlaceholders),
                new CreatorTool("🟢 Remove Reference Placeholders", "Remove all missing reference placeholder components from the scene", AssetCleanupTools.RemoveMissingReferencePlaceholders),

                // Visual Scripting Tools
                new CreatorTool("🟢 Clean Visual Scripting Graphs", "Scan the active scene for Script Machine and State Machine components with third party SDK node types and clear their graphs to stop deserialization errors", AssetCleanupTools.CleanVisualScriptingGraphs),
                new CreatorTool("🟢 Clean third party Project Assets", "Scan a selected folder (including subfolders) for .asset and .prefab files containing third party SDK Visual Scripting nodes. Clears broken graphs and removes missing scripts from prefabs in one pass", AssetCleanupTools.CleanThirdPartyVSProjectAssets),
            };
        }

        public List<CreatorTool> GetTools() => tools;

        public void DrawCategory()
        {
            EditorGUILayout.LabelField("Asset Cleanup Tools", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Clean up missing script references, broken object references, and broken Visual Scripting graphs. Use these tools when migrating scenes from other platforms or updating assets.", MessageType.Info);
            EditorGUILayout.Space(10);

            // Missing Scripts Section
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("🔄 Missing Scripts Workflow:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("1. Replace Missing Scripts → Creates placeholders for safety", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("2. Fix/restore your scripts as needed", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("3. Remove Script Placeholders → Clean up when done", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);

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

            for (int i = 4; i < 7; i++)
            {
                DrawTool(tools[i]);
            }

            EditorGUILayout.Space(10);

            // Visual Scripting Section
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("🎮 Visual Scripting Workflow:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("1. Clean third party Project Assets → Clear broken graphs and missing scripts from project files", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("2. Clean Visual Scripting Graphs → Clear remaining broken graphs in the active scene", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("3. Rebuild logic using U3D components or fresh VS graphs", EditorStyles.miniLabel);
            EditorGUILayout.HelpBox("Use these after porting scenes from third party platforms that embed proprietary Visual Scripting node types. Graphs are cleared without deleting GameObjects.", MessageType.Warning);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);

            DrawTool(tools[7]);
            DrawTool(tools[8]);
        }

        private void DrawTool(CreatorTool tool)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            float windowWidth = EditorGUIUtility.currentViewWidth;

            if (windowWidth < 400f)
            {
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