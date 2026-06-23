using System;
using System.Linq;
using System.Threading.Tasks;
using Lunex.Models;

namespace Lunex.Services
{
    public class CloudSyncService
    {
        private static readonly Lazy<CloudSyncService> _instance = new(() => new CloudSyncService());
        public static CloudSyncService Instance => _instance.Value;

        // Debounce: sync at most once every 30 seconds to prevent DB write spam
        private const int SyncDebounceSeconds = 30;
        private DateTime _lastSyncTime = DateTime.MinValue;
        private System.Threading.Timer? _debounceTimer;
        private Game? _pendingGame;
        private readonly object _syncLock = new();

        // Max reasonable playtime increase per sync (24 hours)
        private const int MaxPlaytimeIncreasePerSync = 1440;

        private CloudSyncService()
        {
        }

        public void Initialize()
        {
            LibraryService.Instance.GameUpdated += OnGameUpdated;
            Task.Run(async () => await SyncAllGamesAsync());
        }

        public async Task SyncAllGamesAsync()
        {
            try
            {
                var token = SettingsService.Instance.CloudAuthToken;
                if (string.IsNullOrEmpty(token)) return;

                var refreshToken = SettingsService.Instance.CloudRefreshToken ?? string.Empty;
                var supabase = SupabaseService.Client;
                var restoredSession = await supabase.Auth.SetSession(token, refreshToken);
                // Persist refreshed tokens if the SDK rotated them
                if (restoredSession?.AccessToken != null)
                    SettingsService.Instance.CloudAuthToken = restoredSession.AccessToken;
                if (restoredSession?.RefreshToken != null)
                    SettingsService.Instance.CloudRefreshToken = restoredSession.RefreshToken;

                var userResponse = await supabase.Auth.GetUser(token);
                if (userResponse == null || string.IsNullOrEmpty(userResponse.Id)) return;
                var userId = userResponse.Id;

                var cloudGamesResult = await supabase.From<UserGameModel>().Filter("user_id", Postgrest.Constants.Operator.Equals, userId).Get();
                var cloudGames = cloudGamesResult.Models;

                var localGames = LibraryService.Instance.LoadGames();
                bool localChanged = false;

                foreach (var localGame in localGames)
                {
                    var cloudGame = cloudGames.FirstOrDefault(c => c.GameId == localGame.Id);
                    if (cloudGame != null)
                    {
                        if (cloudGame.PlaytimeMinutes > localGame.PlayTimeMinutes)
                        {
                            localGame.PlayTimeMinutes = cloudGame.PlaytimeMinutes;
                            localGame.CloudPlayTimeMinutes = cloudGame.PlaytimeMinutes;
                            if (cloudGame.LastPlayed > localGame.LastPlayed)
                                localGame.LastPlayed = cloudGame.LastPlayed;
                            localChanged = true;
                        }
                        else if (localGame.PlayTimeMinutes > cloudGame.PlaytimeMinutes)
                        {
                            await SyncGameplayDataAsync(localGame);
                        }
                        else
                        {
                            if (localGame.CloudPlayTimeMinutes != cloudGame.PlaytimeMinutes)
                            {
                                localGame.CloudPlayTimeMinutes = cloudGame.PlaytimeMinutes;
                                localChanged = true;
                            }
                        }
                    }
                    else
                    {
                        await SyncGameplayDataAsync(localGame);
                    }
                }

                if (localChanged)
                {
                    LibraryService.Instance.SaveGames(localGames);
                    
                    // We must fire GameUpdated to the UI so it refreshes the views if they are open
                    foreach (var game in localGames)
                    {
                        LibraryService.Instance.UpdateGame(game);
                    }
                }
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lunex", "sync_error.log"), $"[{DateTime.Now}] Error during boot sync: {ex}\n");
                System.Diagnostics.Debug.WriteLine($"Error during boot sync: {ex.Message}");
            }
        }

        private void OnGameUpdated(Game game)
        {
            lock (_syncLock)
            {
                _pendingGame = game;

                var elapsed = (DateTime.Now - _lastSyncTime).TotalSeconds;
                if (elapsed >= SyncDebounceSeconds)
                {
                    // Enough time has passed, sync immediately
                    _pendingGame = null;
                    _lastSyncTime = DateTime.Now;
                    _debounceTimer?.Dispose();
                    _debounceTimer = null;
                    Task.Run(async () => await SyncGameplayDataAsync(game));
                }
                else if (_debounceTimer == null)
                {
                    // Schedule a deferred sync after the remaining cooldown
                    var delayMs = (int)((SyncDebounceSeconds - elapsed) * 1000);
                    _debounceTimer = new System.Threading.Timer(_ => FlushPendingSync(), null, delayMs, System.Threading.Timeout.Infinite);
                }
                // If timer is already scheduled, _pendingGame is updated — timer will pick up the latest state
            }
        }

        private void FlushPendingSync()
        {
            Game? gameToSync;
            lock (_syncLock)
            {
                gameToSync = _pendingGame;
                _pendingGame = null;
                _debounceTimer?.Dispose();
                _debounceTimer = null;
                _lastSyncTime = DateTime.Now;
            }

            if (gameToSync != null)
            {
                Task.Run(async () => await SyncGameplayDataAsync(gameToSync));
            }
        }

        private async Task SyncGameplayDataAsync(Game game)
        {
            try
            {
                var token = SettingsService.Instance.CloudAuthToken;
                if (string.IsNullOrEmpty(token)) return;

                if (game.PlayTimeMinutes == game.CloudPlayTimeMinutes) return;

                var refreshToken = SettingsService.Instance.CloudRefreshToken ?? string.Empty;
                var supabase = SupabaseService.Client;
                var restoredSession = await supabase.Auth.SetSession(token, refreshToken);
                // Persist refreshed tokens if the SDK rotated them
                if (restoredSession?.AccessToken != null)
                    SettingsService.Instance.CloudAuthToken = restoredSession.AccessToken;
                if (restoredSession?.RefreshToken != null)
                    SettingsService.Instance.CloudRefreshToken = restoredSession.RefreshToken;

                // Attempt to get the current authenticated user
                var userResponse = await supabase.Auth.GetUser(token);
                if (userResponse == null || string.IsNullOrEmpty(userResponse.Id)) return;

                var userId = userResponse.Id;

                // 1. Upsert Overall Game Playtime
                var existingGameResult = await supabase.From<UserGameModel>()
                    .Select("*")
                    .Filter("user_id", Postgrest.Constants.Operator.Equals, userId)
                    .Filter("game_id", Postgrest.Constants.Operator.Equals, game.Id)
                    .Get();

                var existingGameModel = existingGameResult.Models.FirstOrDefault();

                var gameModel = new UserGameModel
                {
                    UserId = userId,
                    GameId = game.Id,
                    GameTitle = game.Title,
                    PlaytimeMinutes = game.PlayTimeMinutes,
                    LastPlayed = game.LastPlayed ?? DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                if (existingGameModel != null)
                {
                    // Use the ID from the existing record to perform an update
                    gameModel.Id = existingGameModel.Id;
                    gameModel.CreatedAt = existingGameModel.CreatedAt;
                    await supabase.From<UserGameModel>().Update(gameModel);
                }
                else
                {
                    gameModel.CreatedAt = DateTime.UtcNow;
                    await supabase.From<UserGameModel>().Insert(gameModel);
                }

                // Update local storage to reflect successful sync
                game.CloudPlayTimeMinutes = game.PlayTimeMinutes;
                
                var games = LibraryService.Instance.LoadGames();
                var idx = games.FindIndex(g => g.Id == game.Id);
                if (idx != -1)
                {
                    games[idx].CloudPlayTimeMinutes = game.CloudPlayTimeMinutes;
                    // Save silently without triggering GameUpdated events
                    LibraryService.Instance.SaveGames(games);
                }
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lunex", "sync_error.log"), $"[{DateTime.Now}] Error syncing gameplay data: {ex}\n");
                Console.WriteLine($"Error syncing gameplay data to cloud: {ex.Message}");
            }
        }
    }
}
