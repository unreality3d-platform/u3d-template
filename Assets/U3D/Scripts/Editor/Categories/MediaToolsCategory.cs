using Fusion;
using System.Collections.Generic;
using TMPro;
using U3D;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.InputSystem;
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
        private const string SETTINGS_UI_PREFAB_PATH = "Assets/U3D/Prefabs/Settings UI Canvas.prefab";

        public MediaToolsCategory()
        {
            tools = new List<CreatorTool>
            {
                new CreatorTool("🟢 Add Audio Playlist", "Play audio clips through your AudioSource. Add clips, then start playback from a trigger (like U3D Enter Trigger).", ApplyAudioList),
                new CreatorTool("🟢 Make Audio Playlist", "Adds an Audio Playlist component to the selected object. If the object doesn't have an AudioSource, one is added and routed to the Effects mixer (3D spatial).", MakeAudioPlaylist, true),
                new CreatorTool("🟢 Add Ambient Audio Source", "Adds an AudioSource routed to the Ambient channel. 2D playback, same volume everywhere. Good for background music and ambient sound.", CreateAmbientSource),
                new CreatorTool("🟢 Add Local Audio Source", "Adds an AudioSource routed to the Effects channel. 3D spatial, sound fades with distance. Good for sound effects on objects.", CreateLocalSource),
                new CreatorTool("🟢 Add Worldspace UI", "World space canvas with proximity fade and billboard behavior options", CreateWorldspaceUI),
                new CreatorTool("🟢 Make URL Link", "Click to open a URL in a new browser tab. Adds an Interact Trigger wired to open the link.", ApplyURLLink, true),
                new CreatorTool("🟢 Add Video Player", "Stream a video from a URL onto a screen in your world. After placing, select the Video Screen child object and paste a direct .mp4 or .webm link into the Video URL field.", CreateVideoPlayer),
                new CreatorTool("🟢 Add Mirror", "Reflective surface for vanity mirrors, avatar viewing, or scene composition. Each mirror creates its own render texture asset in Assets/U3D/U3D_Assets/Mirrors/.", CreateMirror),
                new CreatorTool("🟢 Add Instructions", "Worldspace UI showing default movement and control patterns with all current input bindings. Updates automatically if you remap controls.", CreateMovementInstructions),
                new CreatorTool("🟢 Add Settings UI", "Adds the U3D Settings UI prefab. Players use this to adjust audio, graphics, and controls at runtime.", AddSettingsUI),
                new CreatorTool("🟢 Add Screenspace UI", "Screen overlay canvas with title and body text. Good for HUDs, menus, or info overlays. Add your own buttons and content.", CreateScreenspaceUI),
                new CreatorTool("🚧 Add Slide Presentation", "Display and cycle through image collections in a sequence, in one UI element", () => { }),
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
        // URL Link
        // ───────────────────────────────────────────

        private static void ApplyURLLink()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("Please select an object first");
                return;
            }

            Undo.RecordObject(selected, "Make URL Link");

            Collider collider = selected.GetComponent<Collider>();
            if (collider == null)
                selected.AddComponent<BoxCollider>();

            if (!selected.GetComponent<NetworkObject>())
            {
                var networkObject = selected.AddComponent<NetworkObject>();
                InteractionToolsCategory.ConfigureNetworkObjectForSharedMode(networkObject);
            }

            U3DInteractTrigger interactTrigger = selected.GetComponent<U3DInteractTrigger>();
            if (interactTrigger == null)
                interactTrigger = selected.AddComponent<U3DInteractTrigger>();

            U3DOpenURL openURL = selected.GetComponent<U3DOpenURL>();
            if (openURL == null)
                openURL = selected.AddComponent<U3DOpenURL>();

            if (interactTrigger.OnInteractTriggered == null)
                interactTrigger.OnInteractTriggered = new UnityEngine.Events.UnityEvent();

            if (!IsAlreadyWired(interactTrigger.OnInteractTriggered, openURL, "Open"))
            {
                UnityEditor.Events.UnityEventTools.AddVoidPersistentListener(
                    interactTrigger.OnInteractTriggered,
                    new UnityEngine.Events.UnityAction(openURL.Open)
                );
            }

            EditorUtility.SetDirty(selected);
        }

        private static bool IsAlreadyWired(UnityEngine.Events.UnityEvent unityEvent, Object target, string methodName)
        {
            for (int i = 0; i < unityEvent.GetPersistentEventCount(); i++)
            {
                if (unityEvent.GetPersistentTarget(i) == target &&
                    unityEvent.GetPersistentMethodName(i) == methodName)
                    return true;
            }
            return false;
        }

        // ───────────────────────────────────────────
        // Audio Playlist
        // ───────────────────────────────────────────

        private static void ApplyAudioList()
        {
            GameObject obj = new GameObject("Audio Playlist");

            // RequireComponent on U3DAudioPlaylist pulls in an AudioSource automatically.
            // The playlist's Reset() configures that AudioSource with U3D's standard 3D
            // spatial defaults (Effects mixer, log rolloff, 1-500m range). So this single
            // AddComponent call produces a fully wired, ready-to-play playlist — same end
            // state as Make Audio Playlist on a pre-existing object.
            obj.AddComponent<U3DAudioPlaylist>();

            PositionInScene(obj);
            Selection.activeGameObject = obj;
            EditorGUIUtility.PingObject(obj);
            EditorUtility.SetDirty(obj);
        }

        private static void MakeAudioPlaylist()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("Please select an object first");
                return;
            }

            Undo.RegisterCompleteObjectUndo(selected, "Make Audio Playlist");

            // Idempotent: don't double-add if the creator runs the tool twice on the same object.
            U3DAudioPlaylist existingPlaylist = selected.GetComponent<U3DAudioPlaylist>();
            if (existingPlaylist != null)
            {
                EditorUtility.DisplayDialog("Audio Playlist",
                    "This object already has an Audio Playlist component.",
                    "OK");
                EditorGUIUtility.PingObject(selected);
                return;
            }

            // RequireComponent on U3DAudioPlaylist pulls in an AudioSource if the selected
            // object doesn't already have one. The playlist's Reset() configures that
            // AudioSource with U3D's standard 3D spatial defaults. If the selected object
            // already had an AudioSource that the creator configured manually, Reset()
            // detects that (playOnAwake check) and leaves their settings alone.
            Undo.AddComponent<U3DAudioPlaylist>(selected);

            EditorGUIUtility.PingObject(selected);
            EditorUtility.SetDirty(selected);
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
            GameObject root = new GameObject("Video Player");
            PositionInScene(root);

            GameObject screenObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screenObj.name = "Video Screen";
            screenObj.transform.SetParent(root.transform, false);
            screenObj.transform.localScale = new Vector3(1.92f, 1.08f, 1f);

            MeshCollider meshCollider = screenObj.GetComponent<MeshCollider>();
            if (meshCollider != null)
                Object.DestroyImmediate(meshCollider);

            // Assign the shared U3D video screen material so the screen shows unlit at edit time.
            // Without this, the URP default Lit material that GameObject.CreatePrimitive applied
            // would produce lighting glare in the Scene view. At runtime, U3DVideoPlayer instantiates
            // a per-instance copy of this material to hold its own RenderTexture (different Video
            // Players therefore play different videos despite sharing the edit-time material).
            MeshRenderer screenRenderer = screenObj.GetComponent<MeshRenderer>();
            if (screenRenderer != null)
            {
                Material screenMat = GetOrCreateVideoScreenMaterial();
                if (screenMat != null)
                    screenRenderer.sharedMaterial = screenMat;
                // If GetOrCreateVideoScreenMaterial returned null, it already logged a warning
                // and the runtime path in U3DVideoPlayer.CreateRenderTexture will attempt the
                // swap when the scene plays.
            }

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

            var uiResources = new DefaultControls.Resources();
            var tmpResources = new TMP_DefaultControls.Resources();

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

            GameObject panelObj = DefaultControls.CreatePanel(uiResources);
            panelObj.name = "Controls Panel";
            panelObj.transform.SetParent(canvasObj.transform, false);
            panelObj.layer = LayerMask.NameToLayer("UI");

            RectTransform panelRect = panelObj.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            U3DUIStyle.ApplyPanelStyle(panelObj);

            Image panelImage = panelObj.GetComponent<Image>();
            if (panelImage != null)
                panelImage.color = new Color(1f, 1f, 1f, 0.95f);

            GameObject buttonObj = TMP_DefaultControls.CreateButton(tmpResources);
            buttonObj.name = "PlayPause Button";
            buttonObj.transform.SetParent(panelObj.transform, false);
            buttonObj.layer = LayerMask.NameToLayer("UI");

            RectTransform buttonRect = buttonObj.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.02f, 0.1f);
            buttonRect.anchorMax = new Vector2(0.18f, 0.9f);
            buttonRect.offsetMin = Vector2.zero;
            buttonRect.offsetMax = Vector2.zero;

            U3DUIStyle.ApplyButtonStyle(buttonObj, "Play");
            TextMeshProUGUI buttonTMP = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            // Button label raycastTarget left at Unity's default (true) so the Button hit test
            // works through the label.

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

            // Apply U3D flat slider style with blue progress fill (Video Player exception
            // documented in spec — blue communicates "progress" in a way a neutral fill does not).
            U3DUIStyle.ApplySliderStyle(sliderObj, new Color(0.4f, 0.6f, 1f, 1f));
            // Restore the Handle Slide Area offsets — sized for the Video Player's compact controls strip.
            // ApplySliderStyle does not touch these because they're authored values that control the
            // knob's bounds and travel range, owned by the caller.
            RectTransform handleSlideArea = sliderObj.transform.Find("Handle Slide Area")?.GetComponent<RectTransform>();
            if (handleSlideArea != null)
            {
                handleSlideArea.offsetMin = new Vector2(20f, 6f);
                handleSlideArea.offsetMax = new Vector2(-20f, -6f);
            }

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
                U3DUIStyle.ApplyStatusStyle(timeTMP);
                timeTMP.raycastTarget = false;
            }

            u3dVideo.playPauseButton = buttonObj.GetComponent<Button>();
            u3dVideo.progressSlider = slider;
            u3dVideo.timeDisplay = timeTMP;
            u3dVideo.playPauseButtonText = buttonTMP;

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            EditorUtility.SetDirty(root);
        }

        // ───────────────────────────────────────────
        // Mirror
        // ───────────────────────────────────────────

        private const string MIRROR_RT_FOLDER = "Assets/U3D/U3D_Assets/Mirrors";

        private static void CreateMirror()
        {
            // Ensure the render texture folder exists.
            if (!AssetDatabase.IsValidFolder(MIRROR_RT_FOLDER))
            {
                if (!AssetDatabase.IsValidFolder("Assets/U3D/U3D_Assets"))
                {
                    if (!AssetDatabase.IsValidFolder("Assets/U3D"))
                        AssetDatabase.CreateFolder("Assets", "U3D");
                    AssetDatabase.CreateFolder("Assets/U3D", "U3D_Assets");
                }
                AssetDatabase.CreateFolder("Assets/U3D/U3D_Assets", "Mirrors");
            }

            // Create the render texture asset with a unique name so multiple mirrors don't collide.
            string rtPath = AssetDatabase.GenerateUniqueAssetPath(MIRROR_RT_FOLDER + "/MirrorRenderTexture.renderTexture");
            RenderTexture rt = new RenderTexture(1024, 1536, 24, RenderTextureFormat.Default);
            rt.name = System.IO.Path.GetFileNameWithoutExtension(rtPath);
            AssetDatabase.CreateAsset(rt, rtPath);
            AssetDatabase.SaveAssets();

            // Create the material that displays the render texture on the mirror surface.
            string matPath = AssetDatabase.GenerateUniqueAssetPath(MIRROR_RT_FOLDER + "/MirrorMaterial.mat");
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetTexture("_BaseMap", rt);
            // Render Face = Both. Required because the negative X scale on the mirror quad
            // (used to flip the image left-to-right) inverts the quad's normals.
            mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            AssetDatabase.CreateAsset(mat, matPath);
            AssetDatabase.SaveAssets();

            // Root.
            GameObject root = new GameObject("Mirror");
            PositionInScene(root);

            // Mirror surface quad. Faces along the root's local +Z by default.
            GameObject surfaceObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            surfaceObj.name = "Mirror Surface";
            surfaceObj.transform.SetParent(root.transform, false);
            // Negative X scale flips the quad's UVs so the camera feed reads as a mirror image
            // (raise your right hand, the reflection raises its left). Standard Unity mirror trick.
            surfaceObj.transform.localScale = new Vector3(-2f, 3f, 1f);

            MeshRenderer surfaceRenderer = surfaceObj.GetComponent<MeshRenderer>();
            if (surfaceRenderer != null)
                surfaceRenderer.sharedMaterial = mat;

            // Reflection camera. Faces opposite the mirror surface so it looks out at the room
            // the player is standing in, not through the back of the mirror.
            GameObject camObj = new GameObject("Reflection Camera");
            camObj.transform.SetParent(root.transform, false);
            camObj.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            Camera reflectionCam = camObj.AddComponent<Camera>();
            reflectionCam.targetTexture = rt;
            // Don't tag this MainCamera and don't let it become the audio listener — the player controller owns both.
            reflectionCam.clearFlags = CameraClearFlags.Skybox;
            // Wide FOV reduces the size mismatch where objects close to the mirror appear larger
            // in the reflection than in the scene. Creators can tune this on the Reflection Camera
            // child in the Inspector if they want a different look.
            reflectionCam.fieldOfView = 120f;


            AudioListener stowawayListener = camObj.GetComponent<AudioListener>();
            if (stowawayListener != null)
                Object.DestroyImmediate(stowawayListener);

            // Mirror driver.
            U3DMirror mirror = root.AddComponent<U3DMirror>();
            mirror.reflectionCamera = reflectionCam;
            mirror.mirrorSurface = surfaceObj.transform;

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            EditorUtility.SetDirty(root);
        }

        // ───────────────────────────────────────────
        // Movement Instructions
        // ───────────────────────────────────────────

        private static void CreateMovementInstructions()
        {
            string instructionText = BuildMovementInstructionsText();

            GameObject canvasObj = new GameObject("Instructions");

            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            canvasObj.AddComponent<CanvasGroup>();
            canvasObj.AddComponent<GraphicRaycaster>();

            U3DWorldspaceUI worldspaceUI = canvasObj.AddComponent<U3DWorldspaceUI>();
            worldspaceUI.faceCamera = true;

            RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(500, 600);
            canvasRect.localScale = Vector3.one * 0.005f;

            var uiResources = new DefaultControls.Resources();
            var tmpResources = new TMP_DefaultControls.Resources();

            // Background panel
            GameObject panelObj = DefaultControls.CreatePanel(uiResources);
            panelObj.name = "Panel";
            panelObj.transform.SetParent(canvasObj.transform, false);
            panelObj.layer = LayerMask.NameToLayer("UI");

            RectTransform panelRect = panelObj.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            U3DUIStyle.ApplyPanelStyle(panelObj);

            // Title
            GameObject titleObj = TMP_DefaultControls.CreateText(tmpResources);
            titleObj.name = "Title";
            titleObj.transform.SetParent(panelObj.transform, false);
            titleObj.layer = LayerMask.NameToLayer("UI");

            RectTransform titleRect = titleObj.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.05f, 0.9f);
            titleRect.anchorMax = new Vector2(0.95f, 0.98f);
            titleRect.offsetMin = Vector2.zero;
            titleRect.offsetMax = Vector2.zero;

            TextMeshProUGUI titleTMP = titleObj.GetComponent<TextMeshProUGUI>();
            if (titleTMP != null)
            {
                titleTMP.text = "CONTROLS";
                U3DUIStyle.ApplyTitleStyle(titleTMP);
                titleTMP.raycastTarget = false;
            }

            // Scrollable content area
            GameObject scrollObj = DefaultControls.CreateScrollView(uiResources);
            scrollObj.name = "Scroll View";
            scrollObj.transform.SetParent(panelObj.transform, false);
            scrollObj.layer = LayerMask.NameToLayer("UI");

            RectTransform scrollRect = scrollObj.GetComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0.03f, 0.03f);
            scrollRect.anchorMax = new Vector2(0.97f, 0.88f);
            scrollRect.offsetMin = Vector2.zero;
            scrollRect.offsetMax = Vector2.zero;

            // ApplyScrollViewStyle: vertical-only, transparent background, flat scrollbar sprites,
            // narrow scrollbar width.
            U3DUIStyle.ApplyScrollViewStyle(scrollObj);

            Transform contentArea = scrollObj.transform.Find("Viewport/Content");

            // Instruction text
            GameObject textObj = TMP_DefaultControls.CreateText(tmpResources);
            textObj.name = "Instructions Text";
            textObj.transform.SetParent(contentArea, false);
            textObj.layer = LayerMask.NameToLayer("UI");

            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.pivot = new Vector2(0.5f, 1f);
            textRect.sizeDelta = new Vector2(0f, 0f);
            textRect.anchoredPosition = Vector2.zero;

            TextMeshProUGUI instructionTMP = textObj.GetComponent<TextMeshProUGUI>();
            if (instructionTMP != null)
            {
                instructionTMP.text = instructionText;
                U3DUIStyle.ApplyBodyStyle(instructionTMP);
                instructionTMP.alignment = TextAlignmentOptions.TopLeft;
                instructionTMP.textWrappingMode = TextWrappingModes.Normal;
                // Must be false — this text fills the entire scroll view content area.
                // With raycastTarget = true, the text swallows drag events and creators
                // can't scroll by clicking on the text itself.
                instructionTMP.raycastTarget = false;
            }

            // Auto-size content to fit text
            ContentSizeFitter fitter = textObj.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            if (contentArea != null)
            {
                VerticalLayoutGroup layout = contentArea.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(10, 10, 10, 10);
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;

                ContentSizeFitter contentFitter = contentArea.gameObject.AddComponent<ContentSizeFitter>();
                contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            PositionInScene(canvasObj);
            Selection.activeGameObject = canvasObj;
            EditorGUIUtility.PingObject(canvasObj);
            EditorUtility.SetDirty(canvasObj);
        }

        private static string BuildMovementInstructionsText()
        {
            var sb = new System.Text.StringBuilder();

            // Find the Player Controller prefab to read enabled-feature flags.
            // Intro sections conditionally hide lines that the creator has disabled.
            U3DPlayerController playerController = FindPlayerControllerForFlags();

            bool showMovement = playerController == null || playerController.EnableMovement;
            bool showJump = playerController == null || playerController.EnableJumping;
            bool showSprint = playerController == null || playerController.EnableSprintToggle;
            bool showCrouch = playerController == null || playerController.EnableCrouchToggle;
            bool showFly = playerController == null || playerController.EnableFlying;
            bool showAutoRun = playerController == null || playerController.EnableAutoRun;
            bool showTeleport = playerController == null || playerController.EnableTeleport;
            bool showZoom = playerController == null || playerController.EnableViewZoom;
            bool showAdvancedCam = playerController == null || playerController.EnableAdvancedCamera;

            // ── BASIC MOVEMENT ──
            var basicLines = new List<string>();
            if (showMovement) basicLines.Add("Walk: W A S D  or  Arrow Keys");
            if (showMovement && showSprint) basicLines.Add("Run: Shift (toggle)");
            if (showMovement && showJump) basicLines.Add("Jump: Space");
            if (showMovement && showCrouch) basicLines.Add("Crouch: C");

            if (basicLines.Count > 0)
            {
                sb.AppendLine("<b>BASIC MOVEMENT</b>");
                sb.AppendLine("─────────────────────────");
                foreach (var line in basicLines) sb.AppendLine(line);
                sb.AppendLine();
            }

            // ── CAMERA + UI ──
            var cameraLines = new List<string>();
            cameraLines.Add("Look: Right Mouse + Move");
            cameraLines.Add("Interact: R");
            cameraLines.Add("Free Cursor (stay in game): Tab");
            cameraLines.Add("Free Cursor (return to browser): Esc");
            if (showZoom) cameraLines.Add("Zoom: Mouse Wheel");

            sb.AppendLine("<b>CAMERA + UI</b>");
            sb.AppendLine("─────────────────────────");
            foreach (var line in cameraLines) sb.AppendLine(line);
            sb.AppendLine();

            // ── SPECIAL MOVEMENT ──
            var specialLines = new List<string>();
            if (showFly) specialLines.Add("Fly: F (toggle)");
            if (showMovement) specialLines.Add("Strafe: Q / E");
            if (showAdvancedCam && showMovement) specialLines.Add("Move Forward: Left + Right Mouse");
            if (showAdvancedCam && showMovement) specialLines.Add("Steer: Left + Right Mouse + Move Mouse");
            if (showAutoRun && showMovement) specialLines.Add("Auto-Run: Num Lock (toggle)");
            if (showTeleport) specialLines.Add("Teleport: Double-Click");

            if (specialLines.Count > 0)
            {
                sb.AppendLine("<b>SPECIAL MOVEMENT</b>");
                sb.AppendLine("─────────────────────────");
                foreach (var line in specialLines) sb.AppendLine(line);
                sb.AppendLine();
            }

            // ── ALL INPUT BINDINGS ──
            sb.AppendLine("<b>ALL INPUT BINDINGS</b>");
            sb.AppendLine("─────────────────────────");

            InputActionAsset inputActions = FindInputActionAsset();
            if (inputActions == null)
            {
                sb.AppendLine("(Input Action asset not found)");
                return sb.ToString();
            }

            var playerMap = inputActions.FindActionMap("Player");
            if (playerMap == null)
            {
                sb.AppendLine("(Player action map not found)");
                return sb.ToString();
            }

            // Map action names to the feature flag that controls them. Null = always shown.
            System.Func<string, bool> isActionEnabled = (actionName) =>
            {
                if (playerController == null) return true;
                switch (actionName)
                {
                    case "Jump": return playerController.EnableJumping;
                    case "Sprint": return playerController.EnableSprintToggle;
                    case "Crouch": return playerController.EnableCrouchToggle;
                    case "Fly": return playerController.EnableFlying;
                    case "AutoRun":
                    case "AutoRunToggle": return playerController.EnableAutoRun;
                    case "Teleport": return playerController.EnableTeleport;
                    case "Zoom": return playerController.EnableViewZoom;
                    case "Move":
                    case "StrafeLeft":
                    case "StrafeRight":
                    case "TurnLeft":
                    case "TurnRight": return playerController.EnableMovement;
                    default: return true;
                }
            };

            // Actions whose XR bindings exist in the asset but do nothing in a VR
            // session by VR camera architecture, not by bug:
            //   Zoom — FOV change has no effect; the WebXR XR Display Subsystem
            //           supplies per-eye projection from the device every frame.
            //   PerspectiveSwitch — third-person is blocked by the head-bone camera
            //           position lock in U3DPlayerController.LateUpdate and the
            //           _isInVRMode early-return in HandleCameraPositioning.
            // Both still work on desktop, so they are only excluded from the VR
            // CONTROLS section below — not from keyboard/mouse. The input bindings
            // are intentionally retained (inert) for if/when VR camera work makes
            // these functional; remove names from this set when that happens.
            var vrNonFunctionalActions = new HashSet<string> { "Zoom", "PerspectiveSwitch" };

            // Keyboard/mouse bindings
            var keyboardLines = new List<string>();
            foreach (var action in playerMap.actions)
            {
                if (!isActionEnabled(action.name)) continue;
                string keys = GetBindingDisplayString(action, BindingDeviceFilter.KeyboardMouse);
                if (!string.IsNullOrEmpty(keys))
                    keyboardLines.Add($"{GetActionDisplayName(action.name)}: {keys}");
            }

            if (keyboardLines.Count > 0)
            {
                foreach (var line in keyboardLines)
                    sb.AppendLine(line);
            }
            else
            {
                sb.AppendLine("(No keyboard or mouse bindings)");
            }

            // ── VR CONTROLS (only if XR bindings exist) ──
            var xrLines = new List<string>();
            foreach (var action in playerMap.actions)
            {
                if (!isActionEnabled(action.name)) continue;
                if (vrNonFunctionalActions.Contains(action.name)) continue;
                string xr = GetBindingDisplayString(action, BindingDeviceFilter.XR);
                if (!string.IsNullOrEmpty(xr))
                    xrLines.Add($"{GetActionDisplayName(action.name)}: {xr}");
            }

            if (xrLines.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("<b>VR CONTROLS</b>");
                sb.AppendLine("─────────────────────────");
                foreach (var line in xrLines)
                    sb.AppendLine(line);
            }

            return sb.ToString();
        }

        private static U3DPlayerController FindPlayerControllerForFlags()
        {
            // Prefer a Player Controller that's actually in the open scene — reflects
            // any scene-level overrides the creator has made.
            U3DPlayerController inScene = Object.FindAnyObjectByType<U3DPlayerController>(
                FindObjectsInactive.Include);
            if (inScene != null) return inScene;

            // Fall back to the prefab — covers the common case where the creator
            // hasn't dropped the player into the scene yet.
            string[] guids = AssetDatabase.FindAssets("U3D_PlayerController t:Prefab");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                var controller = prefab.GetComponent<U3DPlayerController>();
                if (controller != null) return controller;
            }

            return null;
        }

        private static string GetActionDisplayName(string actionName)
        {
            // Override action names that don't self-explain to visitors.
            // The action name itself stays unchanged in the asset — this only
            // affects what the Movement Instructions UI shows.
            switch (actionName)
            {
                case "Pause": return "Free Cursor (stay in game)";
                case "Escape": return "Free Cursor (return to browser)";
                case "PerspectiveSwitch": return "Camera Perspective";
                case "AutoRunToggle": return "Auto-Run";
                case "MouseLeft": return "Primary Click";
                case "MouseRight": return "Camera Look (hold)";
                default: return actionName;
            }
        }

        private static InputActionAsset FindInputActionAsset()
        {
            string[] guids = AssetDatabase.FindAssets("U3DInputActions t:InputActionAsset");
            if (guids.Length == 0)
                guids = AssetDatabase.FindAssets("t:InputActionAsset", new[] { "Assets/U3D" });

            if (guids.Length == 0)
                return null;

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
        }

        private enum BindingDeviceFilter
        {
            KeyboardMouse,
            XR
        }

        private static string GetBindingDisplayString(InputAction action, BindingDeviceFilter filter)
        {
            var entries = new List<string>();
            var bindings = action.bindings;

            for (int i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];

                // Handle composites (like 2D Vector for WASD) as a single entry
                if (binding.isComposite)
                {
                    string compositeDisplay = FormatComposite(bindings, i, filter);
                    if (!string.IsNullOrEmpty(compositeDisplay) && !entries.Contains(compositeDisplay))
                        entries.Add(compositeDisplay);

                    // Skip ahead past all parts of this composite
                    int j = i + 1;
                    while (j < bindings.Count && bindings[j].isPartOfComposite)
                        j++;
                    i = j - 1;
                    continue;
                }

                // Skip orphan composite parts (shouldn't happen, but safe)
                if (binding.isPartOfComposite) continue;

                if (!BindingMatchesFilter(binding.effectivePath, filter)) continue;

                string display = FormatSingleBinding(binding.effectivePath, filter);
                if (!string.IsNullOrEmpty(display) && !entries.Contains(display))
                    entries.Add(display);
            }

            return string.Join("  |  ", entries);
        }

        private static string FormatComposite(IReadOnlyList<InputBinding> bindings, int compositeIndex, BindingDeviceFilter filter)
        {
            // Gather parts of the composite
            var partDisplays = new List<string>();
            for (int j = compositeIndex + 1; j < bindings.Count; j++)
            {
                if (!bindings[j].isPartOfComposite) break;

                string path = bindings[j].effectivePath;
                if (!BindingMatchesFilter(path, filter)) continue;

                string part = FormatSingleBinding(path, filter);
                if (!string.IsNullOrEmpty(part))
                    partDisplays.Add(part);
            }

            if (partDisplays.Count == 0) return null;

            // For 2D Vector composites (WASD, arrows), the order is Up/Down/Left/Right.
            // Render as a compact group rather than "Up | Down | Left | Right".
            if (partDisplays.Count == 4)
                return string.Join(" ", partDisplays);

            // For 1D axis or other composites, join with slashes
            return string.Join(" / ", partDisplays);
        }

        private static bool BindingMatchesFilter(string effectivePath, BindingDeviceFilter filter)
        {
            if (string.IsNullOrEmpty(effectivePath)) return false;

            bool isXR = effectivePath.Contains("<XRController>")
                     || effectivePath.Contains("<XRHMD>")
                     || effectivePath.Contains("<WebXRController>");

            if (filter == BindingDeviceFilter.XR) return isXR;

            // KeyboardMouse: exclude XR, exclude gamepad (not currently supported),
            // include keyboard and mouse
            if (isXR) return false;
            return effectivePath.Contains("<Keyboard>") || effectivePath.Contains("<Mouse>");
        }

        private static string FormatSingleBinding(string effectivePath, BindingDeviceFilter filter)
        {
            if (filter == BindingDeviceFilter.XR)
                return FormatXRBinding(effectivePath);

            string display = InputControlPath.ToHumanReadableString(
                effectivePath,
                InputControlPath.HumanReadableStringOptions.OmitDevice);

            if (string.IsNullOrEmpty(display)) return null;

            display = display
                .Replace("Up Arrow", "↑")
                .Replace("Down Arrow", "↓")
                .Replace("Left Arrow", "←")
                .Replace("Right Arrow", "→")
                .Replace("Left Shift", "Shift")
                .Replace("Left Ctrl", "Ctrl")
                .Replace("Mouse Delta", "Mouse")
                .Replace("Scroll Y", "Mouse Wheel");

            return display;
        }

        private static string FormatXRBinding(string effectivePath)
        {
            if (string.IsNullOrEmpty(effectivePath)) return null;

            // Extract handedness from {LeftHand} or {RightHand} usage tag
            string hand = null;
            if (effectivePath.Contains("{LeftHand}")) hand = "Left";
            else if (effectivePath.Contains("{RightHand}")) hand = "Right";

            // Extract the control name (the last path segment)
            int lastSlash = effectivePath.LastIndexOf('/');
            if (lastSlash < 0 || lastSlash >= effectivePath.Length - 1) return null;

            string control = effectivePath.Substring(lastSlash + 1);

            // Map XR control names to readable labels
            string readable = PrettifyXRControl(control);
            if (string.IsNullOrEmpty(readable)) return null;

            return hand != null ? $"{hand} {readable}" : readable;
        }

        private static string PrettifyXRControl(string control)
        {
            if (string.IsNullOrEmpty(control)) return null;

            switch (control)
            {
                case "trigger":
                case "triggerButton":
                case "triggerPressed":
                    return "Trigger";
                case "grip":
                case "gripButton":
                case "gripPressed":
                    return "Grip";
                case "primaryButton":
                case "primaryPressed":
                    return "Primary Button (A/X)";
                case "secondaryButton":
                case "secondaryPressed":
                    return "Secondary Button (B/Y)";
                case "menuButton":
                    return "Menu Button";
                case "primary2DAxis":
                case "thumbstick":
                    return "Thumbstick";
                case "primary2DAxisClick":
                case "thumbstickClicked":
                    return "Thumbstick Click";
                case "secondary2DAxis":
                case "touchpad":
                    return "Touchpad";
                case "secondary2DAxisClick":
                case "touchpadClicked":
                    return "Touchpad Click";
                case "devicePosition":
                    return "Controller Position";
                case "deviceRotation":
                    return "Controller Rotation";
                case "centerEyePosition":
                    return "Headset Position";
                case "centerEyeRotation":
                    return "Headset Rotation";
                default:
                    // Fallback: insert spaces before capitals and title-case
                    return System.Text.RegularExpressions.Regex.Replace(
                        control, "([a-z])([A-Z])", "$1 $2");
            }
        }

        // ───────────────────────────────────────────
        // Settings UI
        // ───────────────────────────────────────────

        private static void AddSettingsUI()
        {
            var existing = Object.FindAnyObjectByType<Canvas>();
            if (existing != null && existing.gameObject.name.Contains("Settings UI"))
            {
                EditorUtility.DisplayDialog("Settings UI",
                    "A Settings UI Canvas already exists in the scene.\n\nFound: " + existing.gameObject.name,
                    "OK");
                Selection.activeGameObject = existing.gameObject;
                return;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SETTINGS_UI_PREFAB_PATH);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("Settings UI Not Found",
                    "Could not find the Settings UI prefab at:\n" + SETTINGS_UI_PREFAB_PATH +
                    "\n\nMake sure the U3D template prefab has not been moved or renamed.",
                    "OK");
                return;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = "U3D Settings UI";
            Undo.RegisterCreatedObjectUndo(instance, "Add Settings UI");

            Selection.activeGameObject = instance;
            EditorGUIUtility.PingObject(instance);
        }

        // ───────────────────────────────────────────
        // Screenspace UI
        // ───────────────────────────────────────────

        private static void CreateScreenspaceUI()
        {
            // Check if a screenspace canvas with the same name already exists
            GameObject existing = GameObject.Find("Screenspace UI Canvas");
            if (existing != null)
            {
                EditorUtility.DisplayDialog("Screenspace UI",
                    "A Screenspace UI Canvas already exists in the scene.\n\nFound: " + existing.name +
                    "\n\nYou can have multiple, but you'll want to rename them to tell them apart.",
                    "OK");
                Selection.activeGameObject = existing;
                EditorGUIUtility.PingObject(existing);
                return;
            }

            GameObject canvasObj = new GameObject("Screenspace UI Canvas");

            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasObj.AddComponent<GraphicRaycaster>();

            var uiResources = new DefaultControls.Resources();
            var tmpResources = new TMP_DefaultControls.Resources();

            // Default panel: centered, 400x300. Creators resize or reposition as needed.
            GameObject panelObj = DefaultControls.CreatePanel(uiResources);
            panelObj.name = "Panel";
            panelObj.transform.SetParent(canvasObj.transform, false);
            panelObj.layer = LayerMask.NameToLayer("UI");

            U3DUIStyle.ApplyPanelStyle(panelObj);

            RectTransform panelRect = panelObj.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(400, 300);
            panelRect.anchoredPosition = Vector2.zero;

            U3DUIStyle.CreateHeader(panelObj, "Screenspace UI");

            // Body text: fills the lower 80% of the panel with padding, ready for creator content.
            GameObject textObj = TMP_DefaultControls.CreateText(tmpResources);
            textObj.name = "Body Text";
            textObj.transform.SetParent(panelObj.transform, false);
            textObj.layer = LayerMask.NameToLayer("UI");

            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(1f, 0.8f);
            textRect.offsetMin = new Vector2(15f, 15f);
            textRect.offsetMax = new Vector2(-15f, -10f);

            TextMeshProUGUI bodyTMP = textObj.GetComponent<TextMeshProUGUI>();
            if (bodyTMP != null)
            {
                bodyTMP.text = "Replace this with your content. Add buttons, images, or whatever else you need.";
                U3DUIStyle.ApplyBodyStyle(bodyTMP);
                bodyTMP.textWrappingMode = TextWrappingModes.Normal;
                bodyTMP.raycastTarget = false;
            }

            Selection.activeGameObject = canvasObj;
            EditorGUIUtility.PingObject(canvasObj);
            EditorUtility.SetDirty(canvasObj);
        }

        // ───────────────────────────────────────────
        // Video Screen Material Lookup
        // ───────────────────────────────────────────

        private const string VIDEO_PLAYER_FOLDER = "Assets/U3D/U3D_Assets/Video Player";
        private const string VIDEO_SCREEN_MATERIAL_PATH = "Assets/U3D/U3D_Assets/Video Player/U3D_VideoScreenMaterial.mat";

        /// <summary>
        /// Returns the shared unlit material used by all Video Player screens at edit time.
        /// Creates it on disk if it doesn't exist yet. Runtime instantiates per-Video-Player copies
        /// of this material to hold individual RenderTextures.
        /// </summary>
        private static Material GetOrCreateVideoScreenMaterial()
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(VIDEO_SCREEN_MATERIAL_PATH);
            if (existing != null)
                return existing;

            // Ensure the folder hierarchy exists. Same pattern as CreateMirror's MIRROR_RT_FOLDER setup.
            if (!AssetDatabase.IsValidFolder(VIDEO_PLAYER_FOLDER))
            {
                if (!AssetDatabase.IsValidFolder("Assets/U3D/U3D_Assets"))
                {
                    if (!AssetDatabase.IsValidFolder("Assets/U3D"))
                        AssetDatabase.CreateFolder("Assets", "U3D");
                    AssetDatabase.CreateFolder("Assets/U3D", "U3D_Assets");
                }
                AssetDatabase.CreateFolder("Assets/U3D/U3D_Assets", "Video Player");
            }

            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null)
            {
                Debug.LogWarning("Add Video Player: Universal Render Pipeline/Unlit shader not found. Could not create the shared video screen material. The screen will keep its default Lit material and may show lighting glare at edit time. Runtime will attempt to swap to unlit when the scene plays.");
                return null;
            }

            Material mat = new Material(unlitShader);
            mat.name = System.IO.Path.GetFileNameWithoutExtension(VIDEO_SCREEN_MATERIAL_PATH);
            AssetDatabase.CreateAsset(mat, VIDEO_SCREEN_MATERIAL_PATH);
            AssetDatabase.SaveAssets();
            return mat;
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
            canvasRect.sizeDelta = U3DUIStyle.WorldspaceSingleElementSize;
            canvasRect.localScale = Vector3.one * U3DUIStyle.WorldspaceCanvasScale;

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

            U3DUIStyle.ApplyPanelStyle(panelObj);

            var tmpResources = new TMP_DefaultControls.Resources();
            GameObject textObj = TMP_DefaultControls.CreateText(tmpResources);
            textObj.name = "Text (TMP)";
            textObj.transform.SetParent(panelObj.transform, false);
            textObj.layer = LayerMask.NameToLayer("UI");

            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 10f);
            textRect.offsetMax = new Vector2(-10f, -10f);

            TextMeshProUGUI tmpText = textObj.GetComponent<TextMeshProUGUI>();
            if (tmpText != null)
            {
                tmpText.text = "Worldspace UI Text";
                U3DUIStyle.ApplyBodyStyle(tmpText);
                tmpText.raycastTarget = false;
            }

            if (SceneView.lastActiveSceneView != null)
                canvasObj.transform.position = SceneView.lastActiveSceneView.pivot;

            Selection.activeGameObject = canvasObj;
            EditorGUIUtility.PingObject(canvasObj);
            EditorUtility.SetDirty(canvasObj);
        }
    }
}