using System;
using System.Net.NetworkInformation;
using System.Windows.Threading;
using System.Windows.Input;
using Lunex.Services;

namespace Lunex.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private ViewModelBase _currentViewModel;
        private LibraryViewModel? _libraryVm;
        private MusicViewModel? _musicVm;
        private StatsViewModel? _statsVm;
        private ProfileViewModel? _profileVm;
        private SettingsViewModel? _settingsVm;

        private double _uploadSpeed;
        public double UploadSpeed
        {
            get => _uploadSpeed;
            set => SetProperty(ref _uploadSpeed, value);
        }

        private double _downloadSpeed;
        public double DownloadSpeed
        {
            get => _downloadSpeed;
            set => SetProperty(ref _downloadSpeed, value);
        }

        public string UpdateStatusText => UpdateService.Instance.StatusText;

        private long _lastBytesReceived;
        private long _lastBytesSent;
        private DateTime _lastTickTime;
        private readonly DispatcherTimer _networkTimer;

        public ViewModelBase CurrentViewModel
        {
            get => _currentViewModel;
            set
            {
                if (SetProperty(ref _currentViewModel, value))
                {
                    OnPropertyChanged(nameof(IsSplashActive));
                    OnPropertyChanged(nameof(IsMainUiActive));
                    OnPropertyChanged(nameof(ShowLibrarySidebar));
                    OnPropertyChanged(nameof(ActiveView));
                }
            }
        }

        public bool IsSplashActive => CurrentViewModel is SplashViewModel;
        public bool IsMainUiActive => !IsSplashActive;
        public bool ShowLibrarySidebar => CurrentViewModel is LibraryViewModel || CurrentViewModel is GameDetailsViewModel;
        public LibraryViewModel LibraryVm => _libraryVm ??= new LibraryViewModel(this);

        public string ActiveView => CurrentViewModel switch
        {
            SplashViewModel => "splash",
            LibraryViewModel => "library",
            GameDetailsViewModel => "library",
            MusicViewModel => "music",
            StatsViewModel => "stats",
            ProfileViewModel => "profile",
            SettingsViewModel => "settings",
            _ => ""
        };

        public ICommand NavigateCommand { get; }

        public MainViewModel()
        {
            ProfileService.ProfileUpdated += (s, e) => LoadProfileData();
            LoadProfileData();

            UpdateService.Instance.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(UpdateService.StatusText))
                    OnPropertyChanged(nameof(UpdateStatusText));
            };

            GetNetworkBytes(out _lastBytesReceived, out _lastBytesSent);
            _lastTickTime = DateTime.UtcNow;

            _networkTimer = new DispatcherTimer();
            _networkTimer.Interval = TimeSpan.FromSeconds(1);
            _networkTimer.Tick += (s, e) =>
            {
                GetNetworkBytes(out long currentReceived, out long currentSent);
                var now = DateTime.UtcNow;
                double elapsed = (now - _lastTickTime).TotalSeconds;

                if (elapsed > 0)
                {
                    long diffReceived = currentReceived - _lastBytesReceived;
                    long diffSent = currentSent - _lastBytesSent;

                    if (diffReceived < 0) diffReceived = 0;
                    if (diffSent < 0) diffSent = 0;

                    DownloadSpeed = (diffReceived / elapsed) / (1024.0 * 1024.0);
                    UploadSpeed = (diffSent / elapsed) / (1024.0 * 1024.0);
                }

                _lastBytesReceived = currentReceived;
                _lastBytesSent = currentSent;
                _lastTickTime = now;
            };
            _networkTimer.Start();

            NavigateCommand = new RelayCommand(param =>
            {
                if (param is string destination)
                    NavigateTo(destination);
            });

            _currentViewModel = new SplashViewModel(this);
        }

        public void NavigateTo(string destination, object? arg = null)
        {
            if (_currentViewModel is GameDetailsViewModel oldDetails)
            {
                oldDetails.Unsubscribe();
            }

            switch (destination.ToLower())
            {
                case "splash":
                    CurrentViewModel = new SplashViewModel(this);
                    break;
                case "library":
                    CurrentViewModel = LibraryVm;
                    break;
                case "details":
                    if (arg is Models.Game game)
                    {
                        CurrentViewModel = new GameDetailsViewModel(this, game);
                    }
                    break;
                case "music":
                    CurrentViewModel = _musicVm ??= new MusicViewModel(this);
                    break;
                case "stats":
                    CurrentViewModel = _statsVm ??= new StatsViewModel(this);
                    break;
                case "profile":
                    CurrentViewModel = _profileVm ??= new ProfileViewModel(this);
                    break;
                case "settings":
                    CurrentViewModel = _settingsVm ??= new SettingsViewModel(this);
                    break;
            }
        }

        private void GetNetworkBytes(out long received, out long sent)
        {
            received = 0;
            sent = 0;
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var ni in interfaces)
                {
                    if (ni.OperationalStatus == OperationalStatus.Up &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        var stats = ni.GetIPStatistics();
                        received += stats.BytesReceived;
                        sent += stats.BytesSent;
                    }
                }
            }
            catch { }
        }

        private string _profileUsername = "Lunex";
        public string ProfileUsername
        {
            get => _profileUsername;
            set
            {
                if (SetProperty(ref _profileUsername, value))
                {
                    OnPropertyChanged(nameof(ProfileInitials));
                }
            }
        }

        private string? _profileDpPath;
        public string? ProfileDpPath
        {
            get => _profileDpPath;
            set
            {
                if (SetProperty(ref _profileDpPath, value))
                {
                    OnPropertyChanged(nameof(ProfileHasDp));
                }
            }
        }

        public bool ProfileHasDp => !string.IsNullOrEmpty(ProfileDpPath) && System.IO.File.Exists(ProfileDpPath);

        public string ProfileInitials
        {
            get
            {
                if (string.IsNullOrWhiteSpace(ProfileUsername)) return "N";
                var parts = ProfileUsername.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return "N";
                if (parts.Length == 1) return parts[0][0].ToString().ToUpper();
                return (parts[0][0].ToString() + parts[1][0].ToString()).ToUpper();
            }
        }

        private void LoadProfileData()
        {
            var service = new ProfileService();
            var profile = service.LoadProfile();
            ProfileUsername = profile.Username;
            ProfileDpPath = profile.DpPath;
        }
    }

    public class SplashViewModel : ViewModelBase
    {
        private readonly MainViewModel _mainVm;

        public SplashViewModel(MainViewModel mainVm)
        {
            _mainVm = mainVm;
        }

        public void CompleteBoot()
        {
            _mainVm.NavigateTo("library");
        }
    }
}
