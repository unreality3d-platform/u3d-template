using UnityEngine;
using System.Collections.Generic;

namespace U3D.Networking
{
    public class U3DPlayerSpawner : MonoBehaviour
    {
        [Header("Fallback Settings")]
        [Tooltip("Used when no spawn points are found in the scene")]
        [SerializeField] private Vector3 defaultSpawnPosition = Vector3.zero;

        [Tooltip("Default Y rotation when using simple spawn points without U3D_SpawnPoint component")]
        [SerializeField] private float defaultSpawnYRotation = 0f;

        [Header("Spawn Behavior")]
        [Tooltip("Use random spawn points instead of cycling through them")]
        [SerializeField] private bool useRandomSpawning = false;

        private List<U3D_SpawnPoint> enhancedSpawnPoints = new List<U3D_SpawnPoint>();
        private List<Transform> simpleSpawnPoints = new List<Transform>();
        private int lastUsedIndex = -1;

        public static U3DPlayerSpawner Instance { get; private set; }

        void Awake()
        {
            if (Instance == null)
                Instance = this;
            else if (Instance != this)
                Destroy(gameObject);
        }

        void Start()
        {
            if (gameObject.scene.name == "DontDestroyOnLoad")
            {
                Instance = null; // clear so proxy can register as Instance
                CreateSceneLevelProxy();
                enabled = false;
                return;
            }

            FindSpawnPoints();
        }

        private void CreateSceneLevelProxy()
        {
            // Capture world position/rotation before any scene moves
            Vector3 worldPosition = transform.position;
            float worldRotationY = transform.eulerAngles.y;

            var proxyGO = new GameObject("U3D_SpawnPoint_Runtime");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(
                proxyGO,
                UnityEngine.SceneManagement.SceneManager.GetActiveScene()
            );

            var proxy = proxyGO.AddComponent<U3DPlayerSpawner>();
            proxy.defaultSpawnPosition = worldPosition;
            proxy.defaultSpawnYRotation = worldRotationY;
            proxy.useRandomSpawning = useRandomSpawning;
            // proxy.Awake() fires immediately on AddComponent and registers as Instance
        }

        void FindSpawnPoints()
        {
            enhancedSpawnPoints.Clear();
            simpleSpawnPoints.Clear();

            var taggedSpawnPoints = GameObject.FindGameObjectsWithTag("SpawnPoint");

            foreach (var spawnPoint in taggedSpawnPoints)
            {
                var enhancedComponent = spawnPoint.GetComponent<U3D_SpawnPoint>();
                if (enhancedComponent != null)
                    enhancedSpawnPoints.Add(enhancedComponent);
                else
                    simpleSpawnPoints.Add(spawnPoint.transform);
            }
        }

        public Vector3 GetSpawnPosition()
        {
            return GetSpawnData().position;
        }

        public Quaternion GetSpawnRotation()
        {
            return GetSpawnData().rotation;
        }

        public (Vector3 position, Quaternion rotation) GetSpawnData()
        {
            int totalSpawnPoints = enhancedSpawnPoints.Count + simpleSpawnPoints.Count;

            if (totalSpawnPoints == 0)
            {
                return (defaultSpawnPosition, Quaternion.Euler(0, defaultSpawnYRotation, 0));
            }

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

            if (spawnIndex < enhancedSpawnPoints.Count)
            {
                return enhancedSpawnPoints[spawnIndex].GetSpawnData();
            }
            else
            {
                int simpleIndex = spawnIndex - enhancedSpawnPoints.Count;
                return (simpleSpawnPoints[simpleIndex].position,
                        Quaternion.Euler(0, defaultSpawnYRotation, 0));
            }
        }

        public Vector3 GetRandomSpawnPosition()
        {
            bool original = useRandomSpawning;
            useRandomSpawning = true;
            Vector3 position = GetSpawnPosition();
            useRandomSpawning = original;
            return position;
        }

        public void RefreshSpawnPoints()
        {
            FindSpawnPoints();
        }

        public int GetSpawnPointCount()
        {
            return enhancedSpawnPoints.Count + simpleSpawnPoints.Count;
        }
    }
}