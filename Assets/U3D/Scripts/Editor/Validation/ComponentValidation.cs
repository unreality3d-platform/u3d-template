using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace U3D.Editor
{
    public class ComponentValidation : IValidationCategory
    {
        public string CategoryName => "Components & Setup";

        public async System.Threading.Tasks.Task<List<ValidationResult>> RunChecks()
        {
            var results = new List<ValidationResult>();

            results.Add(ValidateRequiredComponents());
            results.Add(ValidateSpawnPoints());
            results.Add(ValidateAudioConfiguration());
            results.Add(ValidateInputSystem());

            await System.Threading.Tasks.Task.Delay(100);
            return results;
        }

        private ValidationResult ValidateRequiredComponents()
        {
            var allComponents = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);

            var hasFirebaseIntegration = allComponents.Any(mb => mb.GetType().Name == "FirebaseIntegration");
            var hasFusionNetworkManager = allComponents.Any(mb => mb.GetType().Name == "U3DFusionNetworkManager");
            var hasPlayerSpawner = allComponents.Any(mb => mb.GetType().Name == "U3DPlayerSpawner");
            var hasCursorManager = allComponents.Any(mb => mb.GetType().Name == "U3DCursorManager");

            var componentsFound = 0;
            var missingComponents = new List<string>();
            var optionalComponents = new List<string>();

            if (hasFirebaseIntegration)
                componentsFound++;
            else
                missingComponents.Add("FirebaseIntegration");

            if (hasFusionNetworkManager)
                componentsFound++;
            else
                missingComponents.Add("U3DFusionNetworkManager");

            if (hasPlayerSpawner)
                componentsFound++;
            else
                missingComponents.Add("U3DPlayerSpawner");

            if (!hasCursorManager)
                optionalComponents.Add("U3DCursorManager");

            var requiredCount = 3;
            var isComplete = componentsFound == requiredCount;

            string message;
            if (isComplete)
            {
                message = optionalComponents.Count > 0
                    ? $"✅ U3D CORE system configured. Optional: {string.Join(", ", optionalComponents)}"
                    : "✅ All U3D CORE components found - networking system ready";
            }
            else
            {
                message = $"❌ Missing U3D CORE components: {string.Join(", ", missingComponents)}. Add 'U3D CORE - DO NOT DELETE' prefab to scene.";
            }

            return new ValidationResult(isComplete, message, isComplete ? ValidationSeverity.Info : ValidationSeverity.Error);
        }

        private ValidationResult ValidateSpawnPoints()
        {
            var spawnPoints = GameObject.FindGameObjectsWithTag("PlayerSpawnPoint");
            var hasSpawnPoints = spawnPoints.Length > 0;

            return new ValidationResult(
                hasSpawnPoints,
                hasSpawnPoints ? $"{spawnPoints.Length} player spawn point(s) found" : "Add a player spawn point - tag a GameObject 'PlayerSpawnPoint' to set where players appear",
                hasSpawnPoints ? ValidationSeverity.Info : ValidationSeverity.Warning
            );
        }

        private ValidationResult ValidateAudioConfiguration()
        {
            var audioSources = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            var playOnAwakeSources = audioSources.Where(a => a.playOnAwake).ToList();

            var isOptimal = playOnAwakeSources.Count <= 2;
            return new ValidationResult(
                isOptimal,
                isOptimal ? "Audio configuration appropriate" : $"{playOnAwakeSources.Count} audio sources set to play on awake (may impact WebGL loading)",
                isOptimal ? ValidationSeverity.Info : ValidationSeverity.Warning
            );
        }

        private ValidationResult ValidateInputSystem()
        {
            var inputActions = AssetDatabase.FindAssets("t:InputActionAsset");
            var hasInputActions = inputActions.Length > 0;

            return new ValidationResult(
                hasInputActions,
                hasInputActions ? "Input System configured" : "Consider setting up Input System for better control handling",
                hasInputActions ? ValidationSeverity.Info : ValidationSeverity.Warning
            );
        }
    }
}