using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
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
                // High Impact Core Systems (Ordered by Implementation Priority)
                new CreatorTool("🟢 Add Single Player Quest System", "Create missions and objectives for single player experiences", () => U3DQuestSystemTools.CreateQuestSystem()),
                new CreatorTool("🚧 Add Inventory System", "Essential for collecting, managing, and using items in exploration games", () => Debug.Log("Applied Inventory System")),
                new CreatorTool("🚧 Add Dialogue System", "Critical for storytelling, NPCs, and guided experiences", () => Debug.Log("Applied Dialogue System")),
                new CreatorTool("🚧 Add Timer System", "Countdown timers, time limits, scheduled events", () => Debug.Log("Applied Timer System")),
                new CreatorTool("🚧 Add State Machine", "Object state management (locked/unlocked, powered/unpowered, etc.)", () => Debug.Log("Applied State Machine")),
                
                // Player Progression Systems
                new CreatorTool("🚧 Add Health/Lives System", "Player progression, challenge mechanics", () => Debug.Log("Applied Health/Lives System")),
                new CreatorTool("🚧 Add Checkpoint System", "Save progress, restart points for complex experiences", () => Debug.Log("Applied Checkpoint System")),
                new CreatorTool("🚧 Add Achievement System", "Unlock rewards, progression tracking", () => Debug.Log("Applied Achievement System")),
                
                // Original Systems (Maintained)
                new CreatorTool("🚧 Add Quiz System", "Interactive questions and knowledge tests", () => Debug.Log("Applied Quiz System")),
                new CreatorTool("🚧 Add Scoreboard Canvas", "Track and display player achievements", () => Debug.Log("Applied Scoreboard Canvas")),
                
                // Social & Community Systems
                new CreatorTool("🚧 Add Social Sharing", "Screenshot/share moments from experiences", () => Debug.Log("Applied Social Sharing")),
                new CreatorTool("🚧 Add Guestbook", "Adds a screen space UI panel with built-in interactivity, instructing visitors to 'Press E to leave '[Your Name] was here!' note.' that gets the user's name and adds the message to the displayed text", () => Debug.Log("Applied Guestbook")),
                
                // UI & Navigation Systems
                new CreatorTool("🟢 Add Billboard UI Panel", "World space canvas that faces camera with proximity fade", CreateBillboardUIPanel),
                new CreatorTool("🚧 Add Worldspace Interaction UI", "3D world canvas for object interactions", () => Debug.Log("Applied Worldspace Interaction UI"), true),
                new CreatorTool("🚧 Add Screenspace Interaction UI", "Screen overlay canvas for user interfaces", () => Debug.Log("Applied Screen Interaction UI")),
                
                // Portal & Navigation Systems
                new CreatorTool("🚧 Add Scene-to-Scene Portal", "Portal to load different scenes", () => Debug.Log("Applied Scene-to-Scene Portal"), true),
                new CreatorTool("🚧 Add 1-Way In-Scene Portal", "Portal for one-direction travel within scene", () => Debug.Log("Applied 1-Way Portal"), true),
                new CreatorTool("🚧 Add 2-Way In-Scene Portal", "Portal for bi-directional travel within scene", () => Debug.Log("Applied 2-Way Portal"), true)
            };
        }

        public List<CreatorTool> GetTools() => tools;

        public void DrawCategory()
        {
            EditorGUILayout.LabelField("Game Systems", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Add complete game systems to enhance player engagement. Systems are organized by impact priority - start with Quest and Inventory for maximum creator value!", MessageType.Info);
            EditorGUILayout.Space(10);

            foreach (var tool in tools)
            {
                ProjectToolsTab.DrawCategoryTool(tool);
            }
        }

        private static void CreateBillboardUIPanel()
        {
            GameObject canvasObj = new GameObject("Billboard UI Canvas");

            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            CanvasGroup canvasGroup = canvasObj.AddComponent<CanvasGroup>();

            canvasObj.AddComponent<GraphicRaycaster>();

            U3DBillboardUI billboard = canvasObj.AddComponent<U3DBillboardUI>();

            RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(400, 300);
            canvasRect.localScale = Vector3.one * 0.01f;

            var uiResources = new DefaultControls.Resources();
            GameObject panelObj = DefaultControls.CreatePanel(uiResources);
            panelObj.name = "Panel";
            panelObj.transform.SetParent(canvasObj.transform, false);
            panelObj.layer = LayerMask.NameToLayer("UI");

            RectTransform panelRect = panelObj.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            Image panelImage = panelObj.GetComponent<Image>();
            if (panelImage != null)
            {
                panelImage.color = new Color(1f, 1f, 1f, 0.5f);
            }

            var tmpResources = new TMP_DefaultControls.Resources();
            GameObject textObj = TMP_DefaultControls.CreateText(tmpResources);
            textObj.name = "Text (TMP)";
            textObj.transform.SetParent(panelObj.transform, false);
            textObj.layer = LayerMask.NameToLayer("UI");

            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.5f, 0.5f);
            textRect.anchorMax = new Vector2(0.5f, 0.5f);
            textRect.sizeDelta = new Vector2(350, 250);
            textRect.anchoredPosition = Vector2.zero;

            TextMeshProUGUI tmpText = textObj.GetComponent<TextMeshProUGUI>();
            if (tmpText != null)
            {
                tmpText.text = "Billboard Text";
                tmpText.fontSize = 18;
                tmpText.color = Color.white;
                tmpText.alignment = TextAlignmentOptions.Center;
            }

            if (SceneView.lastActiveSceneView != null)
            {
                canvasObj.transform.position = SceneView.lastActiveSceneView.pivot;
            }

            Selection.activeGameObject = canvasObj;
            EditorGUIUtility.PingObject(canvasObj);

            Debug.Log("✅ Billboard UI Panel created! Customize text and styling in the Inspector. Adjust hideDistance and showDistance for proximity fade.");
        }
    }
}