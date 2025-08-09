using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public class ProjectStartupConfiguration
{
    private const string STARTUP_SCENE_PATH = "Assets/Scenes/_My Scene.unity";
    private const string BUILD_TARGET_KEY = "HasSetWebGLTarget";
    private const string TEMPLATE_WEBGL_CHECK_KEY = "U3D_TemplateWebGLCheck";

    // Project-specific keys to avoid cross-template conflicts
    private static string BUILD_TARGET_SPECIFIC_KEY => $"{BUILD_TARGET_KEY}_{Application.dataPath.GetHashCode()}";
    private static string SCENE_LOADED_KEY => $"U3D_HasLoadedStartupScene_{Application.dataPath.GetHashCode()}";
    private static string TEMPLATE_CHECK_KEY => $"{TEMPLATE_WEBGL_CHECK_KEY}_{Application.dataPath.GetHashCode()}";

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

        // Check if this is first time opening template
        bool hasCheckedTemplate = false;
        if (!ShouldSkipDuringBuild())
        {
            hasCheckedTemplate = EditorPrefs.GetBool(TEMPLATE_CHECK_KEY, false);
        }

        try
        {
            // NEW: Always verify WebGL is available and provide clear feedback
            bool webglSupported = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL);

            if (!webglSupported)
            {
                Debug.LogError("❌ U3D SDK: WEBGL BUILD SUPPORT NOT INSTALLED");
                Debug.LogError("📋 TO FIX: Unity Hub → Installs → Your Unity Version → Add Modules → WebGL Build Support");

                EditorUtility.DisplayDialog(
                    "WebGL Build Support Required",
                    "This Unreality3D template requires WebGL Build Support to function properly.\n\n" +
                    "To install:\n" +
                    "1. Open Unity Hub\n" +
                    "2. Go to Installs tab\n" +
                    "3. Click the gear icon next to your Unity version\n" +
                    "4. Select 'Add Modules'\n" +
                    "5. Check 'WebGL Build Support'\n" +
                    "6. Install and restart Unity\n\n" +
                    "Note: The template will function but builds will fail until WebGL support is installed.",
                    "OK"
                );

                // Mark as checked to avoid repeated warnings
                if (!ShouldSkipDuringBuild())
                {
                    EditorPrefs.SetBool(TEMPLATE_CHECK_KEY, true);
                }
                return;
            }

            // Check current build target
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            {
                if (!hasCheckedTemplate)
                {
                    Debug.Log("🔄 U3D SDK: Template opened with non-WebGL build target. Switching to WebGL...");

                    bool success = EditorUserBuildSettings.SwitchActiveBuildTarget(
                        BuildTargetGroup.WebGL,
                        BuildTarget.WebGL
                    );

                    if (success)
                    {
                        Debug.Log("✅ U3D SDK: Build target switched to WebGL successfully");
                        Debug.Log("💡 U3D SDK: Template is now configured for WebGL deployment");
                    }
                    else
                    {
                        Debug.LogWarning("⚠️ U3D SDK: Failed to switch to WebGL. Please switch manually via Build Settings.");
                    }
                }
                else
                {
                    Debug.LogWarning($"⚠️ U3D SDK: Build target is {EditorUserBuildSettings.activeBuildTarget}, but template expects WebGL");
                    Debug.LogWarning("💡 U3D SDK: Switch to WebGL in Build Settings for proper deployment");
                }
            }
            else
            {
                if (!hasCheckedTemplate)
                {
                    Debug.Log("✅ U3D SDK: Template opened with WebGL build target (correct configuration)");
                }
            }

            // Mark template as checked
            if (!ShouldSkipDuringBuild())
            {
                EditorPrefs.SetBool(TEMPLATE_CHECK_KEY, true);
            }

            // Load startup scene logic (existing)
            bool hasLoadedStartupScene = false;
            if (!ShouldSkipDuringBuild())
            {
                hasLoadedStartupScene = EditorPrefs.GetBool(SCENE_LOADED_KEY, false);
            }

            bool isFirstTimeEver = !hasLoadedStartupScene;

            if (isFirstTimeEver && System.IO.File.Exists(STARTUP_SCENE_PATH))
            {
                Debug.Log("🎯 U3D SDK: Loading startup scene for first-time template setup");
                EditorSceneManager.OpenScene(STARTUP_SCENE_PATH);

                if (!ShouldSkipDuringBuild())
                {
                    EditorPrefs.SetBool(SCENE_LOADED_KEY, true);
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"❌ U3D SDK: Error in ProjectStartupConfiguration: {ex.Message}");
        }
    }

    /// <summary>
    /// Menu item to reset template configuration (for testing)
    /// </summary>
    //[MenuItem("U3D/Reset Template Configuration")]
    //private static void ResetTemplateConfiguration()
    //{
    //    EditorPrefs.DeleteKey(TEMPLATE_CHECK_KEY);
    //    EditorPrefs.DeleteKey(BUILD_TARGET_SPECIFIC_KEY);
    //    EditorPrefs.DeleteKey(SCENE_LOADED_KEY);
    //    Debug.Log("🔄 U3D SDK: Template configuration reset. Restart Unity to test first-time setup.");
    //}
}