using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace U3D.Editor
{
    public class CheckFixTab : ICreatorTab
    {
        public string TabName => "Check & Fix";
        public bool IsComplete { get; private set; }
        public System.Action<int> OnRequestTabSwitch { get; set; }

        private List<IValidationCategory> validationCategories;
        private List<CreatorTool> optimizationTools;
        private int selectedCategoryIndex = 0;
        private Dictionary<string, List<ValidationResult>> categoryResults;
        private bool analysisRunning = false;
        private Vector2 resultsScrollPosition;
        private Vector2 optimizationScrollPosition;

        public void Initialize()
        {
            InitializeValidationCategories();
            InitializeOptimizationTools();
            categoryResults = new Dictionary<string, List<ValidationResult>>();
        }

        private void InitializeValidationCategories()
        {
            validationCategories = new List<IValidationCategory>
            {
                new BuildSettingsValidation(),
                new AssetOptimizationValidation(),
                new PerformanceValidation(),
                new QualityValidation(),
                new ComponentValidation()
            };
        }

        private void InitializeOptimizationTools()
        {
            optimizationTools = new List<CreatorTool>
            {
                new CreatorTool("Optimize All Textures", "Reduce texture sizes for WebGL deployment", OptimizeAllTextures),
                new CreatorTool("Optimize All Audio", "Compress audio files for faster loading", OptimizeAllAudio),
                new CreatorTool("Remove Unity Splash", "Remove Unity splash screen to save ~2.7MB", RemoveUnitySplashScreen),
                new CreatorTool("Analyze Build Size", "Show largest assets and estimated build size", AnalyzeBuildSize),
                new CreatorTool("Find Resources Usage", "Identify Resources folder usage (WebGL performance issue)", FindResourcesFolderUsage)
            };
        }

        public void DrawTab()
        {
            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("Project Health Check", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Run analysis to find and fix issues that could affect your experience.", MessageType.Info);
            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();

            EditorGUI.BeginDisabledGroup(analysisRunning);
            if (GUILayout.Button("Analyze Project", GUILayout.Height(40)))
            {
                RunProjectAnalysis();
            }
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("Clear Results", GUILayout.Height(40), GUILayout.Width(100)))
            {
                categoryResults.Clear();
                IsComplete = false;
            }

            EditorGUILayout.EndHorizontal();

            if (analysisRunning)
            {
                EditorGUILayout.LabelField("🔍 Analyzing...", EditorStyles.boldLabel);
                return;
            }

            EditorGUILayout.Space(10);

            if (categoryResults.Any())
            {
                EditorGUILayout.BeginHorizontal();
                for (int i = 0; i < validationCategories.Count; i++)
                {
                    var category = validationCategories[i];
                    var buttonText = category.CategoryName;

                    if (categoryResults.ContainsKey(category.CategoryName))
                    {
                        var issues = categoryResults[category.CategoryName].Count(r => !r.passed);
                        if (issues > 0)
                        {
                            buttonText += $" ({issues})";
                        }
                        else
                        {
                            buttonText = "✓ " + buttonText;
                        }
                    }

                    var buttonStyle = selectedCategoryIndex == i ? EditorStyles.miniButtonMid : EditorStyles.miniButton;
                    if (GUILayout.Button(buttonText, buttonStyle))
                    {
                        selectedCategoryIndex = i;
                    }
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(10);

                if (selectedCategoryIndex < validationCategories.Count)
                {
                    var selectedCategory = validationCategories[selectedCategoryIndex];
                    if (categoryResults.ContainsKey(selectedCategory.CategoryName))
                    {
                        DrawCategoryResults(selectedCategory.CategoryName, categoryResults[selectedCategory.CategoryName]);
                    }
                }
            }

            // Add Optimization Tools Section
            EditorGUILayout.Space(20);
            EditorGUILayout.LabelField("WebGL Optimization Tools", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("One-click tools to reduce build size and improve WebGL performance.", MessageType.Info);
            EditorGUILayout.Space(10);

            optimizationScrollPosition = EditorGUILayout.BeginScrollView(optimizationScrollPosition);

            foreach (var tool in optimizationTools)
            {
                DrawOptimizationTool(tool);
            }

            EditorGUILayout.EndScrollView();
        }

        private async void RunProjectAnalysis()
        {
            analysisRunning = true;
            categoryResults.Clear();

            try
            {
                foreach (var category in validationCategories)
                {
                    var results = await category.RunChecks();
                    categoryResults[category.CategoryName] = results;
                }

                var totalIssues = categoryResults.Values.SelectMany(r => r).Count(r => !r.passed);
                IsComplete = totalIssues == 0;
            }
            finally
            {
                analysisRunning = false;
            }
        }

        private void DrawCategoryResults(string categoryName, List<ValidationResult> results)
        {
            EditorGUILayout.LabelField($"Results for {categoryName}", EditorStyles.boldLabel);

            var issues = results.Where(r => !r.passed).ToList();
            var passed = results.Where(r => r.passed).ToList();

            if (issues.Any())
            {
                EditorGUILayout.LabelField($"⚠️ {issues.Count} issues found", EditorStyles.boldLabel);
                EditorGUILayout.Space(5);

                resultsScrollPosition = EditorGUILayout.BeginScrollView(resultsScrollPosition);

                foreach (var issue in issues)
                {
                    DrawValidationResult(issue);
                }

                EditorGUILayout.EndScrollView();
            }

            if (passed.Any())
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField($"✅ {passed.Count} checks passed", EditorStyles.boldLabel);
            }
        }

        private void DrawValidationResult(ValidationResult result)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            string severityIcon = result.severity switch
            {
                ValidationSeverity.Critical => "🛑",
                ValidationSeverity.Error => "❌",
                ValidationSeverity.Warning => "⚠️",
                _ => "ℹ️"
            };

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{severityIcon} {result.message}", EditorStyles.wordWrappedLabel);

            if (result.severity != ValidationSeverity.Info)
            {
                if (GUILayout.Button("Fix", GUILayout.Width(60), GUILayout.Height(25)))
                {
                    Debug.Log($"Auto-fixing: {result.message}");
                }
            }

            EditorGUILayout.EndHorizontal();

            if (result.affectedObjects.Any())
            {
                EditorGUILayout.LabelField($"Affects {result.affectedObjects.Count} objects", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3);
        }

        private void DrawOptimizationTool(CreatorTool tool)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(tool.title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(tool.description, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();

            if (GUILayout.Button("Apply", GUILayout.Width(80), GUILayout.Height(35)))
            {
                tool.action?.Invoke();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);
        }

        // SIMPLIFIED TEXTURE OPTIMIZATION - Creator-friendly
        private void OptimizeAllTextures()
        {
            var textureGuids = AssetDatabase.FindAssets("t:Texture2D");
            int optimizedCount = 0;
            int skippedCount = 0;
            long totalSavings = 0;

            foreach (var guid in textureGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;

                if (importer != null && !path.Contains("U3D/"))
                {
                    // Skip system textures that should stay high quality
                    if (ShouldSkipTexture(path, importer))
                    {
                        skippedCount++;
                        continue;
                    }

                    var originalSize = GetEstimatedTextureSize(path);
                    bool modified = false;

                    // Get WebGL settings
                    var webglSettings = importer.GetPlatformTextureSettings("WebGL");

                    // Only optimize if not already optimized
                    if (!webglSettings.overridden || webglSettings.maxTextureSize > 1024)
                    {
                        webglSettings.overridden = true;
                        webglSettings.maxTextureSize = 1024; // Safe size for WebGL
                        webglSettings.compressionQuality = 50; // Good balance
                        webglSettings.format = importer.DoesSourceTextureHaveAlpha() ?
                            TextureImporterFormat.DXT5 : TextureImporterFormat.DXT1;

                        importer.SetPlatformTextureSettings(webglSettings);
                        modified = true;
                    }

                    // Disable mipmaps for UI textures (saves memory)
                    if ((importer.textureType == TextureImporterType.GUI ||
                         importer.textureType == TextureImporterType.Sprite) &&
                         importer.mipmapEnabled)
                    {
                        importer.mipmapEnabled = false;
                        modified = true;
                    }

                    if (modified)
                    {
                        EditorUtility.SetDirty(importer);
                        importer.SaveAndReimport();

                        var newSize = GetEstimatedTextureSize(path);
                        totalSavings += (originalSize - newSize);
                        optimizedCount++;
                    }
                }
            }

            AssetDatabase.Refresh();

            Debug.Log($"🎨 TEXTURE OPTIMIZATION COMPLETE");
            Debug.Log($"✅ Optimized: {optimizedCount} textures");
            Debug.Log($"⏭️ Skipped: {skippedCount} textures (preserved for quality)");
            Debug.Log($"💾 Estimated savings: {totalSavings / 1024 / 1024:F1} MB");
        }

        private bool ShouldSkipTexture(string path, TextureImporter importer)
        {
            // Skip HDR textures, skyboxes, normal maps, and lightmaps
            var extension = Path.GetExtension(path).ToLower();
            if (extension == ".exr" || extension == ".hdr") return true;

            if (importer.textureShape == TextureImporterShape.TextureCube) return true;
            if (importer.textureType == TextureImporterType.Lightmap) return true;
            if (importer.textureType == TextureImporterType.NormalMap) return true;

            var pathLower = path.ToLower();
            if (pathLower.Contains("skybox") || pathLower.Contains("lightmap")) return true;

            return false;
        }

        private int GetEstimatedTextureSize(string assetPath)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            return texture != null ? texture.width * texture.height * 4 : 0; // Rough estimate
        }

        private void OptimizeAllAudio()
        {
            // Open the audio optimization window instead of auto-processing
            AudioOptimizationWindow.ShowWindow();
        }

        private void AnalyzeBuildSize()
        {
            var textureGuids = AssetDatabase.FindAssets("t:Texture2D");
            var audioGuids = AssetDatabase.FindAssets("t:AudioClip");

            long totalTextureSize = 0;
            long totalAudioSize = 0;
            var largeAssets = new List<(string path, long size, string type)>();

            // Quick analysis of file sizes
            foreach (var guid in textureGuids.Take(100))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var fileInfo = new FileInfo(path);
                if (fileInfo.Exists)
                {
                    totalTextureSize += fileInfo.Length;
                    if (fileInfo.Length > 1024 * 1024) // > 1MB
                    {
                        largeAssets.Add((path, fileInfo.Length, "Texture"));
                    }
                }
            }

            foreach (var guid in audioGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var fileInfo = new FileInfo(path);
                if (fileInfo.Exists)
                {
                    totalAudioSize += fileInfo.Length;
                    if (fileInfo.Length > 1024 * 1024) // > 1MB
                    {
                        largeAssets.Add((path, fileInfo.Length, "Audio"));
                    }
                }
            }

            largeAssets = largeAssets.OrderByDescending(a => a.size).Take(10).ToList();

            Debug.Log($"📊 BUILD SIZE ANALYSIS");
            Debug.Log($"Texture files: {totalTextureSize / 1024 / 1024:F1} MB");
            Debug.Log($"Audio files: {totalAudioSize / 1024 / 1024:F1} MB");
            Debug.Log($"");

            if (largeAssets.Any())
            {
                Debug.Log($"🔍 LARGEST ASSETS (optimize these first):");
                foreach (var asset in largeAssets)
                {
                    Debug.Log($"  • {Path.GetFileName(asset.path)} ({asset.type}): {asset.size / 1024 / 1024:F1} MB",
                              AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(asset.path));
                }
            }

            var estimatedTotal = (totalTextureSize + totalAudioSize) / 1024 / 1024;
            if (estimatedTotal > 50)
            {
                Debug.LogWarning($"⚠️ Asset size ({estimatedTotal:F1} MB) may cause large builds. Run texture/audio optimization.");
            }
            else
            {
                Debug.Log($"✅ Asset size ({estimatedTotal:F1} MB) looks good for WebGL.");
            }
        }

        private void RemoveUnitySplashScreen()
        {
            if (PlayerSettings.SplashScreen.show)
            {
                PlayerSettings.SplashScreen.show = false;
                PlayerSettings.SplashScreen.showUnityLogo = false;
                Debug.Log("🎭 Removed Unity splash screen (saves ~2.7MB)");
            }
            else
            {
                Debug.Log("✅ Unity splash screen already disabled");
            }
        }

        private void FindResourcesFolderUsage()
        {
            var resourcesPaths = AssetDatabase.FindAssets("", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.Contains("/Resources/"))
                .ToList();

            if (resourcesPaths.Any())
            {
                Debug.LogWarning($"📁 Found {resourcesPaths.Count} assets in Resources folders:");
                foreach (var path in resourcesPaths.Take(10))
                {
                    Debug.LogWarning($"  • {path}", AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path));
                }
                Debug.LogWarning("💡 Resources folder increases WebGL load times. Consider using Addressables instead.");
            }
            else
            {
                Debug.Log("✅ No Resources folder usage found - perfect for WebGL!");
            }
        }
    }

    // Audio Optimization Window with Creator-friendly UI
    public class AudioOptimizationWindow : EditorWindow
    {
        private class AudioFileData
        {
            public string path;
            public string fileName;
            public AudioImporter importer;
            public long fileSize;
            public bool isSelected;
            public string currentFormat;
            public AudioClipLoadType currentLoadType;

            public AudioFileData(string audioPath)
            {
                path = audioPath;
                fileName = Path.GetFileName(audioPath);
                importer = AssetImporter.GetAtPath(audioPath) as AudioImporter;

                var fileInfo = new FileInfo(audioPath);
                fileSize = fileInfo.Exists ? fileInfo.Length : 0;

                isSelected = true; // Default to selected

                // Get current settings
                if (importer != null)
                {
                    if (importer.ContainsSampleSettingsOverride("WebGL"))
                    {
                        var webglSettings = importer.GetOverrideSampleSettings("WebGL");
                        currentFormat = webglSettings.compressionFormat.ToString();
                        currentLoadType = webglSettings.loadType;
                    }
                    else
                    {
                        var defaultSettings = importer.defaultSampleSettings;
                        currentFormat = defaultSettings.compressionFormat.ToString();
                        currentLoadType = defaultSettings.loadType;
                    }
                }
                else
                {
                    currentFormat = "Unknown";
                    currentLoadType = AudioClipLoadType.DecompressOnLoad;
                }
            }
        }

        private List<AudioFileData> audioFiles = new List<AudioFileData>();
        private Vector2 scrollPosition;
        private bool showOnlyUnoptimized = true;

        public static void ShowWindow()
        {
            var window = GetWindow<AudioOptimizationWindow>("Audio Optimization");
            window.minSize = new Vector2(600, 400);
            window.RefreshAudioList();
            window.Show();
        }

        private void RefreshAudioList()
        {
            audioFiles.Clear();
            var audioGuids = AssetDatabase.FindAssets("t:AudioClip");

            foreach (var guid in audioGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.Contains("U3D/")) // Skip system audio
                {
                    audioFiles.Add(new AudioFileData(path));
                }
            }

            audioFiles = audioFiles.OrderByDescending(a => a.fileSize).ToList();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Audio Optimization for WebGL", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Select audio files and choose optimization preset. This will override WebGL platform settings.", MessageType.Info);
            EditorGUILayout.Space(10);

            // Controls
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Refresh List"))
            {
                RefreshAudioList();
            }

            if (GUILayout.Button("Select All"))
            {
                audioFiles.ForEach(a => a.isSelected = true);
            }

            if (GUILayout.Button("Select None"))
            {
                audioFiles.ForEach(a => a.isSelected = false);
            }

            showOnlyUnoptimized = EditorGUILayout.Toggle("Show Only Unoptimized", showOnlyUnoptimized);

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(10);

            // File list
            var displayFiles = showOnlyUnoptimized ?
                audioFiles.Where(a => a.currentFormat == "PCM" || !HasWebGLOverride(a.importer)).ToList() :
                audioFiles;

            EditorGUILayout.LabelField($"Audio Files ({displayFiles.Count()} total, {displayFiles.Count(f => f.isSelected)} selected):", EditorStyles.boldLabel);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            foreach (var audioFile in displayFiles)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                audioFile.isSelected = EditorGUILayout.Toggle(audioFile.isSelected, GUILayout.Width(20));

                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(audioFile.fileName, EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Size: {audioFile.fileSize / 1024:F0} KB | Format: {audioFile.currentFormat} | Load: {audioFile.currentLoadType}", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();

                if (GUILayout.Button("Select in Project", GUILayout.Width(120)))
                {
                    var audioClip = AssetDatabase.LoadAssetAtPath<AudioClip>(audioFile.path);
                    Selection.activeObject = audioClip;
                    EditorGUIUtility.PingObject(audioClip);
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(2);
            }

            EditorGUILayout.EndScrollView();

            // Preset buttons
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("WebGL Optimization Presets:", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            // Preset 1: Ambient/Music
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Ambient & Music", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("• Streaming load\n• Low quality (size priority)\n• Best for background audio", EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("Apply to Selected", GUILayout.Height(30)))
            {
                ApplyAmbientMusicPreset();
            }
            EditorGUILayout.EndVertical();

            // Preset 2: Instant Load
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Instant Load", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("• Decompress on load\n• Medium quality\n• Best for intro/start sounds", EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("Apply to Selected", GUILayout.Height(30)))
            {
                ApplyInstantLoadPreset();
            }
            EditorGUILayout.EndVertical();

            // Preset 3: One-Shot/UI
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("UI & One-Shot", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("• Compressed in memory\n• High quality\n• Best for UI/interaction sounds", EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("Apply to Selected", GUILayout.Height(30)))
            {
                ApplyOneShotPreset();
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private void ApplyAmbientMusicPreset()
        {
            ApplyPresetToSelected(new AudioImporterSampleSettings
            {
                loadType = AudioClipLoadType.Streaming,
                compressionFormat = AudioCompressionFormat.Vorbis,
                quality = 0.3f, // Lower quality for size
                sampleRateSetting = AudioSampleRateSetting.OptimizeSampleRate
            }, "Ambient/Music");
        }

        private void ApplyInstantLoadPreset()
        {
            ApplyPresetToSelected(new AudioImporterSampleSettings
            {
                loadType = AudioClipLoadType.DecompressOnLoad,
                compressionFormat = AudioCompressionFormat.Vorbis,
                quality = 0.6f, // Medium quality
                sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate
            }, "Instant Load");
        }

        private void ApplyOneShotPreset()
        {
            ApplyPresetToSelected(new AudioImporterSampleSettings
            {
                loadType = AudioClipLoadType.CompressedInMemory,
                compressionFormat = AudioCompressionFormat.Vorbis,
                quality = 0.8f, // Higher quality for UI sounds
                sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate
            }, "UI/One-Shot");
        }

        private void ApplyPresetToSelected(AudioImporterSampleSettings settings, string presetName)
        {
            var selectedFiles = audioFiles.Where(a => a.isSelected).ToList();

            if (!selectedFiles.Any())
            {
                EditorUtility.DisplayDialog("No Selection", "Please select at least one audio file to optimize.", "OK");
                return;
            }

            int optimizedCount = 0;

            foreach (var audioFile in selectedFiles)
            {
                if (audioFile.importer != null)
                {
                    // Set WebGL platform override
                    audioFile.importer.SetOverrideSampleSettings("WebGL", settings);
                    audioFile.importer.forceToMono = false; // Preserve stereo for quality

                    EditorUtility.SetDirty(audioFile.importer);
                    audioFile.importer.SaveAndReimport();
                    optimizedCount++;
                }
            }

            AssetDatabase.Refresh();
            RefreshAudioList(); // Update the list to show new settings

            Debug.Log($"🔊 Applied '{presetName}' preset to {optimizedCount} audio files");

            EditorUtility.DisplayDialog("Optimization Complete",
                $"Applied '{presetName}' preset to {optimizedCount} audio files.\n\nCheck the Console for details.", "OK");
        }

        private bool HasWebGLOverride(AudioImporter importer)
        {
            return importer != null && importer.ContainsSampleSettingsOverride("WebGL");
        }
    }
}