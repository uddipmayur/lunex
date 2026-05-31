using System.Windows.Input;

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
        public ViewModelBase CurrentViewModel
        {
            get => _currentViewModel;
            set
            {
                if (SetProperty(ref _currentViewModel, value))
                {
                    OnPropertyChanged(nameof(IsSplashActive));
                    OnPropertyChanged(nameof(IsMainUiActive));
                }
            }
        }

        public bool IsSplashActive => CurrentViewModel is SplashViewModel;
        public bool IsMainUiActive => !IsSplashActive;

        private string _searchQuery = string.Empty;
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetProperty(ref _searchQuery, value))
                {
                    // Propagate search query to active viewmodel if it implements search
                    if (CurrentViewModel is LibraryViewModel libraryVm)
                    {
                        libraryVm.SearchQuery = value;
                    }
                }
            }
        }

        public ICommand NavigateCommand { get; }

        public MainViewModel()
        {
            // Initialize sub-view navigation command
            NavigateCommand = new RelayCommand(param =>
            {
                if (param is string destination)
                {
                    NavigateTo(destination);
                }
            });

            // Start on Splash screen view
            _currentViewModel = new SplashViewModel(this);
        }

        public void NavigateTo(string destination, object? arg = null)
        {
            // Unsubscribe from GameDetailsViewModel when switching away since it is recreated on demand.
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
                    CurrentViewModel = _libraryVm ??= new LibraryViewModel(this);
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
    }

    // Dummy placeholder for SplashViewModel until we write it next
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
