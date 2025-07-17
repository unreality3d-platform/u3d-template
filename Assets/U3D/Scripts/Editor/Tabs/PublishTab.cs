using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Text;
using System.Reflection;


namespace U3D.Editor
{
    public class PublishTab : ICreatorTab
    {
        public string TabName => "Publish";
        public bool IsComplete { get; private set; }
        public System.Action<int> OnRequestTabSwitch { get; set; }

        private PublishStep currentStep = PublishStep.Ready;
        private string publishUrl = "";
        private string cachedProductName;
        private bool githubConnected = false;
        private bool projectBuilt = false;
        private bool deploymentComplete = false;
        private Vector2 scrollPosition;
        private string currentStatus = "";
        private bool isPublishing = false;
        private bool shouldCreateNewRepository = false;

        private List<ProjectOption> availableOptions = new List<ProjectOption>();
        private bool optionsLoaded = false;
        private bool loadingOptions = false;
        private int selectedOptionIndex = -1;

        private enum PublishStep
        {
            Ready,
            BuildingLocally,
            CreatingRepository,
            DeployingToGitHub,
            Complete
        }

        /// <summary>
        /// CRITICAL: Check if we should skip operations during builds
        /// </summary>
        private static bool ShouldSkipDuringBuild()
        {
            return BuildPipeline.isBuildingPlayer ||
                   EditorApplication.isCompiling ||
                   EditorApplication.isUpdating;
        }

        public void Initialize()
        {
            // Cache product name on main thread to avoid threading issues
            cachedProductName = Application.productName;

            // CRITICAL: Skip other initialization during builds
            if (ShouldSkipDuringBuild())
            {
                return;
            }

            // FIXED: Don't automatically show success view on tab load
            // Success view should only show immediately after successful publish

            // Clear any stale "just published" flag on tab initialization
            var justPublished = EditorPrefs.GetBool("U3D_JustPublished", false);
            if (justPublished)
            {
                // This was a fresh session load, clear the flag
                EditorPrefs.DeleteKey("U3D_JustPublished");
            }

            // Always start in Ready state to show repository options
            currentStep = PublishStep.Ready;
            IsComplete = false;
            publishUrl = "";

            // Reset all states to show fresh options
            githubConnected = false;
            projectBuilt = false;
            deploymentComplete = false;
            isPublishing = false;
        }

        private void MarkPublishSuccess(string successUrl, string repositoryName)
        {
            // Set success state
            publishUrl = successUrl;
            IsComplete = true;
            currentStep = PublishStep.Complete;
            githubConnected = true;
            projectBuilt = true;
            deploymentComplete = true;

            // Mark that we just published (for this session only)
            EditorPrefs.SetBool("U3D_JustPublished", true);
            EditorPrefs.SetString("U3D_PublishedURL", successUrl);
            EditorPrefs.SetString("U3D_LastRepositoryName", repositoryName);

            Debug.Log($"✅ Marked publish success for: {repositoryName}");
        }

        private bool ValidateProductName(string productName, out string error)
        {
            error = null;

            // Handle null/empty
            if (string.IsNullOrWhiteSpace(productName))
            {
                error = "Product Name cannot be empty";
                return false;
            }

            // Handle length (GitHub repo limit is 100, leave room for sanitization)
            if (productName.Length > 80)
            {
                error = "Product Name too long (max 80 characters)";
                return false;
            }

            // Handle reserved names that conflict with infrastructure
            var reservedNames = new[] { "admin", "api", "www", "test", "app", "web", "dev", "staging" };
            if (reservedNames.Contains(productName.ToLower()))
            {
                error = "Product Name conflicts with reserved words";
                return false;
            }

            // Handle characters that cause encoding issues
            if (productName.Any(c => c > 127)) // Non-ASCII characters
            {
                error = "Product Name should use only standard English characters for best compatibility";
                return false;
            }

            return true;
        }

        private void ResetPublishState()
        {
            EditorPrefs.DeleteKey("U3D_PublishedURL");
            publishUrl = "";
            githubConnected = false;
            projectBuilt = false;
            deploymentComplete = false;
            currentStep = PublishStep.Ready;
            IsComplete = false;
            currentStatus = "";
            isPublishing = false;
            shouldCreateNewRepository = true;

            Debug.Log("Publish state reset - ready to publish again");
        }

        public void OnFocus()
        {
            // Check for external Product Name changes when tab gains focus
            var currentProductName = Application.productName;
            if (currentProductName != cachedProductName)
            {
                Debug.Log($"Product Name changed externally: '{cachedProductName}' → '{currentProductName}'");
                cachedProductName = currentProductName;

                // Reset options so they reload with new name
                optionsLoaded = false;
                loadingOptions = false;
            }
        }

        public void DrawTab()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Publish Your Content", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Share your creation with the world! This will make your content live on the internet.", MessageType.Info);
            EditorGUILayout.Space(15);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            // Check prerequisites
            if (!CanPublish())
            {
                DrawPrerequisites();
            }
            else if (currentStep == PublishStep.Ready && !isPublishing)
            {
                DrawReadyToPublish();
            }

            EditorGUILayout.Space(10);

            if (isPublishing || currentStep != PublishStep.Ready)
            {
                DrawPublishingSteps();
            }

            if (!string.IsNullOrEmpty(currentStatus))
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(currentStatus, MessageType.Info);
            }

            if (currentStep == PublishStep.Complete)
            {
                DrawSuccessSection();
            }

            EditorGUILayout.EndScrollView();
        }

        private bool CanPublish()
        {
            return U3DAuthenticator.IsLoggedIn &&
                   !string.IsNullOrEmpty(U3DAuthenticator.CreatorUsername) &&
                   GitHubTokenManager.HasValidToken;
        }

        private void DrawPrerequisites()
        {
            EditorGUILayout.LabelField("Prerequisites", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            if (!U3DAuthenticator.IsLoggedIn)
            {
                EditorGUILayout.HelpBox("❌ Please complete authentication in the Setup tab first.", MessageType.Warning);
                if (GUILayout.Button("Go to Setup Tab"))
                {
                    OnRequestTabSwitch?.Invoke(0);
                }
            }
            else if (string.IsNullOrEmpty(U3DAuthenticator.CreatorUsername))
            {
                EditorGUILayout.HelpBox("❌ Please reserve your creator username in the Setup tab first.", MessageType.Warning);
                if (GUILayout.Button("Go to Setup Tab"))
                {
                    OnRequestTabSwitch?.Invoke(0);
                }
            }
            else if (!GitHubTokenManager.HasValidToken)
            {
                EditorGUILayout.HelpBox("❌ GitHub token not configured. Please set up your GitHub token in the Setup tab.", MessageType.Warning);
                if (GUILayout.Button("Go to Setup Tab"))
                {
                    OnRequestTabSwitch?.Invoke(0);
                }
            }
        }

        private void DrawReadyToPublish()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Load repository options if not loaded
            if (!optionsLoaded && !loadingOptions)
            {
                EditorGUILayout.LabelField("Analyzing your GitHub repositories...", EditorStyles.centeredGreyMiniLabel);
                loadingOptions = true;
                _ = LoadRepositoryOptionsAsync();
            }
            else if (loadingOptions)
            {
                EditorGUILayout.LabelField("🔍 Checking your GitHub repositories...", EditorStyles.centeredGreyMiniLabel);
            }
            else if (optionsLoaded)
            {
                DrawRepositoryOptions();
            }

            EditorGUILayout.EndVertical();
        }

        private async Task LoadRepositoryOptionsAsync()
        {
            try
            {
                availableOptions.Clear();

                // Use sanitized Product Name for search since repo names are always sanitized
                var sanitizedProductName = GitHubAPI.SanitizeRepositoryName(cachedProductName);

                // Get repositories that match current Product Name
                var repoResult = await GitHubAPI.GetUserRepositories(sanitizedProductName, 50);

                if (!repoResult.Success)
                {
                    Debug.LogError($"Failed to load repositories: {repoResult.ErrorMessage}");
                    optionsLoaded = true;
                    loadingOptions = false;
                    return;
                }

                // Analyze each repository and add UpdateExisting options
                foreach (var repo in repoResult.Repositories)
                {
                    // Check if it's a Unity project
                    repo.IsUnityProject = await GitHubAPI.HasUnityWebGLFiles(repo.Name);

                    // Get GitHub Pages URL if available
                    if (repo.HasPages)
                    {
                        repo.GitHubPagesUrl = await GitHubAPI.GetGitHubPagesUrl(repo.Name);
                    }

                    // Create update option for existing repos that match current Product Name
                    if (IsRelatedRepository(repo.Name, cachedProductName))
                    {
                        availableOptions.Add(new ProjectOption
                        {
                            Type = ProjectOption.OptionType.UpdateExisting,
                            RepositoryName = repo.Name,
                            DisplayName = $"Update \"{repo.Name}\"",
                            Description = repo.IsUnityProject ? "Unity WebGL repository" : "Repository",
                            ProfessionalUrl = $"https://{U3DAuthenticator.CreatorUsername}.unreality3d.com/{repo.Name}/",
                            GitHubPagesUrl = repo.GitHubPagesUrl,
                            LastUpdated = repo.UpdatedAt,
                            IsUnityProject = repo.IsUnityProject
                        });
                    }
                }

                // Always add "Create New Repository" option 
                availableOptions.Add(new ProjectOption
                {
                    Type = ProjectOption.OptionType.CreateNew,
                    RepositoryName = "new-repository", // Placeholder - will be updated based on Product Name
                    DisplayName = "Create New Repository",
                    Description = "New Unity WebGL repository",
                    ProfessionalUrl = $"https://{U3DAuthenticator.CreatorUsername}.unreality3d.com/[product-name]/",
                    GitHubPagesUrl = null,
                    LastUpdated = null,
                    IsUnityProject = false
                });

                // Auto-select the first option
                if (availableOptions.Count > 0)
                {
                    selectedOptionIndex = 0;
                }

                optionsLoaded = true;
                loadingOptions = false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error loading repository options: {ex.Message}");
                optionsLoaded = true;
                loadingOptions = false;
            }
        }

        private void DrawRepositoryOptions()
        {
            if (availableOptions.Count == 0)
            {
                EditorGUILayout.HelpBox("No GitHub repositories found. A new repository will be created.", MessageType.Info);

                if (GUILayout.Button("Make It Live!", GUILayout.Height(50)))
                {
                    shouldCreateNewRepository = true;
                    _ = StartFirebasePublishProcessAsync();
                }
                return;
            }

            EditorGUILayout.LabelField("Choose your publishing option:", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // Check if current Product Name conflicts with existing repositories
            var hasNameConflict = availableOptions.Any(opt =>
                opt.Type == ProjectOption.OptionType.UpdateExisting &&
                string.Equals(opt.RepositoryName, GitHubAPI.SanitizeRepositoryName(cachedProductName), StringComparison.OrdinalIgnoreCase));

            // Draw radio button options
            for (int i = 0; i < availableOptions.Count; i++)
            {
                var option = availableOptions[i];
                var isSelected = selectedOptionIndex == i;

                EditorGUILayout.BeginVertical(isSelected ? EditorStyles.helpBox : EditorStyles.textArea);

                EditorGUILayout.BeginHorizontal();

                // Radio button
                var newSelected = EditorGUILayout.Toggle(isSelected, GUILayout.Width(20));
                if (newSelected && !isSelected)
                {
                    selectedOptionIndex = i;
                }

                // Option details
                EditorGUILayout.BeginVertical();

                var style = new GUIStyle(EditorStyles.boldLabel);

                // Color logic - Green for update existing, Yellow for create new
                if (option.Type == ProjectOption.OptionType.CreateNew && option.RepositoryName == "new-repository")
                {
                    style.normal.textColor = Color.yellow;
                }
                else
                {
                    style.normal.textColor = Color.green;
                }

                // Display text
                string displayText;
                if (option.Type == ProjectOption.OptionType.UpdateExisting)
                {
                    displayText = option.DisplayName; // "Update 'repo-name'"
                }
                else
                {
                    displayText = "Create New Repository"; // The create new option
                }

                EditorGUILayout.LabelField(displayText, style);
                EditorGUILayout.LabelField(option.Description, EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"URL: {option.ProfessionalUrl}", EditorStyles.miniLabel);

                if (option.LastUpdated.HasValue)
                {
                    EditorGUILayout.LabelField($"Last updated: {option.LastUpdated.Value:MMM dd, yyyy}", EditorStyles.miniLabel);
                }

                if (!string.IsNullOrEmpty(option.GitHubPagesUrl))
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Currently live at:", EditorStyles.miniLabel, GUILayout.Width(90));
                    if (GUILayout.Button(option.GitHubPagesUrl, EditorStyles.linkLabel))
                    {
                        Application.OpenURL(option.GitHubPagesUrl);
                    }
                    EditorGUILayout.EndHorizontal();
                }

                // Special handling for "Create New Repository" option
                if (option.Type == ProjectOption.OptionType.CreateNew && option.RepositoryName == "new-repository")
                {
                    EditorGUILayout.Space(5);

                    // Show editable Product Name field with Update button
                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                    EditorGUILayout.LabelField("Product Name:", EditorStyles.miniLabel, GUILayout.Width(80));

                    EditorGUI.BeginChangeCheck();
                    var newProductName = EditorGUILayout.TextField(cachedProductName);
                    bool nameChangedInField = EditorGUI.EndChangeCheck();

                    // Update button logic - enabled when field differs from PlayerSettings
                    bool namesDiffer = !string.IsNullOrEmpty(newProductName) && newProductName != PlayerSettings.productName;

                    GUI.enabled = namesDiffer;
                    if (GUILayout.Button("Update", GUILayout.Width(60)))
                    {
                        // Update Unity's PlayerSettings
                        PlayerSettings.productName = newProductName;
                        cachedProductName = newProductName;

                        Debug.Log($"Product Name updated to: {newProductName}");

                        // Recalculate validation for immediate feedback
                        hasNameConflict = availableOptions.Any(opt =>
                            opt.Type == ProjectOption.OptionType.UpdateExisting &&
                            string.Equals(opt.RepositoryName, GitHubAPI.SanitizeRepositoryName(cachedProductName), StringComparison.OrdinalIgnoreCase));
                    }
                    GUI.enabled = true;

                    // Update cached name for real-time validation feedback
                    if (nameChangedInField)
                    {
                        cachedProductName = newProductName;
                        // Recalculate conflict status for immediate visual feedback
                        hasNameConflict = availableOptions.Any(opt =>
                            opt.Type == ProjectOption.OptionType.UpdateExisting &&
                            string.Equals(opt.RepositoryName, GitHubAPI.SanitizeRepositoryName(cachedProductName), StringComparison.OrdinalIgnoreCase));
                    }

                    EditorGUILayout.EndHorizontal();

                    // Show validation message if name conflicts with existing repos
                    if (hasNameConflict && isSelected)
                    {
                        EditorGUILayout.Space(3);
                        EditorGUILayout.HelpBox(
                            "⚠️ This Product Name matches an existing repository. Please change the Product Name above to create a new repository.",
                            MessageType.Warning
                        );
                    }
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space(3);
            }

            EditorGUILayout.Space(10);

            // Publish button validation
            bool canPublish = selectedOptionIndex >= 0;
            string validationError = null;

            // Additional validation for Create New Repository option with name conflict
            if (canPublish && selectedOptionIndex < availableOptions.Count)
            {
                var selectedOption = availableOptions[selectedOptionIndex];
                if (selectedOption.Type == ProjectOption.OptionType.CreateNew &&
                    selectedOption.RepositoryName == "new-repository" &&
                    hasNameConflict)
                {
                    canPublish = false;
                }
            }

            // Validate Product Name before allowing publish
            if (canPublish && !ValidateProductName(cachedProductName, out validationError))
            {
                canPublish = false;
            }

            GUI.enabled = canPublish;
            if (GUILayout.Button("Make It Live!", GUILayout.Height(50)))
            {
                var selectedOption = availableOptions[selectedOptionIndex];

                string targetRepositoryName;

                if (selectedOption.Type == ProjectOption.OptionType.CreateNew)
                {
                    // Use current Product Name for new repository
                    targetRepositoryName = GitHubAPI.SanitizeRepositoryName(cachedProductName);
                    shouldCreateNewRepository = true;
                    Debug.Log($"🎯 CREATE NEW REPOSITORY: '{targetRepositoryName}' from Product Name: '{cachedProductName}'");
                }
                else
                {
                    // Update existing repository
                    targetRepositoryName = selectedOption.RepositoryName;
                    shouldCreateNewRepository = false;
                    Debug.Log($"🎯 UPDATE EXISTING REPOSITORY: '{targetRepositoryName}' (Product Name: '{cachedProductName}')");
                }

                // Store the target repository name for deployment
                EditorPrefs.SetString("U3D_TargetRepository", targetRepositoryName);

                _ = StartFirebasePublishProcessAsync();
            }
            GUI.enabled = true;

            // Show validation errors
            if (!canPublish && !string.IsNullOrEmpty(validationError))
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox($"⚠️ {validationError}", MessageType.Warning);
            }

            // Show helpful message when button is disabled due to name conflict
            if (selectedOptionIndex >= 0 && selectedOptionIndex < availableOptions.Count)
            {
                var selectedOption = availableOptions[selectedOptionIndex];
                if (selectedOption.Type == ProjectOption.OptionType.CreateNew &&
                    selectedOption.RepositoryName == "new-repository" &&
                    hasNameConflict && string.IsNullOrEmpty(validationError))
                {
                    EditorGUILayout.Space(5);
                    EditorGUILayout.HelpBox(
                        "💡 Change the Product Name above and click 'Update' to create a repository with a unique name.",
                        MessageType.Info
                    );
                }
            }
        }

        private bool IsRelatedRepository(string repoName, string productName)
        {
            var sanitizedProduct = GitHubAPI.SanitizeRepositoryName(productName);
            var lowerRepo = repoName.ToLower();
            var lowerProduct = sanitizedProduct.ToLower();

            // Exact match
            if (lowerRepo == lowerProduct)
                return true;

            // Product name is contained in repo name
            if (lowerRepo.Contains(lowerProduct))
                return true;

            return false;
        }

        private async System.Threading.Tasks.Task StartFirebasePublishProcessAsync()
        {
            try
            {
                await StartFirebasePublishProcess();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Firebase async publishing failed: {ex.Message}");
                EditorUtility.DisplayDialog("Publishing Failed", $"There was an error: {ex.Message}", "OK");

                currentStep = PublishStep.Ready;
                currentStatus = $"Publishing failed: {ex.Message}";

                // Reset states
                githubConnected = false;
                projectBuilt = false;
                deploymentComplete = false;
                isPublishing = false;
            }
        }

        private void DrawPublishingSteps()
        {
            EditorGUILayout.LabelField("Publishing Progress", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            DrawStep("Local Unity Build",
                projectBuilt,
                currentStep == PublishStep.BuildingLocally,
                "✓ Unity WebGL build completed",
                "🔨 Building Unity WebGL locally...");

            DrawStep("GitHub Repository",
                githubConnected,
                currentStep == PublishStep.CreatingRepository,
                "✓ GitHub repository ready",
                "🔗 Setting up GitHub repository...");

            DrawStep("Deploy to Web",
                deploymentComplete,
                currentStep == PublishStep.DeployingToGitHub,
                "✓ Your content is now live!",
                "🚀 Deploying to GitHub Pages...");
        }

        private void DrawStep(string stepName, bool isComplete, bool isActive, string completeMessage, string activeMessage)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            if (isComplete)
            {
                EditorGUILayout.LabelField("🟢", GUILayout.Width(25));
                EditorGUILayout.LabelField(completeMessage, EditorStyles.boldLabel);
            }
            else if (isActive)
            {
                EditorGUILayout.LabelField("⏳", GUILayout.Width(25));
                EditorGUILayout.LabelField(activeMessage);
            }
            else
            {
                EditorGUILayout.LabelField("⏸️", GUILayout.Width(25));
                EditorGUILayout.LabelField($"Waiting: {stepName}");
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(3);
        }

        private async System.Threading.Tasks.Task StartFirebasePublishProcess()
        {
            isPublishing = true;

            try
            {
                // Step 1: Build Unity WebGL locally
                currentStep = PublishStep.BuildingLocally;
                currentStatus = "Building Unity WebGL locally...";

                var buildResult = await BuildUnityProjectLocally();
                if (!buildResult.Success)
                {
                    throw new System.Exception(buildResult.ErrorMessage);
                }

                projectBuilt = true;
                currentStatus = "Unity build completed successfully";

                // Step 2: Deploy via Firebase Cloud Functions
                currentStep = PublishStep.DeployingToGitHub;
                currentStatus = "Deploying via Firebase Cloud Functions...";

                var deployResult = await DeployViaFirebaseStorage(buildResult.BuildPath);
                if (!deployResult.Success)
                {
                    throw new System.Exception(deployResult.ErrorMessage);
                }

                deploymentComplete = true;

                // Complete - use corrected variable names
                var creatorUsername = U3DAuthenticator.CreatorUsername;
                var repositoryName = deployResult.RepositoryName ?? deployResult.ProjectName ?? GitHubAPI.SanitizeRepositoryName(cachedProductName);
                var successUrl = deployResult.ProfessionalUrl ?? $"https://{creatorUsername}.unreality3d.com/{repositoryName}/";

                MarkPublishSuccess(successUrl, repositoryName);

                currentStatus = "Publishing completed successfully!";

                ShowDeploymentSummary(repositoryName);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Firebase publishing failed: {ex.Message}");
                EditorUtility.DisplayDialog("Publishing Failed", $"There was an error: {ex.Message}", "OK");

                currentStep = PublishStep.Ready;
                currentStatus = $"Publishing failed: {ex.Message}";

                // Reset states
                githubConnected = false;
                projectBuilt = false;
                deploymentComplete = false;
            }
            finally
            {
                isPublishing = false;
            }
        }

        private async Task<UnityBuildResult> BuildUnityProjectLocally()
        {
            try
            {
                // Validate build requirements
                if (!UnityBuildHelper.ValidateBuildRequirements())
                {
                    return new UnityBuildResult
                    {
                        Success = false,
                        ErrorMessage = "Build requirements not met. Please check the Console for details."
                    };
                }

                var buildPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), "WebGL");

                currentStatus = $"Building to: {buildPath}";
                Debug.Log($"🎯 Building Unity WebGL to: {buildPath}");

                var buildResult = await UnityBuildHelper.BuildWebGL(buildPath, (status) =>
                {
                    currentStatus = status;
                });

                return buildResult;
            }
            catch (System.Exception ex)
            {
                return new UnityBuildResult
                {
                    Success = false,
                    ErrorMessage = $"Local build failed: {ex.Message}"
                };
            }
        }

        private async Task<FirebaseDeployResult> DeployViaFirebaseStorage(string buildPath)
        {
            try
            {
                currentStatus = "Determining repository name...";

                string targetRepositoryName = EditorPrefs.GetString("U3D_TargetRepository", "");

                if (string.IsNullOrEmpty(targetRepositoryName))
                {
                    // Fallback to sanitized Product Name
                    targetRepositoryName = GitHubAPI.SanitizeRepositoryName(cachedProductName);
                    Debug.LogWarning($"No target repository stored, using fallback: {targetRepositoryName}");
                }

                var deploymentIntent = shouldCreateNewRepository ? "create_new" : "update_existing";

                Debug.Log($"🎯 Deployment: {deploymentIntent}, Target: '{targetRepositoryName}', Product Name: '{cachedProductName}'");

                currentStatus = "Uploading build to Firebase Storage...";
                var storageBucket = FirebaseConfigManager.CurrentConfig?.storageBucket ?? "unreality3d.firebasestorage.app";
                if (string.IsNullOrEmpty(storageBucket) || storageBucket == "setup-required")
                {
                    storageBucket = "unreality3d.firebasestorage.app";
                }

                var uploader = new FirebaseStorageUploader(storageBucket, U3DAuthenticator.GetIdToken());

                var result = await uploader.UploadBuildToStorageWithIntent(
                    buildPath,
                    U3DAuthenticator.CreatorUsername,
                    targetRepositoryName,
                    deploymentIntent
                );

                uploader.Dispose();

                if (result.Success)
                {
                    var actualRepositoryName = result.ActualProjectName ?? targetRepositoryName;

                    if (!ShouldSkipDuringBuild())
                    {
                        EditorPrefs.SetString("U3D_LastRepositoryName", actualRepositoryName);
                        EditorPrefs.DeleteKey("U3D_TargetRepository"); // Clean up
                    }

                    shouldCreateNewRepository = false;

                    return new FirebaseDeployResult
                    {
                        Success = true,
                        RepositoryName = actualRepositoryName,
                        ProjectName = actualRepositoryName,
                        ProfessionalUrl = $"https://{U3DAuthenticator.CreatorUsername}.unreality3d.com/{actualRepositoryName}/",
                        Message = "Deployment successful via Firebase Storage"
                    };
                }
                else
                {
                    return new FirebaseDeployResult
                    {
                        Success = false,
                        ErrorMessage = result.ErrorMessage ?? "Firebase Storage upload failed"
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Firebase Storage deployment error: {ex.Message}");
                return new FirebaseDeployResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private static async Task<Dictionary<string, object>> CallFirebaseFunction(string functionName, Dictionary<string, object> data)
        {
            // Use reflection to access the private method
            var method = typeof(U3DAuthenticator).GetMethod("CallFirebaseFunction",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            if (method == null)
            {
                throw new Exception("CallFirebaseFunction method not found in U3DAuthenticator");
            }

            var task = method.Invoke(null, new object[] { functionName, data }) as Task<Dictionary<string, object>>;
            return await task;
        }

        [System.Serializable]
        public class FirebaseDeployResult
        {
            public bool Success { get; set; }
            public string RepositoryName { get; set; }
            public string ProjectName { get; set; }
            public string ProfessionalUrl { get; set; }
            public string Message { get; set; }
            public string ErrorMessage { get; set; }
        }

        private void ShowDeploymentSummary(string repositoryName)
        {
            string summaryMessage = "🎉 Publishing completed successfully!\n\n";
            summaryMessage += $"🌐 Your URL: {publishUrl}\n\n";
            summaryMessage += "🔧 Build Configuration:\n";
            summaryMessage += "• Unity build: Local (using your Unity license)\n";
            summaryMessage += "• Repository: Creator-owned GitHub repository\n";
            summaryMessage += "• Hosting: GitHub Pages (unlimited bandwidth)\n";
            summaryMessage += "• Professional URL: Unreality3D Load Balancer\n";
            summaryMessage += "\n💡 Next steps:\n";
            summaryMessage += "• Your content is live and accessible\n";
            summaryMessage += "• Push changes to trigger new deployments\n";
            summaryMessage += "• Share your professional URL with anyone\n";

            EditorUtility.DisplayDialog("Publishing Success", summaryMessage, "Great!");
        }

        private void DrawSuccessSection()
        {
            EditorGUILayout.Space(20);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            var successStyle = new GUIStyle(EditorStyles.boldLabel);
            successStyle.normal.textColor = Color.green;
            successStyle.fontSize = 16;

            EditorGUILayout.LabelField("🎉 Success! Your content is live!", successStyle);
            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("Your URL:", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(publishUrl, EditorStyles.textField, GUILayout.Height(30));

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open in Browser", GUILayout.Height(35)))
            {
                Application.OpenURL(publishUrl);
            }

            if (GUILayout.Button("Copy URL", GUILayout.Height(35)))
            {
                EditorGUIUtility.systemCopyBuffer = publishUrl;
                Debug.Log("URL copied to clipboard!");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // Return to options view for updates
            if (GUILayout.Button("Update Repository", GUILayout.Height(35)))
            {
                // Reset to Ready state to show repository options again
                currentStep = PublishStep.Ready;
                deploymentComplete = false;
                projectBuilt = false;
                githubConnected = false;
                isPublishing = false;
                IsComplete = false;

                // Keep options loaded so we show existing options immediately
                Debug.Log("Returning to repository options for updates");
            }

            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("Share this link with anyone to let them play your creation!", EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.EndVertical();
        }
    }

    [System.Serializable]
    public class ProjectOption
    {
        public enum OptionType
        {
            UpdateExisting,
            CreateNew
        }

        public OptionType Type { get; set; }
        public string RepositoryName { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string ProfessionalUrl { get; set; }
        public string GitHubPagesUrl { get; set; }
        public DateTime? LastUpdated { get; set; }
        public bool IsUnityProject { get; set; }
    }

    [System.Serializable]
    public class GitHubRepositoryCreationResult
    {
        public bool Success { get; set; }
        public string RepositoryName { get; set; }
        public string CloneUrl { get; set; }
        public string LocalPath { get; set; }
        public string ErrorMessage { get; set; }
    }
}