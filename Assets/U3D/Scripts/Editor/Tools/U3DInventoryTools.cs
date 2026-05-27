using UnityEngine;
using UnityEditor;
using Fusion;

namespace U3D.Editor
{
    public static class U3DInventoryTools
    {
        /// <summary>
        /// Adds a new Inventory GameObject to the scene at the scene view pivot.
        /// Ignores current selection (matches U3DScorableTools.AddScorable's pattern).
        /// </summary>
        public static void AddInventory()
        {
            U3DInventory existing = Object.FindAnyObjectByType<U3DInventory>();
            if (existing != null)
            {
                Debug.Log("U3DInventory already exists in this scene. Selecting the existing one.");
                Selection.activeGameObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing.gameObject);
                return;
            }

            GameObject inventoryObj = new GameObject("Inventory");
            inventoryObj.AddComponent<U3DInventory>();

            if (SceneView.lastActiveSceneView != null)
                inventoryObj.transform.position = SceneView.lastActiveSceneView.pivot;

            Selection.activeGameObject = inventoryObj;
            EditorGUIUtility.PingObject(inventoryObj);
            EditorUtility.SetDirty(inventoryObj);
        }

        /// <summary>
        /// Adds U3DCollectable to the selected object. Auto-adds a trigger Collider
        /// and (when missing) a NetworkObject configured for Shared Mode.
        /// Also ensures the scene has a U3DInventory — Collectable cannot function
        /// without one, so we pair-add it the same way Make Throwable pair-adds Grabbable.
        /// </summary>
        public static void ApplyCollectable()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("Please select an object first");
                return;
            }

            Collider collider = selected.GetComponent<Collider>();
            if (collider == null)
                collider = selected.AddComponent<BoxCollider>();
            collider.isTrigger = true;

            if (!selected.GetComponent<NetworkObject>())
            {
                var networkObject = selected.AddComponent<NetworkObject>();
                InteractionToolsCategory.ConfigureNetworkObjectForSharedMode(networkObject);
            }

            if (selected.GetComponent<U3DCollectable>() == null)
            {
                selected.AddComponent<U3DCollectable>();
            }
            else
            {
                Debug.Log(
                    $"'{selected.name}' already has a U3D Collectable. " +
                    $"To add a second collectable with different settings, use the Inspector's Add Component button " +
                    $"and search for 'U3D Collectable'."
                );
            }

            // Pair-add: Collectable does nothing without an Inventory. If the scene doesn't
            // have one, create it now so the creator gets a working setup from one click.
            // Restore the original selection afterward so the creator stays focused on the
            // object they just made collectable.
            if (UnityEngine.Object.FindAnyObjectByType<U3DInventory>() == null)
            {
                AddInventory();
                Selection.activeGameObject = selected;
                EditorGUIUtility.PingObject(selected);
            }

            EditorUtility.SetDirty(selected);
        }
    }
}
