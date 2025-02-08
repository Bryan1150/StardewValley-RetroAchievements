using System;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Constants;
using RASharpIntegration.Network;
using xTile;

#nullable disable

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
                "CJBok.CheatsMenu",
                "SMAPI.ConsoleCommands",
                "Brylefi.RetroAchievements",
                "SMAPI.SaveBackup"
            };

        private RequestHeader _header;
        private HttpClient _client;

        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides methods for interacting with the modding API.</param>
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

        /// <summary>Logs in to the RetroAchievements API.</summary>
        /// <param name="user">The username.</param>
        /// <param name="pass">The password.</param>
        private async Task Login(string user, string pass)
        {
            _header.user = user;
            Monitor.Log($"Logging in {user} to {_header.host}...", LogLevel.Warn);

            try
            {
                ApiResponse<LoginResponse> api = await NetworkInterface.TryLogin(_client, _header, pass);

                if (!string.IsNullOrEmpty(api.Failure))
                {
                    Monitor.Log($"Unable to login ({api.Failure})", LogLevel.Warn);
                    return;
                }

                if (!api.Response.Success)
                {
                    Monitor.Log($"Unable to login ({api.Response.Error})", LogLevel.Warn);
                    return;
                }

                _header.token = api.Response.Token;
                Monitor.Log($"{user} has successfully logged in!", LogLevel.Warn);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Exception during login: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Checks if only whitelisted mods are installed.</summary>
        /// <returns>True if only whitelisted mods are installed, otherwise false.</returns>
        private bool IsWhitelistedModsOnly()
        {
            var installedMods = Helper.ModRegistry.GetAll().Select(mod => mod.Manifest.UniqueID).ToList();

            // Ensure every installed mod is in the allowed list
            foreach (string modId in installedMods)
            {
                Monitor.Log($"Mod detected: {modId}", LogLevel.Warn);
                if (!allowedMods.Contains(modId))
                {
                    Monitor.Log($"Blocked mod detected: {modId}", LogLevel.Warn);
                    return false;
                }
            }

            return true;
        }

        /// <summary>Event handler for when the save is loaded.</summary>
        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            // Store achievements the player has already unlocked
            Monitor.Log("All installed mods are whitelisted. Running normally.", LogLevel.Info);
            previousAchievements = new HashSet<int>(Game1.player.achievements);

            // Initialize the RetroAchievements API
            _client = new HttpClient();
            _header = new RequestHeader(
                host: "stage.retroachievements.org",
                game: 32123, // Replace with your game ID
                hardcore: false
            );
            _client.DefaultRequestHeaders.Add("User-Agent", $"StardewValleyRetroAchievements/1.0");
            // Perform login
            Task.Run(() => Login("Bryan1150", "hi")).Wait(); // Replace with your credentials
        }

        /// <summary>Event handler for when the game updates.</summary>
        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            // Run check once every second (game runs at 60 ticks per second)
            if (e.IsMultipleOf(60))
            {
                CheckForNewAchievements();
            }
        }

        /// <summary>Checks for new achievements and sends them to the RetroAchievements API.</summary>
        private async Task CheckForNewAchievements()
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

                    // Send achievement to the RetroAchievements API
                    ApiResponse<AwardAchievementResponse> api = await NetworkInterface.TryAwardAchievement(_client, _header, 483247);
                    if (!string.IsNullOrEmpty(api.Failure))
                    {
                        Monitor.Log($"Failed to unlock achievement ({api.Failure})", LogLevel.Warn);
                    }
                    else if (!api.Response.Success)
                    {
                        Monitor.Log($"Failed to unlock achievement ({api.Response.Error})", LogLevel.Warn);
                    }
                    else
                    {
                        Monitor.Log($"Achievement {achievementId} successfully unlocked on RetroAchievements!", LogLevel.Info);
                    }
                }
            }
        }
    }
}