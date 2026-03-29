using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

namespace U3D.Editor
{
    public class BuildSettingsValidation : IValidationCategory
    {
        public string CategoryName => "Build Settings";

        public async System.Threading.Tasks.Task<List<ValidationResult>> RunChecks()
        {
            var results = new List<ValidationResult>();

            results.Add(ValidateWebGLCompression());
            results.Add(ValidateColorSpace());
            results.Add(ValidateRenderPipeline());
            results.Add(ValidateFirebaseIntegration());
            results.Add(ValidatePlayerControllerSetup());

            await System.Threading.Tasks.Task.Delay(100);
            return results;
        }

        private ValidationResult ValidateWebGLCompression()
        {
            var compressionOk = PlayerSettings.WebGL.compressionFormat == WebGLCompressionFormat.Disabled;
            return new ValidationResult(
                compressionOk,
                compressionOk
                    ? "✅ WebGL compression disabled for GitHub Pages compatibility"
                    : "ℹ️ WebGL compression is enabled. This is automatically corrected during build.",
                ValidationSeverity.Info
            );
        }

        private ValidationResult ValidateColorSpace()
        {
            var colorSpaceOk = PlayerSettings.colorSpace == ColorSpace.Linear;
            return new ValidationResult(
                colorSpaceOk,
                colorSpaceOk
                    ? "✅ Color space set to Linear for optimal rendering"
                    : "⚠️ Consider setting color space to Linear for better rendering quality",
                colorSpaceOk ? ValidationSeverity.Info : ValidationSeverity.Warning
            );
        }

        private ValidationResult ValidateRenderPipeline()
        {
            var urpAsset = GraphicsSettings.defaultRenderPipeline;
            var hasURP = urpAsset != null;
            return new ValidationResult(
                hasURP,
                hasURP
                    ? "✅ Universal Render Pipeline configured"
                    : "💡 Consider using URP for enhanced WebGL performance and visual features",
                hasURP ? ValidationSeverity.Info : ValidationSeverity.Warning
            );
        }

        private ValidationResult ValidateFirebaseIntegration()
        {
            var allComponents = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            var hasFirebaseIntegration = allComponents.Any(mb => mb.GetType().Name == "FirebaseIntegration");

            return new ValidationResult(
                hasFirebaseIntegration,
                hasFirebaseIntegration
                    ? "✅ FirebaseIntegration found in scene"
                    : "❌ FirebaseIntegration not found in scene. Add 'U3D CORE - DO NOT DELETE' prefab.",
                hasFirebaseIntegration ? ValidationSeverity.Info : ValidationSeverity.Error
            );
        }

        private ValidationResult ValidatePlayerControllerSetup()
        {
            var allComponents = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);

            var hasFusionNetworkManager = allComponents.Any(mb => mb.GetType().Name == "U3DFusionNetworkManager");
            var hasPlayerSpawner = allComponents.Any(mb => mb.GetType().Name == "U3DPlayerSpawner");

            var coreSystemsPresent = hasFusionNetworkManager && hasPlayerSpawner;

            string resultMessage;
            ValidationSeverity severity;

            if (coreSystemsPresent)
            {
                resultMessage = "✅ U3D CORE networking system found";
                severity = ValidationSeverity.Info;
            }
            else
            {
                var missingComponents = new List<string>();
                if (!hasFusionNetworkManager) missingComponents.Add("U3DFusionNetworkManager");
                if (!hasPlayerSpawner) missingComponents.Add("U3DPlayerSpawner");

                resultMessage = $"❌ Missing U3D CORE components: {string.Join(", ", missingComponents)}. Add 'U3D CORE - DO NOT DELETE' prefab to scene.";
                severity = ValidationSeverity.Error;
            }

            return new ValidationResult(coreSystemsPresent, resultMessage, severity);
        }
    }
}