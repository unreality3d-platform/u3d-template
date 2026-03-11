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

        public SystemsToolsCategory()
        {
            tools = new List<CreatorTool>
            {
                new CreatorTool("🟢 Add Quest System", "Create missions and objectives for your experience", () => U3DQuestSystemTools.CreateQuestSystem()),
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
    }
}