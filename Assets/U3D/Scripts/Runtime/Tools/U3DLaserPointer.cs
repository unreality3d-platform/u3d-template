using UnityEngine;

namespace U3D
{

    [DisallowMultipleComponent]
    public class U3DLaserPointer : MonoBehaviour
    {
        [Header("Rig References")]
        [Tooltip("Beam emits from here and fires along its forward (+Z). Falls back to this transform if empty.")]
        [SerializeField] private Transform tip;
        [Tooltip("Beam mesh child. Length runs along local +Z, pivot at the emitting (near) end.")]
        [SerializeField] private Transform beam;
        [Tooltip("Dot mesh child placed where the beam lands. A small sphere reads from every viewpoint.")]
        [SerializeField] private Transform dot;

        [Header("Targeting")]
        [SerializeField] private float maxRange = 50f;
        [Tooltip("Coarse layer filter. Leave at Everything; the rig and tag exclusions below do the real work.")]
        [SerializeField] private LayerMask hitMask = ~0;
        [Tooltip("Beam also passes through anything with this tag and that tag's children (e.g. avatars). Clear to let the beam land on tagged objects.")]
        [SerializeField] private string ignoreTag = "Player";

        [Header("Beam Look")]
        [SerializeField] private float beamRadius = 0.01f;
        [Tooltip("Length of the beam mesh at scale 1 along +Z. A 1-unit custom mesh = 1.")]
        [SerializeField] private float beamNativeLength = 1f;
        [SerializeField] private float growDuration = 0.15f;

        [Header("Dot Look")]
        [SerializeField] private float dotBaseScale = 0.05f;
        [SerializeField] private float pulseScale = 3f;
        [SerializeField] private float pulseDuration = 0.4f;

        [Header("Startup")]
        [SerializeField] private bool startActive = false;

        private static readonly RaycastHit[] _hitBuffer = new RaycastHit[32];

        private bool _active;
        private float _growT;
        private float _pulseT;
        private bool _pulsing;

        public bool IsActive => _active;

        private void Awake()
        {
            if (tip == null) tip = transform;
            SetVisualsVisible(false);
            if (startActive) Activate();
        }

        public void Activate() => _active = true;

        public void Deactivate() => _active = false;

        public void Toggle()
        {
            if (_active) Deactivate();
            else Activate();
        }

        public void Pulse()
        {
            _pulseT = 0f;
            _pulsing = true;
        }

        private void Update()
        {
            float target = _active ? 1f : 0f;
            _growT = growDuration <= 0f
                ? target
                : Mathf.MoveTowards(_growT, target, Time.deltaTime / growDuration);

            float pulseMul = 1f;
            if (_pulsing)
            {
                _pulseT += Time.deltaTime;
                if (pulseDuration <= 0f || _pulseT >= pulseDuration)
                    _pulsing = false;
                else
                    pulseMul = Mathf.Lerp(1f, pulseScale, Mathf.Sin((_pulseT / pulseDuration) * Mathf.PI));
            }

            float distance = maxRange;
            bool landed = false;

            if (TryGetBeamTarget(out RaycastHit hit))
            {
                distance = hit.distance;
                landed = true;
            }

            // How far the beam currently reaches. At rest this is 0 (fully retracted).
            float visibleLength = distance * _growT;

            // Beam shows only while active/growing, and retracts to nothing at rest.
            bool beamVisible = _growT > 0f;
            if (beam != null)
            {
                if (beam.gameObject.activeSelf != beamVisible) beam.gameObject.SetActive(beamVisible);
                if (beamVisible)
                {
                    beam.SetPositionAndRotation(tip.position, Quaternion.LookRotation(tip.forward));
                    beam.localScale = new Vector3(beamRadius, beamRadius, visibleLength / Mathf.Max(beamNativeLength, 0.0001f));
                }
            }

            // Dot rides the beam's leading end: parked at the tip when at rest, carried
            // out to the surface as the beam grows. Aimed at nothing, it stays at the tip
            // and only shows at rest, so it never floats in empty space.
            if (dot != null)
            {
                bool showDot = landed || _growT <= 0f;
                if (showDot)
                {
                    if (!dot.gameObject.activeSelf) dot.gameObject.SetActive(true);
                    dot.position = tip.position + tip.forward * (landed ? visibleLength : 0f);
                    dot.localScale = Vector3.one * (dotBaseScale * pulseMul);
                }
                else if (dot.gameObject.activeSelf)
                {
                    dot.gameObject.SetActive(false);
                }
            }
        }

        private bool TryGetBeamTarget(out RaycastHit best)
        {
            best = default;

            int count = Physics.RaycastNonAlloc(tip.position, tip.forward, _hitBuffer, maxRange, hitMask, QueryTriggerInteraction.Ignore);
            if (count <= 0) return false;

            Transform self = transform;
            float nearest = float.MaxValue;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                Collider c = _hitBuffer[i].collider;
                if (c == null) continue;

                Transform ht = c.transform;
                if (ht == self || ht.IsChildOf(self)) continue;
                if (HasIgnoredTag(ht)) continue;

                if (_hitBuffer[i].distance < nearest)
                {
                    nearest = _hitBuffer[i].distance;
                    best = _hitBuffer[i];
                    found = true;
                }
            }

            return found;
        }

        private bool HasIgnoredTag(Transform t)
        {
            if (string.IsNullOrEmpty(ignoreTag)) return false;

            while (t != null)
            {
                if (t.CompareTag(ignoreTag)) return true;
                t = t.parent;
            }

            return false;
        }

        private void SetVisualsVisible(bool visible)
        {
            if (beam != null && beam.gameObject.activeSelf != visible) beam.gameObject.SetActive(visible);
            if (!visible && dot != null && dot.gameObject.activeSelf) dot.gameObject.SetActive(false);
        }
    }

}