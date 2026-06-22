using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Lunex.Services;
using System.Linq;

namespace Lunex.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        public bool LaunchAtStartup
        {
            get => SettingsService.Instance.LaunchAtStartup;
            set
            {
                if (SettingsService.Instance.LaunchAtStartup != value)
                {
                    SettingsService.Instance.LaunchAtStartup = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool MinimizeToTray
        {
            get => SettingsService.Instance.MinimizeToTray;
            set
            {
                if (SettingsService.Instance.MinimizeToTray != value)
                {
                    SettingsService.Instance.MinimizeToTray = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _activeDirectory = "C:\\AntiGravity\\Lunex";
        public string ActiveDirectory
        {
            get => _activeDirectory;
            set => SetProperty(ref _activeDirectory, value);
        }

        // ── Commands ─────────────────────────────────────────────────────────
        public ICommand ClearCommand { get; }
        public ICommand CheckForUpdatesCommand { get; }
        public ICommand InstallUpdateCommand { get; }
        public ICommand OpenPrivacyPolicyCommand { get; }
        public ICommand OpenTermsOfServiceCommand { get; }

        // Raised on the calling thread when Check for Updates confirms no update is available.
        // The View subscribes and shows the dialog (proper MVVM separation).
        public event Action? AlreadyOnLatestVersion;
        public event Action? UpdateAvailableAndDownloading;
        public event Action<string>? UpdateCheckFailed;
        // ── Update status properties (forwarded from UpdateService) ──────────
        public string CurrentVersion => UpdateService.CurrentVersion;

        public bool IsCheckingForUpdate => UpdateService.Instance.IsCheckingForUpdate;
        public bool IsDownloading => UpdateService.Instance.IsDownloading;
        public double DownloadProgress => UpdateService.Instance.DownloadProgress;
        public bool UpdateAvailable => UpdateService.Instance.UpdateAvailable;
        public bool UpdateDownloaded => UpdateService.Instance.UpdateDownloaded;
        public string LatestVersion => UpdateService.Instance.LatestVersion;
        public string UpdateStatusText => UpdateService.Instance.StatusText;
        public string InstallButtonText => UpdateAvailable ? "INSTALL UPDATE NOW" : "REINSTALL APP";

        public bool CheckUpdateButtonEnabled =>
            !UpdateService.Instance.IsCheckingForUpdate &&
            !UpdateService.Instance.IsDownloading;

        public SettingsViewModel(MainViewModel mainVm)
        {
            ClearCommand = new RelayCommand(_ => ExecuteClear());
            CheckForUpdatesCommand = new RelayCommand(async (_) =>
            {
                bool alreadyLatest = await UpdateService.Instance.CheckForUpdateManuallyAsync();
                if (alreadyLatest)
                {
                    AlreadyOnLatestVersion?.Invoke();
                }
                else
                {
                    if (UpdateService.Instance.UpdateAvailable)
                    {
                        UpdateAvailableAndDownloading?.Invoke();
                    }
                    else
                    {
                        UpdateCheckFailed?.Invoke(UpdateService.Instance.StatusText);
                    }
                }
            });
            InstallUpdateCommand = new RelayCommand(_ =>
            {
                UpdateService.Instance.LaunchInstaller();
                Application.Current?.Shutdown();
            });
            OpenPrivacyPolicyCommand = new RelayCommand(() => OpenUrl("https://lunex.nexusrealm.in/privacy-policy"));
            OpenTermsOfServiceCommand = new RelayCommand(() => OpenUrl("https://lunex.nexusrealm.in/terms-of-service"));

            // Forward PropertyChanged from SettingsService to view bindings
            SettingsService.Instance.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != null)
                    OnPropertyChanged(e.PropertyName);
            };

            // Forward PropertyChanged from UpdateService to view bindings
            UpdateService.Instance.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(UpdateService.StatusText))
                {
                    OnPropertyChanged(nameof(UpdateStatusText));
                }
                else
                {
                    OnPropertyChanged(e.PropertyName);
                }

                // Refresh composite button-enabled property when busy state changes
                if (e.PropertyName is nameof(UpdateService.IsCheckingForUpdate)
                    or nameof(UpdateService.IsDownloading))
                {
                    OnPropertyChanged(nameof(CheckUpdateButtonEnabled));
                }

                if (e.PropertyName is nameof(UpdateService.UpdateAvailable))
                {
                    OnPropertyChanged(nameof(InstallButtonText));
                }
            };
        }

        private void ExecuteClear()
        {
            var confirmDialog = new Views.ModernDialog(
                "Clear Library Data",
                "Are you sure you want to clear your integrated games library and gameplay data? This will also wipe your cloud play history and reset your rank. This cannot be undone.",
                isConfirmation: true);

            if (Application.Current?.MainWindow != null)
                confirmDialog.Owner = Application.Current.MainWindow;

            if (confirmDialog.ShowDialog() == true && confirmDialog.Result)
            {
                try
                {
                    var gamesToClear = LibraryService.Instance.LoadGames();
                    foreach (var game in gamesToClear)
                    {
                        game.PlayTimeMinutes = 0;
                        game.CloudPlayTimeMinutes = 0;
                        game.LastPlayed = null;
                        game.SessionHistory?.Clear();
                        game.PopulateWeeklyActivity();
                        LibraryService.Instance.UpdateGame(game);
                    }

                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        try
                        {
                            var token = SettingsService.Instance.CloudAuthToken;
                            if (!string.IsNullOrEmpty(token))
                            {
                                var refreshToken = SettingsService.Instance.CloudRefreshToken ?? string.Empty;
                                var supabase = SupabaseService.Client;
                                var restoredSession = await supabase.Auth.SetSession(token, refreshToken);
                                if (restoredSession?.AccessToken != null) SettingsService.Instance.CloudAuthToken = restoredSession.AccessToken;
                                if (restoredSession?.RefreshToken != null) SettingsService.Instance.CloudRefreshToken = restoredSession.RefreshToken;

                                var userResponse = await supabase.Auth.GetUser(token);
                                if (userResponse != null && !string.IsNullOrEmpty(userResponse.Id))
                                {
                                    var userId = userResponse.Id;

                                    var userGamesResult = await supabase.From<Models.UserGameModel>().Filter("user_id", Postgrest.Constants.Operator.Equals, userId).Get();
                                    foreach (var cg in userGamesResult.Models)
                                    {
                                        await supabase.From<Models.UserGameModel>().Filter("id", Postgrest.Constants.Operator.Equals, cg.Id).Delete();
                                    }

                                    var profileResult = await supabase.From<Models.ProfileModel>().Select("*").Filter("id", Postgrest.Constants.Operator.Equals, userId).Get();
                                    var profile = profileResult.Models.FirstOrDefault();
                                    if (profile != null)
                                    {
                                        profile.Rank = "THE SILENT COMMANDER";
                                        profile.TotalPlaytime = 0;
                                        await supabase.From<Models.ProfileModel>().Update(profile);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to clear cloud data: {ex.Message}");
                        }
                    });

                    var profileService = new ProfileService();
                    var localProfile = profileService.LoadProfile();
                    localProfile.Title = "THE SILENT COMMANDER";
                    profileService.SaveProfile(localProfile);

                    var successDialog = new Views.ModernDialog("Success", "Integrated games library and cloud gameplay data have been cleared successfully.");
                    if (Application.Current?.MainWindow != null)
                        successDialog.Owner = Application.Current.MainWindow;
                    successDialog.ShowDialog();
                }
                catch (Exception ex)
                {
                    var errorDialog = new Views.ModernDialog("Error", $"Failed to clear library data: {ex.Message}");
                    if (Application.Current?.MainWindow != null)
                        errorDialog.Owner = Application.Current.MainWindow;
                    errorDialog.ShowDialog();
                }
            }
        }

        private void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error launching default browser for URL: {url}. Exception: {ex.Message}");
            }
        }
    }
}
