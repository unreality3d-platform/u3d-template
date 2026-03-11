using UnityEngine;
using UnityEditor;
using TMPro;

namespace U3D.Editor
{
    public static class U3DScorableTools
    {
        public static void CreateScorable()
        {
            GameObject selected = Selection.activeGameObject;

            if (selected != null)
            {
                if (selected.GetComponent<U3DScorable>() == null)
                    selected.AddComponent<U3DScorable>();

                EditorUtility.SetDirty(selected);
                return;
            }

            // No selection - create a billboard-style scorable from scratch
            GameObject scoreObj = new GameObject("Scorable");

            Canvas canvas = scoreObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            scoreObj.AddComponent<UnityEngine.UI.CanvasScaler>();
            scoreObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            scoreObj.AddComponent<U3DBillboardUI>();

            RectTransform canvasRect = scoreObj.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(200, 100);
            canvasRect.localScale = Vector3.one * 0.01f;

            var tmpResources = new TMPro.TMP_DefaultControls.Resources();
            GameObject textObj = TMPro.TMP_DefaultControls.CreateText(tmpResources);
            textObj.name = "ScoreText";
            textObj.transform.SetParent(scoreObj.transform, false);
            textObj.layer = LayerMask.NameToLayer("UI");

            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            TextMeshProUGUI tmp = textObj.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.text = "0";
                tmp.fontSize = 36;
                tmp.color = new Color32(50, 50, 50, 255);
                tmp.alignment = TMPro.TextAlignmentOptions.Center;
            }

            U3DScorable scorable = scoreObj.AddComponent<U3DScorable>();

            if (SceneView.lastActiveSceneView != null)
                scoreObj.transform.position = SceneView.lastActiveSceneView.pivot;

            Selection.activeGameObject = scoreObj;
            EditorGUIUtility.PingObject(scoreObj);
            EditorUtility.SetDirty(scoreObj);
        }
    }
}