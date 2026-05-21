using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Fusion;
using Fusion.Editor;
using Fusion.Addons.Physics;

namespace U3D.Editor
{
    [CustomEditor(typeof(U3DObjectSpawner))]
    public class U3DObjectSpawnerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Prefab Setup", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "If 'Networked Spawn' is enabled above, your prefabs must have a NetworkObject component and be registered with Fusion. " +
                "Click the button below to configure all assigned prefabs automatically.",
                MessageType.Info
            );

            if (GUILayout.Button("Configure Prefab(s) for Networking", GUILayout.Height(28)))
            {
                ConfigurePrefabsForNetworking((U3DObjectSpawner)target);
            }
        }

        private static void ConfigurePrefabsForNetworking(U3DObjectSpawner spawner)
        {
            var prefabs = CollectPrefabs(spawner);

            if (prefabs.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Configure Prefab(s) for Networking",
                    "No prefabs are assigned to this spawner. Assign a prefab to 'Prefab To Spawn' or add entries to 'Prefab List' first.",
                    "OK"
                );
                return;
            }

            var report = new List<string>();
            int configuredCount = 0;

            foreach (var prefab in prefabs)
            {
                if (ConfigureSinglePrefab(prefab, report))
                    configuredCount++;
            }

            // Rebuild the Fusion prefab table so newly added NetworkObjects are
            // registered. This is the step creators were having to do manually.
            NetworkProjectConfigUtilities.RebuildPrefabTable();
            report.Add("• Rebuilt Fusion prefab table.");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string summary = configuredCount == 0
                ? "All assigned prefabs were already configured correctly."
                : $"Configured {configuredCount} prefab(s) for networked spawning.";

            string details = string.Join("\n", report);

            Debug.Log($"U3DObjectSpawner: {summary}\n{details}");

            EditorUtility.DisplayDialog(
                "Configure Prefab(s) for Networking",
                $"{summary}\n\nSee the Console for details.",
                "OK"
            );
        }

        private static List<GameObject> CollectPrefabs(U3DObjectSpawner spawner)
        {
            var prefabs = new List<GameObject>();
            var seen = new HashSet<GameObject>();

            if (spawner.prefabToSpawn != null && seen.Add(spawner.prefabToSpawn))
                prefabs.Add(spawner.prefabToSpawn);

            // prefabList is private — read via SerializedObject.
            var so = new SerializedObject(spawner);
            var listProp = so.FindProperty("prefabList");
            if (listProp != null && listProp.isArray)
            {
                for (int i = 0; i < listProp.arraySize; i++)
                {
                    var entry = listProp.GetArrayElementAtIndex(i);
                    var prefabProp = entry.FindPropertyRelative("prefab");
                    if (prefabProp != null && prefabProp.objectReferenceValue is GameObject go && go != null)
                    {
                        if (seen.Add(go))
                            prefabs.Add(go);
                    }
                }
            }

            return prefabs;
        }

        /// <summary>
        /// Configures a single prefab asset for networked spawning. Returns true
        /// if any changes were made, false if it was already correctly configured.
        /// </summary>
        private static bool ConfigureSinglePrefab(GameObject prefab, List<string> report)
        {
            string path = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(path))
            {
                report.Add($"• Skipped '{prefab.name}': not a prefab asset.");
                return false;
            }

            // LoadPrefabContents gives us an isolated editable copy of the prefab.
            // We modify it, then save it back via SaveAsPrefabAsset.
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
            bool changed = false;
            var prefabChanges = new List<string>();

            try
            {
                // 1. NetworkObject
                var networkObject = prefabRoot.GetComponent<NetworkObject>();
                if (networkObject == null)
                {
                    networkObject = prefabRoot.AddComponent<NetworkObject>();
                    prefabChanges.Add("added NetworkObject");
                    changed = true;
                }

                // Always reconfigure flags so existing prefabs with default flags
                // get upgraded to the correct AllowStateAuthorityOverride setting.
                if (ConfigureNetworkObjectFlags(networkObject))
                {
                    if (!prefabChanges.Contains("added NetworkObject"))
                        prefabChanges.Add("updated NetworkObject Flags");
                    changed = true;
                }

                // 2. NetworkRigidbody3D — only if the prefab has a Rigidbody.
                // Physics objects without NetworkRigidbody3D desync between clients,
                // so this is almost always what creators want.
                var rigidbody = prefabRoot.GetComponent<Rigidbody>();
                if (rigidbody != null && prefabRoot.GetComponent<NetworkRigidbody3D>() == null)
                {
                    var nrb = prefabRoot.AddComponent<NetworkRigidbody3D>();
                    ConfigureNetworkRigidbody3DFlags(nrb);
                    prefabChanges.Add("added NetworkRigidbody3D (Rigidbody detected)");
                    changed = true;
                }
                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
                    report.Add($"• {prefab.name}: {string.Join(", ", prefabChanges)}.");
                }
                else
                {
                    report.Add($"• {prefab.name}: already configured correctly.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            return changed;
        }

        /// <summary>
        /// Sets the NetworkObject's Flags to AllowStateAuthorityOverride, matching
        /// the pattern used by InteractionToolsCategory.ConfigureNetworkObjectForSharedMode.
        /// Returns true if the value changed.
        /// </summary>
        private static bool ConfigureNetworkObjectFlags(NetworkObject networkObject)
        {
            var so = new SerializedObject(networkObject);
            var flagsProp = so.FindProperty("Flags");
            if (flagsProp == null) return false;

            int desired = (int)NetworkObjectFlags.AllowStateAuthorityOverride;
            if (flagsProp.intValue == desired) return false;

            flagsProp.intValue = desired;
            so.ApplyModifiedProperties();
            return true;
        }

        private static void ConfigureNetworkRigidbody3DFlags(NetworkRigidbody3D nrb)
        {
            var so = new SerializedObject(nrb);

            var syncParentProp = so.FindProperty("_syncParent");
            if (syncParentProp != null)
                syncParentProp.boolValue = false;

            var syncModeProp = so.FindProperty("_syncMode");
            if (syncModeProp != null)
                syncModeProp.intValue = 0;

            so.ApplyModifiedProperties();
        }

        private static void ConfigureNetworkRigidbody3DFlagsReflection(Component nrb)
        {
            if (nrb == null) return;
            var so = new SerializedObject(nrb);

            var syncParentProp = so.FindProperty("_syncParent");
            if (syncParentProp != null)
                syncParentProp.boolValue = false;

            var syncModeProp = so.FindProperty("_syncMode");
            if (syncModeProp != null)
                syncModeProp.intValue = 0;

            so.ApplyModifiedProperties();
        }
    }
}