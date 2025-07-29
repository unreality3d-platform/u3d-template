using System;
using System.Collections.Generic;
using TMPro;
using U3D;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace U3D.Editor
{
    public class MonetizationToolsCategory : IToolCategory
    {
        public string CategoryName => "Monetization";
        public System.Action<int> OnRequestTabSwitch { get; set; }

        private List<CreatorTool> tools;

        // NEW: Optimistic approach - assume PayPal is configured unless proven otherwise
        private bool paypalConfigurationChecked = false;
        private bool paypalConfigured = true; // Optimistic default

        public MonetizationToolsCategory()
        {
            tools = new List<CreatorTool>
            {
                new CreatorTool("Add Purchase Button", "Single item PayPal purchase with dual transaction (95% to creator)", CreatePurchaseButton, false),
                new CreatorTool("Add Tip Jar", "Accept variable donations with dual transaction splitting", CreateTipJar, false),
                new CreatorTool("Add Scene Gate", "Scene entry payment gate with PayPal dual transaction", CreateSceneGate, false),
                new CreatorTool("Add Shop Object", "3D world PayPal shop with multiple items and dual transactions", CreateShopObject, false),
                new CreatorTool("Add Event Gate", "Timed event access with PayPal dual transaction", CreateEventGate, false),
                new CreatorTool("Add Screen Shop", "Screen overlay PayPal shop interface with dual transactions", CreateScreenShop, false)
            };
        }

        public List<CreatorTool> GetTools() => tools;

        public void DrawCategory()
        {
            EditorGUILayout.LabelField("Monetization Tools", EditorStyles.boldLabel);

            // NEW: Only check PayPal configuration if we haven't already checked
            if (!paypalConfigurationChecked)
            {
                CheckPayPalConfiguration();
            }

            // NEW: Optimistically show configured state unless we know it's not configured
            if (paypalConfigured)
            {
                DrawConfiguredState();
            }
            else
            {
                DrawUnconfiguredState();
            }

            EditorGUILayout.Space(10);

            // Draw tools with conditional enabling
            foreach (var tool in tools)
            {
                if (!paypalConfigured)
                {
                    EditorGUI.BeginDisabledGroup(true);
                }

                ProjectToolsTab.DrawCategoryTool(tool);

                if (!paypalConfigured)
                {
                    EditorGUI.EndDisabledGroup();
                }
            }

            if (!paypalConfigured)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(
                    "All monetization tools require PayPal email to function.",
                    MessageType.Info);
            }
        }

        private void CheckPayPalConfiguration()
        {
            // CRITICAL FIX: Check all possible sources in priority order
            string paypalEmail = "";

            // 1. First check EditorPrefs (most reliable for editor)
            paypalEmail = EditorPrefs.GetString("U3D_PayPalEmail", "");

            // 2. If not found, check ScriptableObject
            if (string.IsNullOrEmpty(paypalEmail))
            {
                var creatorData = Resources.Load<U3DCreatorData>("U3DCreatorData");
                if (creatorData != null)
                {
                    paypalEmail = creatorData.PayPalEmail;
                }
            }

            // 3. Check U3DAuthenticator (method exists in your code)
            if (string.IsNullOrEmpty(paypalEmail))
            {
                paypalEmail = U3DAuthenticator.GetPayPalEmail();
            }

            paypalConfigured = !string.IsNullOrEmpty(paypalEmail);
            paypalConfigurationChecked = true;

            // Debug log to verify detection
            if (paypalConfigured)
            {
                Debug.Log($"✅ PayPal configured: {paypalEmail}");
            }
            else
            {
                Debug.Log("❌ PayPal email not found in any storage location");

                // DIAGNOSTIC: List what we checked
                Debug.Log("🔍 PayPal email search results:");
                Debug.Log($"   EditorPrefs['U3D_PayPalEmail']: '{EditorPrefs.GetString("U3D_PayPalEmail", "")}'");
                Debug.Log($"   U3DAuthenticator.GetPayPalEmail(): '{U3DAuthenticator.GetPayPalEmail()}'");

                var creatorData = Resources.Load<U3DCreatorData>("U3DCreatorData");
                Debug.Log($"   ScriptableObject: {(creatorData != null ? $"Found, email='{creatorData.PayPalEmail}'" : "Not found")}");
            }
        }

        private void DrawConfiguredState()
        {
            // UNIFIED: Get PayPal email from centralized source
            string paypalEmail = U3DAuthenticator.GetPayPalEmail();

            EditorGUILayout.HelpBox(
                $"PayPal Connected: {paypalEmail}\n\n" +
                "Dual Transaction System Ready:\n" +
                "• You keep 95% of all earnings\n" +
                "• Platform fee: 5% (for hosting & infrastructure)\n" +
                "• Automatic payment splitting\n" +
                "• Direct payments to your PayPal account",
                MessageType.Info);
        }

        private void DrawUnconfiguredState()
        {
            EditorGUILayout.HelpBox(
                "PayPal Email Not Added\n\n" +
                "To enable monetization:\n" +
                "1. Go to the Setup tab\n" +
                "2. Add your PayPal email address\n" +
                "3. Return here to add payment tools\n\n" +
                "You'll keep 95% of all earnings!",
                MessageType.Warning);

            EditorGUILayout.Space(5);

            if (GUILayout.Button("Go to Setup Tab", GUILayout.Height(30)))
            {
                OnRequestTabSwitch?.Invoke(0);
            }

            EditorGUILayout.Space(10);
        }

        // NEW: Method to force recheck (called when PayPal setup changes)
        public void RefreshPayPalConfiguration()
        {
            paypalConfigurationChecked = false;
            CheckPayPalConfiguration();
        }

        #region Tool Creation Methods

        private void CreatePurchaseButton()
        {
            // NEW: Only validate PayPal setup when actually creating tools (optimistic approach)
            if (!ValidatePayPalSetupOnDemand()) return;

            GameObject buttonObject = CreatePaymentUI("Purchase Button", CreatePurchaseButtonUI);

            var dualTransaction = buttonObject.AddComponent<PayPalDualTransaction>();
            dualTransaction.SetItemDetails("Premium Content", "Creator content purchase", 5.00f);
            dualTransaction.SetVariableAmount(false);

            // Auto-assign UI references
            AssignUIReferences(buttonObject, dualTransaction);

            LogToolCreation("Purchase Button", "Single-purchase payment button with 95%/5% split");
        }

        private void CreateTipJar()
        {
            if (!ValidatePayPalSetupOnDemand()) return;

            // FIXED: Always create new Canvas for each tool
            Canvas canvas = CreateNewWorldSpaceCanvas("Tip Jar Canvas");

            // COMPLIANT: Use DefaultControls for panel creation
            var uiResources = new DefaultControls.Resources();
            GameObject container = DefaultControls.CreatePanel(uiResources);
            container.name = "Tip Jar";
            container.transform.SetParent(canvas.transform, false);

            var containerRect = container.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.5f, 0.5f);
            containerRect.anchorMax = new Vector2(0.5f, 0.5f);
            containerRect.sizeDelta = new Vector2(300, 200);
            containerRect.anchoredPosition = Vector2.zero;

            // COMPLIANT: Use existing CreateCleanHeaderUI method
            CreateCleanHeaderUI(container, "Tip Jar");

            // COMPLIANT: Initialize TMP resources for text-based elements
            var tmpResources = new TMP_DefaultControls.Resources();

            // Create amount input field using TMP_DefaultControls (COMPLIANT)
            GameObject inputField = TMP_DefaultControls.CreateInputField(tmpResources);
            inputField.name = "AmountInput";
            inputField.transform.SetParent(container.transform, false);

            var inputRect = inputField.GetComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(0.1f, 0.6f);
            inputRect.anchorMax = new Vector2(0.9f, 0.75f);
            inputRect.offsetMin = Vector2.zero;
            inputRect.offsetMax = Vector2.zero;

            var inputComponent = inputField.GetComponent<TMP_InputField>();
            inputComponent.text = "5.00";
            inputComponent.contentType = TMP_InputField.ContentType.DecimalNumber;

            // COMPLIANT: Add placeholder text with proper color standards
            var placeholder = inputComponent.placeholder as TextMeshProUGUI;
            if (placeholder != null)
            {
                placeholder.text = "Enter tip amount ($1.00 - $100.00)";
                placeholder.color = new Color32(150, 150, 150, 128); // Lighter than #323232 for placeholder
            }

            // COMPLIANT: Create tip button using TMP_DefaultControls
            GameObject button = TMP_DefaultControls.CreateButton(tmpResources);
            button.name = "TipButton";
            button.transform.SetParent(container.transform, false);

            var buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.1f, 0.35f);
            buttonRect.anchorMax = new Vector2(0.9f, 0.55f);
            buttonRect.offsetMin = Vector2.zero;
            buttonRect.offsetMax = Vector2.zero;

            // COMPLIANT: Update button text with standard #323232 color
            var buttonText = button.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.text = "Send Tip";
                buttonText.fontSize = 14;
                buttonText.color = new Color32(50, 50, 50, 255); // #323232 - COMPLIANT
                                                                 // Button text is center-aligned by default from TMP_DefaultControls - COMPLIANT
            }

            // COMPLIANT: Use existing CreateCleanStatusText method
            CreateCleanStatusText(container);

            var dualTransaction = container.AddComponent<PayPalDualTransaction>();
            dualTransaction.SetItemDetails("Creator Tip", "Support this creator's work", 5.00f);
            dualTransaction.SetVariableAmount(true, 1.00f, 100.00f);

            // COMPLIANT: Use existing AssignUIReferences method
            AssignUIReferences(container, dualTransaction);

            // FIXED: Select the Canvas (top-level parent) instead of container
            Selection.activeGameObject = canvas.gameObject;
            LogToolCreation("Tip Jar", "Variable donation system with 95%/5% split");
        }

        private void CreateSceneGate()
        {
            if (!ValidatePayPalSetupOnDemand()) return;

            // Use screen overlay UI instead of world space
            GameObject gateObject = CreateScreenOverlayUI("Scene Gate", CreateSceneGateOverlayUI);

            var dualTransaction = gateObject.AddComponent<PayPalDualTransaction>();
            var gateController = gateObject.AddComponent<SceneGateController>();

            dualTransaction.SetItemDetails("Scene Access", "Premium scene access required", 3.00f);
            dualTransaction.SetVariableAmount(false);
            dualTransaction.OnPaymentSuccess.AddListener(gateController.OpenGate);

            // Auto-assign UI references
            AssignUIReferences(gateObject, dualTransaction);

            LogToolCreation("Scene Gate", "Full-screen access gate that blocks gameplay until payment");
        }

        private void CreateSceneGateOverlayUI(GameObject container)
        {
            var tmpResources = new TMP_DefaultControls.Resources();
            var uiResources = new DefaultControls.Resources();

            // Make the container cover the full screen
            var containerRect = container.GetComponent<RectTransform>();
            containerRect.anchorMin = Vector2.zero;
            containerRect.anchorMax = Vector2.one;
            containerRect.offsetMin = Vector2.zero;
            containerRect.offsetMax = Vector2.zero;

            // Add a semi-transparent background to block interaction
            var containerImage = container.GetComponent<Image>();
            if (containerImage == null)
            {
                containerImage = container.AddComponent<Image>();
            }
            containerImage.color = new Color(0, 0, 0, 0.8f); // Dark overlay
            containerImage.raycastTarget = true; // Block clicks

            // Create centered content panel
            GameObject contentPanel = DefaultControls.CreatePanel(uiResources);
            contentPanel.name = "ContentPanel";
            contentPanel.transform.SetParent(container.transform, false);

            var contentRect = contentPanel.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0.5f, 0.5f);
            contentRect.anchorMax = new Vector2(0.5f, 0.5f);
            contentRect.sizeDelta = new Vector2(400, 300);
            contentRect.anchoredPosition = Vector2.zero;

            // Style the content panel
            var contentImage = contentPanel.GetComponent<Image>();
            if (contentImage != null)
            {
                contentImage.color = new Color(1f, 1f, 1f, 0.95f); // White background
            }

            // Create header
            CreateCleanHeaderUI(contentPanel, "Scene Access Required");

            // Create main message text
            GameObject messageText = TMP_DefaultControls.CreateText(tmpResources);
            messageText.name = "MessageText";
            messageText.transform.SetParent(contentPanel.transform, false);

            var messageRect = messageText.GetComponent<RectTransform>();
            messageRect.anchorMin = new Vector2(0.1f, 0.5f);
            messageRect.anchorMax = new Vector2(0.9f, 0.75f);
            messageRect.offsetMin = Vector2.zero;
            messageRect.offsetMax = Vector2.zero;

            var messageTMP = messageText.GetComponent<TextMeshProUGUI>();
            if (messageTMP != null)
            {
                messageTMP.text = "This premium content requires payment to access.\n\nSupport the creator and unlock this experience!";
                messageTMP.fontSize = 14;
                messageTMP.color = new Color32(50, 50, 50, 255); // #323232
                messageTMP.alignment = TextAlignmentOptions.Center;
                messageTMP.raycastTarget = false;
            }

            // Create unlock button
            GameObject button = TMP_DefaultControls.CreateButton(tmpResources);
            button.name = "UnlockButton";
            button.transform.SetParent(contentPanel.transform, false);

            var buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.2f, 0.25f);
            buttonRect.anchorMax = new Vector2(0.8f, 0.4f);
            buttonRect.offsetMin = Vector2.zero;
            buttonRect.offsetMax = Vector2.zero;

            var buttonText = button.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.text = "Unlock Scene - $3.00";
                buttonText.fontSize = 16;
                buttonText.color = new Color32(50, 50, 50, 255); // #323232
            }

            // Style the button
            var buttonImage = button.GetComponent<Image>();
            if (buttonImage != null)
            {
                buttonImage.color = new Color32(100, 200, 100, 255); // Green button
            }

            // Create status text
            GameObject statusText = TMP_DefaultControls.CreateText(tmpResources);
            statusText.name = "StatusText";
            statusText.transform.SetParent(contentPanel.transform, false);

            var statusRect = statusText.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0.1f, 0.05f);
            statusRect.anchorMax = new Vector2(0.9f, 0.2f);
            statusRect.offsetMin = Vector2.zero;
            statusRect.offsetMax = Vector2.zero;

            var statusTMP = statusText.GetComponent<TextMeshProUGUI>();
            if (statusTMP != null)
            {
                statusTMP.text = "Ready to accept payment (95% Creator, 5% Platform)";
                statusTMP.fontSize = 10;
                statusTMP.color = new Color32(50, 50, 50, 255); // #323232
                statusTMP.alignment = TextAlignmentOptions.Center;
                statusTMP.raycastTarget = false;
            }
        }

        private void CreateShopObject()
        {
            if (!ValidatePayPalSetupOnDemand()) return;

            GameObject shopObject = CreatePaymentUI("Shop Object", CreateShopObjectUI);

            var shopController = shopObject.AddComponent<U3D.ShopController>();

            LogToolCreation("Shop Object", "Multi-item 3D shop with individual dual transactions");
        }

        private void CreateEventGate()
        {
            if (!ValidatePayPalSetupOnDemand()) return;

            GameObject eventObject = CreatePaymentUI("Event Gate", CreateEventGateUI);

            var dualTransaction = eventObject.AddComponent<PayPalDualTransaction>();
            var eventController = eventObject.AddComponent<U3D.EventGateController>();

            dualTransaction.SetItemDetails("Event Access", "Special event ticket", 10.00f);
            dualTransaction.SetVariableAmount(false);
            dualTransaction.OnPaymentSuccess.AddListener(eventController.GrantAccess);

            // Auto-assign UI references
            AssignUIReferences(eventObject, dualTransaction);

            LogToolCreation("Event Gate", "Timed event access with payment verification");
        }

        private void CreateScreenShop()
        {
            if (!ValidatePayPalSetupOnDemand()) return;

            GameObject screenShop = CreateScreenOverlayUI("Screen Shop", CreateScreenShopUI);

            var screenShopController = screenShop.AddComponent<U3D.ScreenShopController>();

            LogToolCreation("Screen Shop", "Overlay shop interface with dual transaction support");
        }

        #endregion

        #region UI Creation Helpers

        /// <summary>
        /// Creates a new WorldSpace Canvas for monetization tools - never reuses existing Canvas
        /// </summary>
        private Canvas CreateNewWorldSpaceCanvas(string canvasName)
        {
            var canvasObject = new GameObject(canvasName);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>();
            canvas.transform.localScale = Vector3.one * 0.01f;

            return canvas;
        }

        /// <summary>
        /// Creates a new ScreenSpaceOverlay Canvas for overlay tools
        /// </summary>
        private Canvas CreateNewOverlayCanvas(string canvasName)
        {
            var canvasObject = new GameObject(canvasName);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>();

            return canvas;
        }

        private GameObject CreatePaymentUI(string name, System.Action<GameObject> customSetup)
        {
            // FIXED: Always create new Canvas for each tool with descriptive name
            Canvas canvas = CreateNewWorldSpaceCanvas($"{name} Canvas");

            var uiResources = new DefaultControls.Resources();
            GameObject container = DefaultControls.CreatePanel(uiResources);
            container.name = name;
            container.transform.SetParent(canvas.transform, false);

            var containerRect = container.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.5f, 0.5f);
            containerRect.anchorMax = new Vector2(0.5f, 0.5f);
            containerRect.sizeDelta = new Vector2(300, 200);
            containerRect.anchoredPosition = Vector2.zero;

            CreateCleanHeaderUI(container, name);

            customSetup?.Invoke(container);

            // FIXED: Select the Canvas (top-level parent) instead of container
            Selection.activeGameObject = canvas.gameObject;
            return container;
        }

        private GameObject CreateScreenOverlayUI(string name, System.Action<GameObject> customSetup)
        {
            // FIXED: Always create new ScreenSpaceOverlay Canvas with descriptive name
            Canvas canvas = CreateNewOverlayCanvas($"{name} Canvas");

            var uiResources = new DefaultControls.Resources();
            GameObject container = DefaultControls.CreatePanel(uiResources);
            container.name = name;
            container.transform.SetParent(canvas.transform, false);

            var containerRect = container.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.5f, 0.5f);
            containerRect.anchorMax = new Vector2(0.5f, 0.5f);
            containerRect.sizeDelta = new Vector2(400, 300);
            containerRect.anchoredPosition = Vector2.zero;

            CreateCleanHeaderUI(container, name);
            customSetup?.Invoke(container);

            // FIXED: Select the Canvas (top-level parent) instead of container
            Selection.activeGameObject = canvas.gameObject;
            return container;
        }

        private void CreateCleanHeaderUI(GameObject parent, string title)
        {
            var uiResources = new DefaultControls.Resources();

            GameObject header = DefaultControls.CreatePanel(uiResources);
            header.name = "Header";
            header.transform.SetParent(parent.transform, false);

            var headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0, 0.8f);
            headerRect.anchorMax = new Vector2(1, 1);
            headerRect.offsetMin = Vector2.zero;
            headerRect.offsetMax = Vector2.zero;

            // Create title text using TMP_DefaultControls
            var tmpResources = new TMP_DefaultControls.Resources();
            GameObject titleText = TMP_DefaultControls.CreateText(tmpResources);
            titleText.name = "Title";
            titleText.transform.SetParent(header.transform, false);

            var titleRect = titleText.GetComponent<RectTransform>();
            titleRect.anchorMin = Vector2.zero;
            titleRect.anchorMax = Vector2.one;
            titleRect.offsetMin = new Vector2(10, 0);
            titleRect.offsetMax = new Vector2(-10, 0);

            // Text is already TextMeshPro from TMP_DefaultControls
            var titleTMP = titleText.GetComponent<TextMeshProUGUI>();
            if (titleTMP != null)
            {
                titleTMP.text = title;
                titleTMP.fontSize = 16;
                titleTMP.color = new Color32(50, 50, 50, 255); // #323232
                titleTMP.alignment = TextAlignmentOptions.Center; // Titles are center-aligned
                titleTMP.raycastTarget = false;
            }
        }

        private void CreateCleanStatusText(GameObject container)
        {
            var tmpResources = new TMP_DefaultControls.Resources();

            GameObject statusText = TMP_DefaultControls.CreateText(tmpResources);
            statusText.name = "StatusText";
            statusText.transform.SetParent(container.transform, false);

            var statusRect = statusText.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0, 0);
            statusRect.anchorMax = new Vector2(1, 0.15f);
            statusRect.offsetMin = new Vector2(10, 5);
            statusRect.offsetMax = new Vector2(-10, -5);

            // Text is already TextMeshPro from TMP_DefaultControls
            var statusTMP = statusText.GetComponent<TextMeshProUGUI>();
            if (statusTMP != null)
            {
                statusTMP.text = "Ready to accept payments";
                statusTMP.fontSize = 10;
                statusTMP.color = new Color32(50, 50, 50, 255); // #323232
                statusTMP.raycastTarget = false;
            }
        }

        private void CreatePurchaseButtonUI(GameObject container)
        {
            var tmpResources = new TMP_DefaultControls.Resources();

            // Create price text using TMP_DefaultControls
            GameObject priceText = TMP_DefaultControls.CreateText(tmpResources);
            priceText.name = "PriceText";
            priceText.transform.SetParent(container.transform, false);

            var priceRect = priceText.GetComponent<RectTransform>();
            priceRect.anchorMin = new Vector2(0, 0.5f);
            priceRect.anchorMax = new Vector2(1, 0.7f);
            priceRect.offsetMin = new Vector2(10, 0);
            priceRect.offsetMax = new Vector2(-10, 0);

            var priceTMP = priceText.GetComponent<TextMeshProUGUI>();
            if (priceTMP != null)
            {
                priceTMP.text = "$5.00";
                priceTMP.fontSize = 18;
                priceTMP.color = new Color32(50, 50, 50, 255); // #323232
                priceTMP.alignment = TextAlignmentOptions.Center;
                priceTMP.raycastTarget = false;
            }

            // Create payment button using TMP_DefaultControls
            GameObject button = TMP_DefaultControls.CreateButton(tmpResources);
            button.name = "PaymentButton";
            button.transform.SetParent(container.transform, false);

            var buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.1f, 0.25f);
            buttonRect.anchorMax = new Vector2(0.9f, 0.45f);
            buttonRect.offsetMin = Vector2.zero;
            buttonRect.offsetMax = Vector2.zero;

            var buttonText = button.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.text = "Purchase";
                buttonText.fontSize = 14;
                buttonText.color = new Color32(50, 50, 50, 255); // #323232
            }

            CreateCleanStatusText(container);
        }

        private void CreateSceneGateUI(GameObject container)
        {
            var tmpResources = new TMP_DefaultControls.Resources();

            // Create description text using TMP_DefaultControls
            GameObject descText = TMP_DefaultControls.CreateText(tmpResources);
            descText.name = "DescriptionText";
            descText.transform.SetParent(container.transform, false);

            var descRect = descText.GetComponent<RectTransform>();
            descRect.anchorMin = new Vector2(0, 0.5f);
            descRect.anchorMax = new Vector2(1, 0.7f);
            descRect.offsetMin = new Vector2(10, 0);
            descRect.offsetMax = new Vector2(-10, 0);

            var descTMP = descText.GetComponent<TextMeshProUGUI>();
            if (descTMP != null)
            {
                descTMP.text = "Premium Scene Access Required\nPay to unlock this area";
                descTMP.fontSize = 12;
                descTMP.color = new Color32(50, 50, 50, 255); // #323232
                descTMP.alignment = TextAlignmentOptions.Center;
                descTMP.raycastTarget = false;
            }

            // Create unlock button using TMP_DefaultControls
            GameObject button = TMP_DefaultControls.CreateButton(tmpResources);
            button.name = "UnlockButton";
            button.transform.SetParent(container.transform, false);

            var buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.1f, 0.25f);
            buttonRect.anchorMax = new Vector2(0.9f, 0.45f);
            buttonRect.offsetMin = Vector2.zero;
            buttonRect.offsetMax = Vector2.zero;

            var buttonText = button.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.text = "Unlock Scene";
                buttonText.fontSize = 14;
                buttonText.color = new Color32(50, 50, 50, 255); // #323232
            }

            CreateCleanStatusText(container);
        }

        private void CreateShopObjectUI(GameObject container)
        {
            var tmpResources = new TMP_DefaultControls.Resources();
            var uiResources = new DefaultControls.Resources();

            // Create shop title using TMP_DefaultControls
            GameObject shopTitle = TMP_DefaultControls.CreateText(tmpResources);
            shopTitle.name = "ShopTitle";
            shopTitle.transform.SetParent(container.transform, false);

            var titleRect = shopTitle.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 0.65f);
            titleRect.anchorMax = new Vector2(1, 0.78f);
            titleRect.offsetMin = new Vector2(10, 0);
            titleRect.offsetMax = new Vector2(-10, 0);

            var titleTMP = shopTitle.GetComponent<TextMeshProUGUI>();
            if (titleTMP != null)
            {
                titleTMP.text = "Creator Shop";
                titleTMP.fontSize = 14;
                titleTMP.color = new Color32(50, 50, 50, 255); // #323232
                titleTMP.alignment = TextAlignmentOptions.Center;
                titleTMP.raycastTarget = false;
            }

            // Create scroll view for items using DefaultControls
            GameObject scrollView = DefaultControls.CreateScrollView(uiResources);
            scrollView.name = "ItemScrollView";
            scrollView.transform.SetParent(container.transform, false);

            var scrollRect = scrollView.GetComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0.05f, 0.2f);
            scrollRect.anchorMax = new Vector2(0.95f, 0.6f);
            scrollRect.offsetMin = Vector2.zero;
            scrollRect.offsetMax = Vector2.zero;

            CreateCleanStatusText(container);
        }

        private void CreateEventGateUI(GameObject container)
        {
            var tmpResources = new TMP_DefaultControls.Resources();

            // Create event info text using TMP_DefaultControls
            GameObject eventInfo = TMP_DefaultControls.CreateText(tmpResources);
            eventInfo.name = "EventInfo";
            eventInfo.transform.SetParent(container.transform, false);

            var infoRect = eventInfo.GetComponent<RectTransform>();
            infoRect.anchorMin = new Vector2(0, 0.6f);
            infoRect.anchorMax = new Vector2(1, 0.75f);
            infoRect.offsetMin = new Vector2(10, 0);
            infoRect.offsetMax = new Vector2(-10, 0);

            var infoTMP = eventInfo.GetComponent<TextMeshProUGUI>();
            if (infoTMP != null)
            {
                infoTMP.text = "Special Event Access";
                infoTMP.fontSize = 12;
                infoTMP.color = new Color32(50, 50, 50, 255); // #323232
                infoTMP.alignment = TextAlignmentOptions.Center;
                infoTMP.raycastTarget = false;
            }

            // Create timer text using TMP_DefaultControls
            GameObject timerText = TMP_DefaultControls.CreateText(tmpResources);
            timerText.name = "TimerText";
            timerText.transform.SetParent(container.transform, false);

            var timerRect = timerText.GetComponent<RectTransform>();
            timerRect.anchorMin = new Vector2(0, 0.45f);
            timerRect.anchorMax = new Vector2(1, 0.6f);
            timerRect.offsetMin = new Vector2(10, 0);
            timerRect.offsetMax = new Vector2(-10, 0);

            var timerTMP = timerText.GetComponent<TextMeshProUGUI>();
            if (timerTMP != null)
            {
                timerTMP.text = "Event Active";
                timerTMP.fontSize = 10;
                timerTMP.color = new Color32(50, 50, 50, 255); // #323232
                timerTMP.alignment = TextAlignmentOptions.Center;
                timerTMP.raycastTarget = false;
            }

            // Create access button using TMP_DefaultControls
            GameObject button = TMP_DefaultControls.CreateButton(tmpResources);
            button.name = "AccessButton";
            button.transform.SetParent(container.transform, false);

            var buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.1f, 0.25f);
            buttonRect.anchorMax = new Vector2(0.9f, 0.4f);
            buttonRect.offsetMin = Vector2.zero;
            buttonRect.offsetMax = Vector2.zero;

            var buttonText = button.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.text = "Buy Ticket";
                buttonText.fontSize = 14;
                buttonText.color = new Color32(50, 50, 50, 255); // #323232
            }

            CreateCleanStatusText(container);
        }

        private void CreateScreenShopUI(GameObject container)
        {
            var tmpResources = new TMP_DefaultControls.Resources();
            var uiResources = new DefaultControls.Resources();

            // Create close button using TMP_DefaultControls
            GameObject closeButton = TMP_DefaultControls.CreateButton(tmpResources);
            closeButton.name = "CloseButton";
            closeButton.transform.SetParent(container.transform, false);

            var closeRect = closeButton.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(0.9f, 0.9f);
            closeRect.anchorMax = new Vector2(0.98f, 0.98f);
            closeRect.offsetMin = Vector2.zero;
            closeRect.offsetMax = Vector2.zero;

            var closeText = closeButton.GetComponentInChildren<TextMeshProUGUI>();
            if (closeText != null)
            {
                closeText.text = "X";
                closeText.fontSize = 16;
                closeText.color = new Color32(50, 50, 50, 255); // #323232
            }

            // Create shop content area using DefaultControls
            GameObject contentArea = DefaultControls.CreateScrollView(uiResources);
            contentArea.name = "ShopContent";
            contentArea.transform.SetParent(container.transform, false);

            var contentRect = contentArea.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0.05f, 0.1f);
            contentRect.anchorMax = new Vector2(0.95f, 0.85f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;

            CreateCleanStatusText(container);
        }

        private void AssignUIReferences(GameObject container, PayPalDualTransaction dualTransaction)
        {
            // Find UI components by name
            var paymentButton = container.transform.Find("PaymentButton")?.GetComponent<Button>();
            if (paymentButton == null)
            {
                paymentButton = container.transform.Find("TipButton")?.GetComponent<Button>();
            }
            if (paymentButton == null)
            {
                paymentButton = container.transform.Find("UnlockButton")?.GetComponent<Button>();
            }
            if (paymentButton == null)
            {
                paymentButton = container.transform.Find("AccessButton")?.GetComponent<Button>();
            }

            var statusText = container.transform.Find("StatusText")?.GetComponent<TextMeshProUGUI>();
            var priceText = container.transform.Find("PriceText")?.GetComponent<TextMeshProUGUI>();
            var amountInput = container.transform.Find("AmountInput")?.GetComponent<TMP_InputField>();

            // Use the public method to assign references
            dualTransaction.AssignUIReferences(paymentButton, statusText, priceText, amountInput);

            // UNIFIED: Get PayPal email from centralized system and assign it
            string paypalEmail = U3DAuthenticator.GetPayPalEmail();
            if (!string.IsNullOrEmpty(paypalEmail))
            {
                dualTransaction.SetCreatorPayPalEmail(paypalEmail);
                Debug.Log($"✅ PayPal email assigned to {container.name}: {paypalEmail}");
            }
            else
            {
                Debug.LogWarning($"⚠️ No PayPal email available for {container.name}. Component will require setup before use.");
            }

            Debug.Log($"UI References assigned to {container.name}: Button={paymentButton != null}, Status={statusText != null}, Price={priceText != null}, Input={amountInput != null}");
        }

        #endregion

        #region Validation and Logging

        // UNIFIED: Simplified validation using centralized system
        private bool ValidatePayPalSetupOnDemand()
        {
            // CRITICAL FIX: Use the same multi-source check as CheckPayPalConfiguration
            string paypalEmail = "";

            // 1. Check EditorPrefs first
            paypalEmail = EditorPrefs.GetString("U3D_PayPalEmail", "");

            // 2. Check ScriptableObject if EditorPrefs empty
            if (string.IsNullOrEmpty(paypalEmail))
            {
                var creatorData = Resources.Load<U3DCreatorData>("U3DCreatorData");
                if (creatorData != null)
                {
                    paypalEmail = creatorData.PayPalEmail;
                }
            }

            // 3. Check U3DAuthenticator (method exists in your code)
            if (string.IsNullOrEmpty(paypalEmail))
            {
                paypalEmail = U3DAuthenticator.GetPayPalEmail();
            }

            if (string.IsNullOrEmpty(paypalEmail))
            {
                // Show dialog and offer to go to Setup tab
                bool goToSetup = EditorUtility.DisplayDialog(
                    "PayPal Setup Required",
                    "Please configure your PayPal email to enable monetization tools.\n\n" +
                    "This enables the dual transaction system where you keep 95% of earnings.\n\n" +
                    "Would you like to go to the Setup tab now?",
                    "Yes, Take Me There", "Cancel"
                );

                if (goToSetup)
                {
                    OnRequestTabSwitch?.Invoke(0); // Switch to Setup tab
                }

                // Update our cached state since we now know it's not configured
                paypalConfigured = false;
                paypalConfigurationChecked = true;

                return false;
            }

            // Update our cached state since we now know it IS configured
            paypalConfigured = true;
            paypalConfigurationChecked = true;

            return true;
        }

        private void LogToolCreation(string toolName, string description)
        {
            Debug.Log($"Created {toolName}: {description}");
            Debug.Log($"Dual transaction system ready - Creator keeps 95%, Platform fee: 5%");

            EditorUtility.DisplayDialog(
                "Monetization Tool Created!",
                $"{toolName} has been created successfully.\n\n" +
                "Dual Transaction System:\n" +
                "• You keep 95% of all payments\n" +
                "• Platform fee: 5%\n" +
                "• Automatic payment splitting\n" +
                "• Direct PayPal payments\n\n" +
                "Configure the component settings to customize pricing and behavior.",
                "Great!"
            );
        }

        #endregion
    }
}