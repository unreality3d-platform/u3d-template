using UnityEngine;
using UnityEngine.Events;

#if FUSION_WEAVER
using Fusion;
#endif

namespace U3D
{
    /// <summary>
    /// Spawns a prefab at this object's position and rotation.
    /// Supports local-only (Instantiate) and networked (Fusion runner.Spawn) modes.
    /// Place this component on any GameObject to define where and what spawns.
    /// </summary>
    public class U3DObjectSpawner : MonoBehaviour
    {
        [Header("What to Spawn")]
        [Tooltip("The prefab that will be spawned at this location.")]
        public GameObject prefabToSpawn;

        [Header("Spawn Behavior")]
        [Tooltip("Spawn the object automatically when the scene starts.")]
        public bool spawnOnStart = true;

        [Tooltip("Automatically respawn the object if it is destroyed.")]
        public bool respawnWhenDestroyed = false;

        [Tooltip("Maximum number of spawned objects that can exist at once. New spawns are blocked when this limit is reached.")]
        public int maxInstances = 1;

        [Header("Multiplayer")]
        [Tooltip("Spawn via Fusion NetworkRunner so all players see the object. " +
                 "Requires the prefab to have a NetworkObject component. " +
                 "When disabled, the object is only visible to the local player.")]
        public bool networked = false;

        [Header("Events")]
        public UnityEvent<GameObject> onSpawned;
        public UnityEvent onSpawnFailed;

        private int _activeCount = 0;
        private GameObject _lastSpawned;

#if FUSION_WEAVER
        private NetworkRunner _runner;
#endif

        void Start()
        {
            if (spawnOnStart)
                Spawn();
        }

        public void Spawn()
        {
            if (prefabToSpawn == null)
            {
                Debug.LogWarning($"U3DObjectSpawner on '{name}': No prefab assigned.");
                onSpawnFailed?.Invoke();
                return;
            }

            if (_activeCount >= maxInstances)
            {
                onSpawnFailed?.Invoke();
                return;
            }

            if (networked)
                SpawnNetworked();
            else
                SpawnLocal();
        }

        private void SpawnLocal()
        {
            var instance = Instantiate(prefabToSpawn, transform.position, transform.rotation);
            _activeCount++;
            _lastSpawned = instance;

            if (respawnWhenDestroyed)
            {
                var tracker = instance.AddComponent<U3DSpawnTracker>();
                tracker.Initialize(this);
            }

            onSpawned?.Invoke(instance);
        }

        private void SpawnNetworked()
        {
#if FUSION_WEAVER
            if (_runner == null)
                _runner = FindAnyObjectByType<NetworkRunner>();

            if (_runner == null || !_runner.IsRunning)
            {
                Debug.LogWarning($"U3DObjectSpawner on '{name}': Networked spawn requested but no active NetworkRunner found. Falling back to local spawn.");
                SpawnLocal();
                return;
            }

            var networkObject = prefabToSpawn.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                Debug.LogWarning($"U3DObjectSpawner on '{name}': Networked spawn requires the prefab to have a NetworkObject component. Falling back to local spawn.");
                SpawnLocal();
                return;
            }

            var instance = _runner.Spawn(prefabToSpawn, transform.position, transform.rotation);
            if (instance != null)
            {
                _activeCount++;
                _lastSpawned = instance.gameObject;

                if (respawnWhenDestroyed)
                {
                    var tracker = instance.gameObject.AddComponent<U3DSpawnTracker>();
                    tracker.Initialize(this);
                }

                onSpawned?.Invoke(instance.gameObject);
            }
            else
            {
                Debug.LogWarning($"U3DObjectSpawner on '{name}': Fusion Spawn returned null.");
                onSpawnFailed?.Invoke();
            }
#else
            Debug.LogWarning($"U3DObjectSpawner on '{name}': Networked spawn requested but Fusion is not available. Falling back to local spawn.");
            SpawnLocal();
#endif
        }

        /// <summary>
        /// Called by U3DSpawnTracker when a tracked instance is destroyed.
        /// </summary>
        public void OnTrackedInstanceDestroyed()
        {
            _activeCount = Mathf.Max(0, _activeCount - 1);

            if (respawnWhenDestroyed && _activeCount < maxInstances)
                Spawn();
        }

        void OnDrawGizmos()
        {
            // Cyan diamond - distinct from player spawn point green sphere
            Gizmos.color = new Color(0f, 0.8f, 1f, 0.6f);
            DrawDiamond(transform.position, 0.4f);

            // Forward direction arrow
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

            // Label hint line upward
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
    /// Internal helper. Attached to spawned instances to notify the spawner on destruction.
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