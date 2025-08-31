using UnityEngine;
using Fusion;
using System.Collections.Generic;
using System.Collections;

namespace U3D.Networking
{
    /// <summary>
    /// Manages per-player instances of interactive objects for collaborative experiences
    /// Creators place one object, each Visitor gets their own networked copy
    /// Maintains backward compatibility with existing single-object workflows
    /// </summary>
    public class U3DInstanceManager : NetworkBehaviour
    {
        [Header("Instance Configuration")]
        [Tooltip("Enable per-player instancing for this object")]
        [SerializeField] private bool enableInstancing = true;

        [Tooltip("Original prefab to instance for each player")]
        [SerializeField] private GameObject instancePrefab;

        [Tooltip("Spacing between player instances")]
        [SerializeField] private Vector3 instanceSpacing = new Vector3(1.5f, 0f, 0f);

        [Tooltip("Maximum instances to prevent spam")]
        [SerializeField] private int maxInstances = 10;

        [Tooltip("Auto-destroy instances when player leaves")]
        [SerializeField] private bool cleanupOnPlayerLeave = true;

        [Header("Visual Feedback")]
        [Tooltip("Show instance positions in editor")]
        [SerializeField] private bool showInstancePositions = true;

        // State tracking
        [Networked] public int ActiveInstanceCount { get; set; }
        private Dictionary<PlayerRef, NetworkObject> playerInstances = new Dictionary<PlayerRef, NetworkObject>();
        private Dictionary<PlayerRef, int> playerInstanceIndices = new Dictionary<PlayerRef, int>();
        private bool isInitialized = false;

        // Static registry for network manager integration
        private static HashSet<U3DInstanceManager> allManagers = new HashSet<U3DInstanceManager>();

        public override void Spawned()
        {
            if (!enableInstancing)
            {
                Debug.Log($"Instance Manager on '{name}' disabled - object will use standard single-instance behavior");
                return;
            }

            // Register with global manager list
            allManagers.Add(this);

            // Only authority creates instances
            if (!Object.HasStateAuthority) return;

            // Start initialization after brief delay to ensure all players are connected
            StartCoroutine(DelayedInitialization());
        }

        private IEnumerator DelayedInitialization()
        {
            yield return new WaitForSeconds(0.5f);

            // Create instances for all currently connected players
            foreach (PlayerRef player in Runner.ActivePlayers)
            {
                CreateInstanceForPlayer(player);
            }

            isInitialized = true;
            Debug.Log($"Instance Manager '{name}' initialized with {playerInstances.Count} instances");
        }

        /// <summary>
        /// Create networked instance for specific player
        /// Called automatically when players join or manually for late joiners
        /// </summary>
        public void CreateInstanceForPlayer(PlayerRef player)
        {
            if (!enableInstancing || !Object.HasStateAuthority) return;

            // Check if player already has instance
            if (playerInstances.ContainsKey(player))
            {
                Debug.Log($"Player {player} already has instance of '{name}'");
                return;
            }

            // Enforce max instances limit
            if (ActiveInstanceCount >= maxInstances)
            {
                Debug.LogWarning($"Max instances ({maxInstances}) reached for '{name}' - cannot create more");
                return;
            }

            // Determine spawn position and rotation
            int instanceIndex = ActiveInstanceCount;
            Vector3 spawnPosition = transform.position + (instanceSpacing * instanceIndex);
            Quaternion spawnRotation = transform.rotation;

            // Create the networked instance with player as authority
            NetworkObject instance = Runner.Spawn(instancePrefab, spawnPosition, spawnRotation, player);

            if (instance != null)
            {
                playerInstances[player] = instance;
                playerInstanceIndices[player] = instanceIndex;
                ActiveInstanceCount++;

                // Configure the instance for proper player ownership
                ConfigureInstanceForPlayer(instance, player, instanceIndex);

                Debug.Log($"✅ Created instance of '{name}' for player {player} at position {spawnPosition}");

                // Notify all clients about new instance
                RPC_NotifyInstanceCreated(player, instanceIndex);
            }
            else
            {
                Debug.LogError($"❌ Failed to spawn instance of '{name}' for player {player}");
            }
        }

        /// <summary>
        /// Configure spawned instance with player-specific settings
        /// </summary>
        private void ConfigureInstanceForPlayer(NetworkObject instance, PlayerRef player, int instanceIndex)
        {
            // Set a clear name for debugging
            instance.name = $"{instancePrefab.name}_Player{player}_{instanceIndex}";

            // Configure the instance with a slight delay to ensure spawning completes
            StartCoroutine(DelayedInstanceConfiguration(instance, player, instanceIndex));
        }

        private IEnumerator DelayedInstanceConfiguration(NetworkObject instance, PlayerRef player, int instanceIndex)
        {
            yield return new WaitForSeconds(0.1f); // Wait for spawn to complete

            if (instance != null)
            {
                var grabbable = instance.GetComponent<U3DGrabbable>();
                if (grabbable != null)
                {
                    grabbable.SetInstanceMode(true, player);
                    Debug.Log($"✅ Configured instance '{instance.name}' for player {player}");
                }

                // Configure throwable if present
                var throwable = instance.GetComponent<U3DThrowable>();
                if (throwable != null)
                {
                    throwable.UpdateSpawnPosition(instance.transform.position, instance.transform.rotation);
                }
            }
        }

        /// <summary>
        /// Remove instance when player leaves (if cleanup enabled)
        /// </summary>
        public void RemoveInstanceForPlayer(PlayerRef player)
        {
            if (!playerInstances.TryGetValue(player, out NetworkObject instance))
            {
                return; // Player didn't have an instance
            }

            if (instance != null)
            {
                Runner.Despawn(instance);
                Debug.Log($"🗑️ Removed instance of '{name}' for disconnected player {player}");
            }

            playerInstances.Remove(player);
            playerInstanceIndices.Remove(player);
            ActiveInstanceCount--;

            // Notify all clients about instance removal
            RPC_NotifyInstanceRemoved(player);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_NotifyInstanceCreated(PlayerRef player, int instanceIndex)
        {
            Debug.Log($"📡 Instance created notification: Player {player}, Index {instanceIndex}");
            // Additional client-side setup if needed
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_NotifyInstanceRemoved(PlayerRef player)
        {
            Debug.Log($"📡 Instance removed notification: Player {player}");
            // Additional client-side cleanup if needed
        }

        /// <summary>
        /// Get instance owned by specific player
        /// </summary>
        public NetworkObject GetInstanceForPlayer(PlayerRef player)
        {
            return playerInstances.TryGetValue(player, out NetworkObject instance) ? instance : null;
        }

        /// <summary>
        /// Get all active instances
        /// </summary>
        public Dictionary<PlayerRef, NetworkObject> GetAllInstances()
        {
            return new Dictionary<PlayerRef, NetworkObject>(playerInstances);
        }

        /// <summary>
        /// Static method called by NetworkManager when players join
        /// </summary>
        public static void HandlePlayerJoined(PlayerRef player)
        {
            Debug.Log($"🔗 Instance Manager handling player join: {player}");

            foreach (var manager in allManagers)
            {
                if (manager != null && manager.enableInstancing && manager.isInitialized)
                {
                    manager.CreateInstanceForPlayer(player);
                }
            }
        }

        /// <summary>
        /// Static method called by NetworkManager when players leave
        /// </summary>
        public static void HandlePlayerLeft(PlayerRef player)
        {
            Debug.Log($"🔗 Instance Manager handling player leave: {player}");

            foreach (var manager in allManagers)
            {
                if (manager != null && manager.enableInstancing && manager.cleanupOnPlayerLeave)
                {
                    manager.RemoveInstanceForPlayer(player);
                }
            }
        }

        /// <summary>
        /// Convert existing single object to use instancing
        /// Called by InteractionToolsCategory when adding instance support
        /// </summary>
        public void ConvertToInstancedObject()
        {
            if (instancePrefab == null)
            {
                // Try to create prefab from current object
                var grabbable = GetComponent<U3DGrabbable>();
                var throwable = GetComponent<U3DThrowable>();

                if (grabbable != null || throwable != null)
                {
                    Debug.Log($"Converting '{name}' to instanced object - you may need to assign the Instance Prefab manually");
                    instancePrefab = gameObject; // Temporary assignment - creator should make proper prefab
                }
            }

            enableInstancing = true;
            Debug.Log($"✅ Converted '{name}' to use per-player instancing");
        }

        private void OnDrawGizmosSelected()
        {
            if (!showInstancePositions || !enableInstancing) return;

            // Draw instance positions
            Gizmos.color = Color.cyan;
            for (int i = 0; i < maxInstances; i++)
            {
                Vector3 instancePos = transform.position + (instanceSpacing * i);
                Gizmos.DrawWireCube(instancePos, Vector3.one * 0.5f);

                // Draw index numbers
#if UNITY_EDITOR
                UnityEditor.Handles.Label(instancePos + Vector3.up * 0.7f, $"P{i}");
#endif
            }

            // Draw original position
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 0.3f);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            allManagers.Remove(this);

            // Cleanup all instances if this manager is being destroyed
            if (hasState && Object.HasStateAuthority)
            {
                foreach (var kvp in playerInstances)
                {
                    if (kvp.Value != null)
                    {
                        runner.Despawn(kvp.Value);
                    }
                }
            }

            playerInstances.Clear();
            playerInstanceIndices.Clear();
        }

        private void OnValidate()
        {
            if (maxInstances <= 0)
            {
                maxInstances = 1;
            }

            if (instanceSpacing.magnitude < 0.1f)
            {
                instanceSpacing = new Vector3(0f, 0f, 0f);
            }
        }

        // Debug info for development
        [System.Serializable]
        public struct InstanceManagerDebugInfo
        {
            public bool isEnabled;
            public bool isInitialized;
            public int activeInstances;
            public int maxInstances;
            public bool hasAuthority;
            public Vector3 spacing;
        }

        public InstanceManagerDebugInfo GetDebugInfo()
        {
            return new InstanceManagerDebugInfo
            {
                isEnabled = enableInstancing,
                isInitialized = isInitialized,
                activeInstances = ActiveInstanceCount,
                maxInstances = maxInstances,
                hasAuthority = Object?.HasStateAuthority ?? false,
                spacing = instanceSpacing
            };
        }
    }
}