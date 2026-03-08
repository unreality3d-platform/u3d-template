using UnityEngine;
using UnityEngine.Events;
using Fusion;

namespace U3D
{
    /// <summary>
    /// Spawns a prefab at this object's position and rotation.
    /// All spawns are networked via Fusion so every player sees the result.
    /// Place this component on any GameObject to define where and what spawns.
    /// The prefab must have a NetworkObject component for all players to see it.
    /// Without NetworkObject on the prefab, only the local player will see it.
    /// </summary>
    public class U3DObjectSpawner : NetworkBehaviour
    {
        [Header("What to Spawn")]
        [Tooltip("The prefab to spawn at this location. Add a NetworkObject component to your prefab so all players see it. Without NetworkObject, only the local player will see the spawned object.")]
        public GameObject prefabToSpawn;

        [Header("Spawn Behavior")]
        [Tooltip("Spawn automatically when the scene starts.")]
        public bool spawnOnStart = true;

        [Tooltip("Respawn automatically when the spawned object is destroyed.")]
        public bool respawnWhenDestroyed = false;

        [Tooltip("Maximum number of spawned objects that can exist at once. New spawns are blocked when this limit is reached.")]
        public int maxInstances = 1;

        [Header("Optional Label")]
        [Tooltip("Assign a U3DBillboardUI in your scene to show a label near this spawner. Edit the text on that object directly.")]
        public U3DBillboardUI labelUI;

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
            // Non-networked fallback: if there is no runner (e.g. editor Play mode without Fusion),
            // fall back to local spawn so the component still works during solo testing.
            if (Runner == null && spawnOnStart)
                SpawnLocal();
        }

        /// <summary>
        /// Call this from any UnityEvent, trigger, or script to request a spawn.
        /// Non-host clients automatically forward the request to the host via RPC.
        /// </summary>
        public void Spawn()
        {
            if (prefabToSpawn == null)
            {
                Debug.LogWarning($"U3DObjectSpawner on '{name}': No prefab assigned.");
                onSpawnFailed?.Invoke();
                return;
            }

            if (Runner == null)
            {
                SpawnLocal();
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
                ExecuteNetworkedSpawn();
            }
            else
            {
                // Non-host client: ask the host to spawn
                RPC_RequestSpawn();
            }
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestSpawn()
        {
            if (NetworkActiveCount >= maxInstances)
            {
                return;
            }

            ExecuteNetworkedSpawn();
        }

        private void ExecuteNetworkedSpawn()
        {
            var networkObject = prefabToSpawn.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                // Prefab has no NetworkObject - warn and fall back to local spawn
                Debug.LogWarning($"U3DObjectSpawner on '{name}': Prefab has no NetworkObject component. Only the local player will see this object. Add NetworkObject to your prefab for all players to see it.");
                SpawnLocal();
                return;
            }

            var instance = Runner.Spawn(prefabToSpawn, transform.position, transform.rotation);
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

        private void SpawnLocal()
        {
            if (_localActiveCount >= maxInstances)
            {
                onSpawnFailed?.Invoke();
                return;
            }

            var instance = Instantiate(prefabToSpawn, transform.position, transform.rotation);
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
                    ExecuteNetworkedSpawn();
            }
            else
            {
                _localActiveCount = Mathf.Max(0, _localActiveCount - 1);
                if (labelUI != null) labelUI.gameObject.SetActive(true);
                if (respawnWhenDestroyed && _localActiveCount < maxInstances)
                    SpawnLocal();
            }
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