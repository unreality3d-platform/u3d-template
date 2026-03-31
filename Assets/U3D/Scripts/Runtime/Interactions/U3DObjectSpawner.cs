using UnityEngine;
using UnityEngine.Events;
using Fusion;

namespace U3D
{
    [System.Serializable]
    public struct SpawnEntry
    {
        [Tooltip("The prefab to spawn.")]
        public GameObject prefab;

        [Tooltip("Relative spawn weight. Higher values mean this prefab is chosen more often. A weight of 0 removes it from the pool.")]
        [Min(0f)]
        public float weight;
    }

    /// <summary>
    /// Spawns a prefab at this object's position and rotation.
    /// All spawns are networked via Fusion so every player sees the result.
    /// Place this component on any GameObject to define where and what spawns.
    /// The prefab must have a NetworkObject component for all players to see it.
    /// Without NetworkObject on the prefab, only the local player will see it.
    ///
    /// Supports a single prefab or a weighted list of prefabs with random selection.
    /// When a prefab list is populated, a prefab is chosen based on relative weights.
    /// When the list is empty, the single Prefab To Spawn field is used instead.
    /// </summary>
    public class U3DObjectSpawner : NetworkBehaviour
    {
        [Header("What to Spawn")]
        [Tooltip("The prefab to spawn at this location. If a Prefab List is populated below, that list is used instead. Add a NetworkObject component to your prefab so all players see it. Without NetworkObject, only the local player will see the spawned object.")]
        public GameObject prefabToSpawn;

        [Tooltip("Optional weighted list of prefabs. When populated, a prefab is chosen based on relative weights each time Spawn is called. Each prefab should have a NetworkObject component for networked spawning.")]
        [SerializeField] private SpawnEntry[] prefabList;

        [Header("Spawn Behavior")]
        [Tooltip("Spawn automatically when the scene starts.")]
        public bool spawnOnStart = true;

        [Tooltip("Respawn automatically when the spawned object is destroyed.")]
        public bool respawnWhenDestroyed = false;

        [Tooltip("Maximum number of spawned objects that can exist at once. New spawns are blocked when this limit is reached.")]
        public int maxInstances = 1;

        [Header("Optional Label")]
        [Tooltip("Assign a U3DWorldspaceUI in your scene to show a label near this spawner. Edit the text on that object directly.")]
        public U3DWorldspaceUI labelUI;

        [Header("Events")]
        public UnityEvent<GameObject> onSpawned;
        public UnityEvent onSpawnFailed;

        [Networked] private int NetworkActiveCount { get; set; }

        private int _localActiveCount = 0;

        public override void Spawned()
        {
            if (Object.HasStateAuthority && spawnOnStart)
                Spawn();
        }

        void Start()
        {
            if (Runner == null && spawnOnStart)
                SpawnLocal();
        }

        /// <summary>
        /// Call this from any UnityEvent, trigger, or script to request a spawn.
        /// Non-host clients automatically forward the request to the host via RPC.
        /// </summary>
        public void Spawn()
        {
            GameObject resolved = ResolvePrefab();
            if (resolved == null)
            {
                Debug.LogWarning($"U3DObjectSpawner on '{name}': No prefab assigned.");
                onSpawnFailed?.Invoke();
                return;
            }

            if (Runner == null)
            {
                SpawnLocal(resolved);
                return;
            }

            int activeCount = Object != null ? NetworkActiveCount : _localActiveCount;
            if (activeCount >= maxInstances)
            {
                onSpawnFailed?.Invoke();
                return;
            }

            if (Object.HasStateAuthority)
            {
                ExecuteNetworkedSpawn(resolved);
            }
            else
            {
                RPC_RequestSpawn();
            }
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestSpawn()
        {
            if (NetworkActiveCount >= maxInstances)
                return;

            GameObject resolved = ResolvePrefab();
            if (resolved == null) return;

            ExecuteNetworkedSpawn(resolved);
        }

        private void ExecuteNetworkedSpawn(GameObject prefab)
        {
            var networkObject = prefab.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                Debug.LogWarning($"U3DObjectSpawner on '{name}': Prefab has no NetworkObject component. Only the local player will see this object. Add NetworkObject to your prefab for all players to see it.");
                SpawnLocal(prefab);
                return;
            }

            var instance = Runner.Spawn(prefab, transform.position, transform.rotation);
            if (instance != null)
            {
                NetworkActiveCount++;

                if (respawnWhenDestroyed)
                {
                    var tracker = instance.gameObject.AddComponent<U3DSpawnTracker>();
                    tracker.Initialize(this);
                }

                if (labelUI != null && NetworkActiveCount >= maxInstances && !respawnWhenDestroyed)
                    labelUI.gameObject.SetActive(false);

                onSpawned?.Invoke(instance.gameObject);
            }
            else
            {
                Debug.LogWarning($"U3DObjectSpawner on '{name}': Runner.Spawn returned null.");
                onSpawnFailed?.Invoke();
            }
        }

        private void SpawnLocal(GameObject prefab = null)
        {
            if (prefab == null)
                prefab = ResolvePrefab();

            if (prefab == null)
            {
                onSpawnFailed?.Invoke();
                return;
            }

            if (_localActiveCount >= maxInstances)
            {
                onSpawnFailed?.Invoke();
                return;
            }

            var instance = Instantiate(prefab, transform.position, transform.rotation);
            _localActiveCount++;

            if (respawnWhenDestroyed)
            {
                var tracker = instance.AddComponent<U3DSpawnTracker>();
                tracker.Initialize(this);
            }

            onSpawned?.Invoke(instance);
        }

        /// <summary>
        /// Called by U3DSpawnTracker when a tracked instance is destroyed.
        /// </summary>
        public void OnTrackedInstanceDestroyed()
        {
            if (Runner != null && Object != null && Object.HasStateAuthority)
            {
                NetworkActiveCount = Mathf.Max(0, NetworkActiveCount - 1);
                if (labelUI != null) labelUI.gameObject.SetActive(true);
                if (respawnWhenDestroyed && NetworkActiveCount < maxInstances)
                {
                    GameObject resolved = ResolvePrefab();
                    if (resolved != null)
                        ExecuteNetworkedSpawn(resolved);
                }
            }
            else
            {
                _localActiveCount = Mathf.Max(0, _localActiveCount - 1);
                if (labelUI != null) labelUI.gameObject.SetActive(true);
                if (respawnWhenDestroyed && _localActiveCount < maxInstances)
                    SpawnLocal();
            }
        }

        /// <summary>
        /// Resolves which prefab to use. If prefabList has entries, picks one
        /// using weighted random selection. Otherwise falls back to prefabToSpawn.
        /// </summary>
        private GameObject ResolvePrefab()
        {
            if (prefabList != null && prefabList.Length > 0)
            {
                float totalWeight = 0f;
                for (int i = 0; i < prefabList.Length; i++)
                {
                    if (prefabList[i].prefab != null && prefabList[i].weight > 0f)
                        totalWeight += prefabList[i].weight;
                }

                if (totalWeight > 0f)
                {
                    float roll = Random.Range(0f, totalWeight);
                    float cumulative = 0f;
                    for (int i = 0; i < prefabList.Length; i++)
                    {
                        if (prefabList[i].prefab == null || prefabList[i].weight <= 0f)
                            continue;

                        cumulative += prefabList[i].weight;
                        if (roll < cumulative)
                            return prefabList[i].prefab;
                    }
                }

                Debug.LogWarning($"U3DObjectSpawner on '{name}': Prefab List has no valid weighted entries. Falling back to Prefab To Spawn.");
            }

            return prefabToSpawn;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0f, 0.8f, 1f, 0.6f);
            DrawDiamond(transform.position, 0.4f);

            Gizmos.color = new Color(0f, 0.8f, 1f, 0.9f);
            Vector3 arrowStart = transform.position + Vector3.up * 0.1f;
            Gizmos.DrawRay(arrowStart, transform.forward * 1.5f);

            Vector3 tip = arrowStart + transform.forward * 1.5f;
            Vector3 arrowLeft = Quaternion.Euler(0, -25, 0) * transform.forward.normalized * 0.4f;
            Vector3 arrowRight = Quaternion.Euler(0, 25, 0) * transform.forward.normalized * 0.4f;
            Gizmos.DrawLine(tip, tip - arrowLeft);
            Gizmos.DrawLine(tip, tip - arrowRight);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            DrawDiamond(transform.position, 0.55f);

            Gizmos.color = new Color(0f, 1f, 1f, 0.4f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 1.2f);
        }

        private void DrawDiamond(Vector3 center, float size)
        {
            Vector3 top = center + Vector3.up * size;
            Vector3 bottom = center - Vector3.up * size;
            Vector3 front = center + Vector3.forward * size;
            Vector3 back = center - Vector3.forward * size;
            Vector3 right = center + Vector3.right * size;
            Vector3 left = center - Vector3.right * size;

            Gizmos.DrawLine(top, front); Gizmos.DrawLine(top, back);
            Gizmos.DrawLine(top, right); Gizmos.DrawLine(top, left);
            Gizmos.DrawLine(bottom, front); Gizmos.DrawLine(bottom, back);
            Gizmos.DrawLine(bottom, right); Gizmos.DrawLine(bottom, left);
            Gizmos.DrawLine(front, right); Gizmos.DrawLine(right, back);
            Gizmos.DrawLine(back, left); Gizmos.DrawLine(left, front);
        }
    }

    /// <summary>
    /// Internal helper attached to spawned instances to notify the spawner on destruction.
    /// Not intended for direct use by creators.
    /// </summary>
    public class U3DSpawnTracker : MonoBehaviour
    {
        private U3DObjectSpawner _spawner;

        public void Initialize(U3DObjectSpawner spawner)
        {
            _spawner = spawner;
        }

        void OnDestroy()
        {
            if (_spawner != null)
                _spawner.OnTrackedInstanceDestroyed();
        }
    }
}