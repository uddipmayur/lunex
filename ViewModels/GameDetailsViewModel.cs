using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

        public string Title => _game.Title.ToUpperInvariant();
        public string ExePath => _game.ExePath;
        public string? CoverPath => _game.CoverPath;
        public string? Description => _game.Description;
        public double Rating => _game.Rating;
        public string? ReleaseDate => _game.ReleaseDate;
        public string? Developer => _game.Developer;
        public string? Publisher => _game.Publisher;
        public string? BackgroundImagePath => _game.BackgroundImagePath;

        private string _launchArguments;
        public string LaunchArguments
        {
            get => _launchArguments;
            set => SetProperty(ref _launchArguments, value);
        }

        public bool IsLoggedIn => !string.IsNullOrEmpty(SettingsService.Instance.CloudAuthToken);
        public bool NeedsLogin => !IsLoggedIn;

        public bool HasRawgApiKey => IsLoggedIn && !string.IsNullOrEmpty(SettingsService.Instance.RawgApiKey);
        public bool NeedsRawgApiKey => IsLoggedIn && !HasRawgApiKey;

        private string _rawgApiKeyInput = string.Empty;
        public string RawgApiKeyInput
        {
            get => _rawgApiKeyInput;
            set => SetProperty(ref _rawgApiKeyInput, value);
        }

        public bool IsInstalling
        {
            get => _game.IsInstalling;
            set
            {
                if (_game.IsInstalling != value)
                {
                    _game.IsInstalling = value;
                    OnPropertyChanged(nameof(IsInstalling));
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        public int InstallProgress
        {
            get => _game.InstallProgress;
            set
            {
                if (_game.InstallProgress != value)
                {
                    _game.InstallProgress = value;
                    OnPropertyChanged(nameof(InstallProgress));
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        public bool IsInstalled => _game.IsInstalled;

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

        public string StatusText
        {
            get
            {
                if (IsRunning) return "RUNNING...";
                if (IsInstalling) return $"INSTALLING {InstallProgress}%";
                if (!IsInstalled) return "INSTALL";
                return "PLAY";
            }
        }

        public string FormattedPlayTime => FormatPlayTime(_game.PlayTimeMinutes);
        public string FormattedLastPlayed => FormatLastPlayed(_game.LastPlayed);

        public System.Collections.Generic.List<WeeklyActivityDay> WeeklyDays { get; } = new()
        {
            new WeeklyActivityDay { DayLabel = "M" },
            new WeeklyActivityDay { DayLabel = "T" },
            new WeeklyActivityDay { DayLabel = "W" },
            new WeeklyActivityDay { DayLabel = "T" },
            new WeeklyActivityDay { DayLabel = "F" },
            new WeeklyActivityDay { DayLabel = "S" },
            new WeeklyActivityDay { DayLabel = "S" }
        };

        public ICommand PlayCommand { get; }
        public ICommand SaveArgsCommand { get; }
        public ICommand BackCommand { get; }
        public ICommand OpenDirectoryCommand { get; }
        public ICommand SaveRawgApiKeyCommand { get; }

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
                if (IsRunning) return;
                if (!IsInstalled)
                {
                    if (!IsInstalling) ExecuteInstall();
                }
                else
                {
                    _libraryService.LaunchGame(_game);
                }
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

            SaveRawgApiKeyCommand = new RelayCommand(async () =>
            {
                IsBusy = true;
                if (string.IsNullOrWhiteSpace(RawgApiKeyInput)) { IsBusy = false; return; }
                
                SettingsService.Instance.RawgApiKey = RawgApiKeyInput.Trim();
                OnPropertyChanged(nameof(HasRawgApiKey));
                OnPropertyChanged(nameof(NeedsRawgApiKey));

                // Optionally push to Supabase in the background
                var token = SettingsService.Instance.CloudAuthToken;
                if (!string.IsNullOrEmpty(token))
                {
                    await System.Threading.Tasks.Task.Run(async () =>
                    {
                        try
                        {
                            var supabase = SupabaseService.Client;
                            var userResponse = await supabase.Auth.GetUser(token);
                            if (userResponse != null && !string.IsNullOrEmpty(userResponse.Id))
                            {
                                var profileResult = await supabase.From<Models.ProfileModel>().Select("*").Filter("id", Postgrest.Constants.Operator.Equals, userResponse.Id).Get();
                                var profile = profileResult.Models.FirstOrDefault();
                                if (profile != null)
                                {
                                    profile.RawgApiKey = SettingsService.Instance.RawgApiKey;
                                    await supabase.From<Models.ProfileModel>().Update(profile);
                                }
                            }
                        }
                        catch { }
                    });
                }
                IsBusy = false;
            });

            // Calculate real activity factor heights
            _game.PopulateWeeklyActivity();
            UpdateWeeklyChart();
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
                    _game.SessionHistory = updatedGame.SessionHistory;
                    _game.PopulateWeeklyActivity();
                    UpdateWeeklyChart();
                    OnPropertyChanged(nameof(FormattedPlayTime));
                    OnPropertyChanged(nameof(FormattedLastPlayed));
                    OnPropertyChanged(nameof(IsInstalled));
                    OnPropertyChanged(nameof(StatusText));
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

        private System.Windows.Media.SolidColorBrush? _dominantColorBrush;

        public void SetDominantColorBrush(System.Windows.Media.SolidColorBrush? brush)
        {
            _dominantColorBrush = brush;
            UpdateWeeklyChart();
        }

        private void UpdateWeeklyChart()
        {
            // Always use a frozen brush so WPF Freezable ownership tracking
            // doesn't conflict when the same brush is bound to multiple Borders.
            // Clone + freeze the shared PrimaryBrush resource instead of using it directly.
            System.Windows.Media.SolidColorBrush dominantBrush;
            if (_dominantColorBrush != null)
            {
                dominantBrush = _dominantColorBrush; // already frozen (from ExtractDominantColorBrush)
            }
            else
            {
                var primary = (System.Windows.Media.SolidColorBrush)System.Windows.Application.Current.Resources["PrimaryBrush"];
                dominantBrush = new System.Windows.Media.SolidColorBrush(primary.Color);
                dominantBrush.Freeze();
            }

            var otherBarBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x44, 0x44, 0x44));
            otherBarBrush.Freeze();

            var otherLabelBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55));
            otherLabelBrush.Freeze();

            DayOfWeek[] daysOfWeekOrder = new[]
            {
                DayOfWeek.Monday,
                DayOfWeek.Tuesday,
                DayOfWeek.Wednesday,
                DayOfWeek.Thursday,
                DayOfWeek.Friday,
                DayOfWeek.Saturday,
                DayOfWeek.Sunday
            };

            int maxMinutes = 0;
            foreach (var day in daysOfWeekOrder)
            {
                if (_game.WeeklyActivity.TryGetValue(day, out int mins))
                {
                    if (mins > maxMinutes) maxMinutes = mins;
                }
            }

            double maxBarHeight = 80.0;
            double ghostHeight = 4.0;
            DayOfWeek today = DateTime.Now.DayOfWeek;

            for (int i = 0; i < 7; i++)
            {
                DayOfWeek day = daysOfWeekOrder[i];
                int minutes = 0;
                _game.WeeklyActivity.TryGetValue(day, out minutes);

                double height;
                if (maxMinutes == 0)
                {
                    height = ghostHeight;
                }
                else
                {
                    height = minutes == 0 ? ghostHeight : Math.Max(((double)minutes / maxMinutes) * maxBarHeight, ghostHeight);
                }

                WeeklyDays[i].Height = height;

                if (day == today)
                {
                    WeeklyDays[i].BarBrush = dominantBrush;
                    WeeklyDays[i].LabelBrush = System.Windows.Media.Brushes.White;
                    WeeklyDays[i].LabelWeight = System.Windows.FontWeights.Bold;
                }
                else
                {
                    WeeklyDays[i].BarBrush = otherBarBrush;
                    WeeklyDays[i].LabelBrush = otherLabelBrush;
                    WeeklyDays[i].LabelWeight = System.Windows.FontWeights.Normal;
                }
            }
        }

        public void NotifyPropertiesChanged()
        {
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(CoverPath));
            OnPropertyChanged(nameof(Description));
            OnPropertyChanged(nameof(Rating));
            OnPropertyChanged(nameof(ReleaseDate));
            OnPropertyChanged(nameof(Developer));
            OnPropertyChanged(nameof(Publisher));
            OnPropertyChanged(nameof(BackgroundImagePath));
            OnPropertyChanged(nameof(LaunchArguments));
            OnPropertyChanged(nameof(IsInstalled));
            OnPropertyChanged(nameof(StatusText));
        }

        private void ExecuteInstall()
        {
            IsInstalling = true;
            InstallProgress = 0;
            IsBusy = true;
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i <= 100; i += 10)
                    {
                        await System.Threading.Tasks.Task.Delay(300);
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            InstallProgress = i;
                        });
                    }

                    // Create dummy file at ExePath
                    var dir = Path.GetDirectoryName(_game.ExePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    if (!File.Exists(_game.ExePath))
                    {
                        await File.WriteAllTextAsync(_game.ExePath, "dummy executable simulation");
                    }

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsInstalling = false;
                        IsBusy = false;
                        OnPropertyChanged(nameof(IsInstalled));
                        OnPropertyChanged(nameof(StatusText));
                        _libraryService.UpdateGame(_game);
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error simulating install: {ex.Message}");
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsInstalling = false;
                        IsBusy = false;
                    });
                }
            });
        }

        /// <summary>Unsubscribes service events. Call when navigating away from this view.</summary>
        public void Unsubscribe()
        {
            _libraryService.GameRunningStateChanged -= OnGameRunningStateChanged;
            _libraryService.GameUpdated -= OnGameUpdated;
        }
    }

    public class WeeklyActivityDay : ViewModelBase
    {
        private string _dayLabel = string.Empty;
        public string DayLabel
        {
            get => _dayLabel;
            set => SetProperty(ref _dayLabel, value);
        }

        private double _height = 4.0;
        public double Height
        {
            get => _height;
            set => SetProperty(ref _height, value);
        }

        private System.Windows.Media.Brush _barBrush = System.Windows.Media.Brushes.Transparent;
        public System.Windows.Media.Brush BarBrush
        {
            get => _barBrush;
            set => SetProperty(ref _barBrush, value);
        }

        private System.Windows.Media.Brush _labelBrush = System.Windows.Media.Brushes.Transparent;
        public System.Windows.Media.Brush LabelBrush
        {
            get => _labelBrush;
            set => SetProperty(ref _labelBrush, value);
        }

        private System.Windows.FontWeight _labelWeight = System.Windows.FontWeights.Normal;
        public System.Windows.FontWeight LabelWeight
        {
            get => _labelWeight;
            set => SetProperty(ref _labelWeight, value);
        }
    }
}
