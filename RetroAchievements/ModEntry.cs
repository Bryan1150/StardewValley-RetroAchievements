using System;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Constants;

namespace RetroAchievements
{
    /// <summary>The mod entry point.</summary>
    internal sealed class ModEntry : Mod
    {
        private HashSet<int> previousAchievements = new HashSet<int>();

        // Only these mods are allowed; everything else is blocked
        private readonly HashSet<string> allowedMods = new HashSet<string>
        {
            "Pathoschild.SMAPI",       // SMAPI (always required)
            "CJBok.CheatsMenu"      
        };

        public override void Entry(IModHelper helper)
        {
            if (!IsWhitelistedModsOnly())
            {
                Monitor.Log("Unapproved mods detected! This mod will not run.", LogLevel.Warn);
                return; // Stop execution
            }

            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked; // Runs multiple times per second
        }

        private bool IsWhitelistedModsOnly()
        {
            var installedMods = Helper.ModRegistry.GetAll().Select(mod => mod.Manifest.UniqueID).ToList();

            // Ensure every installed mod is in the allowed list
            foreach (string modId in installedMods)
            {
                if (!allowedMods.Contains(modId))
                {
                    Monitor.Log($"Blocked mod detected: {modId}", LogLevel.Warn);
                    return false;
                }
            }

            return true;
        }

        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            // Store achievements the player has already unlocked
            Monitor.Log("All installed mods are whitelisted. Running normally.", LogLevel.Info);
            previousAchievements = new HashSet<int>(Game1.player.achievements);
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            // Run check once every second (game runs at 60 ticks per second)
            if (e.IsMultipleOf(60))
            {
                CheckForNewAchievements();
            }
        }

        private void CheckForNewAchievements()
        {
            foreach (int achievementId in Game1.player.achievements)
            {
                if (!previousAchievements.Contains(achievementId))
                {
                    string achievementName = Game1.achievements.ContainsKey(achievementId)
                        ? Game1.achievements[achievementId].Split('^')[0]  // Extract name from data
                        : "Unknown Achievement";

                    Monitor.Log($"Achievement unlocked: {achievementId} - {achievementName}", LogLevel.Info);
                    previousAchievements.Add(achievementId);


                    // TODO: Send achievement to the RetroAchievements API here
                }
            }
        }
    }
}