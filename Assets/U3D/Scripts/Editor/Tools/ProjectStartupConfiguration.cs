using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public class ProjectStartupConfiguration
{
    private const string STARTUP_SCENE_PATH = "Assets/Scenes/_My Scene.unity";
    private const string BUILD_TARGET_KEY = "HasSetWebGLTarget";

    // Project-specific keys to avoid cross-template conflicts
    private static string BUILD_TARGET_SPECIFIC_KEY => $"{BUILD_TARGET_KEY}_{Application.dataPath.GetHashCode()}";
    private static string SCENE_LOADED_KEY => $"U3D_HasLoadedStartupScene_{Application.dataPath.GetHashCode()}";

    /// <summary>
    /// CRITICAL: Check if we should skip operations during builds
    /// </summary>
    private static bool ShouldSkipDuringBuild()
    {
        return BuildPipeline.isBuildingPlayer ||
               EditorApplication.isCompiling ||
               EditorApplication.isUpdating;
    }

    static ProjectStartupConfiguration()
    {
        // CRITICAL: Skip initialization during builds to prevent IndexOutOfRangeException
        if (ShouldSkipDuringBuild())
        {
            Debug.Log("🚫 ProjectStartupConfiguration: Skipping initialization during build process");
            return;
        }

        // Enhanced initialization with project-specific tracking
        EditorApplication.delayCall += () => {
            EditorApplication.delayCall += () => {
                EditorApplication.delayCall += ConfigureProjectStartup;
            };
        };
    }

    private static void ConfigureProjectStartup()
    {
        // Enhanced safety checks
        if (ShouldSkipDuringBuild())
        {
            // Retry later if editor is busy
            EditorApplication.delayCall += ConfigureProjectStartup;
            return;
        }

        // FIXED: Use build guards for EditorPrefs access
        bool hasSetBuildTarget = false;
        if (!ShouldSkipDuringBuild())
        {
            hasSetBuildTarget = EditorPrefs.GetBool(BUILD_TARGET_SPECIFIC_KEY, false);
        }

        try
        {
            // Build target logic (unchanged)
            if (!hasSetBuildTarget && EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            {
                // ... existing build target logic unchanged ...
            }

            // COORDINATED SCENE LOADING: Use the same "first time" system as the dashboard
            bool hasOpenedBefore = false;
            bool hasLoadedStartupScene = false;

            if (!ShouldSkipDuringBuild())
            {
                // Use the SAME key as U3DCreatorWindow for consistency
                hasLoadedStartupScene = EditorPrefs.GetBool(SCENE_LOADED_KEY, false);
            }

            // CRITICAL FIX: Only load startup scene on TRUE FIRST TIME (before dashboard has opened)
            bool isFirstTimeEver = !hasLoadedStartupScene;

            if (isFirstTimeEver && System.IO.File.Exists(STARTUP_SCENE_PATH))
            {
                var currentScene = EditorSceneManager.GetActiveScene();
                string currentScenePath = currentScene.path;

                // Enhanced empty scene detection
                bool isEmptyScene = string.IsNullOrEmpty(currentScenePath) ||
                                   !currentScene.IsValid() ||
                                   currentScene.name == "Untitled" ||
                                   currentScene.name == "" ||
                                   currentScenePath.ToLower().Contains("untitled") ||
                                   currentScenePath.ToLower().Contains("new scene");

                // Additional check: if scene has minimal content (truly empty)
                bool sceneIsEmpty = currentScene.rootCount == 0 ||
                                   (currentScene.rootCount <= 2 && // Main Camera + Directional Light is Unity default
                                    currentScene.GetRootGameObjects().Length <= 2);

                bool shouldLoadStartupScene = isEmptyScene || sceneIsEmpty;

                Debug.Log($"🔍 U3D SDK: First time check - HasOpenedBefore: {hasOpenedBefore}, " +
                         $"HasLoadedScene: {hasLoadedStartupScene}, ShouldLoad: {shouldLoadStartupScene}");

                if (shouldLoadStartupScene)
                {
                    EditorSceneManager.OpenScene(STARTUP_SCENE_PATH);
                    Debug.Log($"✅ U3D SDK: Opened startup scene on first launch: {STARTUP_SCENE_PATH}");
                }
                else
                {
                    Debug.Log($"ℹ️ U3D SDK: User has content in scene, respecting their choice");
                }

                // Mark scene loading as completed
                if (!ShouldSkipDuringBuild())
                {
                    EditorPrefs.SetBool(SCENE_LOADED_KEY, true);
                }
            }
            else if (hasOpenedBefore)
            {
                Debug.Log("ℹ️ U3D SDK: Not first time - respecting user's scene choice");
            }

            Debug.Log("✅ U3D SDK: Project startup configuration complete");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ U3D SDK: Error during startup configuration: {e.Message}");
        }
    }
}