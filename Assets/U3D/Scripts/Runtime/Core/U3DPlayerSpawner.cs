using UnityEngine;
using System.Collections.Generic;

namespace U3D.Networking
{
    public class U3DPlayerSpawner : MonoBehaviour
    {
        [Header("Spawn Behavior")]
        [Tooltip("Use random spawn points instead of cycling through them")]
        [SerializeField] private bool useRandomSpawning = false;

        private List<U3DPlayerSpawnPoint> enhancedSpawnPoints = new List<U3DPlayerSpawnPoint>();
        private List<Transform> simpleSpawnPoints = new List<Transform>();
        private int lastUsedIndex = -1;

        // Used only by the DontDestroyOnLoad proxy to carry position into the scene
        private Vector3 _fallbackPosition;
        private float _fallbackRotationY;

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
                Instance = null;
                CreateSceneLevelProxy();
                enabled = false;
                return;
            }

            FindSpawnPoints();
        }

        private void CreateSceneLevelProxy()
        {
            Vector3 worldPosition = transform.position;
            float worldRotationY = transform.eulerAngles.y;

            var proxyGO = new GameObject("U3DPlayerSpawnPoint_Runtime");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(
                proxyGO,
                UnityEngine.SceneManagement.SceneManager.GetActiveScene()
            );

            var proxy = proxyGO.AddComponent<U3DPlayerSpawner>();
            proxy.useRandomSpawning = useRandomSpawning;
            proxy._fallbackPosition = worldPosition;
            proxy._fallbackRotationY = worldRotationY;
        }

        void FindSpawnPoints()
        {
            enhancedSpawnPoints.Clear();
            simpleSpawnPoints.Clear();

            var taggedSpawnPoints = GameObject.FindGameObjectsWithTag("PlayerSpawnPoint");

            foreach (var spawnPoint in taggedSpawnPoints)
            {
                var enhancedComponent = spawnPoint.GetComponent<U3DPlayerSpawnPoint>();
                if (enhancedComponent != null)
                    enhancedSpawnPoints.Add(enhancedComponent);
                else
                    simpleSpawnPoints.Add(spawnPoint.transform);
            }
        }

        public Vector3 GetSpawnPosition() => GetSpawnData().position;
        public Quaternion GetSpawnRotation() => GetSpawnData().rotation;

        public (Vector3 position, Quaternion rotation) GetSpawnData()
        {
            int totalSpawnPoints = enhancedSpawnPoints.Count + simpleSpawnPoints.Count;

            if (totalSpawnPoints == 0)
            {
                Vector3 fallback = _fallbackPosition != Vector3.zero ? _fallbackPosition : transform.position;
                float rotY = _fallbackPosition != Vector3.zero ? _fallbackRotationY : transform.eulerAngles.y;
                return (fallback, Quaternion.Euler(0, rotY, 0));
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
                return enhancedSpawnPoints[spawnIndex].GetSpawnData();

            int simpleIndex = spawnIndex - enhancedSpawnPoints.Count;
            return (simpleSpawnPoints[simpleIndex].position,
                    Quaternion.Euler(0, transform.eulerAngles.y, 0));
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