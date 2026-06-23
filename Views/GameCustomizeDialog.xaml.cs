using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Lunex.Models;
using Lunex.Components;

namespace Lunex.Views
{
    public partial class GameCustomizeDialog : Window
    {
        private readonly Game _game;
        private string? _tempCoverPath;
        private string? _tempIconPath;
        private bool _tempHasCustomCover; // tracks whether the pending cover is manually chosen
        private bool? _pendingDialogResult;
        private bool _isCloseAnimationCompleted = false;
        private bool _isClosingAnimationStarted = false;

        public GameCustomizeDialog(Game game)
        {
            InitializeComponent();
            _game = game;
            
            TitleText.Text = $"CUSTOMIZE: {game.Title.ToUpper()}";
            TxtGameTitle.Text = game.Title;
            _tempCoverPath = game.CoverPath;
            _tempIconPath = game.IconPath;
            _tempHasCustomCover = game.HasCustomCover;

            bool isSupabaseAuth = !string.IsNullOrEmpty(Services.SettingsService.Instance.CloudAuthToken);
            bool hasRawgKey = !string.IsNullOrWhiteSpace(Services.SettingsService.Instance.RawgApiKey);

            if (!isSupabaseAuth || !hasRawgKey)
            {
                RawgSyncLabel.Visibility = Visibility.Collapsed;
                TxtRawgId.Visibility = Visibility.Collapsed;
            }

            UpdatePreviews();
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void CloseWithAnimation(bool? result)
        {
            if (_isClosingAnimationStarted) return;
            _pendingDialogResult = result;
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isCloseAnimationCompleted)
            {
                e.Cancel = true;
                if (_isClosingAnimationStarted) return;
                _isClosingAnimationStarted = true;
                var sb = (System.Windows.Media.Animation.Storyboard)Resources["OnClosingStoryboard"];
                if (sb != null)
                {
                    sb.Completed += (s, ev) =>
                    {
                        _isCloseAnimationCompleted = true;
                        if (_pendingDialogResult.HasValue)
                        {
                            DialogResult = _pendingDialogResult.Value;
                        }
                        else
                        {
                            Close();
                        }
                    };
                    sb.Begin(this);
                }
                else
                {
                    _isCloseAnimationCompleted = true;
                    if (_pendingDialogResult.HasValue)
                    {
                        DialogResult = _pendingDialogResult.Value;
                    }
                    else
                    {
                        Close();
                    }
                }
            }
            base.OnClosing(e);
        }

        private void UpdatePreviews()
        {
            // 1. Cover Preview
            if (!string.IsNullOrEmpty(_tempCoverPath) && File.Exists(_tempCoverPath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(_tempCoverPath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    CoverPreview.Source = bitmap;
                    CoverFallbackIcon.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    CoverPreview.Source = null;
                    CoverFallbackIcon.Visibility = Visibility.Visible;
                }
            }
            else
            {
                CoverPreview.Source = null;
                CoverFallbackIcon.Visibility = Visibility.Visible;
            }

            // 2. Icon Preview
            var iconSource = GameCard.GetGameIcon(_tempIconPath, _game.ExePath);
            if (iconSource != null)
            {
                IconPreview.Source = iconSource;
                IconFallbackIcon.Visibility = Visibility.Collapsed;
            }
            else
            {
                IconPreview.Source = null;
                IconFallbackIcon.Visibility = Visibility.Visible;
            }
        }

        private void BrowseCover_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
                Title = "Select Custom Cover Image"
            };
            if (dialog.ShowDialog() == true)
            {
                _tempCoverPath = dialog.FileName;
                _tempHasCustomCover = true; // user picked a local file — protect from RAWG overwrite
                UpdatePreviews();
            }
        }

        private void ClearCover_Click(object sender, RoutedEventArgs e)
        {
            _tempCoverPath = null;
            _tempHasCustomCover = false; // cover cleared — RAWG may fill it again on next sync
            UpdatePreviews();
        }

        private void BrowseIcon_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.ico)|*.png;*.jpg;*.jpeg;*.ico",
                Title = "Select Custom Icon Image"
            };
            if (dialog.ShowDialog() == true)
            {
                _tempIconPath = dialog.FileName;
                UpdatePreviews();
            }
        }

        private void ClearIcon_Click(object sender, RoutedEventArgs e)
        {
            _tempIconPath = null;
            UpdatePreviews();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var title = TxtGameTitle.Text.Trim();
            if (string.IsNullOrEmpty(title))
            {
                var errorDialog = new ModernDialog("Validation Error", "Game title cannot be empty.");
                errorDialog.Owner = this;
                errorDialog.ShowDialog();
                return;
            }

            bool titleChanged = _game.Title != title;
            var rawgIdInput = TxtRawgId.Text.Trim();
            bool forceRawgSync = !string.IsNullOrEmpty(rawgIdInput);

            // Update local model
            _game.Title = title;
            _game.CoverPath = _tempCoverPath;
            _game.IconPath = _tempIconPath;
            _game.HasCustomCover = _tempHasCustomCover; // commit the custom-cover flag before RAWG sync fires

            // Save to service
            var libraryService = Services.LibraryService.Instance;
            
            bool isSupabaseAuth = !string.IsNullOrEmpty(Services.SettingsService.Instance.CloudAuthToken);
            bool hasRawgKey = !string.IsNullOrWhiteSpace(Services.SettingsService.Instance.RawgApiKey);

            if ((titleChanged || forceRawgSync) && isSupabaseAuth && hasRawgKey)
            {
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        Lunex.Models.RawgGameDetails? rawgData = null;
                        if (forceRawgSync)
                        {
                            rawgData = await Services.RawgApiService.Instance.GetGameByIdAsync(rawgIdInput);
                        }
                        else
                        {
                            rawgData = await Services.RawgApiService.Instance.SearchGameAsync(title);
                        }
                        
                        if (rawgData != null)
                        {
                            _game.RawgId = rawgData.Id;
                            _game.Description = rawgData.DescriptionRaw;
                            _game.Rating = rawgData.Rating;
                            _game.ReleaseDate = rawgData.Released;
                            _game.Developer = rawgData.Developers?.FirstOrDefault()?.Name;
                            _game.Publisher = rawgData.Publishers?.FirstOrDefault()?.Name;

                            if (!string.IsNullOrEmpty(rawgData.BackgroundImage))
                            {
                                await libraryService.CacheRemoteImageAsync(_game, rawgData.BackgroundImage);
                            }
                        }
                        
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            libraryService.UpdateGame(_game);
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error fetching RAWG details on rename: {ex.Message}");
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            libraryService.UpdateGame(_game);
                        });
                    }
                });
            }
            else
            {
                libraryService.UpdateGame(_game);
            }

            CloseWithAnimation(true);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            CloseWithAnimation(false);
        }
    }
}
