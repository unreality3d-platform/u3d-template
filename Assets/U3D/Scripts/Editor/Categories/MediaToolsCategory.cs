using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;
using UnityEngine.Video;

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
                new CreatorTool("🟢 Add Video Player", "Stream a video from a URL onto a screen in your world. After placing, select the Video Screen child object and paste a direct .mp4 or .webm link into the Video URL field.", CreateVideoPlayer),
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
        // Video Player
        // ───────────────────────────────────────────

        private static void CreateVideoPlayer()
        {
            // Parent container so screen + controls move together
            GameObject root = new GameObject("Video Player");
            PositionInScene(root);

            // Video screen quad (child of root)
            GameObject screenObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screenObj.name = "Video Screen";
            screenObj.transform.SetParent(root.transform, false);
            screenObj.transform.localScale = new Vector3(1.92f, 1.08f, 1f);

            MeshCollider meshCollider = screenObj.GetComponent<MeshCollider>();
            if (meshCollider != null)
                Object.DestroyImmediate(meshCollider);

            AudioSource audioSource = screenObj.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
            audioSource.minDistance = 1f;
            audioSource.maxDistance = 30f;
            audioSource.rolloffMode = AudioRolloffMode.Logarithmic;

            AudioMixerGroup effectsGroup = FindMixerGroup("Effects");
            if (effectsGroup != null)
                audioSource.outputAudioMixerGroup = effectsGroup;

            VideoPlayer videoPlayer = screenObj.AddComponent<VideoPlayer>();
            videoPlayer.source = VideoSource.Url;
            videoPlayer.playOnAwake = false;
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
            videoPlayer.SetTargetAudioSource(0, audioSource);
            videoPlayer.hideFlags = HideFlags.HideInInspector;
            audioSource.hideFlags = HideFlags.HideInInspector;

            U3DVideoPlayer u3dVideo = screenObj.AddComponent<U3DVideoPlayer>();

            // ── Built-in UI sprites ──

            Sprite uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            Sprite uiBackground = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            Sprite uiKnob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            var uiResources = new DefaultControls.Resources();
            uiResources.standard = uiSprite;

            var tmpResources = new TMP_DefaultControls.Resources();
            tmpResources.standard = uiSprite;

            // ── Controls Canvas (sibling of screen, not child) ──

            GameObject canvasObj = new GameObject("Video Controls Canvas");
            canvasObj.transform.SetParent(root.transform, false);
            canvasObj.transform.localPosition = new Vector3(0f, -0.7f, 0f);

            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            canvasObj.AddComponent<CanvasGroup>();
            canvasObj.AddComponent<GraphicRaycaster>();

            U3DWorldspaceUI worldspaceUI = canvasObj.AddComponent<U3DWorldspaceUI>();
            worldspaceUI.faceCamera = false;
            worldspaceUI.hideDistance = 8f;
            worldspaceUI.showDistance = 1f;

            RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(300, 40);
            canvasRect.localScale = Vector3.one * 0.006f;

            // Panel background
            GameObject panelObj = DefaultControls.CreatePanel(uiResources);
            panelObj.name = "Controls Panel";
            panelObj.transform.SetParent(canvasObj.transform, false);
            panelObj.layer = LayerMask.NameToLayer("UI");

            RectTransform panelRect = panelObj.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            Image panelImage = panelObj.GetComponent<Image>();
            if (panelImage != null)
                panelImage.color = new Color(1f, 1f, 1f, 0.95f);

            // Play/Pause Button — left 16%
            GameObject buttonObj = TMP_DefaultControls.CreateButton(tmpResources);
            buttonObj.name = "PlayPause Button";
            buttonObj.transform.SetParent(panelObj.transform, false);
            buttonObj.layer = LayerMask.NameToLayer("UI");

            RectTransform buttonRect = buttonObj.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.02f, 0.1f);
            buttonRect.anchorMax = new Vector2(0.18f, 0.9f);
            buttonRect.offsetMin = Vector2.zero;
            buttonRect.offsetMax = Vector2.zero;

            TextMeshProUGUI buttonTMP = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonTMP != null)
            {
                buttonTMP.text = "Play";
                buttonTMP.fontSize = 14;
                buttonTMP.color = new Color32(50, 50, 50, 255);
                buttonTMP.alignment = TextAlignmentOptions.Center;
            }

            // Progress Slider — middle 55%
            GameObject sliderObj = DefaultControls.CreateSlider(uiResources);
            sliderObj.name = "Progress Slider";
            sliderObj.transform.SetParent(panelObj.transform, false);
            sliderObj.layer = LayerMask.NameToLayer("UI");

            RectTransform sliderRect = sliderObj.GetComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(0.20f, 0.1f);
            sliderRect.anchorMax = new Vector2(0.75f, 0.9f);
            sliderRect.offsetMin = Vector2.zero;
            sliderRect.offsetMax = Vector2.zero;

            Slider slider = sliderObj.GetComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 0f;

            Image sliderBg = sliderObj.transform.Find("Background")?.GetComponent<Image>();
            if (sliderBg != null)
                sliderBg.sprite = uiBackground;

            Image fillImage = sliderObj.transform.Find("Fill Area/Fill")?.GetComponent<Image>();
            if (fillImage != null)
            {
                fillImage.sprite = uiSprite;
                fillImage.color = new Color(0.4f, 0.6f, 1f, 1f);
            }

            Image handleImage = sliderObj.transform.Find("Handle Slide Area/Handle")?.GetComponent<Image>();
            if (handleImage != null)
                handleImage.sprite = uiKnob;

            RectTransform handleSlideArea = sliderObj.transform.Find("Handle Slide Area")?.GetComponent<RectTransform>();
            if (handleSlideArea != null)
            {
                handleSlideArea.offsetMin = new Vector2(10f, 6f);
                handleSlideArea.offsetMax = new Vector2(-10f, -6f);
            }

            // Time Display — right 23%
            GameObject timeObj = TMP_DefaultControls.CreateText(tmpResources);
            timeObj.name = "Time Display";
            timeObj.transform.SetParent(panelObj.transform, false);
            timeObj.layer = LayerMask.NameToLayer("UI");

            RectTransform timeRect = timeObj.GetComponent<RectTransform>();
            timeRect.anchorMin = new Vector2(0.77f, 0.1f);
            timeRect.anchorMax = new Vector2(0.98f, 0.9f);
            timeRect.offsetMin = Vector2.zero;
            timeRect.offsetMax = Vector2.zero;

            TextMeshProUGUI timeTMP = timeObj.GetComponent<TextMeshProUGUI>();
            if (timeTMP != null)
            {
                timeTMP.text = "0:00 / 0:00";
                timeTMP.fontSize = 11;
                timeTMP.color = new Color32(50, 50, 50, 255);
                timeTMP.alignment = TextAlignmentOptions.Center;
                timeTMP.raycastTarget = false;
            }

            // Wire references on the runtime component
            u3dVideo.playPauseButton = buttonObj.GetComponent<Button>();
            u3dVideo.progressSlider = slider;
            u3dVideo.timeDisplay = timeTMP;
            u3dVideo.playPauseButtonText = buttonTMP;

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            EditorUtility.SetDirty(root);
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