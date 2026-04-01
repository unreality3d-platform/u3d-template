using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
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

        private const string MIXER_PATH = "Assets/U3D/Prefabs/U3D_AudioMixer.mixer";

        public MediaToolsCategory()
        {
            tools = new List<CreatorTool>
            {
                new CreatorTool("🟢 Add Audio Playlist", "Play audio clips through your AudioSource. Add clips, then start playback from a trigger (like U3D Enter Trigger).", ApplyAudioList),
                new CreatorTool("🟢 Add Ambient Audio Source", "Adds an AudioSource routed to the Ambient channel. 2D playback, same volume everywhere. Good for background music and ambient sound.", CreateAmbientSource),
                new CreatorTool("🟢 Add Local Audio Source", "Adds an AudioSource routed to the Effects channel. 3D spatial, sound fades with distance. Good for sound effects on objects.", CreateLocalSource),
                new CreatorTool("🟢 Add Worldspace UI", "World space canvaswith proximity fade and billboard behavior options", CreateWorldspaceUI),
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

        // ───────────────────────────────────────────
        // Audio Playlist
        // ───────────────────────────────────────────

        private static void ApplyAudioList()
        {
            GameObject obj = new GameObject("Audio Playlist");
            obj.AddComponent<U3DAudioPlaylist>();

            PositionInScene(obj);
            Selection.activeGameObject = obj;
            EditorGUIUtility.PingObject(obj);
            EditorUtility.SetDirty(obj);
        }

        // ───────────────────────────────────────────
        // Ambient Source
        // ───────────────────────────────────────────

        private static void CreateAmbientSource()
        {
            AudioMixerGroup ambientGroup = FindMixerGroup("Ambient");
            if (ambientGroup == null) return;

            GameObject obj = new GameObject("Ambient Audio Source");

            AudioSource source = obj.AddComponent<AudioSource>();
            source.outputAudioMixerGroup = ambientGroup;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.loop = false;

            PositionInScene(obj);
            Selection.activeGameObject = obj;
            EditorGUIUtility.PingObject(obj);
            EditorUtility.SetDirty(obj);
        }

        // ───────────────────────────────────────────
        // Local Source
        // ───────────────────────────────────────────

        private static void CreateLocalSource()
        {
            AudioMixerGroup effectsGroup = FindMixerGroup("Effects");
            if (effectsGroup == null) return;

            GameObject obj = new GameObject("Local Audio Source");

            AudioSource source = obj.AddComponent<AudioSource>();
            source.outputAudioMixerGroup = effectsGroup;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.minDistance = 1f;
            source.maxDistance = 500f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.loop = false;

            PositionInScene(obj);
            Selection.activeGameObject = obj;
            EditorGUIUtility.PingObject(obj);
            EditorUtility.SetDirty(obj);
        }

        // ───────────────────────────────────────────
        // Mixer Lookup
        // ───────────────────────────────────────────

        private static AudioMixerGroup FindMixerGroup(string groupName)
        {
            AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MIXER_PATH);
            if (mixer == null)
            {
                EditorUtility.DisplayDialog("Audio Mixer Not Found",
                    "Could not find U3D_AudioMixer at:\n" + MIXER_PATH +
                    "\n\nMake sure the U3D template audio mixer has not been moved or renamed.",
                    "OK");
                return null;
            }

            AudioMixerGroup[] groups = mixer.FindMatchingGroups(groupName);
            if (groups == null || groups.Length == 0)
            {
                EditorUtility.DisplayDialog("Mixer Group Not Found",
                    "Could not find the '" + groupName + "' group in U3D_AudioMixer." +
                    "\n\nExpected groups: Master, Ambient, Effects, Music, Voice.",
                    "OK");
                return null;
            }

            return groups[0];
        }

        // ───────────────────────────────────────────
        // Scene Positioning
        // ───────────────────────────────────────────

        private static void PositionInScene(GameObject obj)
        {
            if (SceneView.lastActiveSceneView != null)
                obj.transform.position = SceneView.lastActiveSceneView.pivot;
        }

        // ───────────────────────────────────────────
        // Worldspace UI
        // ───────────────────────────────────────────

        private static void CreateWorldspaceUI()
        {
            GameObject canvasObj = new GameObject("Worldspace UI Canvas");

            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            canvasObj.AddComponent<CanvasGroup>();
            canvasObj.AddComponent<GraphicRaycaster>();
            canvasObj.AddComponent<U3DWorldspaceUI>();

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