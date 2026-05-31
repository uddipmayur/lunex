using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using Lunex.Models;
using Lunex.Services;

namespace Lunex.ViewModels
{
    public class GameDetailsViewModel : ViewModelBase
    {
        private readonly MainViewModel _mainVm;
        private readonly LibraryService _libraryService;
        private readonly Game _game;

        public Game Game => _game;

        public string Title => _game.Title;
        public string ExePath => _game.ExePath;
        public string? CoverPath => _game.CoverPath;

        private string _launchArguments;
        public string LaunchArguments
        {
            get => _launchArguments;
            set => SetProperty(ref _launchArguments, value);
        }

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (SetProperty(ref _isRunning, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        public string StatusText => IsRunning ? "RUNNING..." : "PLAY";

        public string FormattedPlayTime => FormatPlayTime(_game.PlayTimeMinutes);
        public string FormattedLastPlayed => FormatLastPlayed(_game.LastPlayed);

        public List<double> WeeklyActivityHeights { get; }

        public ICommand PlayCommand { get; }
        public ICommand SaveArgsCommand { get; }
        public ICommand BackCommand { get; }
        public ICommand OpenDirectoryCommand { get; }

        public GameDetailsViewModel(MainViewModel mainVm, Game game)
        {
            _mainVm = mainVm;
            _libraryService = LibraryService.Instance;
            _game = game;
            _launchArguments = game.LaunchArguments;

            // Setup state listeners
            _libraryService.GameRunningStateChanged += OnGameRunningStateChanged;
            _libraryService.GameUpdated += OnGameUpdated;

            PlayCommand = new RelayCommand(() =>
            {
                if (!IsRunning) _libraryService.LaunchGame(_game);
            });

            SaveArgsCommand = new RelayCommand(() =>
            {
                _game.LaunchArguments = LaunchArguments;
                var games = _libraryService.LoadGames();
                var idx = games.FindIndex(g => g.Id == _game.Id);
                if (idx != -1)
                {
                    games[idx].LaunchArguments = LaunchArguments;
                    _libraryService.SaveGames(games);
                }
            });

            BackCommand = new RelayCommand(() => _mainVm.NavigateTo("library"));

            OpenDirectoryCommand = new RelayCommand(() =>
            {
                try
                {
                    if (File.Exists(_game.ExePath))
                    {
                        Process.Start("explorer.exe", $"/select,\"{_game.ExePath}\"");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error opening directory: {ex.Message}");
                }
            });

            // Calculate mock factors based on playtime for visual chart fidelity
            WeeklyActivityHeights = CalculateWeeklyChart(_game.PlayTimeMinutes);
        }

        private void OnGameRunningStateChanged(string gameId, bool isRunning)
        {
            if (gameId == _game.Id)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    IsRunning = isRunning;
                });
            }
        }

        private void OnGameUpdated(Game updatedGame)
        {
            if (updatedGame.Id == _game.Id)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    _game.PlayTimeMinutes = updatedGame.PlayTimeMinutes;
                    _game.LastPlayed = updatedGame.LastPlayed;
                    OnPropertyChanged(nameof(FormattedPlayTime));
                    OnPropertyChanged(nameof(FormattedLastPlayed));
                });
            }
        }

        private string FormatPlayTime(int minutes)
        {
            if (minutes == 0) return "0 hours";
            if (minutes < 60) return $"{minutes} mins";
            int hours = minutes / 60;
            int remaining = minutes % 60;
            if (remaining == 0) return $"{hours}h";
            return $"{hours}h {remaining}m";
        }

        private string FormatLastPlayed(DateTime? date)
        {
            if (date == null) return "Never";
            var diff = (DateTime.Now - date.Value).Days;
            if (diff == 0) return "Today";
            if (diff == 1) return "Yesterday";
            return $"{diff} days ago";
        }

        private List<double> CalculateWeeklyChart(int totalMinutes)
        {
            var baseFactors = new List<double> { 0.08, 0.12, 0.22, 0.05, 0.38, 0.15, 0.02 };
            var heights = new List<double>();
            if (totalMinutes == 0)
            {
                for (int i = 0; i < 7; i++) heights.Add(0.0);
            }
            else
            {
                foreach (var factor in baseFactors)
                {
                    heights.Add(factor * 180.0);
                }
            }
            return heights;
        }

        public void NotifyPropertiesChanged()
        {
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(CoverPath));
            OnPropertyChanged(nameof(LaunchArguments));
        }

        /// <summary>Unsubscribes service events. Call when navigating away from this view.</summary>
        public void Unsubscribe()
        {
            _libraryService.GameRunningStateChanged -= OnGameRunningStateChanged;
            _libraryService.GameUpdated -= OnGameUpdated;
        }
    }
}
