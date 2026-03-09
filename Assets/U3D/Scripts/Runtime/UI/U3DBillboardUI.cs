using UnityEngine;

namespace U3D
{
    /// <summary>
    /// Worldspace UI component with proximity fade and optional camera-facing billboard behavior.
    /// Attach to a World Space Canvas or any parent transform containing UI elements.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class U3DBillboardUI : MonoBehaviour
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
        private float targetAlpha;

        void Awake()
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        void Start()
        {
            FindTargetCamera();
        }

        void LateUpdate()
        {
            if (targetCamera == null)
            {
                FindTargetCamera();
                if (targetCamera == null) return;
            }

            if (faceCamera)
                UpdateBillboardRotation();

            UpdateProximityFade();
        }

        void FindTargetCamera()
        {
            targetCamera = Camera.main;
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
            float distance = Vector3.Distance(targetCamera.transform.position, transform.position);

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

            canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, targetAlpha, fadeSpeed * Time.deltaTime);
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