using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Lunex.Services;
using Lunex.ViewModels;

namespace Lunex.Components
{
    public partial class LunexSidebar : UserControl
    {
        private ProfileService? _profileService;

        public LunexSidebar()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _profileService = new ProfileService();
            LoadProfileData();
            UpdateActiveButton();

            // Subscribe to profile updates
            ProfileService.ProfileUpdated += OnProfileUpdated;

            // Setup listener for view swaps
            if (DataContext is MainViewModel mainVm)
            {
                mainVm.PropertyChanged += (s, args) =>
                {
                    if (args.PropertyName == nameof(MainViewModel.CurrentViewModel))
                    {
                        UpdateActiveButton();
                    }
                };
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            ProfileService.ProfileUpdated -= OnProfileUpdated;
        }

        private void OnProfileUpdated(object? sender, EventArgs e)
        {
            // Call on UI thread to ensure thread-safety for WPF elements
            Dispatcher.Invoke(LoadProfileData);
        }

        public void LoadProfileData()
        {
            if (_profileService == null) return;
            var profile = _profileService.LoadProfile();

            UsernameBlock.Text = profile.Username;
            RankBlock.Text = profile.Title.ToUpper();

            // Compute fallback initials
            if (string.IsNullOrWhiteSpace(profile.Username))
            {
                InitialsBlock.Text = "N";
            }
            else
            {
                var parts = profile.Username.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                InitialsBlock.Text = parts.Length switch
                {
                    0 => "N",
                    1 => parts[0][0].ToString().ToUpper(),
                    _ => (parts[0][0].ToString() + parts[1][0].ToString()).ToUpper()
                };
            }

            // Set biometric DP image if exists
            if (!string.IsNullOrEmpty(profile.DpPath) && File.Exists(profile.DpPath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(profile.DpPath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();

                    ProfileImageBrush.ImageSource = bitmap;
                    ProfileEllipse.Visibility = Visibility.Visible;
                }
                catch
                {
                    ProfileEllipse.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                ProfileEllipse.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateActiveButton()
        {
            if (DataContext is not MainViewModel mainVm) return;

            BtnLibrary.Tag = null;
            BtnMusic.Tag = null;
            BtnStats.Tag = null;
            BtnProfile.Tag = null;
            BtnSettings.Tag = null;

            var currentType = mainVm.CurrentViewModel?.GetType().Name;
            if (currentType == nameof(LibraryViewModel) || currentType == nameof(GameDetailsViewModel))
            {
                BtnLibrary.Tag = "Active";
            }
            else if (currentType == nameof(MusicViewModel))
            {
                BtnMusic.Tag = "Active";
            }
            else if (currentType == nameof(StatsViewModel))
            {
                BtnStats.Tag = "Active";
            }
            else if (currentType == nameof(ProfileViewModel))
            {
                BtnProfile.Tag = "Active";
            }
            else if (currentType == nameof(SettingsViewModel))
            {
                BtnSettings.Tag = "Active";
            }
        }

        private void NavigateLibrary(object sender, RoutedEventArgs e) => (DataContext as MainViewModel)?.NavigateTo("library");
        private void NavigateMusic(object sender, RoutedEventArgs e) => (DataContext as MainViewModel)?.NavigateTo("music");
        private void NavigateStats(object sender, RoutedEventArgs e) => (DataContext as MainViewModel)?.NavigateTo("stats");
        private void NavigateProfile(object sender, RoutedEventArgs e) => (DataContext as MainViewModel)?.NavigateTo("profile");
        private void NavigateSettings(object sender, RoutedEventArgs e) => (DataContext as MainViewModel)?.NavigateTo("settings");
    }
}
