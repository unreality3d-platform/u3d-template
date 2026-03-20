using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEditor;

namespace U3D.Editor
{
    public class MediaToolsCategory : IToolCategory
    {
        public string CategoryName => "Media & Content";
        public System.Action<int> OnRequestTabSwitch { get; set; }
        private List<CreatorTool> tools;

        public MediaToolsCategory()
        {
            tools = new List<CreatorTool>
            {
                new CreatorTool("🟢 Add Audio Playlist", "Play random audio clips from a list through an AudioSource", ApplyAudioList, true),
                new CreatorTool("🟢 Add Worldspace UI", "World space canvas that faces camera with proximity fade", CreateWorldspaceUI),
                new CreatorTool("🚧 Add Screenspace UI", "Screen overlay canvas for user interfaces", () => { }),
                new CreatorTool("🚧 Add Video Player", "Stream videos from URLs in your world", () => { }),
                new CreatorTool("🚧 Add Image Gallery", "Display rotating image collections", () => { }),
                new CreatorTool("🚧 Add Guestbook", "Visitors can leave a note that appears in your world", () => { }),
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

        private static void ApplyAudioList()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("Please select an object first");
                return;
            }

            if (selected.GetComponent<U3DAudioPlaylist>() != null)
            {
                Debug.LogWarning("Object already has an AudioPlaylist component");
                return;
            }

            Undo.RecordObject(selected, "Add AudioList Component");
            selected.AddComponent<U3DAudioPlaylist>();

            EditorUtility.SetDirty(selected);
        }

        private static void CreateWorldspaceUI()
        {
            GameObject canvasObj = new GameObject("Worldspace UI Canvas");

            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            canvasObj.AddComponent<CanvasGroup>();
            canvasObj.AddComponent<GraphicRaycaster>();
            canvasObj.AddComponent<U3DBillboardUI>();

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
                panelImage.color = new Color(1f, 1f, 1f, 0.5f);

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
                tmpText.text = "Worldspace UI Text";
                tmpText.fontSize = 18;
                tmpText.color = Color.white;
                tmpText.alignment = TextAlignmentOptions.Center;
            }

            if (SceneView.lastActiveSceneView != null)
                canvasObj.transform.position = SceneView.lastActiveSceneView.pivot;

            Selection.activeGameObject = canvasObj;
            EditorGUIUtility.PingObject(canvasObj);

            EditorUtility.SetDirty(canvasObj);
        }
    }
}