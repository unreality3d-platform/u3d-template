using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

namespace U3D.Editor
{
    public class MonetizationToolsCategory : IToolCategory
    {
        public string CategoryName => "Monetization";
        public System.Action<int> OnRequestTabSwitch { get; set; }

        private List<CreatorTool> tools;

        public MonetizationToolsCategory()
        {
            tools = new List<CreatorTool>
            {
                new CreatorTool("Add Purchase Button", "Single item PayPal purchase with dual transaction (95% to creator)", CreatePurchaseButton, true),
                new CreatorTool("Add Tip Jar", "Accept variable donations with dual transaction splitting", CreateTipJar, true),
                new CreatorTool("Add Scene Gate", "Scene entry payment gate with PayPal dual transaction", CreateSceneGate, true),
                new CreatorTool("Add Shop Object", "3D world PayPal shop with multiple items and dual transactions", CreateShopObject, true),
                new CreatorTool("Add Event Gate", "Timed event access with PayPal dual transaction", CreateEventGate, true),
                new CreatorTool("Add Screen Shop", "Screen overlay PayPal shop interface with dual transactions", CreateScreenShop, true)
            };
        }

        public List<CreatorTool> GetTools() => tools;

        public void DrawCategory()
        {
            EditorGUILayout.LabelField("Monetization Tools", EditorStyles.boldLabel);

            // Check PayPal setup status
            var creatorData = Resources.Load<U3DCreatorData>("U3DCreatorData");
            string paypalEmail = (creatorData != null) ? creatorData.PayPalEmail : "";
            bool paypalConfigured = !string.IsNullOrEmpty(paypalEmail);

            if (paypalConfigured)
            {
                EditorGUILayout.HelpBox(
                    $"PayPal Connected: {paypalEmail}\n\n" +
                    "Dual Transaction System Ready:\n" +
                    "• You keep 95% of all earnings\n" +
                    "• Platform fee: 5% (for hosting & infrastructure)\n" +
                    "• Automatic payment splitting\n" +
                    "• Direct payments to your PayPal account",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "PayPal Not Configured\n\n" +
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

            EditorGUILayout.Space(10);

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
                    "All monetization tools require PayPal configuration to function.",
                    MessageType.Info);
            }
        }

        #region Tool Creation Methods

        private void CreatePurchaseButton()
        {
            if (!ValidatePayPalSetup()) return;

            GameObject buttonObject = CreatePaymentUI("Purchase Button", CreatePurchaseButtonUI);

            var dualTransaction = buttonObject.AddComponent<PayPalDualTransaction>();
            dualTransaction.SetItemDetails("Premium Content", "Creator content purchase", 5.00f);
            dualTransaction.SetVariableAmount(false);

            // NEW: Auto-assign UI references
            AssignUIReferences(buttonObject, dualTransaction);

            LogToolCreation("Purchase Button", "Single-purchase payment button with 95%/5% split");
        }
        private void CreateTipJar()
        {
            if (!ValidatePayPalSetup()) return;

            // Find or create Canvas using Unity 6+ method
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                var canvasObject = new GameObject("Canvas");
                canvas = canvasObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvasObject.AddComponent<CanvasScaler>();
                canvasObject.AddComponent<GraphicRaycaster>();
                canvas.transform.localScale = Vector3.one * 0.01f;
            }

            var uiResources = new DefaultControls.Resources();
            GameObject container = DefaultControls.CreatePanel(uiResources);
            container.name = "Tip Jar";
            container.transform.SetParent(canvas.transform, false);

            var containerRect = container.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.5f, 0.5f);
            containerRect.anchorMax = new Vector2(0.5f, 0.5f);
            containerRect.sizeDelta = new Vector2(300, 200);
            containerRect.anchoredPosition = Vector2.zero;

            CreateCleanHeaderUI(container, "Tip Jar");

            // Create amount input field using TMP_DefaultControls
            var tmpResources = new TMP_DefaultControls.Resources();
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

            // Create tip button using TMP_DefaultControls
            GameObject button = TMP_DefaultControls.CreateButton(tmpResources);
            button.name = "TipButton";
            button.transform.SetParent(container.transform, false);

            var buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.1f, 0.35f);
            buttonRect.anchorMax = new Vector2(0.9f, 0.55f);
            buttonRect.offsetMin = Vector2.zero;
            buttonRect.offsetMax = Vector2.zero;

            // Update button text (already TextMeshPro from TMP_DefaultControls)
            var buttonText = button.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.text = "Send Tip";
                buttonText.fontSize = 14;
                buttonText.color = new Color32(50, 50, 50, 255); // #323232
            }

            CreateCleanStatusText(container);

            var dualTransaction = container.AddComponent<PayPalDualTransaction>();
            dualTransaction.SetItemDetails("Creator Tip", "Support this creator's work", 5.00f);
            dualTransaction.SetVariableAmount(true, 1.00f, 100.00f);

            // NEW: Auto-assign UI references
            AssignUIReferences(container, dualTransaction);

            Selection.activeGameObject = container;
            LogToolCreation("Tip Jar", "Variable donation system with 95%/5% split");
        }

        private void CreateSceneGate()
        {
            if (!ValidatePayPalSetup()) return;

            GameObject gateObject = CreatePaymentUI("Scene Gate", CreateSceneGateUI);

            var dualTransaction = gateObject.AddComponent<PayPalDualTransaction>();
            var gateController = gateObject.AddComponent<SceneGateController>();

            dualTransaction.SetItemDetails("Scene Access", "Premium scene entry fee", 3.00f);
            dualTransaction.SetVariableAmount(false);
            dualTransaction.OnPaymentSuccess.AddListener(gateController.OpenGate);

            // NEW: Auto-assign UI references
            AssignUIReferences(gateObject, dualTransaction);

            LogToolCreation("Scene Gate", "Entry payment gate with automatic unlocking");
        }

        private void CreateShopObject()
        {
            if (!ValidatePayPalSetup()) return;

            GameObject shopObject = CreatePaymentUI("Shop Object", CreateShopObjectUI);

            var shopController = shopObject.AddComponent<ShopController>();

            LogToolCreation("Shop Object", "Multi-item 3D shop with individual dual transactions");
        }

        private void CreateEventGate()
        {
            if (!ValidatePayPalSetup()) return;

            GameObject eventObject = CreatePaymentUI("Event Gate", CreateEventGateUI);

            var dualTransaction = eventObject.AddComponent<PayPalDualTransaction>();
            var eventController = eventObject.AddComponent<EventGateController>();

            dualTransaction.SetItemDetails("Event Access", "Special event ticket", 10.00f);
            dualTransaction.SetVariableAmount(false);
            dualTransaction.OnPaymentSuccess.AddListener(eventController.GrantAccess);

            // NEW: Auto-assign UI references
            AssignUIReferences(eventObject, dualTransaction);

            LogToolCreation("Event Gate", "Timed event access with payment verification");
        }

        private void CreateScreenShop()
        {
            if (!ValidatePayPalSetup()) return;

            GameObject screenShop = CreateScreenOverlayUI("Screen Shop", CreateScreenShopUI);

            var screenShopController = screenShop.AddComponent<ScreenShopController>();

            LogToolCreation("Screen Shop", "Overlay shop interface with dual transaction support");
        }

        #endregion

        #region UI Creation Helpers

        private GameObject CreatePaymentUI(string name, System.Action<GameObject> customSetup)
        {
            // Find or create Canvas using Unity 6+ method
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                var canvasObject = new GameObject("Canvas");
                canvas = canvasObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvasObject.AddComponent<CanvasScaler>();
                canvasObject.AddComponent<GraphicRaycaster>();
                canvas.transform.localScale = Vector3.one * 0.01f;
            }

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

            Selection.activeGameObject = container;
            return container;
        }

        private GameObject CreateScreenOverlayUI(string name, System.Action<GameObject> customSetup)
        {
            // Find or create Canvas (Screen Space Overlay) using Unity 6+ method
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                var canvasObject = new GameObject("UI Canvas");
                canvas = canvasObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasObject.AddComponent<CanvasScaler>();
                canvasObject.AddComponent<GraphicRaycaster>();
            }

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

            Selection.activeGameObject = container;
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
                priceTMP.raycastTarget = false;
            }

            // Create payment button using TMP_DefaultControls
            GameObject button = TMP_DefaultControls.CreateButton(tmpResources);
            button.name = "PaymentButton";
            button.transform.SetParent(container.transform, false);

            var buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.1f, 0.1f);
            buttonRect.anchorMax = new Vector2(0.9f, 0.4f);
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
            descRect.anchorMin = new Vector2(0, 0.6f);
            descRect.anchorMax = new Vector2(1, 0.75f);
            descRect.offsetMin = new Vector2(10, 0);
            descRect.offsetMax = new Vector2(-10, 0);

            var descTMP = descText.GetComponent<TextMeshProUGUI>();
            if (descTMP != null)
            {
                descTMP.text = "Premium Scene Access Required";
                descTMP.fontSize = 12;
                descTMP.color = new Color32(50, 50, 50, 255); // #323232
                descTMP.raycastTarget = false;
            }

            // Create unlock button using TMP_DefaultControls
            GameObject button = TMP_DefaultControls.CreateButton(tmpResources);
            button.name = "UnlockButton";
            button.transform.SetParent(container.transform, false);

            var buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.1f, 0.35f);
            buttonRect.anchorMax = new Vector2(0.9f, 0.55f);
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
            titleRect.anchorMin = new Vector2(0, 0.7f);
            titleRect.anchorMax = new Vector2(1, 0.85f);
            titleRect.offsetMin = new Vector2(10, 0);
            titleRect.offsetMax = new Vector2(-10, 0);

            var titleTMP = shopTitle.GetComponent<TextMeshProUGUI>();
            if (titleTMP != null)
            {
                titleTMP.text = "Creator Shop";
                titleTMP.fontSize = 14;
                titleTMP.color = new Color32(50, 50, 50, 255); // #323232
                titleTMP.raycastTarget = false;
            }

            // Create scroll view for items using DefaultControls
            GameObject scrollView = DefaultControls.CreateScrollView(uiResources);
            scrollView.name = "ItemScrollView";
            scrollView.transform.SetParent(container.transform, false);

            var scrollRect = scrollView.GetComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0.05f, 0.2f);
            scrollRect.anchorMax = new Vector2(0.95f, 0.65f);
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
                timerTMP.raycastTarget = false;
            }

            // Create access button using TMP_DefaultControls
            GameObject button = TMP_DefaultControls.CreateButton(tmpResources);
            button.name = "AccessButton";
            button.transform.SetParent(container.transform, false);

            var buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.1f, 0.2f);
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
            closeRect.anchorMax = new Vector2(1f, 1f);
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

            // Set PayPal email directly from ScriptableObject
            var creatorData = Resources.Load<U3DCreatorData>("U3DCreatorData");
            if (creatorData != null && !string.IsNullOrEmpty(creatorData.PayPalEmail))
            {
                dualTransaction.SetCreatorPayPalEmail(creatorData.PayPalEmail);
            }

            Debug.Log($"UI References assigned to {container.name}: Button={paymentButton != null}, Status={statusText != null}, Price={priceText != null}, Input={amountInput != null}");
        }

        #endregion

        #region Helper Components

        // Scene Gate Controller Component
        public class SceneGateController : MonoBehaviour
        {
            [SerializeField] private GameObject gateObject;
            [SerializeField] private bool isOpen = false;

            public void OpenGate()
            {
                isOpen = true;
                if (gateObject != null)
                {
                    gateObject.SetActive(false);
                }
                Debug.Log("Scene gate opened - access granted!");

                var statusText = GetComponentInChildren<TextMeshProUGUI>();
                if (statusText != null && statusText.name == "StatusText")
                {
                    statusText.text = "Access granted!";
                }
            }

            public bool IsOpen() => isOpen;
        }

        // Shop Controller Component
        public class ShopController : MonoBehaviour
        {
            [System.Serializable]
            public class ShopItem
            {
                public string itemName = "Item";
                public string description = "Shop item description";
                public float price = 5.00f;
                public Sprite icon;
            }

            [SerializeField] private List<ShopItem> shopItems = new List<ShopItem>();
            [SerializeField] private Transform itemContainer;

            private void Start()
            {
                SetupDefaultItems();
                CreateItemUI();
            }

            private void SetupDefaultItems()
            {
                if (shopItems.Count == 0)
                {
                    shopItems.Add(new ShopItem { itemName = "Premium Avatar", description = "Exclusive creator avatar", price = 10.00f });
                    shopItems.Add(new ShopItem { itemName = "Special Effect", description = "Unique visual effect", price = 5.00f });
                    shopItems.Add(new ShopItem { itemName = "Creator Pack", description = "Bundle of creator content", price = 15.00f });
                }
            }

            private void CreateItemUI()
            {
                var scrollView = GetComponentInChildren<ScrollRect>();
                if (scrollView != null)
                {
                    itemContainer = scrollView.content;
                }

                if (itemContainer == null) return;

                foreach (var item in shopItems)
                {
                    CreateShopItemButton(item);
                }
            }

            private void CreateShopItemButton(ShopItem item)
            {
                var tmpResources = new TMP_DefaultControls.Resources();
                GameObject itemButton = TMP_DefaultControls.CreateButton(tmpResources);
                itemButton.name = $"Item_{item.itemName}";
                itemButton.transform.SetParent(itemContainer, false);

                var buttonRect = itemButton.GetComponent<RectTransform>();
                buttonRect.sizeDelta = new Vector2(250, 40);

                var dualTransaction = itemButton.AddComponent<PayPalDualTransaction>();
                dualTransaction.SetItemDetails(item.itemName, item.description, item.price);
                dualTransaction.SetVariableAmount(false);

                // NEW: Auto-assign UI references for shop item buttons
                dualTransaction.AssignEssentialReferences(
                    itemButton.GetComponent<Button>(),
                    null // Shop items don't have individual status text
                );

                var buttonText = itemButton.GetComponentInChildren<TextMeshProUGUI>();
                if (buttonText != null)
                {
                    buttonText.text = $"{item.itemName} - ${item.price:F2}";
                    buttonText.color = new Color32(50, 50, 50, 255); // #323232
                }
            }
        }

        // Event Gate Controller Component
        public class EventGateController : MonoBehaviour
        {
            [SerializeField] private float eventDuration = 3600f; // 1 hour
            [SerializeField] private bool eventActive = true;
            [SerializeField] private bool accessGranted = false;

            private float timeRemaining;
            private TextMeshProUGUI timerText;

            private void Start()
            {
                timeRemaining = eventDuration;
                timerText = transform.Find("TimerText")?.GetComponent<TextMeshProUGUI>();
            }

            private void Update()
            {
                if (eventActive && timeRemaining > 0)
                {
                    timeRemaining -= Time.deltaTime;
                    UpdateTimerDisplay();

                    if (timeRemaining <= 0)
                    {
                        EndEvent();
                    }
                }
            }

            private void UpdateTimerDisplay()
            {
                if (timerText != null)
                {
                    int minutes = Mathf.FloorToInt(timeRemaining / 60);
                    int seconds = Mathf.FloorToInt(timeRemaining % 60);
                    timerText.text = $"{minutes:00}:{seconds:00} remaining";
                }
            }

            private void EndEvent()
            {
                eventActive = false;
                if (timerText != null)
                {
                    timerText.text = "Event Ended";
                }

                var button = GetComponentInChildren<Button>();
                if (button != null)
                {
                    button.interactable = false;
                    var buttonText = button.GetComponentInChildren<TextMeshProUGUI>();
                    if (buttonText != null)
                    {
                        buttonText.text = "Event Ended";
                    }
                }
            }

            public void GrantAccess()
            {
                if (eventActive)
                {
                    accessGranted = true;
                    Debug.Log("Event access granted!");

                    var statusText = transform.Find("StatusText")?.GetComponent<TextMeshProUGUI>();
                    if (statusText != null)
                    {
                        statusText.text = "Event access granted!";
                    }
                }
            }

            public bool HasAccess() => accessGranted && eventActive;
        }

        // Screen Shop Controller Component
        public class ScreenShopController : MonoBehaviour
        {
            [SerializeField] private bool isVisible = true;

            private void Start()
            {
                var closeButton = transform.Find("CloseButton")?.GetComponent<Button>();
                if (closeButton != null)
                {
                    closeButton.onClick.AddListener(CloseShop);
                }

                SetupShopContent();
            }

            private void SetupShopContent()
            {
                var scrollView = GetComponentInChildren<ScrollRect>();
                if (scrollView != null)
                {
                    var content = scrollView.content;
                    // Add shop items to content area
                    // This would be similar to ShopController.CreateItemUI()
                }
            }

            public void CloseShop()
            {
                gameObject.SetActive(false);
                isVisible = false;
            }

            public void OpenShop()
            {
                gameObject.SetActive(true);
                isVisible = true;
            }

            public bool IsVisible() => isVisible;
        }

        #endregion

        #region Validation and Logging

        private bool ValidatePayPalSetup()
        {
            // Only use ScriptableObject approach - no EditorPrefs
            var creatorData = Resources.Load<U3DCreatorData>("U3DCreatorData");
            string paypalEmail = (creatorData != null) ? creatorData.PayPalEmail : "";

            if (string.IsNullOrEmpty(paypalEmail))
            {
                EditorUtility.DisplayDialog(
                    "PayPal Setup Required",
                    "Please configure your PayPal email in the Setup tab before creating monetization tools.\n\n" +
                    "This enables the dual transaction system where you keep 95% of earnings.",
                    "OK"
                );
                return false;
            }
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