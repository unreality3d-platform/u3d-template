using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace U3D
{
    /// <summary>
    /// Controller for Screen Shop monetization tool.
    /// Manages overlay shop interface with multiple purchasable items.
    /// </summary>
    public class ScreenShopController : MonoBehaviour
    {
        [System.Serializable]
        public class ScreenShopItem
        {
            public string itemName = "Digital Item";
            public string description = "Screen shop item";
            public float price = 8.00f;
            public Sprite icon;
        }

        [Header("Shop Configuration")]
        [SerializeField] private bool isVisible = true;
        [SerializeField] private List<ScreenShopItem> shopItems = new List<ScreenShopItem>();
        [SerializeField] private Transform contentContainer;
        [SerializeField] private bool autoSetupItems = true;

        private Button closeButton;

        private void Start()
        {
            // Setup close button
            closeButton = transform.Find("CloseButton")?.GetComponent<Button>();
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
                contentContainer = scrollView.content;

                if (autoSetupItems)
                {
                    SetupDefaultScreenItems();
                }
                CreateScreenShopItems();
            }
        }

        private void SetupDefaultScreenItems()
        {
            if (shopItems.Count == 0)
            {
                shopItems.Add(new ScreenShopItem { itemName = "Digital Collectible", description = "Limited edition digital item", price = 12.00f });
                shopItems.Add(new ScreenShopItem { itemName = "VIP Access", description = "Premium features unlock", price = 20.00f });
                shopItems.Add(new ScreenShopItem { itemName = "Creator Support", description = "Direct creator support", price = 8.00f });
            }
        }

        private void CreateScreenShopItems()
        {
            if (contentContainer == null)
            {
                Debug.LogWarning("Screen Shop Controller: No content container found.");
                return;
            }

            foreach (var item in shopItems)
            {
                CreateScreenShopItemButton(item);
            }
        }

        private void CreateScreenShopItemButton(ScreenShopItem item)
        {
            var tmpResources = new TMP_DefaultControls.Resources();
            var uiResources = new DefaultControls.Resources();

            // Create item panel
            GameObject itemPanel = DefaultControls.CreatePanel(uiResources);
            itemPanel.name = $"ScreenItem_{item.itemName}";
            itemPanel.transform.SetParent(contentContainer, false);

            var panelRect = itemPanel.GetComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(350, 60);

            // Create purchase button
            GameObject itemButton = TMP_DefaultControls.CreateButton(tmpResources);
            itemButton.name = "PurchaseButton";
            itemButton.transform.SetParent(itemPanel.transform, false);

            var buttonRect = itemButton.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.7f, 0.2f);
            buttonRect.anchorMax = new Vector2(0.95f, 0.8f);
            buttonRect.offsetMin = Vector2.zero;
            buttonRect.offsetMax = Vector2.zero;

            // Add PayPalDualTransaction to the button
            var dualTransaction = itemButton.AddComponent<PayPalDualTransaction>();
            dualTransaction.SetItemDetails(item.itemName, item.description, item.price);
            dualTransaction.SetVariableAmount(false);

            // Assign UI references
            var purchaseButton = itemButton.GetComponent<Button>();
            dualTransaction.AssignEssentialReferences(purchaseButton, null);

            // Set PayPal email from centralized system
            var creatorData = Resources.Load<U3DCreatorData>("U3DCreatorData");
            if (creatorData != null && !string.IsNullOrEmpty(creatorData.PayPalEmail))
            {
                dualTransaction.SetCreatorPayPalEmail(creatorData.PayPalEmail);
            }

            var buttonText = itemButton.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.text = $"${item.price:F2}";
                buttonText.color = new Color32(50, 50, 50, 255); // #323232
            }

            // Create item info text
            GameObject itemInfo = TMP_DefaultControls.CreateText(tmpResources);
            itemInfo.name = "ItemInfo";
            itemInfo.transform.SetParent(itemPanel.transform, false);

            var infoRect = itemInfo.GetComponent<RectTransform>();
            infoRect.anchorMin = new Vector2(0.05f, 0.1f);
            infoRect.anchorMax = new Vector2(0.65f, 0.9f);
            infoRect.offsetMin = Vector2.zero;
            infoRect.offsetMax = Vector2.zero;

            var infoText = itemInfo.GetComponent<TextMeshProUGUI>();
            if (infoText != null)
            {
                infoText.text = $"{item.itemName}\n{item.description}";
                infoText.fontSize = 10;
                infoText.color = new Color32(50, 50, 50, 255); // #323232
                infoText.raycastTarget = false;
            }
        }

        /// <summary>
        /// Close the shop overlay
        /// </summary>
        public void CloseShop()
        {
            gameObject.SetActive(false);
            isVisible = false;
        }

        /// <summary>
        /// Open the shop overlay
        /// </summary>
        public void OpenShop()
        {
            gameObject.SetActive(true);
            isVisible = true;
        }

        /// <summary>
        /// Check if shop is currently visible
        /// </summary>
        public bool IsVisible() => isVisible;

        /// <summary>
        /// Add a new shop item dynamically
        /// </summary>
        public void AddScreenShopItem(string name, string description, float price)
        {
            shopItems.Add(new ScreenShopItem { itemName = name, description = description, price = price });
            if (contentContainer != null)
            {
                CreateScreenShopItemButton(shopItems[shopItems.Count - 1]);
            }
        }

        /// <summary>
        /// Clear all shop items
        /// </summary>
        public void ClearShopItems()
        {
            shopItems.Clear();
            if (contentContainer != null)
            {
                for (int i = contentContainer.childCount - 1; i >= 0; i--)
                {
                    DestroyImmediate(contentContainer.GetChild(i).gameObject);
                }
            }
        }

        /// <summary>
        /// Get current shop items
        /// </summary>
        public List<ScreenShopItem> GetShopItems()
        {
            return new List<ScreenShopItem>(shopItems);
        }

        /// <summary>
        /// Set the content container manually
        /// </summary>
        public void SetContentContainer(Transform container)
        {
            contentContainer = container;
        }

        /// <summary>
        /// Toggle shop visibility
        /// </summary>
        public void ToggleShop()
        {
            if (isVisible)
            {
                CloseShop();
            }
            else
            {
                OpenShop();
            }
        }
    }
}