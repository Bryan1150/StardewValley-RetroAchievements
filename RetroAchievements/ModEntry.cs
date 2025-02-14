using System;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Constants;
using RASharpIntegration.Network;
using xTile;

#nullable disable

namespace RetroAchievements
{
    /// <summary>The mod entry point.</summary>
    internal sealed class ModEntry : Mod
    {
        public sealed class ModConfig
        {
            public string Username { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public bool Hardcore { get; set; } = true; // Add this line
        }
        private ModConfig Config;

        private const string Host = "stage.retroachievements.org";
        private const string UserAgent = "StardewValleyRetroAchievements/1.0";
        private const int GameId = 32123;
        private string Username;
        private string Password;

        private HashSet<int> previousAchievements = new HashSet<int>();

        // Only these mods are allowed; everything else is blocked
        private readonly HashSet<string> allowedMods = new HashSet<string>
            {
                "Pathoschild.SMAPI",       // SMAPI (always required)
                "Brylefi.customAchievements",
                "CJBok.CheatsMenu",
                "SMAPI.ConsoleCommands",
                "Brylefi.RetroAchievements",
                "SMAPI.SaveBackup",
                "spacechase0.GenericModConfigMenu",
                "Pathoschild.ContentPatcher"
            };

        private RequestHeader _header;
        private HttpClient _client;

        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides methods for interacting with the modding API.</param>
        public override void Entry(IModHelper helper)
        {
            Config = Helper.ReadConfig<ModConfig>();
            if (!IsWhitelistedModsOnly())
            {
                Monitor.Log("Unapproved mods detected! This mod will not run.", LogLevel.Warn);
                return; // Stop execution
            }

            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked; // Runs multiple times per second
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            //helper.Events.GameLoop.DayStarted += OnDayStarted;
        }

        //Function to send chat messages in-game
        public static void SendChatMessage(string message, Color color)
        {
            Game1.chatBox.addMessage(message, color);
        }

        /// <summary>Event handler for when the game updates.</summary>
        private async void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            // Run check once every second (game runs at 60 ticks per second)
            if (e.IsMultipleOf(60) && Context.IsWorldReady)
            {
                if (Game1.player.stats.CropsShipped >= 1 && !Game1.player.achievements.Contains(100))
                {
                    UnlockAchievement(100, "Hello Crop");
                }
                if (Game1.player.stats.FishCaught >= 1 && !Game1.player.achievements.Contains(101))
                {
                    UnlockAchievement(101, "Hello Fish");
                }
                if (Game1.player.mineralsFound.Count() >= 1 && !Game1.player.achievements.Contains(102))
                {
                    UnlockAchievement(102, "Hello Mine");
                }
                if (Game1.player.craftingRecipes.TryGetValue("Wood Fence", out int count) && count > 0 && !Game1.player.achievements.Contains(103))
                {
                    UnlockAchievement(103, "Hello Craft");
                }
                if (Game1.player.Money >= 1000 && !Game1.player.achievements.Contains(105))
                {
                    UnlockAchievement(104, "Hello Wealth");
                }

                await CheckForNewAchievements();
            }
        }

        //Function of what to append when the game is launched.
        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            // get Generic Mod Config Menu's API (if it's installed)
            var configMenu = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu is null)
                return;

            // register mod
            configMenu.Register(
                mod: ModManifest,
                reset: () => Config = new ModConfig(),
                save: () => Helper.WriteConfig(Config)
            );

            // add some config options
            configMenu.AddBoolOption(
                mod: ModManifest,
                name: () => "Turn on Hardcore",
                tooltip: () => "This blocks non-white listed mods",
                getValue: () => Config.Hardcore,
                setValue: value => Config.Hardcore = value
            );
            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => "Username",
                getValue: () => Config.Username,
                setValue: value => Config.Username = value
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => "Password",
                getValue: () => new string('*', Config.Password.Length), // Mask it
                setValue: value =>
                {
                    if (!value.All(c => c == '*'))
                    {
                        Config.Password = value;
                    }
                }
            );

            // Hide config menu when a save is loaded
            Helper.Events.GameLoop.SaveLoaded += (s, args) => configMenu.Unregister(ModManifest);

        }

        //Function of what to append when returned to title.
        private void OnReturnedToTitle(object sender, ReturnedToTitleEventArgs e)
        {
            OnGameLaunched(sender, null);
        }

        /// <summary>Event handler for when the save is loaded.</summary>
        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            // Print the list of crafted items for debugging purposes
            //foreach (var item in Game1.player.craftingRecipes.Pairs)
            //{
            //    Monitor.Log($"Crafted item: {item.Key}, Quantity: {item.Value}", LogLevel.Debug);
            //}

            // Store achievements the player has already unlocked

            Monitor.Log("All installed mods are whitelisted. Running normally.", LogLevel.Info);
            previousAchievements = new HashSet<int>(Game1.player.achievements);

            // Initialize the RetroAchievements API
            _client = new HttpClient();
            _header = new RequestHeader(
                host: Host,
                game: GameId,
                hardcore: false
            );
            _client.DefaultRequestHeaders.Add("User-Agent", $"StardewValleyRetroAchievements/1.0");
            // Perform login
            Username = Config.Username;
            Password = Config.Password;
            Task.Run(() => Login(Username, Password)).Wait(); // Replace with your credentials
        }

        //Function to unlock an specific achievement in-game
        private void UnlockAchievement(int id, string name)
        {
            if (!Game1.player.achievements.Contains(id))
            {
                Game1.player.achievements.Add(id);
                Game1.addHUDMessage(new HUDMessage($"Achievement Unlocked: {name}!", 2));
            }
        }

        /// <summary>Logs in to the RetroAchievements API.</summary>
        /// <param name="user">The username.</param>
        /// <param name="pass">The password.</param>
        private async Task Login(string user, string pass)
        {
            _header.user = user;
            SendChatMessage($"Logging in {user} to {_header.host}...", Color.LimeGreen);
            Monitor.Log($"Logging in {user} to {_header.host}...", LogLevel.Warn);

            try
            {
                ApiResponse<LoginResponse> api = await NetworkInterface.TryLogin(_client, _header, pass);

                if (!string.IsNullOrEmpty(api.Failure))
                {
                    Monitor.Log($"Unable to login ({api.Failure})", LogLevel.Warn);
                    SendChatMessage($"Unable to login ({api.Failure})", Color.Red);
                    return;
                }

                if (!api.Response.Success)
                {
                    Monitor.Log($"Unable to login ({api.Response.Error})", LogLevel.Warn);
                    SendChatMessage($"Unable to login ({api.Response.Error})", Color.Red);
                    return;
                }

                _header.token = api.Response.Token;
                Monitor.Log($"{user} has successfully logged in RetroAchievements!", LogLevel.Warn);
                SendChatMessage($"{user} has successfully logged in RetroAchievements!", Color.LimeGreen);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Exception during login: {ex.Message}", LogLevel.Error);
                SendChatMessage($"Exception during login: {ex.Message}", Color.Red);
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

        private readonly Dictionary<int, int> achievementIdMap = new Dictionary<int, int>
        {
            { 100, 483647},
            { 101, 483648},
            { 102, 483649},
            { 103, 483650},
            { 104, 483651}
        };

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
                    SendChatMessage($"Achievement unlocked: {achievementId} - {achievementName}", Color.LimeGreen);
                    previousAchievements.Add(achievementId);

                    // Map in-game achievement ID to RetroAchievements ID
                    if (achievementIdMap.TryGetValue(achievementId, out int retroAchievementId))
                    {
                        // Send achievement to the RetroAchievements API
                        await SendAchievementToApi(retroAchievementId);
                    }
                    else
                    {
                        Monitor.Log($"No mapping found for in-game achievement ID: {achievementId}", LogLevel.Warn);
                    }
                }
            }
        }

        private async Task SendAchievementToApi(int achievementId)
        {
            // Send achievement to the RetroAchievements API
            ApiResponse<AwardAchievementResponse> api = await NetworkInterface.TryAwardAchievement(_client, _header, achievementId);
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