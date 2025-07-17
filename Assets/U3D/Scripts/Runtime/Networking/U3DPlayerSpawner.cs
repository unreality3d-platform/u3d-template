using UnityEngine;
using System.Collections.Generic;

namespace U3D.Networking
{
    /// <summary>
    /// Minimal player spawner - just finds spawn points and returns positions
    /// </summary>
    public class U3DPlayerSpawner : MonoBehaviour
    {
        [Header("Basic Settings")]
        [SerializeField] private Vector3 defaultSpawnPosition = Vector3.zero;

        // Runtime state
        private List<Transform> spawnPoints = new List<Transform>();
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
            var foundSpawnPoints = GameObject.FindGameObjectsWithTag("SpawnPoint");

            spawnPoints.Clear();
            foreach (var spawnPoint in foundSpawnPoints)
            {
                spawnPoints.Add(spawnPoint.transform);
            }

            Debug.Log($"Found {spawnPoints.Count} spawn points");
        }

        /// <summary>
        /// Get a spawn position - cycles through available spawn points
        /// </summary>
        public Vector3 GetSpawnPosition()
        {
            // If no spawn points, use default
            if (spawnPoints.Count == 0)
            {
                Debug.LogWarning("No spawn points found, using default position");
                return defaultSpawnPosition;
            }

            // Cycle through spawn points
            lastUsedIndex = (lastUsedIndex + 1) % spawnPoints.Count;
            Vector3 spawnPos = spawnPoints[lastUsedIndex].position;

            Debug.Log($"Using spawn point {lastUsedIndex}: {spawnPos}");
            return spawnPos;
        }

        /// <summary>
        /// Get a random spawn position
        /// </summary>
        public Vector3 GetRandomSpawnPosition()
        {
            if (spawnPoints.Count == 0)
            {
                return defaultSpawnPosition;
            }

            int randomIndex = Random.Range(0, spawnPoints.Count);
            return spawnPoints[randomIndex].position;
        }
    }
}