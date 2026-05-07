using UnityEngine;
using System.Collections;

namespace U3D
{
    /// <summary>
    /// Drop on a trigger-zone GameObject when an object starts the scene already
    /// inside the trigger volume and you want the trigger to fire on the first
    /// "real" entry. Disables the collider for a few frames so OnTriggerEnter
    /// only fires on transition, not on scene-load overlap.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class U3DDelayedTriggerActivation : MonoBehaviour
    {
        [Tooltip("Seconds to wait after scene load before enabling the trigger collider.")]
        [SerializeField] private float activationDelay = 0.5f;

        private void Start()
        {
            GetComponent<Collider>().enabled = false;
            StartCoroutine(EnableAfterDelay());
        }

        private IEnumerator EnableAfterDelay()
        {
            yield return new WaitForSeconds(activationDelay);
            GetComponent<Collider>().enabled = true;
        }
    }
}