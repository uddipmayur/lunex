using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Microsoft.Win32;
using Lunex.Models;
using Lunex.Services;

namespace Lunex.ViewModels
{
    public enum LibrarySortOption { All, MostPlayed, RecentlyPlayed }

    public class LibraryViewModel : ViewModelBase
    {
        private readonly MainViewModel _mainVm;
        private readonly LibraryService _libraryService;
        private List<Game> _allGames;
        private HashSet<string> _installedIds = new();
        private static readonly HashSet<string> _runningIds = new();

        public ObservableCollection<Game> FilteredGames { get; } = new();

        private string _searchQuery = string.Empty;
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetProperty(ref _searchQuery, value))
                {
                    ApplyFiltersAndSort();
                }
            }
        }

        private LibrarySortOption _activeOption = LibrarySortOption.RecentlyPlayed;
        public LibrarySortOption ActiveOption
        {
            get => _activeOption;
            set
            {
                if (SetProperty(ref _activeOption, value))
                {
                    ApplyFiltersAndSort();
                }
            }
        }

        public ICommand AddGameCommand { get; }
        public ICommand SelectGameCommand { get; }

        public LibraryViewModel(MainViewModel mainVm)
        {
            _mainVm = mainVm;
            _libraryService = LibraryService.Instance;
            _allGames = _libraryService.LoadGames();

            // Setup listeners
            _libraryService.GameRunningStateChanged += OnGameRunningStateChanged;
            _libraryService.GameUpdated += OnGameUpdated;
            _libraryService.GameRemoved += OnGameRemoved;

            AddGameCommand = new RelayCommand(ExecuteAddGame);
            SelectGameCommand = new RelayCommand(param =>
            {
                if (param is Game game)
                {
                    _mainVm.NavigateTo("details", game);
                }
            });

            // Trigger async refresh of installed statuses
            RefreshInstalledGames();
        }

        /// <summary>Unsubscribes service events when this VM is navigated away from.</summary>
        public void Unsubscribe()
        {
            _libraryService.GameRunningStateChanged -= OnGameRunningStateChanged;
            _libraryService.GameUpdated -= OnGameUpdated;
            _libraryService.GameRemoved -= OnGameRemoved;
        }

        public async void RefreshInstalledGames()
        {
            _allGames = _libraryService.LoadGames();
            _installedIds = await _libraryService.GetInstalledGameIdsAsync(_allGames);
            ApplyFiltersAndSort();
        }

        private void OnGameRunningStateChanged(string gameId, bool isRunning)
        {
            lock (_runningIds)
            {
                if (isRunning) _runningIds.Add(gameId);
                else _runningIds.Remove(gameId);
            }
            // Refresh filters on UI thread
            System.Windows.Application.Current.Dispatcher.Invoke(ApplyFiltersAndSort);
        }

        private void OnGameUpdated(Game game)
        {
            var existing = _allGames.FirstOrDefault(g => g.Id == game.Id);
            if (existing != null)
            {
                existing.PlayTimeMinutes = game.PlayTimeMinutes;
                existing.LastPlayed = game.LastPlayed;
                existing.LaunchArguments = game.LaunchArguments;
            }
            else
            {
                _allGames.Add(game);
            }
            // Only persist when a game session ends (IsRunning → false), not on every interim update.
            // LaunchGame's finally block already calls SaveGames, so skip redundant I/O here.
            System.Windows.Application.Current.Dispatcher.Invoke(ApplyFiltersAndSort);
        }

        private void OnGameRemoved(string gameId)
        {
            _allGames.RemoveAll(g => g.Id == gameId);
            System.Windows.Application.Current.Dispatcher.Invoke(ApplyFiltersAndSort);
        }

        public void ApplyFiltersAndSort()
        {
            var query = _allGames.AsEnumerable();

            // 1. Filter by Search Query
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                var search = SearchQuery.ToLower();
                query = query.Where(g => g.Title.ToLower().Contains(search));
            }

            // 2. Sorting based on ActiveOption
            query = ActiveOption switch
            {
                LibrarySortOption.MostPlayed => query.OrderByDescending(g => g.PlayTimeMinutes),
                LibrarySortOption.RecentlyPlayed => query.OrderByDescending(g => g.LastPlayed ?? DateTime.MinValue).ThenBy(g => g.Title),
                _ => query.OrderBy(g => g.Title) // All / Alphabetical
            };

            // Clear and populate observable list
            FilteredGames.Clear();
            foreach (var game in query)
            {
                FilteredGames.Add(game);
            }
            OnPropertyChanged(nameof(IsLibraryEmpty));
        }

        public bool IsLibraryEmpty => FilteredGames.Count == 0;


        private void ExecuteAddGame()
        {
            // Open standard Win32 File Dialog to select exe
            var dialog = new OpenFileDialog
            {
                Filter = "Executables (*.exe)|*.exe",
                Title = "Select Game Executable"
            };

            if (dialog.ShowDialog() == true)
            {
                var filePath = dialog.FileName;
                var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);

                // Add to list and save
                var newGame = new Game
                {
                    Id = DateTime.Now.Ticks.ToString(),
                    Title = fileName,
                    ExePath = filePath,
                    CoverPath = null,
                    PlayTimeMinutes = 0,
                    LaunchArguments = ""
                };

                _allGames.Add(newGame);
                _libraryService.SaveGames(_allGames);
                RefreshInstalledGames();
            }
        }
    }
}
