using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace U3D
{
    /// <summary>
    /// Central style helper for all U3D-created UI.
    /// Every method either sets a small, explicit subset of values or strips a sprite.
    /// Anything not explicitly set here is left at Unity's default.
    ///
    /// The goal of this helper is to keep U3D tool-created UI visually consistent
    /// by routing every tool through the same style methods, rather than hardcoding
    /// font sizes and colors in each tool. See U3D-UI-Style-Spec.md for the rules
    /// this helper encodes and when to deviate.
    /// </summary>
    public static class U3DUIStyle
    {
        // ───────────────────────────────────────────
        // Text Colors
        // ───────────────────────────────────────────

        /// <summary>Dark gray used for all title, body, status, and button text. Reads well against Unity's default panel.</summary>
        public static readonly Color32 TextColor = new Color32(50, 50, 50, 255);

        /// <summary>Muted gray used for input field placeholder text.</summary>
        public static readonly Color32 PlaceholderColor = new Color32(150, 150, 150, 128);

        // ───────────────────────────────────────────
        // Font Sizes
        // ───────────────────────────────────────────

        public const float TitleFontSize = 16f;
        public const float BodyFontSize = 18f;
        public const float StatusFontSize = 10f;
        public const float ButtonFontSize = 14f;

        // ───────────────────────────────────────────
        // Worldspace Canvas Defaults
        // ───────────────────────────────────────────

        /// <summary>Default scale for worldspace UI canvases created via U3D tools.</summary>
        public const float WorldspaceCanvasScale = 0.01f;

        /// <summary>Default sizeDelta for a single-purpose worldspace UI (like the Worldspace UI sign tool).</summary>
        public static readonly Vector2 WorldspaceSingleElementSize = new Vector2(180, 100);

        // ───────────────────────────────────────────
        // Panel Styling
        // ───────────────────────────────────────────

        /// <summary>
        /// Strip the sprite off a panel or button Image so it renders as a flat rectangle.
        /// Unity's default DefaultControls.CreatePanel with an empty Resources struct already
        /// produces this result, but calling StripSprite makes the intent explicit and also
        /// handles cases where a sprite was assigned elsewhere.
        /// </summary>
        public static void StripSprite(GameObject target)
        {
            if (target == null) return;
            var image = target.GetComponent<Image>();
            if (image != null)
                image.sprite = null;
        }

        /// <summary>
        /// Apply the U3D panel style to an already-created panel GameObject.
        /// Currently this means: strip the sprite so corners are square, and leave the
        /// Image color at Unity's default. Does not touch RectTransform, layout, or children.
        /// </summary>
        public static void ApplyPanelStyle(GameObject panel)
        {
            StripSprite(panel);
        }

        /// <summary>
        /// Apply the U3D button style to an already-created button GameObject.
        /// Strips the button's background sprite, leaves Unity default color and hover/pressed/disabled
        /// states alone, and styles the child TextMeshPro label if one is present.
        /// </summary>
        public static void ApplyButtonStyle(GameObject button, string label = null)
        {
            if (button == null) return;

            StripSprite(button);

            var buttonText = button.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                if (label != null)
                    buttonText.text = label;
                ApplyButtonTextStyle(buttonText);
            }
        }

        // ───────────────────────────────────────────
        // Text Styling
        // ───────────────────────────────────────────

        /// <summary>Title text: 16pt, dark gray, center-aligned, not raycast-blocking.</summary>
        public static void ApplyTitleStyle(TextMeshProUGUI text)
        {
            if (text == null) return;
            text.fontSize = TitleFontSize;
            text.color = TextColor;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
        }

        /// <summary>Body/price text: 18pt, dark gray, center-aligned, not raycast-blocking.</summary>
        public static void ApplyBodyStyle(TextMeshProUGUI text)
        {
            if (text == null) return;
            text.fontSize = BodyFontSize;
            text.color = TextColor;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
        }

        /// <summary>Status text: 10pt, dark gray, center-aligned, not raycast-blocking.</summary>
        public static void ApplyStatusStyle(TextMeshProUGUI text)
        {
            if (text == null) return;
            text.fontSize = StatusFontSize;
            text.color = TextColor;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
        }

        /// <summary>Button label text: 14pt, dark gray, center-aligned. Leaves raycastTarget untouched so the button can receive clicks via its label.</summary>
        public static void ApplyButtonTextStyle(TextMeshProUGUI text)
        {
            if (text == null) return;
            text.fontSize = ButtonFontSize;
            text.color = TextColor;
            text.alignment = TextAlignmentOptions.Center;
        }

        /// <summary>Placeholder text for TMP_InputField: muted gray. Font size is left at whatever the input field picked.</summary>
        public static void ApplyPlaceholderStyle(TextMeshProUGUI placeholder)
        {
            if (placeholder == null) return;
            placeholder.color = PlaceholderColor;
        }

        // ───────────────────────────────────────────
        // Combined Panel + Title Builder
        // ───────────────────────────────────────────

        /// <summary>
        /// Create a styled header (panel with title text) anchored to the top of a parent container.
        /// Returns the header GameObject so callers can anchor additional content against it if needed.
        /// Replaces the per-tool CreateCleanHeaderUI methods that were duplicated across tool categories.
        /// </summary>
        public static GameObject CreateHeader(GameObject parent, string title)
        {
            if (parent == null) return null;

            var uiResources = new DefaultControls.Resources();
            GameObject header = DefaultControls.CreatePanel(uiResources);
            header.name = "Header";
            header.transform.SetParent(parent.transform, false);

            ApplyPanelStyle(header);

            var headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 0.8f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.offsetMin = Vector2.zero;
            headerRect.offsetMax = Vector2.zero;

            var tmpResources = new TMP_DefaultControls.Resources();
            GameObject titleTextObj = TMP_DefaultControls.CreateText(tmpResources);
            titleTextObj.name = "Title";
            titleTextObj.transform.SetParent(header.transform, false);

            var titleRect = titleTextObj.GetComponent<RectTransform>();
            titleRect.anchorMin = Vector2.zero;
            titleRect.anchorMax = Vector2.one;
            titleRect.offsetMin = new Vector2(10f, 0f);
            titleRect.offsetMax = new Vector2(-10f, 0f);

            var titleTMP = titleTextObj.GetComponent<TextMeshProUGUI>();
            if (titleTMP != null)
            {
                titleTMP.text = title;
                ApplyTitleStyle(titleTMP);
            }

            return header;
        }

        /// <summary>
        /// Create a styled status text anchored to the bottom of a parent container.
        /// Returns the TextMeshProUGUI so callers can assign it to controllers that update it at runtime.
        /// Replaces the per-tool CreateCleanStatusText methods that were duplicated across tool categories.
        /// </summary>
        public static TextMeshProUGUI CreateStatusText(GameObject parent, string initialText = "")
        {
            if (parent == null) return null;

            var tmpResources = new TMP_DefaultControls.Resources();
            GameObject statusTextObj = TMP_DefaultControls.CreateText(tmpResources);
            statusTextObj.name = "StatusText";
            statusTextObj.transform.SetParent(parent.transform, false);

            var statusRect = statusTextObj.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0f, 0f);
            statusRect.anchorMax = new Vector2(1f, 0.15f);
            statusRect.offsetMin = new Vector2(10f, 5f);
            statusRect.offsetMax = new Vector2(-10f, -5f);

            var statusTMP = statusTextObj.GetComponent<TextMeshProUGUI>();
            if (statusTMP != null)
            {
                statusTMP.text = initialText;
                ApplyStatusStyle(statusTMP);
            }

            return statusTMP;
        }
    }
}