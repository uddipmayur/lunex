using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Lunex.Models;

namespace Lunex.Services
{
    public class LibraryService
    {
        private static readonly Lazy<LibraryService> _instance = new(() => new LibraryService());
        public static LibraryService Instance => _instance.Value;

        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
        private readonly string _appDataDir;
        private readonly string _storageFilePath;

        public event Action<string, bool>? GameRunningStateChanged;
        public event Action<Game>? GameUpdated;
        public event Action<string>? GameRemoved;

        private LibraryService()
        {
            _appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lunex");
            if (!Directory.Exists(_appDataDir))
            {
                Directory.CreateDirectory(_appDataDir);
            }
            _storageFilePath = Path.Combine(_appDataDir, "lunex_games.json");
        }

        public List<Game> LoadGames()
        {
            try
            {
                if (File.Exists(_storageFilePath))
                {
                    var jsonContent = File.ReadAllText(_storageFilePath);
                    var games = JsonSerializer.Deserialize<List<Game>>(jsonContent) ?? new List<Game>();
                    foreach (var game in games)
                    {
                        game.PopulateWeeklyActivity();
                    }
                    return games;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading games: {ex.Message}");
            }
            return new List<Game>();
        }

        public void SaveGames(List<Game> games)
        {
            try
            {
                PruneOrphanedImages(games);

                var jsonContent = JsonSerializer.Serialize(games, _jsonOptions);
                File.WriteAllText(_storageFilePath, jsonContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving games: {ex.Message}");
            }
        }

        public async Task<HashSet<string>> GetInstalledGameIdsAsync(List<Game> games)
        {
            return await Task.Run(() =>
            {
                var installedIds = new HashSet<string>();
                foreach (var game in games)
                {
                    if (!string.IsNullOrEmpty(game.ExePath) && File.Exists(game.ExePath))
                    {
                        installedIds.Add(game.Id);
                    }
                }
                return installedIds;
            });
        }

        public void LaunchGame(Game game)
        {
            // Try fetching missing metadata on launch
            if (!game.RawgId.HasValue || string.IsNullOrEmpty(game.Description))
            {
                Task.Run(async () =>
                {
                    try
                    {
                        var rawgData = await RawgApiService.Instance.SearchGameAsync(game.Title);
                        if (rawgData != null)
                        {
                            game.RawgId = rawgData.Id;
                            game.Description = rawgData.DescriptionRaw;
                            game.Rating = rawgData.Rating;
                            game.ReleaseDate = rawgData.Released;
                            game.Developer = rawgData.Developers?.FirstOrDefault()?.Name;
                            game.Publisher = rawgData.Publishers?.FirstOrDefault()?.Name;

                            if (!string.IsNullOrEmpty(rawgData.BackgroundImage))
                            {
                                await CacheRemoteImageAsync(game, rawgData.BackgroundImage);
                            }
                            
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                UpdateGame(game);
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error fetching RAWG details on launch: {ex.Message}");
                    }
                });
            }

            // update timestamp so users can see how much life they wasted
            game.LastPlayed = DateTime.Now;
            GameUpdated?.Invoke(game);

            // fire event to hide the UI, otherwise the app steals focus from the game
            GameRunningStateChanged?.Invoke(game.Id, true);

            Task.Run(async () =>
            {
                var startTime = DateTime.Now;
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = game.ExePath,
                        WorkingDirectory = Path.GetDirectoryName(game.ExePath),
                        UseShellExecute = true
                    };

                    // pass down launch args from custom settings
                    if (!string.IsNullOrWhiteSpace(game.LaunchArguments))
                    {
                        startInfo.Arguments = game.LaunchArguments;
                    }

                    using var process = Process.Start(startInfo);
                    if (process != null)
                    {
                        await process.WaitForExitAsync();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to launch process: {ex.Message}");
                }
                finally
                {
                    var endTime = DateTime.Now;
                    var elapsedMinutes = (int)Math.Ceiling((endTime - startTime).TotalMinutes);

                    // DO NOT FUCKING REMOVE THIS. Playtime tracking relies on process exit timing. If this fails, players get 0 hours.
                    game.PlayTimeMinutes += elapsedMinutes;

                    if (game.SessionHistory == null)
                    {
                        game.SessionHistory = new List<PlaySession>();
                    }
                    game.SessionHistory.Add(new PlaySession
                    {
                        Timestamp = startTime,
                        DurationMinutes = elapsedMinutes
                    });
                    game.PopulateWeeklyActivity();

                    UpdateGame(game);

                    // bring main window back from the dead
                    GameRunningStateChanged?.Invoke(game.Id, false);
                }
            });
        }

        public void UpdateGame(Game game)
        {
            CacheGameImages(game);

            var games = LoadGames();
            var idx = games.FindIndex(g => g.Id == game.Id);
            if (idx != -1)
            {
                games[idx] = game;
                SaveGames(games);
                GameUpdated?.Invoke(game);
            }
        }

        public void RemoveGame(string gameId)
        {
            var games = LoadGames();
            var existing = games.Find(g => g.Id == gameId);
            if (existing != null)
            {
                games.Remove(existing);
                SaveGames(games);
                GameRemoved?.Invoke(gameId);
            }
        }

        private void CacheGameImages(Game game)
        {
            var cacheDir = Path.Combine(_appDataDir, "Cache");
            if (!Directory.Exists(cacheDir))
            {
                try
                {
                    Directory.CreateDirectory(cacheDir);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error creating cache directory: {ex.Message}");
                    return;
                }
            }

            // copy game cover art to local appdata folder so we don't depend on temp user paths
            if (!string.IsNullOrEmpty(game.CoverPath))
            {
                var isAlreadyCached = game.CoverPath.StartsWith(cacheDir, StringComparison.OrdinalIgnoreCase);
                if (!isAlreadyCached && File.Exists(game.CoverPath))
                {
                    try
                    {
                        var ext = Path.GetExtension(game.CoverPath);
                        var existingFiles = Directory.GetFiles(cacheDir, $"{game.Id}_cover.*");
                        foreach (var file in existingFiles)
                        {
                            try { File.Delete(file); } catch { }
                        }

                        var destPath = Path.Combine(cacheDir, $"{game.Id}_cover{ext}");
                        File.Copy(game.CoverPath, destPath, true);
                        game.CoverPath = destPath;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error caching cover: {ex.Message}");
                    }
                }
            }
            else
            {
                try
                {
                    var existingFiles = Directory.GetFiles(cacheDir, $"{game.Id}_cover.*");
                    foreach (var file in existingFiles)
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
                catch { }
            }

            // copy game icon to local appdata
            if (!string.IsNullOrEmpty(game.IconPath))
            {
                var isAlreadyCached = game.IconPath.StartsWith(cacheDir, StringComparison.OrdinalIgnoreCase);
                if (!isAlreadyCached && File.Exists(game.IconPath))
                {
                    try
                    {
                        var ext = Path.GetExtension(game.IconPath);
                        var existingFiles = Directory.GetFiles(cacheDir, $"{game.Id}_icon.*");
                        foreach (var file in existingFiles)
                        {
                            try { File.Delete(file); } catch { }
                        }

                        var destPath = Path.Combine(cacheDir, $"{game.Id}_icon{ext}");
                        File.Copy(game.IconPath, destPath, true);
                        game.IconPath = destPath;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error caching icon: {ex.Message}");
                    }
                }
            }
            else
            {
                try
                {
                    var existingFiles = Directory.GetFiles(cacheDir, $"{game.Id}_icon.*");
                    foreach (var file in existingFiles)
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
                catch { }
            }
        }

        public async Task CacheRemoteImageAsync(Game game, string imageUrl, bool overwriteCover = true)
        {
            if (string.IsNullOrWhiteSpace(imageUrl)) return;

            var cacheDir = Path.Combine(_appDataDir, "Cache");
            if (!Directory.Exists(cacheDir))
            {
                try
                {
                    Directory.CreateDirectory(cacheDir);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error creating cache directory: {ex.Message}");
                    return;
                }
            }

            try
            {
                var imageBytes = await RawgApiService.Instance.DownloadImageAsync(imageUrl);
                if (imageBytes != null && imageBytes.Length > 0)
                {
                    var ext = ".jpg"; // RAWG usually returns jpg or png, default to jpg for saving
                    var destPath = Path.Combine(cacheDir, $"{game.Id}_bg{ext}");
                    await File.WriteAllBytesAsync(destPath, imageBytes);
                    game.BackgroundImagePath = destPath;

                    // Always overwrite the cover when coming from a RAWG sync (overwriteCover=true).
                    // CoverPath was non-empty. Now we delete the old cached file and replace it.
                    // EXCEPTION: if the user manually chose a cover via Customize dialog
                    // (HasCustomCover=true), never touch it — RAWG must not clobber user choices.
                    bool shouldUpdateCover = (overwriteCover || string.IsNullOrEmpty(game.CoverPath))
                                            && !game.HasCustomCover;

                    if (shouldUpdateCover)
                    {
                        var coverDestPath = Path.Combine(cacheDir, $"{game.Id}_cover{ext}");

                        // Delete old stale cached cover if it exists and differs from the new path
                        if (!string.IsNullOrEmpty(game.CoverPath) &&
                            game.CoverPath != coverDestPath &&
                            File.Exists(game.CoverPath))
                        {
                            try { File.Delete(game.CoverPath); } catch { }
                        }

                        await File.WriteAllBytesAsync(coverDestPath, imageBytes);
                        game.CoverPath = coverDestPath;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error caching remote image: {ex.Message}");
            }
        }


        // DO NOT TOUCH. Deletes cached covers/icons of games that were removed from the library to prevent user storage leakage.
        private void PruneOrphanedImages(List<Game> games)
        {
            try
            {
                var cacheDir = Path.Combine(_appDataDir, "Cache");
                if (Directory.Exists(cacheDir))
                {
                    var files = Directory.GetFiles(cacheDir);
                    foreach (var file in files)
                    {
                        var fileName = Path.GetFileName(file);
                        string gameId = "";
                        if (fileName.Contains("_cover."))
                        {
                            gameId = fileName.Substring(0, fileName.IndexOf("_cover."));
                        }
                        else if (fileName.Contains("_icon."))
                        {
                            gameId = fileName.Substring(0, fileName.IndexOf("_icon."));
                        }
                        else if (fileName.Contains("_bg."))
                        {
                            gameId = fileName.Substring(0, fileName.IndexOf("_bg."));
                        }

                        if (!string.IsNullOrEmpty(gameId))
                        {
                            if (!games.Any(g => g.Id == gameId))
                            {
                                try { File.Delete(file); } catch { }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error pruning orphaned images: {ex.Message}");
            }
        }
    }
}
