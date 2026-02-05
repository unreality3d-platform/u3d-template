using UnityEngine;

namespace U3D
{
    /// <summary>
    /// Marks a surface as climbable. Add via Creator Dashboard "Make Climbable" button.
    /// The player's U3DClimbingController detects objects on the Climbable layer
    /// and allows vertical traversal using standard movement controls.
    /// 
    /// W = climb up, S = climb down, A/D = lateral movement, Space = detach.
    /// Works in both first-person and third-person camera modes.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class U3DClimbable : MonoBehaviour
    {
        [Header("Climbable Surface Settings")]
        [Tooltip("Movement speed multiplier for this specific surface (1.0 = default climb speed)")]
        [SerializeField] private float speedMultiplier = 1.0f;

        public float SpeedMultiplier => speedMultiplier;

        /// <summary>
        /// The layer index used for climbable detection.
        /// Must match U3DClimbingController.climbableLayerMask.
        /// </summary>
        public const int CLIMBABLE_LAYER = 6;
        public const string CLIMBABLE_LAYER_NAME = "Climbable";

        void OnValidate()
        {
            if (gameObject.layer != CLIMBABLE_LAYER)
            {
                Debug.LogWarning($"U3DClimbable: '{name}' is not on the Climbable layer ({CLIMBABLE_LAYER}). " +
                    "Use the Creator Dashboard 'Make Climbable' button to set this up correctly.");
            }
        }
    }
}
