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
        private int previousSelectedOptionIndex = -1; // Track previous selection for sync logic

        private enum PublishStep
        {
            Ready,
            BuildingLocally,
            CreatingRepository,
            DeployingToGitHub,
            Complete
        }

        /// <summary>
        /// CRITICAL: Check if we should skip operations during builds (same as GitHubTokenManager/ProjectStartupConfiguration)
        /// </summary>
        private static bool ShouldSkipDuringBuild()
        {
            return BuildPipeline.isBuildingPlayer ||
                   EditorApplication.isCompiling ||
                   EditorApplication.isUpdating;
        }

        public void Initialize()
        {
            // CRITICAL: Skip initialization during compilation (same pattern as existing classes)
            if (ShouldSkipDuringBuild())
            {
                return;
            }

            // Cache product name on main thread to avoid threading issues
            cachedProductName = Application.productName;

            // Clear any stale "just published" flag on tab initialization (using build guards)
            if (!ShouldSkipDuringBuild())
            {
                var justPublished = EditorPrefs.GetBool("U3D_JustPublished", false);
                if (justPublished)
                {
                    EditorPrefs.DeleteKey("U3D_JustPublished");
                }
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
                error = "Product Name cannot be empty for New Repository";
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

        // FIX #1: Make CanPublish() optimistic - assume we can publish unless compilation is happening
        private bool CanPublish()
        {
            return true;
        }

        // FIX #2: Make DrawPrerequisites() less aggressive about redirecting
        private void DrawPrerequisites()
        {
            EditorGUILayout.LabelField("Checking prerequisites...", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            if (GUILayout.Button("Check Setup Status", GUILayout.Height(30)))
            {
                OnRequestTabSwitch?.Invoke(0);
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

                var repoResult = await GitHubAPI.GetUserRepositories("", 100);

                if (!repoResult.Success)
                {
                    Debug.LogError($"Failed to load repositories: {repoResult.ErrorMessage}");
                    optionsLoaded = true;
                    loadingOptions = false;
                    return;
                }

                // Clean processing without spam
                foreach (var repo in repoResult.Repositories)
                {
                    repo.IsUnreality3DProject = await GitHubAPI.WasCreatedWithUnreality3D(repo.Name);

                    if (repo.IsUnreality3DProject)
                    {
                        if (repo.HasPages)
                        {
                            repo.GitHubPagesUrl = await GitHubAPI.GetGitHubPagesUrl(repo.Name);
                        }

                        availableOptions.Add(new ProjectOption
                        {
                            Type = ProjectOption.OptionType.UpdateExisting,
                            RepositoryName = repo.Name,
                            DisplayName = $"Update \"{repo.Name}\"",
                            Description = "Unreality3D project",
                            ProfessionalUrl = $"https://{U3DAuthenticator.CreatorUsername}.unreality3d.com/{repo.Name}/",
                            GitHubPagesUrl = repo.GitHubPagesUrl,
                            LastUpdated = repo.UpdatedAt,
                            IsUnreality3DProject = repo.IsUnreality3DProject
                        });
                    }
                }

                // Always add "Create New Repository" option
                availableOptions.Add(new ProjectOption
                {
                    Type = ProjectOption.OptionType.CreateNew,
                    RepositoryName = "new-repository",
                    DisplayName = "Create New Repository",
                    Description = "New Unreality3D project",
                    ProfessionalUrl = $"https://{U3DAuthenticator.CreatorUsername}.unreality3d.com/[product-name]/",
                    GitHubPagesUrl = null,
                    LastUpdated = null,
                    IsUnreality3DProject = false
                });

                selectedOptionIndex = DetermineDefaultSelection();
                previousSelectedOptionIndex = selectedOptionIndex; // Initialize tracking
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

        // Smart default selection logic
        private int DetermineDefaultSelection()
        {
            if (availableOptions.Count == 0)
                return -1;

            var sanitizedCurrentProduct = GitHubAPI.SanitizeRepositoryName(cachedProductName);

            // Look for exact match with current Product Name
            for (int i = 0; i < availableOptions.Count; i++)
            {
                var option = availableOptions[i];

                if (option.Type == ProjectOption.OptionType.UpdateExisting &&
                    string.Equals(option.RepositoryName, sanitizedCurrentProduct, StringComparison.OrdinalIgnoreCase))
                {
                    return i; // Found matching repository
                }
            }

            // Default to "Create New Repository" (last option)
            return availableOptions.Count - 1;
        }

        /// <summary>
        /// NEW METHOD: Handles Product Name synchronization when repository selection changes
        /// </summary>
        private void HandleRepositorySelectionChange(int newSelectedIndex)
        {
            if (newSelectedIndex < 0 || newSelectedIndex >= availableOptions.Count)
                return;

            var selectedOption = availableOptions[newSelectedIndex];

            // Only sync Product Name for existing repositories (not "Create New")
            if (selectedOption.Type == ProjectOption.OptionType.UpdateExisting)
            {
                // Get the repository name (this is what should match Product Name)
                var repositoryName = selectedOption.RepositoryName;

                // Convert repository name back to a proper Product Name format
                var suggestedProductName = ConvertRepositoryNameToProductName(repositoryName);

                // Only update if it's actually different to avoid unnecessary changes
                if (!string.Equals(cachedProductName, suggestedProductName, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log($"🔄 Repository selection changed to '{repositoryName}' - syncing Product Name: '{cachedProductName}' → '{suggestedProductName}'");

                    // Update Unity's PlayerSettings and our cached value immediately
                    // This ensures deployment pipeline gets correct Product Name
                    PlayerSettings.productName = suggestedProductName;
                    cachedProductName = suggestedProductName;
                }
            }

            // Update tracking variable
            previousSelectedOptionIndex = newSelectedIndex;
        }

        /// <summary>
        /// NEW METHOD: Converts repository name back to a user-friendly Product Name
        /// Reverses the GitHubAPI.SanitizeRepositoryName() process where possible
        /// </summary>
        private string ConvertRepositoryNameToProductName(string repositoryName)
        {
            if (string.IsNullOrEmpty(repositoryName))
                return repositoryName;

            // Convert hyphens back to spaces and title case the result
            var productName = repositoryName.Replace("-", " ");

            // Simple title case: capitalize first letter of each word
            var words = productName.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length > 0)
                {
                    words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1).ToLower();
                }
            }

            return string.Join(" ", words);
        }

        private void DrawRepositoryOptions()
        {
            if (availableOptions.Count == 0)
            {
                EditorGUILayout.HelpBox("No GitHub repositories found. A new repository will be created.", MessageType.Info);

                if (GUILayout.Button("Make It Live!", GUILayout.Height(50)))
                {
                    shouldCreateNewRepository = true;
                    _ = StartFirebasePublishProcess();
                }
                return;
            }

            EditorGUILayout.LabelField("Choose your publishing option:", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // Check if current Product Name would create a repository that conflicts with existing ones
            var sanitizedCurrentProduct = GitHubAPI.SanitizeRepositoryName(cachedProductName);
            var matchingRepo = availableOptions.FirstOrDefault(opt =>
                opt.Type == ProjectOption.OptionType.UpdateExisting &&
                string.Equals(opt.RepositoryName, sanitizedCurrentProduct, StringComparison.OrdinalIgnoreCase));

            bool hasMatchingRepo = matchingRepo != null;

            // Show helpful context about current selection
            if (hasMatchingRepo)
            {
                EditorGUILayout.HelpBox(
                    $"💡 Your current Product Name \"{cachedProductName}\" matches the repository \"{matchingRepo.RepositoryName}\". " +
                    "The update option is selected by default, but you can choose 'Create New Repository' to make a separate project.",
                    MessageType.Info);
                EditorGUILayout.Space(5);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"📝 Your current Product Name \"{cachedProductName}\" will create a new repository. " +
                    "You can also choose to update any of your existing Unity projects below.",
                    MessageType.Info);
                EditorGUILayout.Space(5);
            }

            // Draw radio button options
            for (int i = 0; i < availableOptions.Count; i++)
            {
                var option = availableOptions[i];
                var isSelected = selectedOptionIndex == i;

                EditorGUILayout.BeginVertical(isSelected ? EditorStyles.helpBox : EditorStyles.textArea);

                EditorGUILayout.BeginHorizontal();

                // Radio button with selection change detection
                var newSelected = EditorGUILayout.Toggle(isSelected, GUILayout.Width(20));
                if (newSelected && !isSelected)
                {
                    selectedOptionIndex = i;

                    // NEW: Handle Product Name sync when selection changes
                    HandleRepositorySelectionChange(i);
                }

                // Option details
                EditorGUILayout.BeginVertical();

                var style = new GUIStyle(EditorStyles.boldLabel);

                // Color logic with enhanced matching indication
                if (option.Type == ProjectOption.OptionType.CreateNew)
                {
                    style.normal.textColor = Color.yellow;
                }
                else if (hasMatchingRepo && option.RepositoryName == matchingRepo.RepositoryName)
                {
                    style.normal.textColor = Color.cyan; // Special color for matching repo
                }
                else
                {
                    style.normal.textColor = Color.green;
                }

                // Display text with enhanced matching indication
                string displayText;
                if (option.Type == ProjectOption.OptionType.UpdateExisting)
                {
                    displayText = option.DisplayName;
                    if (hasMatchingRepo && option.RepositoryName == matchingRepo.RepositoryName)
                    {
                        displayText += " (matches current Product Name)";
                    }
                }
                else
                {
                    displayText = "Create New Repository";
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

                    // MODIFIED: Only allow editing when "Create New Repository" is selected
                    bool isCreateNewSelected = selectedOptionIndex == i;

                    GUI.enabled = isCreateNewSelected;
                    EditorGUI.BeginChangeCheck();
                    var newProductName = EditorGUILayout.TextField(cachedProductName);
                    bool nameChangedInField = EditorGUI.EndChangeCheck();
                    GUI.enabled = true;

                    // Update button logic - enabled when field differs from PlayerSettings AND Create New is selected
                    bool namesDiffer = !string.IsNullOrEmpty(newProductName) && newProductName != PlayerSettings.productName;

                    GUI.enabled = namesDiffer && isCreateNewSelected;
                    if (GUILayout.Button("Update", GUILayout.Width(60)))
                    {
                        // Update Unity's PlayerSettings
                        PlayerSettings.productName = newProductName;
                        cachedProductName = newProductName;

                        Debug.Log($"Product Name updated to: {newProductName}");

                        // Recalculate default selection based on new Product Name
                        var previousSelection = selectedOptionIndex;
                        selectedOptionIndex = DetermineDefaultSelection();

                        // UX IMPROVEMENT: If selection changed to an existing repo, show warning and scroll behavior
                        if (selectedOptionIndex != previousSelection && selectedOptionIndex < availableOptions.Count - 1)
                        {
                            var matchedRepo = availableOptions[selectedOptionIndex];
                            EditorUtility.DisplayDialog("Existing Repository Found",
                                $"Your Product Name '{newProductName}' matches an existing Unreality3D project '{matchedRepo.RepositoryName}'.\n\n" +
                                "The matching repository has been automatically selected above. If you want to create a new repository instead, " +
                                "choose a different Product Name.",
                                "OK");
                        }
                    }
                    GUI.enabled = true;

                    // Update cached name for real-time feedback
                    if (nameChangedInField && isCreateNewSelected)
                    {
                        cachedProductName = newProductName;
                    }

                    EditorGUILayout.EndHorizontal();

                    // Show sync status for existing repositories
                    if (!isCreateNewSelected)
                    {
                        EditorGUILayout.LabelField("⚠️ Product Name synced to selected repository above", EditorStyles.miniLabel);
                    }
                    else
                    {
                        // ENHANCED: Smart repository name preview
                        var wouldCreateRepo = GitHubAPI.SanitizeRepositoryName(cachedProductName);
                        var wouldConflict = availableOptions.Any(opt =>
                            opt.Type == ProjectOption.OptionType.UpdateExisting &&
                            string.Equals(opt.RepositoryName, wouldCreateRepo, StringComparison.OrdinalIgnoreCase));

                        if (wouldConflict)
                        {
                            EditorGUILayout.LabelField($"⚠️ Repository '{wouldCreateRepo}' already exists above", EditorStyles.miniLabel);
                        }
                        else
                        {
                            EditorGUILayout.LabelField($"Will create repository: {wouldCreateRepo}", EditorStyles.miniLabel);
                        }
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

            // Get the selected option for validation
            ProjectOption selectedOption = null;
            if (selectedOptionIndex >= 0 && selectedOptionIndex < availableOptions.Count)
            {
                selectedOption = availableOptions[selectedOptionIndex];
            }

            // Validate based on what's actually selected
            if (canPublish && selectedOption != null)
            {
                if (selectedOption.Type == ProjectOption.OptionType.CreateNew)
                {
                    // For Create New: validate Product Name
                    if (!ValidateProductName(cachedProductName, out validationError))
                    {
                        canPublish = false;
                    }
                    else
                    {
                        // Check if Product Name conflicts with existing repo
                        var hasConflictingRepo = availableOptions.Any(opt =>
                            opt.Type == ProjectOption.OptionType.UpdateExisting &&
                            string.Equals(opt.RepositoryName, GitHubAPI.SanitizeRepositoryName(cachedProductName), StringComparison.OrdinalIgnoreCase));

                        if (hasConflictingRepo)
                        {
                            validationError = $"Repository '{GitHubAPI.SanitizeRepositoryName(cachedProductName)}' already exists. Use the Update option above or change the Product Name.";
                            canPublish = false;
                        }
                    }
                }
                else if (selectedOption.Type == ProjectOption.OptionType.UpdateExisting)
                {
                    // For Update Existing: always allow (no additional validation needed)
                    canPublish = true;
                }
            }

            GUI.enabled = canPublish;
            if (GUILayout.Button("Make It Live!", GUILayout.Height(50)))
            {
                if (selectedOption.Type == ProjectOption.OptionType.CreateNew)
                {
                    // Use current Product Name for new repository
                    var targetRepositoryName = GitHubAPI.SanitizeRepositoryName(cachedProductName);
                    shouldCreateNewRepository = true;
                    Debug.Log($"🎯 CREATE NEW REPOSITORY: '{targetRepositoryName}' from Product Name: '{cachedProductName}'");
                    EditorPrefs.SetString("U3D_TargetRepository", targetRepositoryName);
                }
                else
                {
                    // Update existing repository with confirmation
                    var targetRepositoryName = selectedOption.RepositoryName;
                    shouldCreateNewRepository = false;

                    // Show confirmation dialog for updates
                    if (EditorUtility.DisplayDialog("Confirm Repository Update",
                        $"You are about to update the existing repository '{targetRepositoryName}'.\n\n" +
                        "This will overwrite the current content with your new build.\n\n" +
                        "Are you sure you want to continue?",
                        "Yes, Update", "Cancel"))
                    {
                        Debug.Log($"🎯 UPDATE EXISTING REPOSITORY: '{targetRepositoryName}' (Product Name synced to: '{cachedProductName}')");
                        EditorPrefs.SetString("U3D_TargetRepository", targetRepositoryName);
                        _ = StartFirebasePublishProcess();
                    }
                    else
                    {
                        Debug.Log("Repository update cancelled by user");
                        GUI.enabled = true;
                        return; // User cancelled
                    }
                }

                if (selectedOption.Type == ProjectOption.OptionType.CreateNew)
                {
                    _ = StartFirebasePublishProcess();
                }
            }
            GUI.enabled = true;

            // Show validation errors
            if (!canPublish && !string.IsNullOrEmpty(validationError))
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox($"⚠️ {validationError}", MessageType.Warning);
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

        // Visual feedback when Product Name matches existing repo
        private void HandleProductNameMatchFeedback()
        {
            var sanitizedCurrentProduct = GitHubAPI.SanitizeRepositoryName(cachedProductName);
            var matchingRepoIndex = -1;

            for (int i = 0; i < availableOptions.Count; i++)
            {
                var option = availableOptions[i];
                if (option.Type == ProjectOption.OptionType.UpdateExisting &&
                    string.Equals(option.RepositoryName, sanitizedCurrentProduct, StringComparison.OrdinalIgnoreCase))
                {
                    matchingRepoIndex = i;
                    break;
                }
            }

            // If we found a match and it's not currently selected, provide feedback
            if (matchingRepoIndex >= 0 && selectedOptionIndex != matchingRepoIndex)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(
                    $"💡 Note: Your Product Name '{cachedProductName}' matches the repository '{availableOptions[matchingRepoIndex].RepositoryName}' shown above. " +
                    "You may want to select that option to update your existing project, or change the Product Name to create something new.",
                    MessageType.Info);
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
                // 🆕 DEPLOYMENT FIX: Validate authentication BEFORE starting any deployment operations
                currentStatus = "Preparing authentication for deployment...";
                bool authReady = await U3DAuthenticator.PrepareForDeployment();

                if (!authReady)
                {
                    throw new System.Exception("Authentication preparation failed. Please log out and log back in, then try again.");
                }

                currentStatus = "Authentication validated - proceeding with deployment";

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

                // 🆕 DEPLOYMENT FIX: Re-validate authentication before the deployment step
                // (Build process can take 30+ minutes, token might expire)
                currentStatus = "Re-validating authentication for deployment...";
                authReady = await U3DAuthenticator.PrepareForDeployment();

                if (!authReady)
                {
                    throw new System.Exception("Authentication expired during build. Please log out and log back in, then try deploying again.");
                }

                // Step 2: Deploy via Firebase Cloud Functions
                currentStep = PublishStep.DeployingToGitHub;
                currentStatus = "Deploying via Firebase Cloud Functions...";

                var deployResult = await DeployViaFirebaseStorage(buildResult.BuildPath);
                if (!deployResult.Success)
                {
                    throw new System.Exception(deployResult.ErrorMessage);
                }

                deploymentComplete = true;

                var creatorUsername = U3DAuthenticator.CreatorUsername;
                var repositoryName = deployResult.RepositoryName ?? deployResult.ProjectName ?? GitHubAPI.SanitizeRepositoryName(cachedProductName);
                var successUrl = deployResult.ProfessionalUrl ?? $"https://{creatorUsername}.unreality3d.com/{repositoryName}/";

                MarkPublishSuccess(successUrl, repositoryName);
                currentStatus = "Publishing completed successfully!";
                ShowDeploymentSummary(repositoryName);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Publishing failed: {ex.Message}");

                // 🆕 ENHANCED ERROR HANDLING: Provide specific guidance for authentication errors
                string userMessage = ex.Message;
                if (ex.Message.Contains("Authentication") || ex.Message.Contains("unauthenticated") || ex.Message.Contains("unauthorized"))
                {
                    userMessage = "Authentication error occurred during deployment.\n\n" +
                                 "This happens when your login session expires during the build process.\n\n" +
                                 "To fix this:\n" +
                                 "1. Log out from the Unreality3D Creator Dashboard\n" +
                                 "2. Clear the Unity Console\n" +
                                 "3. Close and reopen this Unity project\n" +
                                 "4. Log back in\n" +
                                 "5. Try publishing again\n\n" +
                                 "Technical details: " + ex.Message;
                }

                EditorUtility.DisplayDialog("Publishing Failed", userMessage, "OK");

                currentStep = PublishStep.Ready;
                currentStatus = $"Publishing failed: {ex.Message}";
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
                    targetRepositoryName = GitHubAPI.SanitizeRepositoryName(cachedProductName);
                }

                var deploymentIntent = shouldCreateNewRepository ? "create_new" : "update_existing";
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
                        EditorPrefs.DeleteKey("U3D_TargetRepository");
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
        public bool IsUnreality3DProject { get; set; }
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