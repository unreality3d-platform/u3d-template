using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace U3D.Editor
{
    /// <summary>
    /// Asset cleanup utilities for the U3D SDK.
    /// Handles missing script detection, replacement, and cleanup.
    /// Also handles missing object reference detection and placeholder management.
    /// </summary>
    public static class AssetCleanupTools
    {
        /// <summary>
        /// Replace missing script references with placeholder components
        /// </summary>
        public static void ReplaceMissingScriptsWithPlaceholders()
        {
            int replacedCount = 0;
            int suggestionsFound = 0;
            GameObject[] allGameObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);

            foreach (GameObject go in allGameObjects)
            {
                int numComponents = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                if (numComponents > 0)
                {
                    // Register undo operation
                    Undo.RegisterCompleteObjectUndo(go, "Replace missing scripts with placeholders");

                    // Check for suggestions before adding placeholders
                    string suggestion = ComponentSuggestions.GetSuggestionForGameObject(go.name);
                    if (!string.IsNullOrEmpty(suggestion))
                    {
                        suggestionsFound++;
                    }

                    // Add placeholder components (suggestions auto-populate in Awake)
                    for (int i = 0; i < numComponents; i++)
                    {
                        go.AddComponent<MissingScriptPlaceholder>();
                    }

                    // Remove missing script components
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                    replacedCount += numComponents;

                    // Mark object as dirty
                    EditorUtility.SetDirty(go);
                }
            }

            if (replacedCount > 0)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                Debug.Log($"🔧 U3D SDK: Replaced {replacedCount} missing scripts with placeholder components.");

                if (suggestionsFound > 0)
                {
                    Debug.Log($"💡 U3D SDK: Found {suggestionsFound} objects with replacement suggestions! Check placeholder components in Inspector for details.");
                }
            }
            else
            {
                Debug.Log("✅ U3D SDK: No missing scripts found in scene.");
            }
        }

        /// <summary>
        /// Remove all MissingScriptPlaceholder components from the scene
        /// </summary>
        public static void RemovePlaceholderComponents()
        {
            int removedCount = 0;
            GameObject[] allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);

            foreach (GameObject go in allObjects)
            {
                var placeholders = go.GetComponents<MissingScriptPlaceholder>();
                foreach (var placeholder in placeholders)
                {
                    Undo.DestroyObjectImmediate(placeholder);
                    removedCount++;
                }
            }

            if (removedCount > 0)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                Debug.Log($"🧼 U3D SDK: Removed {removedCount} placeholder component(s) from scene.");
            }
            else
            {
                Debug.Log("✅ U3D SDK: No placeholder components found in scene.");
            }
        }

        /// <summary>
        /// Detect missing object references in components and add placeholder tracking components
        /// </summary>
        public static void ReplaceMissingReferencesWithPlaceholders()
        {
            int gameObjectsProcessed = 0;
            int missingReferencesFound = 0;
            int placeholdersAdded = 0;

            GameObject[] allGameObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);

            foreach (GameObject go in allGameObjects)
            {
                gameObjectsProcessed++;
                var missingRefsOnThisObject = new List<MissingReferenceInfo>();

                // Get all components on this GameObject
                Component[] components = go.GetComponents<Component>();

                foreach (Component component in components)
                {
                    if (component == null) continue; // Skip null/missing components
                    if (component is MissingReferencePlaceholder) continue; // Skip our own placeholders

                    // Use SerializedObject to inspect all properties
                    SerializedObject serializedObject = new SerializedObject(component);
                    SerializedProperty property = serializedObject.GetIterator();

                    // Iterate through all properties
                    bool enterChildren = true;
                    while (property.NextVisible(enterChildren))
                    {
                        enterChildren = false; // Only enter children on first iteration

                        // Check for missing object references
                        if (property.propertyType == SerializedPropertyType.ObjectReference)
                        {
                            // Missing reference: null value but non-zero instance ID indicates missing reference
                            if (property.objectReferenceValue == null && property.objectReferenceInstanceIDValue != 0)
                            {
                                string gameObjectPath = GetGameObjectPath(go);
                                string componentType = component.GetType().Name;
                                string propertyName = property.name;
                                string expectedType = GetExpectedReferenceType(property);
                                string propertyPath = property.propertyPath;

                                var missingRefInfo = new MissingReferenceInfo(
                                    componentType,
                                    propertyName,
                                    expectedType,
                                    propertyPath,
                                    gameObjectPath
                                );

                                missingRefsOnThisObject.Add(missingRefInfo);
                                missingReferencesFound++;
                            }
                        }
                    }
                }

                // If we found missing references on this GameObject, add a placeholder component
                if (missingRefsOnThisObject.Count > 0)
                {
                    Undo.RegisterCompleteObjectUndo(go, "Add missing reference placeholder");

                    // Check if a MissingReferencePlaceholder already exists
                    MissingReferencePlaceholder existingPlaceholder = go.GetComponent<MissingReferencePlaceholder>();

                    if (existingPlaceholder == null)
                    {
                        existingPlaceholder = go.AddComponent<MissingReferencePlaceholder>();
                        placeholdersAdded++;
                    }

                    // Add all missing reference info to the placeholder
                    foreach (var missingRef in missingRefsOnThisObject)
                    {
                        existingPlaceholder.AddMissingReference(
                            missingRef.componentType,
                            missingRef.propertyName,
                            missingRef.expectedType,
                            missingRef.propertyPath,
                            missingRef.gameObjectPath
                        );
                    }

                    EditorUtility.SetDirty(go);
                }
            }

            if (missingReferencesFound > 0)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                Debug.Log($"🔧 U3D SDK: Found {missingReferencesFound} missing references across {gameObjectsProcessed} GameObjects.");
                Debug.Log($"💡 U3D SDK: Added {placeholdersAdded} MissingReferencePlaceholder components to track missing references.");
                Debug.Log($"🔍 U3D SDK: Look for MissingReferencePlaceholder components in Inspector to see details and use 'Find Missing Reference Placeholders' tool to locate them all.");
            }
            else
            {
                Debug.Log($"✅ U3D SDK: No missing references found in {gameObjectsProcessed} GameObjects.");
            }
        }

        /// <summary>
        /// Find all GameObjects with MissingReferencePlaceholder components for easy identification
        /// </summary>
        public static void FindMissingReferencePlaceholders()
        {
            var placeholderObjects = new List<GameObject>();
            int totalMissingReferences = 0;

            GameObject[] allGameObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);

            foreach (GameObject go in allGameObjects)
            {
                MissingReferencePlaceholder placeholder = go.GetComponent<MissingReferencePlaceholder>();
                if (placeholder != null && placeholder.HasMissingReferences())
                {
                    placeholderObjects.Add(go);
                    totalMissingReferences += placeholder.GetMissingReferences().Count;
                }
            }

            if (placeholderObjects.Count > 0)
            {
                Debug.Log($"🔍 U3D SDK: Found {placeholderObjects.Count} GameObjects with missing reference placeholders tracking {totalMissingReferences} missing references:");

                foreach (GameObject go in placeholderObjects)
                {
                    MissingReferencePlaceholder placeholder = go.GetComponent<MissingReferencePlaceholder>();
                    var missingRefs = placeholder.GetMissingReferences();

                    string gameObjectPath = GetGameObjectPath(go);
                    Debug.Log($"📍 {gameObjectPath} - {missingRefs.Count} missing references", go);

                    // Log details for each missing reference
                    foreach (var missingRef in missingRefs)
                    {
                        Debug.Log($"   • {missingRef.componentType}.{missingRef.propertyName} (expecting {missingRef.expectedType})", go);
                    }
                }

                // Select all objects with placeholders for easy inspection
                Selection.objects = placeholderObjects.ToArray();
                Debug.Log($"🎯 U3D SDK: Selected all {placeholderObjects.Count} GameObjects with missing reference placeholders in Hierarchy.");
            }
            else
            {
                Debug.Log("✅ U3D SDK: No missing reference placeholders found in scene.");
            }
        }

        /// <summary>
        /// Remove all MissingReferencePlaceholder components from the scene
        /// </summary>
        public static void RemoveMissingReferencePlaceholders()
        {
            int removedCount = 0;
            GameObject[] allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);

            foreach (GameObject go in allObjects)
            {
                var placeholders = go.GetComponents<MissingReferencePlaceholder>();
                foreach (var placeholder in placeholders)
                {
                    Undo.DestroyObjectImmediate(placeholder);
                    removedCount++;
                }
            }

            if (removedCount > 0)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                Debug.Log($"🧼 U3D SDK: Removed {removedCount} missing reference placeholder component(s) from scene.");
            }
            else
            {
                Debug.Log("✅ U3D SDK: No missing reference placeholder components found in scene.");
            }
        }

        /// <summary>
        /// Remove missing scripts directly from scene GameObjects
        /// </summary>
        public static void RemoveMissingScriptsFromScene()
        {
            int removedCount = 0;
            int gameObjectCount = 0;

            // Loop through all open scenes
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                GameObject[] rootObjects = scene.GetRootGameObjects();
                foreach (GameObject root in rootObjects)
                {
                    GameObject[] allObjects = root.GetComponentsInChildren<Transform>(true)
                                                  .Select(t => t.gameObject)
                                                  .ToArray();

                    foreach (GameObject go in allObjects)
                    {
                        gameObjectCount++;
                        int before = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                        if (before > 0)
                        {
                            Undo.RegisterCompleteObjectUndo(go, "Remove missing scripts");
                            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                            removedCount += before;
                            EditorUtility.SetDirty(go);
                        }
                    }
                }
            }

            if (removedCount > 0)
            {
                EditorSceneManager.MarkAllScenesDirty();
                Debug.Log($"🗑️ U3D SDK: Removed {removedCount} missing script(s) from {gameObjectCount} GameObject(s) across all open scenes.");
            }
            else
            {
                Debug.Log($"✅ U3D SDK: No missing scripts found in {gameObjectCount} GameObject(s) across all open scenes.");
            }
        }

        /// <summary>
        /// Clean missing scripts from prefabs in a selected folder
        /// </summary>
        public static void CleanPrefabsInFolder()
        {
            string selectedPath = "";

            // Check if we have a selected folder in the project window
            if (Selection.activeObject != null)
            {
                string assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
                if (AssetDatabase.IsValidFolder(assetPath))
                {
                    selectedPath = assetPath;
                }
            }

            // If no folder selected, prompt user
            if (string.IsNullOrEmpty(selectedPath))
            {
                selectedPath = EditorUtility.OpenFolderPanel("Select folder containing prefabs", "Assets", "");

                if (string.IsNullOrEmpty(selectedPath))
                {
                    Debug.Log("🚫 U3D SDK: Prefab cleanup cancelled - no folder selected.");
                    return;
                }

                // Convert absolute path to relative path
                if (selectedPath.StartsWith(Application.dataPath))
                {
                    selectedPath = "Assets" + selectedPath.Substring(Application.dataPath.Length);
                }
                else
                {
                    Debug.LogError("🚫 U3D SDK: Selected folder must be within the Assets folder.");
                    return;
                }
            }

            CleanPrefabsInFolderInternal(selectedPath);
        }

        private static void CleanPrefabsInFolderInternal(string folderPath)
        {
            int removedComponentsCount = 0;
            int processedPrefabsCount = 0;

            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });
            var visitedPrefabs = new HashSet<Object>();

            for (int i = 0; i < prefabGuids.Length; i++)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);

                EditorUtility.DisplayProgressBar(
                    "U3D SDK: Cleaning Prefabs",
                    $"Processing: {Path.GetFileName(prefabPath)}",
                    (float)i / prefabGuids.Length
                );

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab != null)
                {
                    var result = CleanPrefabRecursively(prefab, visitedPrefabs);
                    removedComponentsCount += result.removedComponents;
                    if (result.wasModified)
                    {
                        processedPrefabsCount++;
                    }
                }
            }

            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (processedPrefabsCount > 0)
            {
                Debug.Log($"🧹 U3D SDK: Processed {processedPrefabsCount} prefab(s) and removed {removedComponentsCount} missing script component(s) from folder: {folderPath}");
            }
            else
            {
                Debug.Log($"✅ U3D SDK: No missing scripts found in prefabs in folder: {folderPath}");
            }
        }

        private static (int removedComponents, bool wasModified) CleanPrefabRecursively(GameObject prefabRoot, HashSet<Object> visitedPrefabs)
        {
            if (!visitedPrefabs.Add(prefabRoot)) return (0, false);

            int totalRemoved = 0;
            bool wasModified = false;

            var allObjects = prefabRoot.GetComponentsInChildren<Transform>(true)
                .Select(t => t.gameObject);

            foreach (var go in allObjects)
            {
                if (PrefabUtility.IsPartOfAnyPrefab(go))
                {
                    var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
                    if (source != null && visitedPrefabs.Add(source))
                    {
                        var result = CleanGameObjectPrefab(source);
                        totalRemoved += result.removedCount;
                        if (result.wasModified) wasModified = true;
                    }
                }

                var goResult = CleanGameObjectPrefab(go);
                totalRemoved += goResult.removedCount;
                if (goResult.wasModified) wasModified = true;
            }

            return (totalRemoved, wasModified);
        }

        private static (int removedCount, bool wasModified) CleanGameObjectPrefab(GameObject go)
        {
            int missingCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            if (missingCount > 0)
            {
                Undo.RegisterCompleteObjectUndo(go, "Remove missing scripts from prefab");
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                EditorUtility.SetDirty(go);
                return (missingCount, true);
            }
            return (0, false);
        }

        /// <summary>
        /// Get the full hierarchy path of a GameObject
        /// </summary>
        private static string GetGameObjectPath(GameObject go)
        {
            if (go.transform.parent == null)
                return go.name;

            return GetGameObjectPath(go.transform.parent.gameObject) + "/" + go.name;
        }

        /// <summary>
        /// Get the expected type name for an object reference property
        /// </summary>
        private static string GetExpectedReferenceType(SerializedProperty property)
        {
            // Try to get type info from the property
            if (property.type.StartsWith("PPtr<$"))
            {
                // Extract type from PPtr<$TypeName>
                string typeName = property.type.Substring(6, property.type.Length - 7);
                return typeName;
            }

            // Fallback to generic Object if we can't determine the type
            return "Object";
        }
    }
}