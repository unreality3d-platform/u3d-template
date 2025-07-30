using System;
using System.Runtime.InteropServices;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace U3D
{
    /// <summary>
    /// Core component for PayPal dual transaction processing.
    /// Handles automatic 95%/5% payment splitting between creator and platform.
    /// </summary>
    public class PayPalDualTransaction : MonoBehaviour
    {
        [Header("Transaction Configuration")]
        [SerializeField] private string itemName = "Premium Content";
        [SerializeField] private string itemDescription = "Creator content purchase";
        [SerializeField] private float itemPrice = 5.00f;
        [SerializeField] private bool allowVariableAmount = false;
        [SerializeField] private float minimumAmount = 1.00f;
        [SerializeField] private float maximumAmount = 100.00f;

        [Header("UI References")]
        [SerializeField] private Button paymentButton;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text priceText;
        [SerializeField] private TMP_InputField amountInputField;
        [SerializeField] private GameObject loadingIndicator;

        [Header("Events")]
        public UnityEngine.Events.UnityEvent OnPaymentSuccess = new UnityEngine.Events.UnityEvent();
        public UnityEngine.Events.UnityEvent OnPaymentFailed = new UnityEngine.Events.UnityEvent();
        public UnityEngine.Events.UnityEvent<string> OnStatusChanged = new UnityEngine.Events.UnityEvent<string>();

        [Header("Tip Jar Customization")]
        [SerializeField] private string creatorMessage = "Thank you for supporting my work!";

        // JavaScript bridge imports - UPDATED with GameObject name support
        [DllImport("__Internal")]
        private static extern void UnityStartDualTransactionWithGameObject(string gameObjectName, string itemName, string itemDescription, string price, string transactionId);

        [DllImport("__Internal")]
        private static extern void UnityCheckAuthenticationStatusWithGameObject(string gameObjectName);

        // Keep original methods for backward compatibility
        [DllImport("__Internal")]
        private static extern void UnityStartDualTransaction(string itemName, string itemDescription, string price, string transactionId);

        [DllImport("__Internal")]
        private static extern void UnityCheckAuthenticationStatus();

        private string currentTransactionId;
        private bool isProcessing = false;
        private string creatorPayPalEmail;

        private void Start()
        {
            InitializeComponent();
            ValidateSetup();
        }

        private void InitializeComponent()
        {
            // CRITICAL FIX: Multi-source PayPal email detection for maximum reliability
            LoadCreatorPayPalEmail();

            // Setup UI
            if (paymentButton != null)
            {
                paymentButton.onClick.AddListener(StartPayment);
            }

            if (amountInputField != null)
            {
                amountInputField.gameObject.SetActive(allowVariableAmount);
                amountInputField.onValueChanged.AddListener(OnAmountChanged);
                amountInputField.text = itemPrice.ToString("F2");
            }

            UpdateUI();
        }

        // CRITICAL FIX: New method to reliably load PayPal email from all possible sources
        private void LoadCreatorPayPalEmail()
        {
            string paypalEmail = "";

            // Method 1: Try ScriptableObject first (runtime-accessible)
            var creatorData = Resources.Load<U3DCreatorData>("U3DCreatorData");
            if (creatorData != null && !string.IsNullOrEmpty(creatorData.PayPalEmail))
            {
                paypalEmail = creatorData.PayPalEmail;
                Debug.Log($"✅ PayPal email loaded from ScriptableObject: {paypalEmail}");
            }

#if UNITY_EDITOR
            // Method 2: In editor, also check EditorPrefs as fallback
            if (string.IsNullOrEmpty(paypalEmail))
            {
                paypalEmail = EditorPrefs.GetString("U3D_PayPalEmail", "");
                if (!string.IsNullOrEmpty(paypalEmail))
                {
                    Debug.Log($"✅ PayPal email loaded from EditorPrefs: {paypalEmail}");

                    // CRITICAL FIX: If found in EditorPrefs but not ScriptableObject, sync them
                    SyncPayPalEmailToScriptableObject(paypalEmail);
                }
            }

            // Method 3: Try U3DAuthenticator via reflection (EDITOR ONLY - runtime can't access editor classes)
            if (string.IsNullOrEmpty(paypalEmail))
            {
                try
                {
                    var authenticatorType = System.Type.GetType("U3DAuthenticator");
                    if (authenticatorType != null)
                    {
                        var getPayPalMethod = authenticatorType.GetMethod("GetPayPalEmail", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        if (getPayPalMethod != null)
                        {
                            var authEmail = (string)getPayPalMethod.Invoke(null, null);
                            if (!string.IsNullOrEmpty(authEmail))
                            {
                                paypalEmail = authEmail;
                                Debug.Log($"✅ PayPal email loaded from U3DAuthenticator: {paypalEmail}");

                                // CRITICAL FIX: If found in U3DAuthenticator but not ScriptableObject, sync them
                                SyncPayPalEmailToScriptableObject(paypalEmail);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Could not access U3DAuthenticator via reflection: {ex.Message}");
                }
            }
#endif

            this.creatorPayPalEmail = paypalEmail;

            // DIAGNOSTIC: Log the final result
            if (string.IsNullOrEmpty(this.creatorPayPalEmail))
            {
                Debug.LogWarning("❌ PayPal email not found in any storage location");
                Debug.LogWarning("🔍 Checked: ScriptableObject, EditorPrefs, U3DAuthenticator");

                // Additional diagnostic info
                Debug.LogWarning($"🔍 ScriptableObject exists: {(creatorData != null)}");
                Debug.LogWarning($"🔍 ScriptableObject PayPal: '{creatorData?.PayPalEmail ?? "null"}'");

#if UNITY_EDITOR
                Debug.LogWarning($"🔍 EditorPrefs PayPal: '{EditorPrefs.GetString("U3D_PayPalEmail", "")}'");

                // Try U3DAuthenticator via reflection
                try
                {
                    var authenticatorType = System.Type.GetType("U3DAuthenticator");
                    if (authenticatorType != null)
                    {
                        var getPayPalMethod = authenticatorType.GetMethod("GetPayPalEmail", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        if (getPayPalMethod != null)
                        {
                            var authEmail = (string)getPayPalMethod.Invoke(null, null);
                            Debug.LogWarning($"🔍 U3DAuthenticator PayPal: '{authEmail ?? "null"}'");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"🔍 U3DAuthenticator: Could not access via reflection: {ex.Message}");
                }
#else
                Debug.LogWarning("🔍 Runtime build - EditorPrefs and U3DAuthenticator not accessible");
#endif
            }
            else
            {
                Debug.Log($"✅ Final PayPal email for runtime: {this.creatorPayPalEmail}");
            }
        }

#if UNITY_EDITOR
        // CRITICAL FIX: Method to sync PayPal email to ScriptableObject when found elsewhere
        private void SyncPayPalEmailToScriptableObject(string email)
        {
            try
            {
                var assetPath = "Assets/U3D/Resources/U3DCreatorData.asset";

                // Ensure the Resources folder exists
                var resourcesPath = "Assets/U3D/Resources";
                if (!AssetDatabase.IsValidFolder(resourcesPath))
                {
                    if (!AssetDatabase.IsValidFolder("Assets/U3D"))
                    {
                        AssetDatabase.CreateFolder("Assets", "U3D");
                    }
                    AssetDatabase.CreateFolder("Assets/U3D", "Resources");
                }

                var data = AssetDatabase.LoadAssetAtPath<U3DCreatorData>(assetPath);
                if (data == null)
                {
                    data = ScriptableObject.CreateInstance<U3DCreatorData>();
                    AssetDatabase.CreateAsset(data, assetPath);
                }

                data.PayPalEmail = email;
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssets();

                Debug.Log($"✅ Synced PayPal email to ScriptableObject: {email}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Could not sync PayPal email to ScriptableObject: {ex.Message}");
            }
        }
#endif

        private void ValidateSetup()
        {
            // CRITICAL FIX: Re-check PayPal email if it's empty (might have been set after initialization)
            if (string.IsNullOrEmpty(creatorPayPalEmail))
            {
                LoadCreatorPayPalEmail();
            }

            if (string.IsNullOrEmpty(creatorPayPalEmail))
            {
                SetStatus("PayPal email not saved. Please complete setup first.");
                if (paymentButton != null)
                    paymentButton.interactable = false;
                return;
            }

            if (itemPrice < 0.01f)
            {
                SetStatus("Invalid price configuration.");
                if (paymentButton != null)
                    paymentButton.interactable = false;
                return;
            }

            // All good - ready for payments with appropriate message
            if (allowVariableAmount)
            {
                SetStatus("Ready to send tip (95% Creator, 5% Platform)");
            }
            else
            {
                SetStatus("Ready to accept payments (95% Creator, 5% Platform)");
            }

            if (paymentButton != null)
                paymentButton.interactable = true;
        }

        // CRITICAL FIX: Add public method to refresh PayPal email (callable from editor)
        public void RefreshPayPalEmail()
        {
            LoadCreatorPayPalEmail();
            ValidateSetup();
            Debug.Log($"🔄 PayPal email refreshed: {creatorPayPalEmail}");
        }

        private void UpdateUI()
        {
            if (priceText != null)
            {
                if (allowVariableAmount)
                {
                    priceText.text = $"${minimumAmount:F2} - ${maximumAmount:F2}";
                }
                else
                {
                    priceText.text = $"${itemPrice:F2}";
                }
            }

            if (paymentButton != null)
            {
                var buttonText = paymentButton.GetComponentInChildren<TMP_Text>();
                if (buttonText != null)
                {
                    if (allowVariableAmount)
                    {
                        buttonText.text = "Send Payment";
                    }
                    else
                    {
                        buttonText.text = $"Pay ${itemPrice:F2}";
                    }
                }
            }
        }

        public void StartPayment()
        {
            if (isProcessing)
            {
                Debug.LogWarning("Payment already in progress");
                return;
            }

            float finalAmount = GetFinalAmount();
            if (!ValidateAmount(finalAmount))
            {
                return;
            }

            if (string.IsNullOrEmpty(creatorPayPalEmail))
            {
                SetStatus("Creator PayPal email not configured");
                return;
            }

            // Generate unique transaction ID
            currentTransactionId = Guid.NewGuid().ToString();

            SetProcessingState(true);
            SetStatus("Initializing payment...");

            try
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                // CRITICAL FIX: Use new method that passes GameObject name
                Debug.Log($"🚀 Starting dual transaction for GameObject: {gameObject.name}");
                UnityStartDualTransactionWithGameObject(
                    gameObject.name,           // ← CRITICAL: Pass the actual GameObject name
                    itemName,
                    itemDescription,
                    finalAmount.ToString("F2"),
                    currentTransactionId
                );
#else
                // Editor testing
                Debug.Log($"[EDITOR] Would start dual transaction: {itemName} - ${finalAmount:F2}");
                Debug.Log($"[EDITOR] GameObject name: {gameObject.name}");
                Debug.Log($"[EDITOR] Creator email: {creatorPayPalEmail}");
                Debug.Log($"[EDITOR] Creator amount: ${(finalAmount * 0.95f):F2}");
                Debug.Log($"[EDITOR] Platform amount: ${(finalAmount * 0.05f):F2}");

                // Simulate success in editor
                StartCoroutine(SimulateEditorPayment());
#endif
            }
            catch (Exception ex)
            {
                Debug.LogError($"Payment initialization failed: {ex.Message}");
                SetStatus("Payment initialization failed");
                SetProcessingState(false);
            }
        }

        private System.Collections.IEnumerator SimulateEditorPayment()
        {
            yield return new UnityEngine.WaitForSeconds(2f);
            OnPaymentComplete("true");
        }

        private float GetFinalAmount()
        {
            if (allowVariableAmount && amountInputField != null)
            {
                if (float.TryParse(amountInputField.text, out float userAmount))
                {
                    return userAmount;
                }
                return minimumAmount;
            }
            return itemPrice;
        }

        private bool ValidateAmount(float amount)
        {
            if (allowVariableAmount)
            {
                if (amount < minimumAmount)
                {
                    SetStatus($"Minimum tip amount is ${minimumAmount:F2}");
                    return false;
                }
                if (amount > maximumAmount)
                {
                    SetStatus($"Maximum tip amount is ${maximumAmount:F2}");
                    return false;
                }
            }

            if (amount < 0.01f)
            {
                SetStatus("Amount must be at least $0.01");
                return false;
            }

            // Clear any previous error messages on valid amount
            if (allowVariableAmount)
            {
                SetStatus("Ready to send tip (95% Creator, 5% Platform)");
            }
            else
            {
                SetStatus("Ready to purchase (95% Creator, 5% Platform)");
            }

            return true;
        }

        private void OnAmountChanged(string value)
        {
            if (float.TryParse(value, out float amount))
            {
                ValidateAmount(amount); // Add real-time validation
                UpdateUI();
            }
            else if (!string.IsNullOrEmpty(value))
            {
                SetStatus("Please enter a valid amount");
            }
        }

        // Called by JavaScript when authentication check completes
        public void OnAuthenticationChecked(string isAuthenticated)
        {
            if (isAuthenticated == "true")
            {
                ContinueWithPayment();
            }
            else
            {
                SetStatus("Please log in to make payments");
                SetProcessingState(false);
            }
        }

        private void ContinueWithPayment()
        {
            float finalAmount = GetFinalAmount();

            SetStatus("Starting PayPal payment...");

            try
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                // Check authentication first with GameObject name
                UnityCheckAuthenticationStatusWithGameObject(gameObject.name);
#endif
            }
            catch (Exception ex)
            {
                Debug.LogError($"PayPal payment failed: {ex.Message}");
                SetStatus("Payment failed to start");
                SetProcessingState(false);
            }
        }

        // Called by JavaScript when payment completes
        public void OnPaymentComplete(string success)
        {
            SetProcessingState(false);

            if (success == "true")
            {
                // Different success messages based on payment type
                if (allowVariableAmount)
                {
                    SetStatus("Tip sent successfully! Thank you!");
                }
                else
                {
                    SetStatus("Payment successful!");
                }

                OnPaymentSuccess?.Invoke();

                // For one-time purchases, disable the button
                if (!allowVariableAmount && paymentButton != null)
                {
                    paymentButton.interactable = false;
                    var buttonText = paymentButton.GetComponentInChildren<TMP_Text>();
                    if (buttonText != null)
                    {
                        buttonText.text = "Paid";
                    }
                }
                // For tip jars, reset to ready state after a delay
                else if (allowVariableAmount)
                {
                    StartCoroutine(ResetTipJarAfterDelay());
                }

                Debug.Log($"Dual transaction completed successfully for {itemName}");
            }
            else
            {
                SetStatus("Payment failed. Please try again.");
                OnPaymentFailed?.Invoke();
                Debug.LogWarning($"Dual transaction failed for {itemName}");
            }
        }

        private System.Collections.IEnumerator ResetTipJarAfterDelay()
        {
            yield return new UnityEngine.WaitForSeconds(3f);

            if (allowVariableAmount)
            {
                SetStatus("Ready to send tip");

                // Optionally reset amount to default
                if (amountInputField != null)
                {
                    amountInputField.text = itemPrice.ToString("F2");
                }
            }
        }

        // Called by JavaScript with transaction details
        public void OnTransactionDetails(string transactionData)
        {
            try
            {
                var data = JsonUtility.FromJson<TransactionDetails>(transactionData);
                Debug.Log($"Transaction details received: Creator: {data.creatorTransactionId}, Platform: {data.platformTransactionId}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to parse transaction details: {ex.Message}");
            }
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
            OnStatusChanged?.Invoke(message);
        }

        private void SetProcessingState(bool processing)
        {
            isProcessing = processing;

            if (paymentButton != null)
            {
                paymentButton.interactable = !processing && !string.IsNullOrEmpty(creatorPayPalEmail);
            }

            if (loadingIndicator != null)
            {
                loadingIndicator.SetActive(processing);
            }

            if (amountInputField != null)
            {
                amountInputField.interactable = !processing;
            }
        }

        // Public methods for external configuration
        public void SetItemDetails(string name, string description, float price)
        {
            itemName = name;
            itemDescription = description;
            itemPrice = price;
            UpdateUI();
        }

        public void SetVariableAmount(bool enabled, float min = 1.00f, float max = 100.00f)
        {
            allowVariableAmount = enabled;
            minimumAmount = min;
            maximumAmount = max;

            if (amountInputField != null)
            {
                amountInputField.gameObject.SetActive(enabled);
            }

            UpdateUI();
        }

        public void SetCreatorPayPalEmail(string email)
        {
            creatorPayPalEmail = email;
            ValidateSetup();
        }

        public string GetCreatorPayPalEmail()
        {
            return creatorPayPalEmail;
        }

        public float GetCreatorAmount(float totalAmount)
        {
            return totalAmount * 0.95f;
        }

        public float GetPlatformAmount(float totalAmount)
        {
            return totalAmount * 0.05f;
        }

        /// <summary>
        /// Assigns UI references programmatically after component creation
        /// </summary>
        public void AssignUIReferences(Button payButton, TMP_Text statusTextComponent, TMP_Text priceTextComponent = null, TMP_InputField amountInput = null)
        {
            // Clear existing listeners to prevent duplicates
            if (paymentButton != null)
            {
                paymentButton.onClick.RemoveAllListeners();
            }

            if (amountInputField != null)
            {
                amountInputField.onValueChanged.RemoveAllListeners();
            }

            // Assign new references
            paymentButton = payButton;
            statusText = statusTextComponent;
            priceText = priceTextComponent;
            amountInputField = amountInput;

            // Setup button listener
            if (paymentButton != null)
            {
                paymentButton.onClick.AddListener(StartPayment);
            }

            // Setup amount input listener and configuration
            if (amountInputField != null)
            {
                amountInputField.gameObject.SetActive(allowVariableAmount);
                amountInputField.onValueChanged.AddListener(OnAmountChanged);
                amountInputField.text = itemPrice.ToString("F2");

                // Set character limit for currency input
                amountInputField.characterLimit = 7; // Allows up to "999.99"

                // Ensure decimal number content type
                amountInputField.contentType = TMP_InputField.ContentType.DecimalNumber;
            }

            // Update UI with current settings
            UpdateUI();
            ValidateSetup();
        }

        /// <summary>
        /// Sets a custom creator message for tip jars and donations
        /// </summary>
        public void SetCreatorMessage(string message)
        {
            creatorMessage = message;

            // Update the item description with the creator message
            if (!string.IsNullOrEmpty(message))
            {
                itemDescription = message;
            }
        }

        /// <summary>
        /// Gets the current creator message
        /// </summary>
        public string GetCreatorMessage()
        {
            return creatorMessage;
        }

        /// <summary>
        /// Enhanced method to configure tip jar specifically
        /// </summary>
        public void SetupAsTipJar(float minAmount = 1.00f, float maxAmount = 100.00f, string message = "Thank you for supporting my work!")
        {
            SetItemDetails("Creator Tip", message, 5.00f);
            SetVariableAmount(true, minAmount, maxAmount);
            SetCreatorMessage(message);
        }

        /// <summary>
        /// Validates UI components and provides clear feedback about missing references
        /// </summary>
        public bool ValidateUIComponents()
        {
            bool isValid = true;
            System.Text.StringBuilder issues = new System.Text.StringBuilder();

            if (paymentButton == null)
            {
                issues.AppendLine("• Payment Button is not assigned");
                isValid = false;
            }

            if (statusText == null)
            {
                issues.AppendLine("• Status Text is not assigned");
                isValid = false;
            }

            if (allowVariableAmount && amountInputField == null)
            {
                issues.AppendLine("• Amount Input Field is required for variable amounts");
                isValid = false;
            }

            if (!isValid)
            {
                Debug.LogWarning($"PayPal UI Validation Issues:\n{issues}");
                SetStatus("UI components not properly configured");
            }
            else
            {
                Debug.Log("✅ PayPal UI components validated successfully");
            }

            return isValid;
        }

        /// <summary>
        /// Quick method to assign just the essential button and status text
        /// </summary>
        public void AssignEssentialReferences(Button payButton, TMP_Text statusTextComponent)
        {
            AssignUIReferences(payButton, statusTextComponent, null, null);
        }

        /// <summary>
        /// Automatically find and assign UI references by searching child components
        /// </summary>
        public void AutoAssignUIReferences()
        {
            // Search for components in children
            var foundButton = GetComponentInChildren<Button>();
            var foundStatusText = transform.Find("StatusText")?.GetComponent<TextMeshProUGUI>();
            var foundPriceText = transform.Find("PriceText")?.GetComponent<TextMeshProUGUI>();
            var foundAmountInput = GetComponentInChildren<TMP_InputField>();

            AssignUIReferences(foundButton, foundStatusText, foundPriceText, foundAmountInput);
        }

        /// <summary>
        /// Check if UI references are properly assigned
        /// </summary>
        public bool HasValidUIReferences()
        {
            return paymentButton != null && statusText != null;
        }

        // Helper class for transaction data
        [Serializable]
        private class TransactionDetails
        {
            public string creatorTransactionId;
            public string platformTransactionId;
            public float creatorAmount;
            public float platformAmount;
            public float totalAmount;
        }

        // Inspector validation
        private void OnValidate()
        {
            if (itemPrice < 0)
                itemPrice = 0;

            if (minimumAmount < 0.01f)
                minimumAmount = 0.01f;

            if (maximumAmount < minimumAmount)
                maximumAmount = minimumAmount;
        }

        private void OnEnable()
        {
            // Validate setup when component becomes active (both in editor and runtime)
            if (Application.isPlaying)
            {
                // Runtime validation happens in Start()
                return;
            }

#if UNITY_EDITOR
            // Editor validation
            ValidateSetupInEditor();
#endif
        }

#if UNITY_EDITOR
        private void ValidateSetupInEditor()
        {
            // Check if PayPal email exists in ScriptableObject
            var creatorData = Resources.Load<U3DCreatorData>("U3DCreatorData");
            bool hasPayPalEmail = creatorData != null && !string.IsNullOrEmpty(creatorData.PayPalEmail);

            if (!hasPayPalEmail)
            {
                // Update status text to show configuration needed
                if (statusText != null)
                {
                    if (allowVariableAmount)
                    {
                        statusText.text = "PayPal email address required in Setup";
                    }
                    else
                    {
                        statusText.text = "PayPal email address required in Setup";
                    }
                }
            }
            else
            {
                // Show ready state
                if (statusText != null)
                {
                    if (allowVariableAmount)
                    {
                        statusText.text = "Ready to send tip (95% Creator, 5% Platform)";
                    }
                    else
                    {
                        statusText.text = "Ready to accept payments (95% Creator, 5% Platform)";
                    }
                }
            }
        }
#endif
    }
}