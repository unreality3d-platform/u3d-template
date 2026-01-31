using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace U3D.Editor
{
    public class U3DCreatorWindow : EditorWindow
    {
        [SerializeField] private int selectedTab = 0;
        [SerializeField] private Vector2 scrollPosition;

        private List<ICreatorTab> tabs;
        private GUIStyle headerStyle;
        private GUIStyle tabButtonStyle;
        private GUIStyle activeTabButtonStyle;
        private bool stylesInitialized = false;
        private Texture2D logoTexture;

        /// <summary>
        /// CRITICAL: Check if we should skip operations during actual builds (not editor startup)
        /// </summary>
        private static bool ShouldSkipDuringBuild()
        {
            // Only skip for actual build operations, not editor initialization
            return BuildPipeline.isBuildingPlayer ||
                   EditorApplication.isCompiling;
        }

        /// <summary>
        /// Check if editor is still initializing (separate from build operations)
        /// </summary>
        private static bool IsEditorInitializing()
        {
            return EditorApplication.isUpdating;
        }

        [MenuItem("U3D/Creator Dashboard")]
        public static void ShowWindow()
        {
            var window = GetWindow<U3DCreatorWindow>("U3D Creator Dashboard");
            window.minSize = new Vector2(320, 560);
            window.Show();
        }

        [InitializeOnLoadMethod]
        static void OpenOnStartup()
        {
            // Single-shot registration - no recursion
            EditorApplication.delayCall += OnEditorReady;
        }

        static void OnEditorReady()
        {
            // Skip if build is in progress
            if (BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling)
                return;

            // Skip during play mode transitions
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            bool hasOpenedBefore = EditorPrefs.GetBool("U3D_HasOpenedBefore", false);
            bool showOnStartup = EditorPrefs.GetBool("U3D_ShowOnStartup", true);

            if (!hasOpenedBefore || showOnStartup)
            {
                ShowWindow();
                EditorPrefs.SetBool("U3D_HasOpenedBefore", true);
            }
        }

        void OnEnable()
        {
            InitializeTabs();
            LoadLogo();
            // DON'T initialize styles here - wait for OnGUI when Editor is ready
        }

        void LoadLogo()
        {
            string[] guids = AssetDatabase.FindAssets("U3D512Logo t:Texture2D");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                logoTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
        }

        void InitializeTabs()
        {
            tabs = new List<ICreatorTab>
            {
                new SetupTab(),
                new ProjectToolsTab(),
                new CheckFixTab(),
                new PublishTab()
            };

            foreach (var tab in tabs)
            {
                tab.Initialize();
                // Add navigation callback
                tab.OnRequestTabSwitch = SwitchToTab;
            }
        }

        // Navigation method for tabs to request tab switches
        private void SwitchToTab(int tabIndex)
        {
            if (tabIndex >= 0 && tabIndex < tabs.Count)
            {
                selectedTab = tabIndex;
                Repaint(); // Refresh the window
            }
        }

        void InitializeStyles()
        {
            if (stylesInitialized) return;

            try
            {
                // Only create styles if EditorStyles is ready
                if (EditorStyles.boldLabel == null) return;

                headerStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 18,
                    normal = { textColor = new Color(0.4f, 0.5f, 0.9f) },
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true
                };

                // Use the same base style as your existing buttons
                tabButtonStyle = new GUIStyle("Button")
                {
                    fontSize = 12,
                    fixedHeight = 35,
                    normal = {
                        textColor = Color.white
                    },
                    hover = {
                        textColor = Color.white
                    },
                    onNormal = {
                        textColor = Color.white
                    }
                };

                activeTabButtonStyle = new GUIStyle("Button")
                {
                    fontSize = 12,
                    fixedHeight = 35,
                    normal = {
                        textColor = Color.white
                    },
                    hover = {
                        textColor = Color.white
                    },
                    onNormal = {
                        textColor = Color.white
                    },
                    fontStyle = FontStyle.Bold
                };

                stylesInitialized = true;
            }
            catch (System.Exception)
            {
                // EditorStyles not ready yet, will try again next OnGUI call
                return;
            }
        }

        void OnGUI()
        {
            // Initialize styles safely during OnGUI when Editor is ready
            InitializeStyles();

            DrawHeader();
            DrawTabNavigation();
            DrawCurrentTab();

            // Add startup preference control at bottom
            DrawStartupPreference();
        }

        void DrawStartupPreference()
        {
            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            // FIXED: Use build guards for EditorPrefs access
            bool showOnStartup = true;
            if (!ShouldSkipDuringBuild())
            {
                showOnStartup = EditorPrefs.GetBool("U3D_ShowOnStartup", true);
            }

            bool newShowOnStartup = EditorGUILayout.ToggleLeft("Show dashboard when Unity starts", showOnStartup, GUILayout.Width(250));

            if (newShowOnStartup != showOnStartup)
            {
                // FIXED: Use build guards for EditorPrefs access
                if (!ShouldSkipDuringBuild())
                {
                    EditorPrefs.SetBool("U3D_ShowOnStartup", newShowOnStartup);
                }
            }

            // Add Reset Startup Setup button
            GUILayout.Space(10);
            if (GUILayout.Button("Reset Startup Setup", GUILayout.Width(140)))
            {
                bool confirmed = EditorUtility.DisplayDialog(
                    "Reset Startup Setup",
                    "This will reset the template startup configuration, allowing the startup scene to load again when this project is next opened.\n\n" +
                    "This is useful for testing the first-time template experience.",
                    "Reset",
                    "Cancel"
                );

                if (confirmed)
                {
                    ProjectStartupConfiguration.ResetTemplateConfiguration();
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        void DrawHeader()
        {
            float windowWidth = position.width;

            if (logoTexture != null)
            {
                float maxLogoWidth = windowWidth * 0.8f;
                float maxLogoHeight = 180f;

                float aspectRatio = (float)logoTexture.width / logoTexture.height;
                float logoWidth = Mathf.Min(maxLogoWidth, maxLogoHeight * aspectRatio);
                float logoHeight = logoWidth / aspectRatio;

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                Rect logoRect = GUILayoutUtility.GetRect(logoWidth, logoHeight,
                    GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
                GUI.DrawTexture(logoRect, logoTexture, ScaleMode.ScaleToFit);

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }

            DrawTemplateVersionInfo();
            EditorGUILayout.Space(8);
        }

        void DrawTabNavigation()
        {
            EditorGUILayout.BeginHorizontal();

            for (int i = 0; i < tabs.Count; i++)
            {
                var tabName = tabs[i].TabName;

                if (tabs[i].IsComplete)
                {
                    tabName = "✓ " + tabName;
                }

                bool isSelected = selectedTab == i;
                bool newSelection = GUILayout.Toggle(isSelected, tabName,
                    isSelected ? activeTabButtonStyle : tabButtonStyle);

                if (newSelection && !isSelected)
                {
                    selectedTab = i;
                }

                if (i < tabs.Count - 1)
                {
                    GUILayout.Space(1f);
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(5);
        }

        void DrawCurrentTab()
        {
            if (tabs != null && selectedTab < tabs.Count)
            {
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
                tabs[selectedTab].DrawTab();
                EditorGUILayout.EndScrollView();
            }
        }

        /// <summary>
        /// Displays template version info - call this in DrawHeader() after the logo
        /// </summary>
        void DrawTemplateVersionInfo()
        {
            string templateVersion = GetTemplateVersion();

            if (!string.IsNullOrEmpty(templateVersion))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                // Create a subtle style for version display
                var versionStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    fontSize = 10,
                    normal = { textColor = new Color(0.7f, 0.7f, 0.7f) },
                    alignment = TextAnchor.MiddleCenter
                };

                EditorGUILayout.LabelField($"Template Version: {templateVersion}", versionStyle);

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(3);
            }
        }

        /// <summary>
        /// Gets the current template version from Assets/U3D/Resources/version.txt
        /// </summary>
        string GetTemplateVersion()
        {
            try
            {
                // Try to load the version file from Resources
                TextAsset versionAsset = Resources.Load<TextAsset>("version");
                if (versionAsset != null)
                {
                    return versionAsset.text.Trim();
                }

                // Fallback: try to read directly from file system
                string versionPath = "Assets/U3D/Resources/version.txt";
                if (System.IO.File.Exists(versionPath))
                {
                    return System.IO.File.ReadAllText(versionPath).Trim();
                }
            }
            catch (System.Exception ex)
            {
                // Silent fail - don't spam console during normal operation
                Debug.LogWarning($"Could not load U3D template version: {ex.Message}");
            }

            return null; // Don't show anything if version can't be determined
        }

        private Texture2D CreateRoundedTexture(Color color)
        {
            int size = 16;
            Texture2D texture = new Texture2D(size, size);
            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    pixels[y * size + x] = color;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++)
                pix[i] = col;

            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }
    }
}