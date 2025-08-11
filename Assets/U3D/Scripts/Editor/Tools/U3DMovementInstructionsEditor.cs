using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;

namespace U3D.Editor
{
    /// <summary>
    /// Editor script to add movement instructions UI via context menu
    /// Dynamically reads key bindings from U3D_PlayerController prefab
    /// </summary>
    public static class U3DMovementInstructionsEditor
    {
        [MenuItem("GameObject/U3D/Add Movement Instructions", false, 10)]
        private static void AddMovementInstructions()
        {
            CreateMovementInstructionsUI();
        }

        private static void CreateMovementInstructionsUI()
        {
            // Initialize both resource types for UI creation
            var uiResources = new DefaultControls.Resources();
            var tmpResources = new TMP_DefaultControls.Resources();

            // Create main canvas
            GameObject canvas = CreateCanvas();

            // Create main panel
            GameObject mainPanel = DefaultControls.CreatePanel(uiResources);
            mainPanel.name = "MovementInstructionsPanel";
            mainPanel.transform.SetParent(canvas.transform, false);

            // Configure main panel
            ConfigureMainPanel(mainPanel);

            // Create title
            GameObject titleObject = CreateTitle(tmpResources, mainPanel.transform);

            // Create scroll view for instructions
            GameObject scrollView = DefaultControls.CreateScrollView(uiResources);
            scrollView.name = "InstructionsScrollView";
            scrollView.transform.SetParent(mainPanel.transform, false);
            ConfigureScrollView(scrollView, titleObject);

            // Get content area from scroll view
            Transform contentArea = scrollView.transform.Find("Viewport/Content");

            // Generate instruction text from player controller
            string instructionText = GenerateInstructionText();

            // Create instruction text object
            GameObject instructionTextObject = TMP_DefaultControls.CreateText(tmpResources);
            instructionTextObject.name = "InstructionText";
            instructionTextObject.transform.SetParent(contentArea, false);
            ConfigureInstructionText(instructionTextObject, instructionText);

            // Create close button
            GameObject closeButton = TMP_DefaultControls.CreateButton(tmpResources);
            closeButton.name = "CloseButton";
            closeButton.transform.SetParent(mainPanel.transform, false);
            ConfigureCloseButton(closeButton, mainPanel);

            // Select the created object in hierarchy
            Selection.activeGameObject = canvas;

            Debug.Log("✅ Movement Instructions UI created successfully!");
        }

        private static GameObject CreateCanvas()
        {
            GameObject canvasGO = new GameObject("MovementInstructionsCanvas");
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100; // Ensure it appears above other UI

            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();

            return canvasGO;
        }

        private static void ConfigureMainPanel(GameObject panel)
        {
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.2f, 0.1f);
            panelRect.anchorMax = new Vector2(0.8f, 0.9f);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
        }

        private static GameObject CreateTitle(TMP_DefaultControls.Resources tmpResources, Transform parent)
        {
            GameObject titleObject = TMP_DefaultControls.CreateText(tmpResources);
            titleObject.name = "Title";
            titleObject.transform.SetParent(parent, false);

            // Configure title positioning
            RectTransform titleRect = titleObject.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0.85f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(20f, 0f);
            titleRect.offsetMax = new Vector2(-20f, -20f);

            // Configure title text
            TextMeshProUGUI titleTMP = titleObject.GetComponent<TextMeshProUGUI>();
            titleTMP.text = "Movement Controls";
            titleTMP.fontSize = 24f;
            titleTMP.fontStyle = FontStyles.Bold;
            titleTMP.color = new Color32(50, 50, 50, 255); // #323232
            titleTMP.alignment = TextAlignmentOptions.Center;

            return titleObject;
        }

        private static void ConfigureScrollView(GameObject scrollView, GameObject titleObject)
        {
            RectTransform scrollRect = scrollView.GetComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0f, 0.1f);
            scrollRect.anchorMax = new Vector2(1f, 0.85f);
            scrollRect.offsetMin = new Vector2(20f, 60f); // Leave space for close button
            scrollRect.offsetMax = new Vector2(-20f, -10f);
        }

        private static void ConfigureInstructionText(GameObject textObject, string instructionText)
        {
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.pivot = new Vector2(0f, 1f);

            TextMeshProUGUI textTMP = textObject.GetComponent<TextMeshProUGUI>();
            textTMP.text = instructionText;
            textTMP.fontSize = 16f;
            textTMP.color = new Color32(50, 50, 50, 255); // #323232
            textTMP.alignment = TextAlignmentOptions.TopLeft;
            textTMP.textWrappingMode = TextWrappingModes.Normal;

            // Set content size to fit text
            ContentSizeFitter sizeFitter = textObject.AddComponent<ContentSizeFitter>();
            sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Position text at top of content area
            textRect.offsetMin = new Vector2(20f, 0f);
            textRect.offsetMax = new Vector2(-20f, 0f);
            textRect.anchoredPosition = Vector2.zero;
        }

        private static void ConfigureCloseButton(GameObject button, GameObject panel)
        {
            RectTransform buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.4f, 0.02f);
            buttonRect.anchorMax = new Vector2(0.6f, 0.08f);
            buttonRect.offsetMin = Vector2.zero;
            buttonRect.offsetMax = Vector2.zero;

            // Configure button text
            Transform buttonTextTransform = button.transform.Find("Text (TMP)");
            if (buttonTextTransform != null)
            {
                TextMeshProUGUI buttonText = buttonTextTransform.GetComponent<TextMeshProUGUI>();
                buttonText.text = "Close";
                buttonText.color = new Color32(50, 50, 50, 255); // #323232
            }

            // Add close functionality
            Button buttonComponent = button.GetComponent<Button>();
            buttonComponent.onClick.AddListener(() => {
                if (panel != null)
                {
                    GameObject.DestroyImmediate(panel.transform.parent.gameObject); // Destroy entire canvas
                }
            });
        }

        private static string GenerateInstructionText()
        {
            // Look for U3D_PlayerController prefab to get actual key bindings
            string[] prefabGuids = AssetDatabase.FindAssets("U3D_PlayerController t:GameObject");
            InputActionAsset inputActions = null;

            if (prefabGuids.Length > 0)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[0]);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

                if (prefab != null)
                {
                    // Try to find PlayerInput component and get InputActionAsset
                    PlayerInput playerInput = prefab.GetComponent<PlayerInput>();
                    if (playerInput != null && playerInput.actions != null)
                    {
                        inputActions = playerInput.actions;
                    }
                }
            }

            if (inputActions != null)
            {
                return GenerateInstructionsFromInputActions(inputActions);
            }
            else
            {
                return GenerateDefaultInstructions();
            }
        }

        private static string GenerateInstructionsFromInputActions(InputActionAsset inputActions)
        {
            var instructions = new System.Text.StringBuilder();
            instructions.AppendLine("MOVEMENT CONTROLS");
            instructions.AppendLine("================\n");

            // Find Player action map
            var playerMap = inputActions.FindActionMap("Player");
            if (playerMap == null)
            {
                return GenerateDefaultInstructions();
            }

            // Define the actions we want to display and their descriptions
            var actionDescriptions = new System.Collections.Generic.Dictionary<string, string>()
            {
                {"Move", "Move"},
                {"Look", "Look Around"},
                {"Jump", "Jump"},
                {"Sprint", "Sprint"},
                {"Crouch", "Crouch"},
                {"Fly", "Fly Mode"},
                {"AutoRun", "Auto Run"},
                {"Interact", "Interact"},
                {"PerspectiveSwitch", "Camera Zoom"},
                {"StrafeLeft", "Strafe Left"},
                {"StrafeRight", "Strafe Right"},
                {"MouseLeft", "Mouse Left"},
                {"MouseRight", "Mouse Right"},
                {"Zoom", "Zoom"},
                {"Teleport", "Teleport"}
            };

            foreach (var actionDesc in actionDescriptions)
            {
                var action = playerMap.FindAction(actionDesc.Key);
                if (action != null)
                {
                    string keys = GetKeysForAction(action);
                    if (!string.IsNullOrEmpty(keys))
                    {
                        instructions.AppendLine($"{actionDesc.Value}: {keys}");
                    }
                }
            }

            // Add special movement combinations
            instructions.AppendLine("\nSPECIAL MOVEMENT");
            instructions.AppendLine("================");
            instructions.AppendLine("Move Forward: Left + Right Mouse Button");
            instructions.AppendLine("AAA Camera Mode: Enhanced mouse look and movement");
            instructions.AppendLine("Auto-Run Toggle: Num Lock");

            return instructions.ToString();
        }

        private static string GetKeysForAction(InputAction action)
        {
            var keys = new System.Collections.Generic.List<string>();

            foreach (var binding in action.bindings)
            {
                if (binding.isComposite) continue;

                string displayString = InputControlPath.ToHumanReadableString(
                    binding.effectivePath,
                    InputControlPath.HumanReadableStringOptions.OmitDevice);

                if (!string.IsNullOrEmpty(displayString))
                {
                    // Clean up the display string
                    displayString = displayString.Replace("Up Arrow", "↑")
                                                .Replace("Down Arrow", "↓")
                                                .Replace("Left Arrow", "←")
                                                .Replace("Right Arrow", "→")
                                                .Replace("Space", "Space Bar")
                                                .Replace("Left Shift", "Shift")
                                                .Replace("Left Ctrl", "Ctrl")
                                                .Replace("Mouse Delta", "Mouse")
                                                .Replace("Scroll Y", "Mouse Wheel");

                    if (!keys.Contains(displayString))
                    {
                        keys.Add(displayString);
                    }
                }
            }

            return string.Join(" | ", keys);
        }

        private static string GenerateDefaultInstructions()
        {
            return @"MOVEMENT CONTROLS
================

Move: W A S D | Arrow Keys
Look Around: Mouse
Jump: Space Bar
Sprint: Left Shift
Crouch: C
Fly Mode: F
Auto Run: Num Lock
Interact: E
Camera Zoom: Mouse Wheel
Strafe Left: Q
Strafe Right: E

SPECIAL MOVEMENT
================
Move Forward: Left + Right Mouse Button
AAA Camera Mode: Enhanced mouse look and movement
Auto-Run Toggle: Num Lock

MOUSE CONTROLS
================
Left Mouse: Primary Action
Right Mouse: Secondary Action / Camera Look
Middle Mouse: Zoom / Perspective Switch
Both Mouse Buttons: Move Forward

Note: Key bindings are read from the U3D_PlayerController prefab.
If this displays default values, ensure the prefab is properly configured.";
        }
    }
}