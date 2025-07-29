using UnityEngine;
using TMPro;

namespace U3D
{
    /// <summary>
    /// Controller for Scene Gate monetization tool.
    /// Manages scene access control after PayPal payment completion.
    /// </summary>
    public class SceneGateController : MonoBehaviour
    {
        [Header("Gate Configuration")]
        [SerializeField] private GameObject gateObject;
        [SerializeField] private bool isOpen = false;

        [Header("Access Control")]
        [SerializeField] private string requiredSceneName = "";
        [SerializeField] private bool blockMovement = true;

        private void Start()
        {
            // Ensure gate starts closed
            if (gateObject != null)
            {
                gateObject.SetActive(!isOpen);
            }
        }

        /// <summary>
        /// Opens the gate after successful payment
        /// </summary>
        public void OpenGate()
        {
            isOpen = true;

            // Hide/disable the gate object
            if (gateObject != null)
            {
                gateObject.SetActive(false);
            }

            Debug.Log("Scene gate opened - access granted!");

            // Update status text if available
            var statusText = GetComponentInChildren<TextMeshProUGUI>();
            if (statusText != null && statusText.name == "StatusText")
            {
                statusText.text = "Access granted!";
            }

            // Update button if available
            var unlockButton = GetComponentInChildren<UnityEngine.UI.Button>();
            if (unlockButton != null)
            {
                unlockButton.interactable = false;
                var buttonText = unlockButton.GetComponentInChildren<TextMeshProUGUI>();
                if (buttonText != null)
                {
                    buttonText.text = "Access Granted";
                }
            }
        }

        /// <summary>
        /// Closes the gate (for testing or reset purposes)
        /// </summary>
        public void CloseGate()
        {
            isOpen = false;

            if (gateObject != null)
            {
                gateObject.SetActive(true);
            }

            var statusText = GetComponentInChildren<TextMeshProUGUI>();
            if (statusText != null && statusText.name == "StatusText")
            {
                statusText.text = "Ready to accept payments";
            }

            var unlockButton = GetComponentInChildren<UnityEngine.UI.Button>();
            if (unlockButton != null)
            {
                unlockButton.interactable = true;
                var buttonText = unlockButton.GetComponentInChildren<TextMeshProUGUI>();
                if (buttonText != null)
                {
                    buttonText.text = "Unlock Scene";
                }
            }
        }

        /// <summary>
        /// Check if the gate is currently open
        /// </summary>
        public bool IsOpen() => isOpen;

        /// <summary>
        /// Set the gate object to control
        /// </summary>
        public void SetGateObject(GameObject gate)
        {
            gateObject = gate;
            if (gateObject != null)
            {
                gateObject.SetActive(!isOpen);
            }
        }

        /// <summary>
        /// Set required scene name for access control
        /// </summary>
        public void SetRequiredScene(string sceneName)
        {
            requiredSceneName = sceneName;
        }

        // Editor validation
        private void OnValidate()
        {
            if (gateObject != null && Application.isPlaying)
            {
                gateObject.SetActive(!isOpen);
            }
        }
    }
}