﻿using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace U3D.Editor
{
    public class ProjectToolsTab : ICreatorTab
    {
        public string TabName => "Project Tools";
        public bool IsComplete => false;
        public System.Action<int> OnRequestTabSwitch { get; set; }

        private List<IToolCategory> categories;
        private int selectedCategoryIndex = 0;
        private string searchText = "";
        private Vector2 categoryScrollPosition;

        private GUIStyle subtabButtonStyle;
        private GUIStyle activeSubtabButtonStyle;
        private bool stylesInitialized = false;

        public void Initialize()
        {
            InitializeToolCategories();
        }

        private void InitializeStyles()
        {
            if (stylesInitialized) return;

            try
            {
                // Only create styles if EditorStyles is ready
                if (EditorStyles.miniButton == null) return;

                // Match ONLY the color settings from your main window
                subtabButtonStyle = new GUIStyle(EditorStyles.miniButton)
                {
                    normal = {
                textColor = Color.white
            },
                    hover = {
                textColor = Color.white
            },
                    onNormal = {
                textColor = Color.white
            }
                };

                activeSubtabButtonStyle = new GUIStyle(EditorStyles.miniButtonMid)
                {
                    normal = {
                textColor = Color.white
            },
                    hover = {
                textColor = Color.white
            },
                    onNormal = {
                textColor = Color.white
            },
                    fontStyle = FontStyle.Bold
                };

                stylesInitialized = true;
            }
            catch (System.Exception)
            {
                // EditorStyles not ready yet, will try again next DrawTab() call
                return;
            }
        }

        private void InitializeToolCategories()
        {
            categories = new List<IToolCategory>
            {
                new InteractionToolsCategory(),
                new MonetizationToolsCategory(),
                new SystemsToolsCategory(),
                new MediaToolsCategory(),
                //new MigrationToolsCategory()
            };

            // Pass navigation callback to all categories
            foreach (var category in categories)
            {
                category.OnRequestTabSwitch = OnRequestTabSwitch;
            }
        }

        public void DrawTab()
        {
            InitializeStyles();

            // ✅ FIX: Ensure navigation callbacks are always set
            foreach (var category in categories)
            {
                category.OnRequestTabSwitch = OnRequestTabSwitch;
            }

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Search Tools:", GUILayout.Width(80));
            searchText = EditorGUILayout.TextField(searchText);
            if (GUILayout.Button("Clear", GUILayout.Width(50)))
            {
                searchText = "";
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(10);

            // Responsive subtab layout - wrap buttons when window is narrow
            float windowWidth = EditorGUIUtility.currentViewWidth;
            bool useVerticalLayout = windowWidth < 500f; // Threshold for wrapping

            if (useVerticalLayout)
            {
                // Vertical layout for narrow windows
                for (int i = 0; i < categories.Count; i++)
                {
                    bool isSelected = selectedCategoryIndex == i;
                    bool newSelection = GUILayout.Toggle(isSelected, categories[i].CategoryName,
                        isSelected ? activeSubtabButtonStyle : subtabButtonStyle);

                    if (newSelection && !isSelected)
                    {
                        selectedCategoryIndex = i;
                        // NEW: Refresh MonetizationToolsCategory when switching to it
                        RefreshCategoryIfNeeded(i);
                    }
                }
            }
            else
            {
                // Horizontal layout for wider windows
                EditorGUILayout.BeginHorizontal();
                for (int i = 0; i < categories.Count; i++)
                {
                    bool isSelected = selectedCategoryIndex == i;
                    bool newSelection = GUILayout.Toggle(isSelected, categories[i].CategoryName,
                        isSelected ? activeSubtabButtonStyle : subtabButtonStyle);

                    if (newSelection && !isSelected)
                    {
                        selectedCategoryIndex = i;
                        // NEW: Refresh MonetizationToolsCategory when switching to it
                        RefreshCategoryIfNeeded(i);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(10);

            categoryScrollPosition = EditorGUILayout.BeginScrollView(categoryScrollPosition);

            if (selectedCategoryIndex >= 0 && selectedCategoryIndex < categories.Count)
            {
                if (string.IsNullOrEmpty(searchText))
                {
                    categories[selectedCategoryIndex].DrawCategory();
                }
                else
                {
                    DrawFilteredTools();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        // NEW: Method to refresh specific categories when switching to them
        private void RefreshCategoryIfNeeded(int categoryIndex)
        {
            if (categoryIndex >= 0 && categoryIndex < categories.Count)
            {
                var category = categories[categoryIndex];

                // Refresh MonetizationToolsCategory to check current PayPal status
                if (category is MonetizationToolsCategory monetizationCategory)
                {
                    monetizationCategory.RefreshPayPalConfiguration();
                }
            }
        }

        private void DrawFilteredTools()
        {
            EditorGUILayout.LabelField($"Search Results for \"{searchText}\"", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);

            bool foundAny = false;
            foreach (var category in categories)
            {
                var matchingTools = category.GetTools().Where(tool =>
                    tool.title.IndexOf(searchText, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tool.description.IndexOf(searchText, System.StringComparison.OrdinalIgnoreCase) >= 0
                ).ToList();

                if (matchingTools.Count > 0)
                {
                    foundAny = true;
                    EditorGUILayout.LabelField($"{category.CategoryName}", EditorStyles.boldLabel);
                    foreach (var tool in matchingTools)
                    {
                        DrawResponsiveTool(tool);
                    }
                    EditorGUILayout.Space(10);
                }
            }

            if (!foundAny)
            {
                EditorGUILayout.HelpBox($"No tools found matching \"{searchText}\"", MessageType.Info);
            }
        }

        // Copy CheckFixTab's DrawOptimizationTool method exactly - NO width detection
        private void DrawResponsiveTool(CreatorTool tool)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(tool.title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(tool.description, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();

            bool canExecute = CanExecuteTool(tool);
            EditorGUI.BeginDisabledGroup(!canExecute);

            if (GUILayout.Button("Apply", GUILayout.Width(80), GUILayout.Height(35)))
            {
                tool.action?.Invoke();
            }

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();

            if (tool.requiresSelection && Selection.activeGameObject == null)
            {
                EditorGUILayout.LabelField("Select an object", EditorStyles.centeredGreyMiniLabel);
            }
            else if (tool.title == "Make Throwable" && !HasGrabbableComponent())
            {
                EditorGUILayout.LabelField("Select a Grabbable object", EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);
        }

        // Public method for tool categories - exact copy of CheckFixTab pattern with special handling for Throwable
        public static void DrawCategoryTool(CreatorTool tool)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(tool.title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(tool.description, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();

            bool canExecute = CanExecuteTool(tool);
            EditorGUI.BeginDisabledGroup(!canExecute);

            if (GUILayout.Button("Apply", GUILayout.Width(80), GUILayout.Height(35)))
            {
                tool.action?.Invoke();
            }

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();

            if (tool.requiresSelection && Selection.activeGameObject == null)
            {
                EditorGUILayout.LabelField("Select an object", EditorStyles.centeredGreyMiniLabel);
            }
            else if (tool.title == "Make Throwable" && !HasGrabbableComponent())
            {
                EditorGUILayout.LabelField("Select a Grabbable object", EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);
        }

        private static bool CanExecuteTool(CreatorTool tool)
        {
            // Special handling for Make Throwable
            if (tool.title == "Make Throwable")
            {
                return Selection.activeGameObject != null && HasGrabbableComponent();
            }

            // Default handling
            return !tool.requiresSelection || Selection.activeGameObject != null;
        }

        private static bool HasGrabbableComponent()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null) return false;

            return selected.GetComponent<U3DGrabbableNear>() != null ||
                   selected.GetComponent<U3DGrabbableFar>() != null;
        }
    }
}