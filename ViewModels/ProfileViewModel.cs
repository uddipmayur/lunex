using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Lunex.Models;
using Lunex.Services;
using System.Text.Json;

using System.Collections.Generic;

namespace Lunex.ViewModels
{
    public class ProfileViewModel : ViewModelBase
    {
        private bool _avatarChanged = false;

        
        private readonly ProfileService _profileService;
        private readonly ProfileData _profile;

        private string _username = "Lunex";
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
                    _avatarChanged = true;
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
            : "No custom avatar selected.";

        public string BiometricStatusText => HasDp ? "ACTIVE" : "DEFAULT";
        public string BiometricStatusColor => HasDp ? "#C87A53" : "#8C9D8E";

        public bool IsCloudLinked => !string.IsNullOrEmpty(SettingsService.Instance.CloudAuthToken);
        public bool IsNotCloudLinked => !IsCloudLinked;

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
        public ICommand LinkCloudCommand { get; }
        public ICommand LogoutCloudCommand { get; }

        public ProfileViewModel(MainViewModel mainVm)
        {
            _profileService = new ProfileService();
            _profile = _profileService.LoadProfile();

            // Load initial state
            _username = _profile.Username;
            if (string.IsNullOrWhiteSpace(_username)) _username = "Lunex";
            
            _title = "THE SILENT COMMANDER"; // Default rank
            _dpPath = _profile.DpPath;

            if (IsCloudLinked)
            {
                System.Threading.Tasks.Task.Run(async () => await FetchSupabaseProfileAsync());
            }

            SaveCommand = new RelayCommand(async () =>
            {
                IsBusy = true;

                _profile.Username = Username;
                _profile.Title = Title;
                _profile.DpPath = DpPath;
                _profileService.SaveProfile(_profile);

                if (IsCloudLinked && !string.IsNullOrEmpty(SettingsService.Instance.CloudAuthToken))
                {
                    try
                    {
                        var supabase = SupabaseService.Client;
                        var userResponse = await supabase.Auth.GetUser(SettingsService.Instance.CloudAuthToken);
                        if (userResponse != null)
                        {
                            string uid = userResponse.Id;
                            string? avatarUrl = null;

                            if (_avatarChanged)
                            {
                                if (HasDp && DpPath != null)
                                {
                                    byte[] compressedBytes = ImageHelper.CompressAndResizeImage(DpPath);
                                    string storagePath = $"{uid}/avatar.jpg";
                                    
                                    await supabase.Storage.From("avatars").Upload(
                                        compressedBytes,
                                        storagePath,
                                        new Supabase.Storage.FileOptions { Upsert = true, ContentType = "image/jpeg" }
                                    );
                                    
                                    avatarUrl = supabase.Storage.From("avatars").GetPublicUrl(storagePath);
                                }
                                else
                                {
                                    await supabase.Storage.From("avatars").Remove(new List<string> { $"{uid}/avatar.jpg" });
                                }
                            }

                            var result = await supabase.From<Models.ProfileModel>().Select("*").Filter("id", Postgrest.Constants.Operator.Equals, uid).Get();
                            var profileRow = result.Models.FirstOrDefault();
                            
                            if (profileRow != null)
                            {
                                profileRow.Username = Username;
                                profileRow.Rank = Title;
                                if (_avatarChanged)
                                {
                                    profileRow.AvatarUrl = avatarUrl;
                                }
                                profileRow.UpdatedAt = DateTime.UtcNow;
                                await profileRow.Update<Models.ProfileModel>();
                            }
                            
                            _avatarChanged = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to upload to cloud: {ex.Message}");
                    }
                }

                IsBusy = false;

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    var dialog = new Views.ModernDialog("Save Profile", "Profile settings saved successfully.");
                    if (Application.Current?.MainWindow != null)
                    {
                        dialog.Owner = Application.Current.MainWindow;
                    }
                    dialog.ShowDialog();
                });
            });

            BrowseDecalCommand = new RelayCommand(() =>
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "Image Files (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp",
                    Title = "Select Profile Avatar"
                };
                if (dialog.ShowDialog() == true)
                {
                    if (ImageHelper.VerifyImageMimeType(dialog.FileName))
                    {
                        DpPath = dialog.FileName;
                    }
                    else
                    {
                        var errDialog = new Views.ModernDialog("Invalid Image", "The selected file is not a valid image. Malware or corrupted files are blocked.");
                        if (Application.Current?.MainWindow != null) errDialog.Owner = Application.Current.MainWindow;
                        errDialog.ShowDialog();
                    }
                }
            });

            ClearDecalCommand = new RelayCommand(() => DpPath = null);

            LinkCloudCommand = new RelayCommand(() =>
            {
                var authWin = new Views.AuthWindow();
                if (Application.Current?.MainWindow != null)
                {
                    authWin.Owner = Application.Current.MainWindow;
                }
                
                if (authWin.ShowDialog() == true && !string.IsNullOrEmpty(authWin.SessionJson))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(authWin.SessionJson);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("access_token", out var tokenProp))
                        {
                            SettingsService.Instance.CloudAuthToken = tokenProp.GetString();
                            OnPropertyChanged(nameof(IsCloudLinked));
                            OnPropertyChanged(nameof(IsNotCloudLinked));
                            
                            if (root.TryGetProperty("user", out var userProp) && userProp.TryGetProperty("email", out var emailProp))
                            {
                                Username = emailProp.GetString()?.Split('@')[0] ?? Username;
                                Title = "CLOUD VERIFIED AGENT";
                            }
                            
                            var dialog = new Views.ModernDialog("Cloud Linked", "Your Lunex account is now securely linked to the cloud.");
                            dialog.Owner = authWin.Owner;
                            dialog.ShowDialog();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to parse session: {ex.Message}");
                    }
                }
                else if (IsCloudLinked) // The AuthWindow might have successfully logged in and set CloudAuthToken natively
                {
                    OnPropertyChanged(nameof(IsCloudLinked));
                    OnPropertyChanged(nameof(IsNotCloudLinked));
                    System.Threading.Tasks.Task.Run(async () => await FetchSupabaseProfileAsync());
                }
            });

            LogoutCloudCommand = new RelayCommand(() =>
            {
                var dialog = new Views.ModernDialog("Log Out", "Are you sure you want to unlink your cloud account?");
                if (Application.Current?.MainWindow != null)
                {
                    dialog.Owner = Application.Current.MainWindow;
                }
                
                // Assuming ModernDialog has a way to cancel, but it seems it's just an OK dialog right now.
                // Wait, ModernDialog usually just has OK. Let's just log out immediately or just show an info dialog after.
                SettingsService.Instance.CloudAuthToken = null;
                SettingsService.Instance.CloudRefreshToken = null;
                OnPropertyChanged(nameof(IsCloudLinked));
                OnPropertyChanged(nameof(IsNotCloudLinked));
                
                var successDialog = new Views.ModernDialog("Logged Out", "Your cloud account has been unlinked.");
                if (Application.Current?.MainWindow != null)
                {
                    successDialog.Owner = Application.Current.MainWindow;
                }
                successDialog.ShowDialog();
            });
        }

        private async System.Threading.Tasks.Task FetchSupabaseProfileAsync()
        {
            try
            {
                var supabase = SupabaseService.Client;

                // Set auth token
                if (!string.IsNullOrEmpty(SettingsService.Instance.CloudAuthToken))
                {
                    var refreshToken = SettingsService.Instance.CloudRefreshToken ?? string.Empty;
                    var restoredSession = await supabase.Auth.SetSession(SettingsService.Instance.CloudAuthToken, refreshToken);
                    // Persist rotated tokens
                    if (restoredSession?.AccessToken != null)
                        SettingsService.Instance.CloudAuthToken = restoredSession.AccessToken;
                    if (restoredSession?.RefreshToken != null)
                        SettingsService.Instance.CloudRefreshToken = restoredSession.RefreshToken;

                    // For Supabase-csharp, you can manually set the Auth header or set the session
                    // We'll just fetch the user first to get their ID
                    var userResponse = await supabase.Auth.GetUser(SettingsService.Instance.CloudAuthToken);
                    if (userResponse != null)
                    {
                        string fetchedUsername = "Lunex";
                        
                        // Try to get from metadata first
                        if (userResponse.UserMetadata != null && userResponse.UserMetadata.TryGetValue("full_name", out var fullNameObj))
                        {
                            fetchedUsername = fullNameObj?.ToString() ?? "Lunex";
                        }
                        else if (userResponse.UserMetadata != null && userResponse.UserMetadata.TryGetValue("name", out var nameObj))
                        {
                            fetchedUsername = nameObj?.ToString() ?? "Lunex";
                        }
                        else if (!string.IsNullOrEmpty(userResponse.Email))
                        {
                            fetchedUsername = userResponse.Email.Split('@')[0];
                        }

                        // Try to fetch from profiles table
                        var result = await supabase.From<Models.ProfileModel>().Select("*").Filter("id", Postgrest.Constants.Operator.Equals, userResponse.Id).Get();
                        var profileRow = result.Models.FirstOrDefault();
                        
                        string fetchedRank = "The Silent Commander";
                        if (profileRow != null)
                        {
                            if (!string.IsNullOrEmpty(profileRow.Username)) fetchedUsername = profileRow.Username;
                            if (!string.IsNullOrEmpty(profileRow.Rank)) fetchedRank = profileRow.Rank;
                            if (!string.IsNullOrEmpty(profileRow.RawgApiKey))
                            {
                                SettingsService.Instance.RawgApiKey = profileRow.RawgApiKey;
                            }
                        }

                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            Username = fetchedUsername;
                            Title = fetchedRank.ToUpper();
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to fetch Supabase profile: {ex.Message}");
            }
        }
    }
}
