using UnityEngine;
using System.Collections.Generic;

namespace U3D.Networking
{
    /// <summary>
    /// Enhanced player spawner with rotation support
    /// Supports both simple tagged GameObjects and enhanced U3D_SpawnPoint components
    /// </summary>
    public class U3DPlayerSpawner : MonoBehaviour
    {
        [Header("Fallback Settings")]
        [Tooltip("Used when no spawn points are found")]
        [SerializeField] private Vector3 defaultSpawnPosition = Vector3.zero;

        [Tooltip("Default Y rotation when using simple spawn points without U3D_SpawnPoint component")]
        [SerializeField] private float defaultSpawnYRotation = 0f;

        [Header("Spawn Behavior")]
        [Tooltip("Use random spawn points instead of cycling through them")]
        [SerializeField] private bool useRandomSpawning = false;

        // Runtime state - support both enhanced and simple spawn points
        private List<U3D_SpawnPoint> enhancedSpawnPoints = new List<U3D_SpawnPoint>();
        private List<Transform> simpleSpawnPoints = new List<Transform>();
        private int lastUsedIndex = -1;

        // Singleton access
        public static U3DPlayerSpawner Instance { get; private set; }

        void Awake()
        {
            // Singleton pattern
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            // Find all spawn points
            FindSpawnPoints();
        }

        void FindSpawnPoints()
        {
            // Clear existing lists
            enhancedSpawnPoints.Clear();
            simpleSpawnPoints.Clear();

            // Find all GameObjects with SpawnPoint tag
            var taggedSpawnPoints = GameObject.FindGameObjectsWithTag("SpawnPoint");

            foreach (var spawnPoint in taggedSpawnPoints)
            {
                // Check if it has the enhanced component
                var enhancedComponent = spawnPoint.GetComponent<U3D_SpawnPoint>();
                if (enhancedComponent != null)
                {
                    enhancedSpawnPoints.Add(enhancedComponent);
                }
                else
                {
                    // Simple spawn point (just Transform)
                    simpleSpawnPoints.Add(spawnPoint.transform);
                }
            }

            int totalSpawnPoints = enhancedSpawnPoints.Count + simpleSpawnPoints.Count;
            Debug.Log($"Found {enhancedSpawnPoints.Count} enhanced spawn points and {simpleSpawnPoints.Count} simple spawn points (Total: {totalSpawnPoints})");
        }

        /// <summary>
        /// Get spawn position - maintains backward compatibility
        /// </summary>
        public Vector3 GetSpawnPosition()
        {
            var spawnData = GetSpawnData();
            return spawnData.position;
        }

        /// <summary>
        /// Get spawn rotation - NEW functionality
        /// </summary>
        public Quaternion GetSpawnRotation()
        {
            var spawnData = GetSpawnData();
            return spawnData.rotation;
        }

        /// <summary>
        /// Get complete spawn data (position + rotation)
        /// </summary>
        public (Vector3 position, Quaternion rotation) GetSpawnData()
        {
            int totalSpawnPoints = enhancedSpawnPoints.Count + simpleSpawnPoints.Count;

            // If no spawn points, use defaults
            if (totalSpawnPoints == 0)
            {
                Debug.LogWarning("No spawn points found, using default position and rotation");
                return (defaultSpawnPosition, Quaternion.Euler(0, defaultSpawnYRotation, 0));
            }

            // Determine spawn point index
            int spawnIndex;
            if (useRandomSpawning)
            {
                spawnIndex = Random.Range(0, totalSpawnPoints);
            }
            else
            {
                lastUsedIndex = (lastUsedIndex + 1) % totalSpawnPoints;
                spawnIndex = lastUsedIndex;
            }

            // Get spawn data from appropriate list
            if (spawnIndex < enhancedSpawnPoints.Count)
            {
                // Use enhanced spawn point (preferred)
                var enhancedPoint = enhancedSpawnPoints[spawnIndex];
                var spawnData = enhancedPoint.GetSpawnData();

                Debug.Log($"Using enhanced spawn point {spawnIndex}: pos={spawnData.position}, rot={spawnData.rotation.eulerAngles.y}°");
                return spawnData;
            }
            else
            {
                // Use simple spawn point with default rotation
                int simpleIndex = spawnIndex - enhancedSpawnPoints.Count;
                Vector3 spawnPos = simpleSpawnPoints[simpleIndex].position;
                Quaternion spawnRot = Quaternion.Euler(0, defaultSpawnYRotation, 0);

                Debug.Log($"Using simple spawn point {simpleIndex}: pos={spawnPos}, default rot={defaultSpawnYRotation}°");
                return (spawnPos, spawnRot);
            }
        }

        /// <summary>
        /// Get random spawn position - maintains backward compatibility
        /// </summary>
        public Vector3 GetRandomSpawnPosition()
        {
            bool originalSetting = useRandomSpawning;
            useRandomSpawning = true;

            Vector3 position = GetSpawnPosition();

            useRandomSpawning = originalSetting;
            return position;
        }

        /// <summary>
        /// Refresh spawn points - call if spawn points are added/removed at runtime
        /// </summary>
        public void RefreshSpawnPoints()
        {
            FindSpawnPoints();
        }

        /// <summary>
        /// Get total spawn point count
        /// </summary>
        public int GetSpawnPointCount()
        {
            return enhancedSpawnPoints.Count + simpleSpawnPoints.Count;
        }
    }
}