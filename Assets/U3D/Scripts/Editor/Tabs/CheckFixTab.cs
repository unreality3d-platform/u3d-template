﻿using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace U3D.Editor
{
    public class CheckFixTab : ICreatorTab
    {
        public string TabName => "Check & Fix";
        public bool IsComplete { get; private set; }
        public System.Action<int> OnRequestTabSwitch { get; set; }

        private List<IValidationCategory> validationCategories;
        private List<CreatorTool> optimizationTools;
        private int selectedCategoryIndex = 0;
        private Dictionary<string, List<ValidationResult>> categoryResults;
        private bool analysisRunning = false;
        private Vector2 resultsScrollPosition;
        private Vector2 optimizationScrollPosition;

        public void Initialize()
        {
            InitializeValidationCategories();
            InitializeOptimizationTools();
            categoryResults = new Dictionary<string, List<ValidationResult>>();
        }

        private void InitializeValidationCategories()
        {
            validationCategories = new List<IValidationCategory>
            {
                new BuildSettingsValidation(),
                new AssetOptimizationValidation(),
                new PerformanceValidation(),
                new QualityValidation(),
                new ComponentValidation()
            };
        }

        private void InitializeOptimizationTools()
        {
            optimizationTools = new List<CreatorTool>
            {
                new CreatorTool("Optimize All Textures", "Batch optimize textures by type with compression settings", () => Debug.Log("Applied Texture Optimization")),
                new CreatorTool("Optimize All Audio", "Batch optimize audio files by usage type", () => Debug.Log("Applied Audio Optimization")),
                new CreatorTool("Find Duplicate Materials", "Locate and merge duplicate materials in project", () => Debug.Log("Applied Material Deduplication")),
                new CreatorTool("Extract Model Materials", "Extract materials from imported models for editing", () => Debug.Log("Applied Material Extraction")),
                new CreatorTool("Analyze Build Size", "Generate detailed build size report", () => Debug.Log("Applied Build Analysis")),
                new CreatorTool("Clean Unused Assets", "Remove unreferenced assets from project", () => Debug.Log("Applied Asset Cleanup")),
                new CreatorTool("Optimize Lighting", "Configure optimal light settings for WebGL", () => Debug.Log("Applied Lighting Optimization"))
            };
        }

        public void DrawTab()
        {
            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("Project Health Check", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Run analysis to find and fix issues that could affect your experience.", MessageType.Info);
            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();

            EditorGUI.BeginDisabledGroup(analysisRunning);
            if (GUILayout.Button("Analyze Project", GUILayout.Height(40)))
            {
                RunProjectAnalysis();
            }
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("Clear Results", GUILayout.Height(40), GUILayout.Width(100)))
            {
                categoryResults.Clear();
                IsComplete = false;
            }

            EditorGUILayout.EndHorizontal();

            if (analysisRunning)
            {
                EditorGUILayout.LabelField("🔍 Analyzing...", EditorStyles.boldLabel);
                return;
            }

            EditorGUILayout.Space(10);

            if (categoryResults.Any())
            {
                EditorGUILayout.BeginHorizontal();
                for (int i = 0; i < validationCategories.Count; i++)
                {
                    var category = validationCategories[i];
                    var buttonText = category.CategoryName;

                    if (categoryResults.ContainsKey(category.CategoryName))
                    {
                        var issues = categoryResults[category.CategoryName].Count(r => !r.passed);
                        if (issues > 0)
                        {
                            buttonText += $" ({issues})";
                        }
                        else
                        {
                            buttonText = "✓ " + buttonText;
                        }
                    }

                    var buttonStyle = selectedCategoryIndex == i ? EditorStyles.miniButtonMid : EditorStyles.miniButton;
                    if (GUILayout.Button(buttonText, buttonStyle))
                    {
                        selectedCategoryIndex = i;
                    }
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(10);

                if (selectedCategoryIndex < validationCategories.Count)
                {
                    var selectedCategory = validationCategories[selectedCategoryIndex];
                    if (categoryResults.ContainsKey(selectedCategory.CategoryName))
                    {
                        DrawCategoryResults(selectedCategory.CategoryName, categoryResults[selectedCategory.CategoryName]);
                    }
                }
            }

            // Add Optimization Tools Section
            EditorGUILayout.Space(20);
            EditorGUILayout.LabelField("Optimization Tools", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Optimize your project for better performance and smaller builds.", MessageType.Info);
            EditorGUILayout.Space(10);

            optimizationScrollPosition = EditorGUILayout.BeginScrollView(optimizationScrollPosition);

            foreach (var tool in optimizationTools)
            {
                DrawOptimizationTool(tool);
            }

            EditorGUILayout.EndScrollView();
        }

        private async void RunProjectAnalysis()
        {
            analysisRunning = true;
            categoryResults.Clear();

            try
            {
                foreach (var category in validationCategories)
                {
                    var results = await category.RunChecks();
                    categoryResults[category.CategoryName] = results;
                }

                var totalIssues = categoryResults.Values.SelectMany(r => r).Count(r => !r.passed);
                IsComplete = totalIssues == 0;
            }
            finally
            {
                analysisRunning = false;
            }
        }

        private void DrawCategoryResults(string categoryName, List<ValidationResult> results)
        {
            EditorGUILayout.LabelField($"Results for {categoryName}", EditorStyles.boldLabel);

            var issues = results.Where(r => !r.passed).ToList();
            var passed = results.Where(r => r.passed).ToList();

            if (issues.Any())
            {
                EditorGUILayout.LabelField($"⚠️ {issues.Count} issues found", EditorStyles.boldLabel);
                EditorGUILayout.Space(5);

                resultsScrollPosition = EditorGUILayout.BeginScrollView(resultsScrollPosition);

                foreach (var issue in issues)
                {
                    DrawValidationResult(issue);
                }

                EditorGUILayout.EndScrollView();
            }

            if (passed.Any())
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField($"✅ {passed.Count} checks passed", EditorStyles.boldLabel);
            }
        }

        private void DrawValidationResult(ValidationResult result)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            string severityIcon = result.severity switch
            {
                ValidationSeverity.Critical => "🛑",
                ValidationSeverity.Error => "❌",
                ValidationSeverity.Warning => "⚠️",
                _ => "ℹ️"
            };

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{severityIcon} {result.message}", EditorStyles.wordWrappedLabel);

            if (result.severity != ValidationSeverity.Info)
            {
                if (GUILayout.Button("Fix", GUILayout.Width(60), GUILayout.Height(25)))
                {
                    Debug.Log($"Auto-fixing: {result.message}");
                }
            }

            EditorGUILayout.EndHorizontal();

            if (result.affectedObjects.Any())
            {
                EditorGUILayout.LabelField($"Affects {result.affectedObjects.Count} objects", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3);
        }

        private void DrawOptimizationTool(CreatorTool tool)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(tool.title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(tool.description, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();

            if (GUILayout.Button("Apply", GUILayout.Width(80), GUILayout.Height(35)))
            {
                tool.action?.Invoke();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);
        }
    }
}