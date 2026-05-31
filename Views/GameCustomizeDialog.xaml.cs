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
        private bool? _pendingDialogResult;
        private bool _isCloseAnimationCompleted = false;

        public GameCustomizeDialog(Game game)
        {
            InitializeComponent();
            _game = game;
            
            TitleText.Text = $"CUSTOMIZE: {game.Title.ToUpper()}";
            TxtGameTitle.Text = game.Title;
            _tempCoverPath = game.CoverPath;
            _tempIconPath = game.IconPath;

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
            _pendingDialogResult = result;
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isCloseAnimationCompleted)
            {
                e.Cancel = true;
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
                UpdatePreviews();
            }
        }

        private void ClearCover_Click(object sender, RoutedEventArgs e)
        {
            _tempCoverPath = null;
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

            // Update local model
            _game.Title = title;
            _game.CoverPath = _tempCoverPath;
            _game.IconPath = _tempIconPath;

            // Save to service
            var libraryService = Services.LibraryService.Instance;
            libraryService.UpdateGame(_game);

            CloseWithAnimation(true);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            CloseWithAnimation(false);
        }
    }
}
