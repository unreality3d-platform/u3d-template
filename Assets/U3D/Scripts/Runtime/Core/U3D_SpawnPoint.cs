using UnityEngine;

namespace U3D.Networking
{
    /// <summary>
    /// Enhanced spawn point component with rotation control and editor gizmos
    /// Add this component to GameObjects with "SpawnPoint" tag for full control
    /// </summary>
    public class U3D_SpawnPoint : MonoBehaviour
    {
        [Header("Spawn Settings")]
        [Tooltip("Use this spawn point's Y rotation for player facing direction")]
        public bool useRotation = true;

        [Header("Editor Visualization")]
        [Tooltip("Show direction arrow and spawn area in Scene view")]
        public bool showGizmos = true;

        [Tooltip("Color of the spawn point gizmos")]
        public Color gizmoColor = Color.green;

        /// <summary>
        /// Get spawn position (same as transform.position)
        /// </summary>
        public Vector3 GetSpawnPosition()
        {
            return transform.position;
        }

        /// <summary>
        /// Get spawn rotation - returns Y rotation only for player orientation
        /// </summary>
        public Quaternion GetSpawnRotation()
        {
            if (useRotation)
            {
                // Only use Y rotation for player facing direction
                return Quaternion.Euler(0, transform.eulerAngles.y, 0);
            }

            return Quaternion.identity;
        }

        /// <summary>
        /// Get complete spawn data (position + rotation)
        /// </summary>
        public (Vector3 position, Quaternion rotation) GetSpawnData()
        {
            return (GetSpawnPosition(), GetSpawnRotation());
        }

        void OnDrawGizmos()
        {
            if (!showGizmos) return;

            // Draw spawn position circle
            Gizmos.color = gizmoColor;
            Gizmos.DrawWireSphere(transform.position, 0.5f);

            // Draw rotation arrow if enabled
            if (useRotation)
            {
                // Blue forward arrow
                Gizmos.color = Color.blue;
                Vector3 forward = transform.forward * 2f;
                Vector3 arrowStart = transform.position + Vector3.up * 0.1f;

                // Main arrow line
                Gizmos.DrawRay(arrowStart, forward);

                // Arrow head
                Vector3 arrowTip = arrowStart + forward;
                Vector3 arrowLeft = Quaternion.Euler(0, -25, 0) * forward.normalized * 0.5f;
                Vector3 arrowRight = Quaternion.Euler(0, 25, 0) * forward.normalized * 0.5f;

                Gizmos.DrawLine(arrowTip, arrowTip - arrowLeft);
                Gizmos.DrawLine(arrowTip, arrowTip - arrowRight);

                // Draw small Y rotation indicator
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireCube(transform.position + Vector3.up * 0.5f, Vector3.one * 0.2f);
            }
        }

        void OnDrawGizmosSelected()
        {
            // Enhanced gizmos when selected - larger and more visible
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 0.7f);

            if (useRotation)
            {
                // More prominent forward direction when selected
                Gizmos.color = Color.cyan;
                Vector3 forward = transform.forward * 3f;
                Vector3 arrowStart = transform.position + Vector3.up * 0.1f;

                // Thicker arrow visualization
                Gizmos.DrawRay(arrowStart, forward);
                Gizmos.DrawRay(arrowStart + Vector3.right * 0.05f, forward);
                Gizmos.DrawRay(arrowStart - Vector3.right * 0.05f, forward);

                // Show rotation degree
                Vector3 textPos = transform.position + Vector3.up * 1f;
                // Note: Gizmos don't support text, but the yellow cube above indicates rotation is active
            }
        }
    }
}