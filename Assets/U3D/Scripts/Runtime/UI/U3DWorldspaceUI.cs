using UnityEngine;

namespace U3D
{
    /// <summary>
    /// Worldspace UI component with proximity fade and optional camera-facing billboard behavior.
    /// Attach to a World Space Canvas or any parent transform containing UI elements.
    /// Proximity fade is measured from the local player's body position, not the camera,
    /// so third-person camera distance doesn't affect visibility.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class U3DWorldspaceUI : MonoBehaviour
    {
        [Header("Billboard Settings")]
        [Tooltip("Face the camera. Disable to keep the canvas at a fixed rotation.")]
        public bool faceCamera = true;

        [Tooltip("Lock Y-axis rotation (prevents tilting when camera looks up/down)")]
        public bool lockYAxis = true;

        [Header("Proximity Settings")]
        [Tooltip("Distance at which UI becomes fully hidden")]
        [Min(0.1f)]
        public float hideDistance = 10f;

        [Tooltip("Distance at which UI becomes fully visible (should be less than hideDistance)")]
        [Min(0.1f)]
        public float showDistance = 2f;

        [Header("Animation Settings")]
        [Tooltip("Speed of fade animation")]
        [Min(0.1f)]
        public float fadeSpeed = 5f;

        private CanvasGroup canvasGroup;
        private Camera targetCamera;
        private Transform _localPlayerTransform;
        private float targetAlpha;
        private bool _hasSnappedToInitialAlpha;

        // Throttled search for Camera.main while it doesn't exist yet (bootstrap window).
        private const float CameraSearchInterval = 0.25f;
        private float _nextCameraSearchTime;

        void Awake()
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
                canvasGroup = gameObject.AddComponent<CanvasGroup>();

            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        void Start()
        {
            FindTargetCamera();
        }

        void LateUpdate()
        {
            if (targetCamera == null)
            {
                if (Time.unscaledTime >= _nextCameraSearchTime)
                {
                    FindTargetCamera();
                    _nextCameraSearchTime = Time.unscaledTime + CameraSearchInterval;
                }
                if (targetCamera == null) return;
            }

            if (_localPlayerTransform == null)
                FindLocalPlayer();

            if (faceCamera)
                UpdateBillboardRotation();

            UpdateProximityFade();
        }

        void FindTargetCamera()
        {
            targetCamera = Camera.main;
        }

        void FindLocalPlayer()
        {
            var localPlayer = U3DPlayerController.FindLocalPlayer();
            if (localPlayer != null)
                _localPlayerTransform = localPlayer.transform;
        }

        void UpdateBillboardRotation()
        {
            Vector3 directionToCamera = targetCamera.transform.position - transform.position;

            if (lockYAxis)
                directionToCamera.y = 0f;

            if (directionToCamera.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(-directionToCamera);
        }

        void UpdateProximityFade()
        {
            Transform distanceSource = _localPlayerTransform != null
                ? _localPlayerTransform
                : targetCamera.transform;

            float distance = Vector3.Distance(distanceSource.position, transform.position);

            if (distance >= hideDistance)
            {
                targetAlpha = 0f;
            }
            else if (distance <= showDistance)
            {
                targetAlpha = 1f;
            }
            else
            {
                float t = (distance - showDistance) / (hideDistance - showDistance);
                targetAlpha = 1f - t;
            }

            // Gate on first valid frame: don't start fading until the local player exists,
            // so the initial distance calculation reflects the real viewpoint. Without this
            // gate, labels would fade in during the bootstrap window based on the wrong
            // distance source (Camera.main fallback).
            if (!_hasSnappedToInitialAlpha && _localPlayerTransform != null)
            {
                _hasSnappedToInitialAlpha = true;
            }

            if (_hasSnappedToInitialAlpha)
            {
                canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, targetAlpha, fadeSpeed * Time.deltaTime);
            }

            canvasGroup.interactable = canvasGroup.alpha > 0.1f;
            canvasGroup.blocksRaycasts = canvasGroup.alpha > 0.1f;
        }

        void OnValidate()
        {
            if (showDistance >= hideDistance)
                showDistance = hideDistance - 0.5f;

            if (showDistance < 0.1f)
                showDistance = 0.1f;
        }
    }
}