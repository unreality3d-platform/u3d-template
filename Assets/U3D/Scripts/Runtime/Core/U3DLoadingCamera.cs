using System.Collections;
using UnityEngine;

namespace U3D
{
    /// <summary>
    /// Provides a visible view of the scene during the window between scene load
    /// and local player spawn. Self-destroys its GameObject once the local player's
    /// camera becomes active, so no perf cost persists after handoff.
    ///
    /// Intended to live as a child of the primary player spawn point so it frames
    /// the same viewpoint the player will get on spawn.
    ///
    /// Also enforces Depth = -10 so the player camera (depth -1) always renders
    /// on top, preventing a stuck-view bug if a creator changes the depth value.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class U3DLoadingCamera : MonoBehaviour
    {
        [Tooltip("Seconds to wait after detecting the local player camera before destroying this GameObject. A small delay avoids any visual seam during the swap.")]
        [Min(0f)]
        [SerializeField] private float handoffDelay = 0.1f;

        [Tooltip("Maximum seconds to wait for the local player camera before giving up and leaving this camera in place as a fallback.")]
        [Min(1f)]
        [SerializeField] private float maxWaitSeconds = 30f;

        private const float EnforcedDepth = -10f;
        private const float PollInterval = 0.25f;

        private Camera _camera;

        void Awake()
        {
            _camera = GetComponent<Camera>();
            if (_camera != null)
                _camera.depth = EnforcedDepth;
        }

        void OnValidate()
        {
            var cam = GetComponent<Camera>();
            if (cam != null)
                cam.depth = EnforcedDepth;
        }

        void Start()
        {
            StartCoroutine(WaitForLocalPlayerCamera());
        }

        private IEnumerator WaitForLocalPlayerCamera()
        {
            float elapsed = 0f;

            while (elapsed < maxWaitSeconds)
            {
                Camera localCam = FindLocalPlayerCamera();
                if (localCam != null && localCam != _camera && localCam.enabled)
                {
                    if (handoffDelay > 0f)
                        yield return new WaitForSeconds(handoffDelay);

                    Destroy(gameObject);
                    yield break;
                }

                yield return new WaitForSeconds(PollInterval);
                elapsed += PollInterval;
            }

            // Timed out. Leave the loading camera in place as a fallback rather than
            // destroying it and leaving the player with no view at all.
            Debug.LogWarning("U3DLoadingCamera: Local player camera was not detected within " +
                             maxWaitSeconds + " seconds. LoadingCamera will remain active as a fallback view.");
        }

        private Camera FindLocalPlayerCamera()
        {
            // Preferred: use the player controller's static helper so we get the
            // local player deterministically, then grab its camera.
            var localPlayer = U3DPlayerController.FindLocalPlayer();
            if (localPlayer != null)
            {
                Camera cam = localPlayer.GetComponentInChildren<Camera>();
                if (cam != null && cam.enabled)
                    return cam;
            }

            // Fallback: any Camera.main that isn't us. Kicks in if the player controller
            // lookup hasn't finished setup but the camera is already tagged MainCamera.
            Camera mainCam = Camera.main;
            if (mainCam != null && mainCam != _camera)
                return mainCam;

            return null;
        }
    }
}