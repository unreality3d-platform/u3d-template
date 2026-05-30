using UnityEngine;
using UnityEngine.Events;
using Fusion;

namespace U3D
{
    /// <summary>
    /// Add to any networked or non-networked object to give it a destroy mechanism.
    /// Call RequestDestroy() from code, events, or a U3DTrashHandler trigger zone.
    /// In networked mode the object despawns itself under its own state authority —
    /// either immediately (if already authority) or after requesting it.
    /// In non-networked mode, Destroy() is called directly.
    /// </summary>
    public class U3DDestroyable : NetworkBehaviour
    {
        [Header("Events")]
        [Tooltip("Called on all clients just before this object is destroyed. Use this to spawn effects, update score, play sounds, etc.")]
        public UnityEvent OnDestroyed;

        private bool isNetworked = false;
        private bool destroyRequested = false;
        private bool isRequestingAuthority = false;
        private float authorityRequestTime = 0f;
        private const float AUTHORITY_REQUEST_TIMEOUT = 2f;

        private void Awake()
        {
            isNetworked = GetComponent<NetworkObject>() != null;
        }

        private void Update()
        {
            if (isRequestingAuthority && Time.time - authorityRequestTime > AUTHORITY_REQUEST_TIMEOUT)
            {
                Debug.LogWarning($"U3DDestroyable: Authority request timed out on '{name}' — destroy cancelled.");
                isRequestingAuthority = false;
                destroyRequested = false;
            }
        }

        /// <summary>
        /// Request that this object be destroyed. Safe to call from any client.
        /// In networked mode, authority is requested if not already held and
        /// Runner.Despawn() is called once granted. In non-networked mode,
        /// Destroy() is called immediately.
        /// </summary>
        public void RequestDestroy()
        {
            if (destroyRequested) return;
            destroyRequested = true;

            if (!isNetworked)
            {
                OnDestroyed?.Invoke();
                Destroy(gameObject);
                return;
            }

            if (Object == null || !Object.IsValid) return;

            if (Object.HasStateAuthority)
            {
                ExecuteDestroy();
            }
            else
            {
                isRequestingAuthority = true;
                authorityRequestTime = Time.time;
                Object.RequestStateAuthority();
            }
        }

        public void OnStateAuthorityChanged()
        {
            if (!isNetworked) return;
            if (!destroyRequested) return;
            if (!Object.HasStateAuthority) return;

            isRequestingAuthority = false;
            ExecuteDestroy();
        }

        private void ExecuteDestroy()
        {
            if (Runner == null || !Runner.IsRunning) return;

            OnDestroyed?.Invoke();
            Runner.Despawn(Object);
        }
    }
}
