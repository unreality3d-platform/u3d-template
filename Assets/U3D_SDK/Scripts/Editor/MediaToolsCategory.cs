﻿using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace U3D.Editor
{
    public class MediaToolsCategory : IToolCategory
    {
        public string CategoryName => "Media & Content";
        private List<CreatorTool> tools;

        public MediaToolsCategory()
        {
            tools = new List<CreatorTool>
            {
                new CreatorTool("🚧 Add Video Player Object", "Stream videos from URLs in your world", () => Debug.Log("Applied Video Player Object"), true),
                new CreatorTool("🚧 Add Audio Trigger", "Sounds that play on player interaction", () => Debug.Log("Applied Audio Trigger"), true),
                new CreatorTool("🚧 Add Screenshare Object", "Share desktop screens within your experience", () => Debug.Log("Applied Screenshare Object"), true),
                new CreatorTool("🚧 Add Image Gallery", "Display rotating image collections", () => Debug.Log("Applied Image Gallery"), true),
                new CreatorTool("🚧 Add Text Display", "Dynamic text that can be updated", () => Debug.Log("Applied Text Display"), true)
            };
        }

        public List<CreatorTool> GetTools() => tools;

        public void DrawCategory()
        {
            EditorGUILayout.LabelField("Media & Content Tools", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Add multimedia elements to enrich your experiences.", MessageType.Info);
            EditorGUILayout.Space(10);

            foreach (var tool in tools)
            {
                ProjectToolsTab.DrawCategoryTool(tool);
            }
        }
    }
}