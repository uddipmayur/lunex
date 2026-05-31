using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Lunex.Models;
using Lunex.Services;

namespace Lunex.ViewModels
{
    public class ProfileViewModel : ViewModelBase
    {
        private readonly ProfileService _profileService;
        private readonly ProfileData _profile;

        private string _username = "Lunex Shell";
        public string Username
        {
            get => _username;
            set
            {
                if (SetProperty(ref _username, value))
                {
                    OnPropertyChanged(nameof(Initials));
                }
            }
        }

        private string _title = "THE SILENT COMMANDER";
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        private string? _dpPath;
        public string? DpPath
        {
            get => _dpPath;
            set
            {
                if (SetProperty(ref _dpPath, value))
                {
                    OnPropertyChanged(nameof(HasDp));
                    OnPropertyChanged(nameof(AvatarVisibility));
                    OnPropertyChanged(nameof(FallbackVisibility));
                    OnPropertyChanged(nameof(DecalFilenameText));
                    OnPropertyChanged(nameof(BiometricStatusText));
                    OnPropertyChanged(nameof(BiometricStatusColor));
                }
            }
        }

        public bool HasDp => !string.IsNullOrEmpty(DpPath) && System.IO.File.Exists(DpPath);

        public string AvatarVisibility => HasDp ? "Visible" : "Collapsed";
        public string FallbackVisibility => HasDp ? "Collapsed" : "Visible";

        public string DecalFilenameText => HasDp 
            ? $"Active: {System.IO.Path.GetFileName(DpPath)}" 
            : "Procedural holographic projection is active.";

        public string BiometricStatusText => HasDp ? "ENCRYPTED" : "PROCEDURAL";
        public string BiometricStatusColor => HasDp ? "#C3C0FF" : "#DDB8FF";

        public string Initials
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Username)) return "N";
                var parts = Username.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return "N";
                if (parts.Length == 1) return parts[0][0].ToString().ToUpper();
                return (parts[0][0].ToString() + parts[1][0].ToString()).ToUpper();
            }
        }

        public ICommand SaveCommand { get; }
        public ICommand BrowseDecalCommand { get; }
        public ICommand ClearDecalCommand { get; }

        public ProfileViewModel(MainViewModel mainVm)
        {
            _profileService = new ProfileService();
            _profile = _profileService.LoadProfile();

            // Load initial state
            _username = _profile.Username;
            _title = _profile.Title;
            _dpPath = _profile.DpPath;

            SaveCommand = new RelayCommand(() =>
            {
                _profile.Username = Username;
                _profile.Title = Title;
                _profile.DpPath = DpPath;
                _profileService.SaveProfile(_profile);

                var dialog = new Views.ModernDialog("Save Profile", "Profile settings saved successfully.");
                if (Application.Current?.MainWindow != null)
                {
                    dialog.Owner = Application.Current.MainWindow;
                }
                dialog.ShowDialog();
            });

            BrowseDecalCommand = new RelayCommand(() =>
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "Image Files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
                    Title = "Select Biometric Decal"
                };
                if (dialog.ShowDialog() == true)
                {
                    DpPath = dialog.FileName;
                }
            });

            ClearDecalCommand = new RelayCommand(() => DpPath = null);
        }
    }
}
