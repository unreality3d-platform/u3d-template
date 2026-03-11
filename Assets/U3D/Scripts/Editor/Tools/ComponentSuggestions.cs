using System.Collections.Generic;
using UnityEngine;

namespace U3D.Editor
{
    /// <summary>
    /// Simple helper for suggesting U3D SDK tool replacements during asset cleanup
    /// </summary>
    public static class ComponentSuggestions
    {
        private static Dictionary<string, string> commonReplacements = new Dictionary<string, string>
        {
            // Interaction Systems
            {"grabbable", "Make Grabbable (Interactions)"},
            {"interactable", "Make Enter Trigger (Interactions)"},
            {"pickup", "Make Grabbable (Interactions)"},
            {"trigger", "Make Enter Trigger (Interactions)"},
            {"switch", "Make Enter Trigger (Interactions)"},
            {"door", "Make Enter Trigger (Interactions)"},
            {"collect", "Make Object Destroy Trigger (Interactions)"},
            {"click", "Add Click Trigger (Interactions)"},
            {"spawn", "Add Object Spawner (Interactions)"},
            {"climb", "Make Climbable (Interactions)"},
            {"grab", "Make Grabbable (Interactions)"},
            {"throw", "Make Throwable (Interactions)"},
            {"kick", "Make Kickable (Interactions)"},

            // Navigation
            {"portal", "Add Scene Portal (Interactions)"},
            {"teleport", "Add 1-Way Portal (Interactions)"},
            {"warp", "Add 1-Way Portal (Interactions)"},

            // Core Game Systems
            {"quest", "Add Quest System (Game Systems)"},
            {"mission", "Add Quest System (Game Systems)"},
            {"objective", "Add Quest System (Game Systems)"},
            {"inventory", "Add Inventory System (Game Systems)"},
            {"bag", "Add Inventory System (Game Systems)"},
            {"item", "Add Inventory System (Game Systems)"},
            {"dialogue", "Add Dialogue System (Game Systems)"},
            {"conversation", "Add Dialogue System (Game Systems)"},
            {"npc", "Add Dialogue System (Game Systems)"},
            {"timer", "Add Timer System (Game Systems)"},
            {"countdown", "Add Timer System (Game Systems)"},
            {"clock", "Add Timer System (Game Systems)"},
            {"progress", "Add Progress Bar (Game Systems)"},
            {"achievement", "Add Achievement / Award System (Game Systems)"},
            {"unlock", "Add Achievement / Award System (Game Systems)"},
            {"reward", "Add Achievement / Award System (Game Systems)"},
            {"score", "Add Scorable (Game Systems)"},
            {"scoreboard", "Add Scorable (Game Systems)"},
            {"checkpoint", "Add Checkpoint System (Game Systems)"},
            {"save", "Add Checkpoint System (Game Systems)"},
            {"respawn", "Add Checkpoint System (Game Systems)"},

            // Quiz
            {"quiz", "Add Quiz System (Game Systems)"},
            {"question", "Add Quiz System (Game Systems)"},

            // Media & Content
            {"audio", "Add Audio List (Media & Content)"},
            {"sound", "Add Audio List (Media & Content)"},
            {"music", "Add Audio List (Media & Content)"},
            {"video", "Add Video Player (Media & Content)"},
            {"gallery", "Add Image Gallery (Media & Content)"},
            {"image", "Add Image Gallery (Media & Content)"},
            {"guestbook", "Add Guestbook (Media & Content)"},
            {"message", "Add Guestbook (Media & Content)"},
            {"feedback", "Add Guestbook (Media & Content)"},
            {"ui", "Add Worldspace UI (Media & Content)"},
            {"canvas", "Add Worldspace UI (Media & Content)"},
            {"sign", "Add Worldspace UI (Media & Content)"},

            // Monetization
            {"shop", "Add Shop Object (Monetization)"},
            {"purchase", "Add Purchase Button (Monetization)"},
            {"tip", "Add Tip Jar (Monetization)"},
            {"donate", "Add Tip Jar (Monetization)"},
            {"gate", "Add Scene Gate (Monetization)"},
            {"ticket", "Add Event Gate (Monetization)"},
            {"event", "Add Event Gate (Monetization)"},
        };

        public static string GetSuggestionForGameObject(string gameObjectName)
        {
            if (string.IsNullOrEmpty(gameObjectName)) return "";

            string lowerName = gameObjectName.ToLower();

            foreach (var kvp in commonReplacements)
            {
                if (lowerName.Contains(kvp.Key))
                {
                    return kvp.Value;
                }
            }

            return "";
        }
    }
}