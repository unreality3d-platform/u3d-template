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
                new CreatorTool("Optimize All Textures", "Batch optimize textures by type with compression settings", OptimizeAllTextures),
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

        // ENHANCED TEXTURE OPTIMIZATION - Opens dedicated window
        private void OptimizeAllTextures()
        {
            TextureOptimizationWindow.ShowWindow();
        }

        private void OptimizeAllAudio()
        {
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

    // SOPHISTICATED TEXTURE OPTIMIZATION WINDOW
    public class TextureOptimizationWindow : EditorWindow
    {
        private class TextureFileData
        {
            public string path;
            public string fileName;
            public TextureImporter importer;
            public long fileSize;
            public bool isSelected;
            public TextureImporterType textureType;
            public TextureImporterShape textureShape;
            public int currentMaxSize;
            public bool hasCrunchCompression;
            public bool generateMipMaps;
            public string platformOverride;

            public TextureFileData(string texturePath)
            {
                path = texturePath;
                fileName = Path.GetFileName(texturePath);
                importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;

                var fileInfo = new FileInfo(texturePath);
                fileSize = fileInfo.Exists ? fileInfo.Length : 0;

                isSelected = true; // Default to selected

                if (importer != null)
                {
                    textureType = importer.textureType;
                    textureShape = importer.textureShape;
                    generateMipMaps = importer.mipmapEnabled;

                    // Get WebGL platform settings or default
                    if (importer.GetPlatformTextureSettings("WebGL").overridden)
                    {
                        var webglSettings = importer.GetPlatformTextureSettings("WebGL");
                        currentMaxSize = webglSettings.maxTextureSize;
                        hasCrunchCompression = webglSettings.crunchedCompression;
                        platformOverride = "WebGL Override";
                    }
                    else
                    {
                        var defaultSettings = importer.GetDefaultPlatformTextureSettings();
                        currentMaxSize = defaultSettings.maxTextureSize;
                        hasCrunchCompression = defaultSettings.crunchedCompression;
                        platformOverride = "Default";
                    }
                }
                else
                {
                    textureType = TextureImporterType.Default;
                    textureShape = TextureImporterShape.Texture2D;
                    currentMaxSize = 2048;
                    hasCrunchCompression = false;
                    generateMipMaps = true;
                    platformOverride = "Unknown";
                }
            }

            public bool ShouldExcludeFromSizeOptimization()
            {
                // Exclude normal maps and cube textures (skyboxes) from size optimization
                return textureType == TextureImporterType.NormalMap ||
                       textureShape == TextureImporterShape.TextureCube;
            }

            public bool ShouldExcludeFromCompression()
            {
                // Be more conservative with compression exclusions
                return textureType == TextureImporterType.NormalMap ||
                       textureType == TextureImporterType.Lightmap ||
                       textureShape == TextureImporterShape.TextureCube;
            }
        }

        private List<TextureFileData> textureFiles = new List<TextureFileData>();
        private Vector2 scrollPosition;
        private bool showOnlyUnoptimized = true;
        private bool excludeNormalMaps = true;
        private bool excludeSkyboxes = true;
        private bool showOnlyProjectAssets = true;

        public static void ShowWindow()
        {
            var window = GetWindow<TextureOptimizationWindow>("Texture Optimization");
            window.minSize = new Vector2(700, 500);
            window.RefreshTextureList();
            window.Show();
        }

        private void RefreshTextureList()
        {
            textureFiles.Clear();
            var textureGuids = AssetDatabase.FindAssets("t:Texture2D");
            int totalFound = 0;
            int systemExcluded = 0;
            int fontExcluded = 0;

            foreach (var guid in textureGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                totalFound++;

                // Filter out Unity system assets and packages
                if (ShouldExcludeSystemAsset(path))
                {
                    if (IsFontRelatedTexture(Path.GetFileName(path.ToLower()), path.ToLower()))
                        fontExcluded++;
                    else
                        systemExcluded++;
                    continue;
                }

                textureFiles.Add(new TextureFileData(path));
            }

            textureFiles = textureFiles.OrderByDescending(t => t.fileSize).ToList();

            Debug.Log($"🎨 TEXTURE ANALYSIS: Found {totalFound} total textures");
            Debug.Log($"📂 Showing {textureFiles.Count} project textures");
            Debug.Log($"🚫 Excluded {systemExcluded} system textures, {fontExcluded} font-related textures");
        }

        private bool ShouldExcludeSystemAsset(string path)
        {
            var pathLower = path.ToLower();
            var fileName = Path.GetFileName(pathLower);

            // Always exclude Unity packages and system directories
            if (pathLower.Contains("packages/") ||
                pathLower.Contains("u3d/") ||
                pathLower.Contains("library/"))
                return true;

            // Exclude font files (TTF files shouldn't appear in Texture2D search, but just in case)
            if (pathLower.EndsWith(".ttf") || pathLower.EndsWith(".otf"))
                return true;

            // Exclude ALL font-related textures (comprehensive font filtering)
            if (IsFontRelatedTexture(fileName, pathLower))
                return true;

            // Exclude common Unity generated textures
            if (pathLower.Contains("unity_builtin_extra") ||
                pathLower.Contains("default-") ||
                pathLower.Contains("builtin_"))
                return true;

            // Exclude shader and material preview textures
            if (pathLower.Contains("preview") &&
                (pathLower.Contains("material") || pathLower.Contains("shader")))
                return true;

            return false;
        }

        private bool IsFontRelatedTexture(string fileName, string pathLower)
        {
            // TextMeshPro font assets and atlases
            if (pathLower.Contains("textmeshpro") ||
                fileName.Contains("tmp") ||
                fileName.Contains("sdf"))
                return true;

            // Font atlas patterns (common TextMeshPro naming)
            if (fileName.Contains("atlas") && (
                fileName.Contains("font") ||
                fileName.Contains("liberation") ||
                fileName.Contains("arial") ||
                fileName.Contains("opensans")))
                return true;

            // Common font names that become atlas textures
            string[] fontKeywords = {
                "liberation", "arial", "opensans", "roboto", "ubuntu",
                "calibri", "times", "helvetica", "verdana", "georgia",
                "trebuchet", "impact", "comic", "courier"
            };

            foreach (var keyword in fontKeywords)
            {
                if (fileName.Contains(keyword) && (
                    fileName.Contains("sdf") ||
                    fileName.Contains("atlas") ||
                    fileName.Contains("font")))
                    return true;
            }

            // TextMeshPro generated file patterns
            if (fileName.Contains(" sdf") ||
                fileName.EndsWith("_atlas") ||
                fileName.EndsWith(" atlas") ||
                fileName.Contains("lut"))
                return true;

            return false;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Texture Optimization for WebGL", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Select textures and choose optimization presets. Uses WebGL platform overrides with crunch compression.", MessageType.Info);

            if (showOnlyProjectAssets)
            {
                EditorGUILayout.HelpBox("✅ Showing only your project textures (excludes Unity packages, TextMeshPro font atlases, TTF files, and system assets)", MessageType.Info);
            }

            EditorGUILayout.Space(10);

            // Controls
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Refresh List"))
            {
                RefreshTextureList();
            }

            if (GUILayout.Button("Select All"))
            {
                var filteredTextures = GetFilteredTextures();
                textureFiles.ForEach(t => t.isSelected = filteredTextures.Contains(t));
            }

            if (GUILayout.Button("Select None"))
            {
                textureFiles.ForEach(t => t.isSelected = false);
            }

            if (GUILayout.Button("Show Excluded"))
            {
                ShowExcludedAssets();
            }

            EditorGUILayout.EndHorizontal();

            // Filter controls
            EditorGUILayout.BeginHorizontal();
            showOnlyUnoptimized = EditorGUILayout.Toggle("Show Only Unoptimized", showOnlyUnoptimized);
            showOnlyProjectAssets = EditorGUILayout.Toggle("Project Assets Only", showOnlyProjectAssets);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            excludeNormalMaps = EditorGUILayout.Toggle("Exclude Normal Maps", excludeNormalMaps);
            excludeSkyboxes = EditorGUILayout.Toggle("Exclude Skyboxes", excludeSkyboxes);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(10);

            // File list
            var displayFiles = GetFilteredTextures();

            EditorGUILayout.LabelField($"Texture Files ({displayFiles.Count()} total, {displayFiles.Count(t => t.isSelected)} selected):", EditorStyles.boldLabel);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            foreach (var textureFile in displayFiles)
            {
                DrawTextureFileRow(textureFile);
            }

            EditorGUILayout.EndScrollView();

            // Preset buttons
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("WebGL Optimization Presets:", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            // Preset 1: Ultra Optimized (512px)
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Ultra Optimized", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("• Max size: 512px\n• Crunch compression\n• Quality: 50%\n• Best for size reduction", EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("Apply to Selected", GUILayout.Height(30)))
            {
                ApplyOptimizationPreset(512, true, 50);
            }
            EditorGUILayout.EndVertical();

            // Preset 2: Balanced (1024px)
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Balanced", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("• Max size: 1024px\n• Crunch compression\n• Quality: 50%\n• Good quality/size balance", EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("Apply to Selected", GUILayout.Height(30)))
            {
                ApplyOptimizationPreset(1024, true, 50);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // Mip Map Controls
            EditorGUILayout.LabelField("Mip Map Controls:", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Enable Mip Maps", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("• Better for 3D objects\n• Uses more memory\n• Reduces aliasing at distance", EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("Enable for Selected", GUILayout.Height(30)))
            {
                ApplyMipMapSetting(true);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Disable Mip Maps", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("• Better for UI/2D sprites\n• Saves memory\n• Faster loading", EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("Disable for Selected", GUILayout.Height(30)))
            {
                ApplyMipMapSetting(false);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private List<TextureFileData> GetFilteredTextures()
        {
            var filtered = textureFiles.AsEnumerable();

            if (showOnlyProjectAssets)
            {
                filtered = filtered.Where(t => IsProjectAsset(t.path));
            }

            if (showOnlyUnoptimized)
            {
                filtered = filtered.Where(t => !HasWebGLOptimization(t) || t.currentMaxSize > 1024);
            }

            if (excludeNormalMaps)
            {
                filtered = filtered.Where(t => t.textureType != TextureImporterType.NormalMap);
            }

            if (excludeSkyboxes)
            {
                filtered = filtered.Where(t => t.textureShape != TextureImporterShape.TextureCube);
            }

            return filtered.ToList();
        }

        private bool IsProjectAsset(string path)
        {
            var pathLower = path.ToLower();
            var fileName = Path.GetFileName(pathLower);

            // Only show assets in the main Assets folder (not packages, libraries, etc.)
            if (!pathLower.StartsWith("assets/"))
                return false;

            // Exclude font-related assets even if manually added to Assets
            if (IsFontRelatedTexture(fileName, pathLower))
                return false;

            // Exclude TextMeshPro assets that might be in Assets folder
            if (pathLower.Contains("textmeshpro") ||
                pathLower.Contains("resources/fonts") ||
                pathLower.Contains("tmpresources"))
                return false;

            // Exclude other auto-generated content in Assets
            if (pathLower.Contains("streamingassets") ||
                pathLower.Contains("addressableassetsdata") ||
                pathLower.Contains("xlua"))
                return false;

            return true;
        }

        private void DrawTextureFileRow(TextureFileData textureFile)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            textureFile.isSelected = EditorGUILayout.Toggle(textureFile.isSelected, GUILayout.Width(20));

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(textureFile.fileName, EditorStyles.boldLabel);

            string infoText = $"Size: {textureFile.fileSize / 1024:F0} KB | Type: {textureFile.textureType} | Shape: {textureFile.textureShape}";
            infoText += $"\nMax Size: {textureFile.currentMaxSize} | Crunch: {(textureFile.hasCrunchCompression ? "Yes" : "No")} | Mips: {(textureFile.generateMipMaps ? "Yes" : "No")} | Override: {textureFile.platformOverride}";

            EditorGUILayout.LabelField(infoText, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            if (GUILayout.Button("Select", GUILayout.Width(80)))
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(textureFile.path);
                Selection.activeObject = texture;
                EditorGUIUtility.PingObject(texture);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2);
        }

        private void ApplyOptimizationPreset(int maxTextureSize, bool useCrunchCompression, int quality)
        {
            var selectedFiles = textureFiles.Where(t => t.isSelected).ToList();

            if (!selectedFiles.Any())
            {
                EditorUtility.DisplayDialog("No Selection", "Please select at least one texture to optimize.", "OK");
                return;
            }

            int optimizedCount = 0;
            int skippedCount = 0;

            foreach (var textureFile in selectedFiles)
            {
                if (textureFile.importer != null)
                {
                    // Skip textures that shouldn't be size-optimized
                    bool skipSizeOptimization = textureFile.ShouldExcludeFromSizeOptimization();
                    bool skipCompression = textureFile.ShouldExcludeFromCompression();

                    if (skipSizeOptimization && skipCompression)
                    {
                        skippedCount++;
                        continue;
                    }

                    // Get or create WebGL platform settings
                    var webglSettings = textureFile.importer.GetPlatformTextureSettings("WebGL");
                    webglSettings.overridden = true;

                    // Apply size optimization (if appropriate)
                    if (!skipSizeOptimization)
                    {
                        webglSettings.maxTextureSize = maxTextureSize;
                    }

                    // Apply compression optimization (if appropriate)
                    if (!skipCompression)
                    {
                        webglSettings.crunchedCompression = useCrunchCompression;
                        if (useCrunchCompression)
                        {
                            webglSettings.compressionQuality = quality;
                        }

                        // Use appropriate format based on alpha channel
                        if (textureFile.importer.DoesSourceTextureHaveAlpha())
                        {
                            webglSettings.format = TextureImporterFormat.DXT5Crunched;
                        }
                        else
                        {
                            webglSettings.format = TextureImporterFormat.DXT1Crunched;
                        }
                    }

                    textureFile.importer.SetPlatformTextureSettings(webglSettings);
                    EditorUtility.SetDirty(textureFile.importer);
                    textureFile.importer.SaveAndReimport();
                    optimizedCount++;
                }
            }

            AssetDatabase.Refresh();
            RefreshTextureList();

            Debug.Log($"🎨 TEXTURE OPTIMIZATION COMPLETE");
            Debug.Log($"✅ Optimized: {optimizedCount} textures (max size: {maxTextureSize}px, crunch: {useCrunchCompression})");
            Debug.Log($"⏭️ Skipped: {skippedCount} textures (normal maps/skyboxes preserved)");

            EditorUtility.DisplayDialog("Optimization Complete",
                $"Applied optimization to {optimizedCount} textures.\nSkipped {skippedCount} textures (normal maps/skyboxes).\n\nCheck Console for details.", "OK");
        }

        private void ApplyMipMapSetting(bool enableMipMaps)
        {
            var selectedFiles = textureFiles.Where(t => t.isSelected).ToList();

            if (!selectedFiles.Any())
            {
                EditorUtility.DisplayDialog("No Selection", "Please select at least one texture.", "OK");
                return;
            }

            int modifiedCount = 0;

            foreach (var textureFile in selectedFiles)
            {
                if (textureFile.importer != null && textureFile.importer.mipmapEnabled != enableMipMaps)
                {
                    textureFile.importer.mipmapEnabled = enableMipMaps;
                    EditorUtility.SetDirty(textureFile.importer);
                    textureFile.importer.SaveAndReimport();
                    modifiedCount++;
                }
            }

            AssetDatabase.Refresh();
            RefreshTextureList();

            Debug.Log($"🔧 MIP MAP SETTING APPLIED");
            Debug.Log($"✅ Modified: {modifiedCount} textures (mip maps: {(enableMipMaps ? "enabled" : "disabled")})");

            EditorUtility.DisplayDialog("Mip Map Setting Applied",
                $"Modified {modifiedCount} textures.\nMip maps are now {(enableMipMaps ? "enabled" : "disabled")} for selected textures.", "OK");
        }

        private void ShowExcludedAssets()
        {
            var textureGuids = AssetDatabase.FindAssets("t:Texture2D");
            var excludedAssets = new List<string>();
            var fontAssets = new List<string>();

            foreach (var guid in textureGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (ShouldExcludeSystemAsset(path))
                {
                    if (IsFontRelatedTexture(Path.GetFileName(path.ToLower()), path.ToLower()))
                        fontAssets.Add(path);
                    else
                        excludedAssets.Add(path);
                }
            }

            Debug.Log($"🔍 EXCLUDED ASSETS REPORT ({excludedAssets.Count + fontAssets.Count} total)");

            if (fontAssets.Any())
            {
                Debug.Log($"\n📝 FONT-RELATED TEXTURES ({fontAssets.Count}):");
                foreach (var asset in fontAssets.Take(10))
                {
                    Debug.Log($"  • {asset}");
                }
                if (fontAssets.Count > 10)
                    Debug.Log($"  ... and {fontAssets.Count - 10} more font textures");
            }

            if (excludedAssets.Any())
            {
                Debug.Log($"\n🛠️ SYSTEM/PACKAGE TEXTURES ({excludedAssets.Count}):");
                foreach (var asset in excludedAssets.Take(10))
                {
                    Debug.Log($"  • {asset}");
                }
                if (excludedAssets.Count > 10)
                    Debug.Log($"  ... and {excludedAssets.Count - 10} more system textures");
            }

            Debug.Log($"\n✅ This filtering keeps your optimization tool focused on actual project textures!");
        }

        private bool HasWebGLOptimization(TextureFileData textureFile)
        {
            return textureFile.importer != null &&
                   textureFile.importer.GetPlatformTextureSettings("WebGL").overridden;
        }
    }

    // Audio Optimization Window (preserved for completeness)
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

                isSelected = true;

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
                if (!path.Contains("U3D/"))
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
                quality = 0.3f,
                sampleRateSetting = AudioSampleRateSetting.OptimizeSampleRate
            }, "Ambient/Music");
        }

        private void ApplyInstantLoadPreset()
        {
            ApplyPresetToSelected(new AudioImporterSampleSettings
            {
                loadType = AudioClipLoadType.DecompressOnLoad,
                compressionFormat = AudioCompressionFormat.Vorbis,
                quality = 0.6f,
                sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate
            }, "Instant Load");
        }

        private void ApplyOneShotPreset()
        {
            ApplyPresetToSelected(new AudioImporterSampleSettings
            {
                loadType = AudioClipLoadType.CompressedInMemory,
                compressionFormat = AudioCompressionFormat.Vorbis,
                quality = 0.8f,
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
                    audioFile.importer.SetOverrideSampleSettings("WebGL", settings);
                    audioFile.importer.forceToMono = false;

                    EditorUtility.SetDirty(audioFile.importer);
                    audioFile.importer.SaveAndReimport();
                    optimizedCount++;
                }
            }

            AssetDatabase.Refresh();
            RefreshAudioList();

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