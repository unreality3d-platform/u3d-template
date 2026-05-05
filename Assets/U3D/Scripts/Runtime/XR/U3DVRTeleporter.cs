using UnityEngine;

namespace U3D.XR
{
    /// <summary>
    /// Handles VR-specific teleport targeting and execution for U3DPlayerController.
    /// Uses a parabolic arc from the player camera forward, aimed by head direction.
    /// Activated by the Move axis hold-override gesture: Move Y above aim threshold
    /// suppresses locomotion and shows the arc; releasing below the threshold fires
    /// the teleport if a valid target was found.
    /// </summary>
    public class U3DVRTeleporter : MonoBehaviour
    {
        [Header("Gesture Thresholds")]
        [Tooltip("Move Y must exceed this value to enter aim mode and suppress locomotion.")]
        [SerializeField] private float aimThreshold = 0.8f;

        [Header("Arc Physics")]
        [Tooltip("Initial velocity magnitude of the simulated arc projectile.")]
        [SerializeField] private float arcVelocity = 8f;
        [Tooltip("Gravity applied to the arc simulation. Does not affect player gravity.")]
        [SerializeField] private float arcGravity = 18f;
        [Tooltip("Number of line segments used to draw the arc. Higher = smoother but more costly.")]
        [SerializeField] private int arcSegments = 30;
        [Tooltip("Maximum world-space distance a teleport destination can be from the player.")]
        [SerializeField] private float maxArcDistance = 20f;
        [Tooltip("Layer mask for valid teleport surfaces. Defaults to Default layer.")]
        [SerializeField] private LayerMask teleportLayerMask = 1;

        [Header("Visuals")]
        [Tooltip("Color of the arc line and reticle when a valid target is found.")]
        [SerializeField] private Color validColor = new Color(0.3f, 0.8f, 1f, 1f);
        [Tooltip("Color of the arc line and reticle when no valid target is found.")]
        [SerializeField] private Color invalidColor = new Color(1f, 0.3f, 0.3f, 1f);
        [Tooltip("Width of the arc line renderer.")]
        [SerializeField] private float arcLineWidth = 0.02f;
        [Tooltip("Radius of the ground reticle circle.")]
        [SerializeField] private float reticleRadius = 0.4f;
        [Tooltip("Number of segments used to draw the reticle circle.")]
        [SerializeField] private int reticleSegments = 32;

        // State
        private bool _isAiming;
        private bool _wasAimingLastFrame;
        private bool _hasValidTarget;
        private Vector3 _targetPosition;

        // Visual components — created at runtime, destroyed with this component
        private LineRenderer _arcLine;
        private LineRenderer _reticleLine;
        private GameObject _visualRoot;

        // Owner reference — set by U3DPlayerController on Start/Spawned
        private U3DPlayerController _controller;
        private Camera _playerCamera;

        public bool IsAiming => _isAiming;

        /// <summary>Called by U3DPlayerController after Spawned to wire up references.</summary>
        public void Initialize(U3DPlayerController controller, Camera camera)
        {
            _controller = controller;
            _playerCamera = camera;
            BuildVisuals();
            SetVisualsActive(false);
        }

        void OnDestroy()
        {
            if (_visualRoot != null)
                Destroy(_visualRoot);
        }

        // ── Public API called from U3DPlayerController ─────────────────────────

        /// <summary>
        /// Called every Update (Render) frame while in VR mode.
        /// Reads the current Move Y value, manages aim state, and updates visuals.
        /// Returns true if locomotion should be suppressed this frame.
        /// </summary>
        public bool UpdateAiming(float moveY)
        {
            _wasAimingLastFrame = _isAiming;
            _isAiming = moveY >= aimThreshold;

            if (_isAiming)
            {
                SimulateAndDraw();
            }
            else
            {
                if (_wasAimingLastFrame && _hasValidTarget)
                    ExecuteTeleport();

                _hasValidTarget = false;
                SetVisualsActive(false);
            }

            return _isAiming;
        }

        /// <summary>Cleans up aim state without firing — call on VR exit or dismount.</summary>
        public void CancelAim()
        {
            _isAiming = false;
            _wasAimingLastFrame = false;
            _hasValidTarget = false;
            SetVisualsActive(false);
        }

        // ── Internal ────────────────────────────────────────────────────────────

        private void SimulateAndDraw()
        {
            if (_playerCamera == null) return;

            Vector3 origin = _playerCamera.transform.position;
            // Head-directed: flatten to horizon, project forward with slight downward pitch
            Vector3 forward = _playerCamera.transform.forward;
            // Use a mild downward arc regardless of actual head pitch so the arc
            // always reaches the ground even when the player looks straight ahead.
            Vector3 flatForward = new Vector3(forward.x, Mathf.Min(forward.y, -0.1f), forward.z).normalized;
            Vector3 velocity = flatForward * arcVelocity;

            Vector3[] points = new Vector3[arcSegments];
            _hasValidTarget = false;
            _targetPosition = Vector3.zero;

            float stepTime = 0.1f;

            for (int i = 0; i < arcSegments; i++)
            {
                float t = i * stepTime;
                points[i] = origin + velocity * t + Vector3.down * (arcGravity * t * t * 0.5f);

                if (i > 0)
                {
                    Vector3 segStart = points[i - 1];
                    Vector3 segEnd = points[i];
                    Vector3 dir = segEnd - segStart;
                    float segLen = dir.magnitude;

                    if (Vector3.Distance(origin, segEnd) > maxArcDistance)
                    {
                        TruncateArc(points, i);
                        break;
                    }

                    if (Physics.Raycast(segStart, dir.normalized, out RaycastHit hit, segLen, teleportLayerMask))
                    {
                        // Snap end point to the hit, truncate remainder
                        points[i] = hit.point;
                        TruncateArc(points, i + 1);
                        _hasValidTarget = true;
                        _targetPosition = hit.point;
                        break;
                    }
                }
            }

            Color lineColor = _hasValidTarget ? validColor : invalidColor;
            DrawArc(points, lineColor);
            DrawReticle(_hasValidTarget ? _targetPosition : points[arcSegments - 1], _hasValidTarget, lineColor);
            SetVisualsActive(true);
        }

        private void TruncateArc(Vector3[] points, int usedCount)
        {
            // Fill remaining segments with the last valid point so the
            // LineRenderer has no zero-length degenerate segments.
            Vector3 last = points[usedCount - 1];
            for (int j = usedCount; j < arcSegments; j++)
                points[j] = last;
        }

        private void ExecuteTeleport()
        {
            if (_controller == null) return;

            CharacterController cc = _controller.CharacterController;
            float playerHeight = cc != null ? cc.height : 2f;
            Vector3 teleportPos = _targetPosition;
            teleportPos.y += playerHeight * 0.5f + 0.1f;

            cc.enabled = false;
            _controller.transform.position = teleportPos;
            cc.enabled = true;

            _controller.NetworkPosition = teleportPos;
            _controller.NetworkRotation = _controller.transform.rotation;
        }

        // ── Visuals ─────────────────────────────────────────────────────────────

        private void BuildVisuals()
        {
            _visualRoot = new GameObject("VRTeleportVisuals");
            _visualRoot.transform.SetParent(null); // world space — not parented to player

            _arcLine = CreateLineRenderer("ArcLine", arcLineWidth, arcSegments);
            _reticleLine = CreateLineRenderer("ReticleLine", arcLineWidth * 1.5f, reticleSegments + 1);

            _arcLine.transform.SetParent(_visualRoot.transform, false);
            _reticleLine.transform.SetParent(_visualRoot.transform, false);
        }

        private LineRenderer CreateLineRenderer(string goName, float width, int positionCount)
        {
            var go = new GameObject(goName);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.startWidth = width;
            lr.endWidth = width;
            lr.positionCount = positionCount;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 4;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            // Use Sprites/Default so the color tint works without a custom material.
            // URP: replace with "Universal Render Pipeline/Unlit" if Sprites/Default
            // produces magenta in a URP project.
            var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            lr.material = mat;
            lr.startColor = validColor;
            lr.endColor = validColor;

            return lr;
        }

        private void DrawArc(Vector3[] points, Color color)
        {
            _arcLine.positionCount = points.Length;
            _arcLine.SetPositions(points);
            _arcLine.startColor = color;
            _arcLine.endColor = color;
        }

        private void DrawReticle(Vector3 center, bool valid, Color color)
        {
            if (!valid)
            {
                _reticleLine.enabled = false;
                return;
            }

            _reticleLine.enabled = true;
            _reticleLine.startColor = color;
            _reticleLine.endColor = color;

            // Slightly above the surface to avoid z-fighting
            Vector3 up = Vector3.up * 0.01f;
            for (int i = 0; i <= reticleSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / reticleSegments;
                Vector3 point = center + up + new Vector3(
                    Mathf.Cos(angle) * reticleRadius,
                    0f,
                    Mathf.Sin(angle) * reticleRadius);
                _reticleLine.SetPosition(i, point);
            }
        }

        private void SetVisualsActive(bool active)
        {
            if (_arcLine != null) _arcLine.enabled = active;
            if (_reticleLine != null) _reticleLine.enabled = active && _hasValidTarget;
        }
    }
}