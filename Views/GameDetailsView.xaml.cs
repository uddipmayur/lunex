using System;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Lunex.ViewModels;

namespace Lunex.Views
{
    public partial class GameDetailsView : UserControl
    {
        public GameDetailsView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is GameDetailsViewModel oldVm)
            {
                oldVm.PropertyChanged -= OnViewModelPropertyChanged;
            }
            if (e.NewValue is GameDetailsViewModel detailVm)
            {
                detailVm.PropertyChanged += OnViewModelPropertyChanged;
                UpdateMedia(detailVm);
            }
        }

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender is GameDetailsViewModel detailVm && (e.PropertyName == nameof(GameDetailsViewModel.CoverPath) || e.PropertyName == nameof(GameDetailsViewModel.Title)))
            {
                UpdateMedia(detailVm);
            }
        }

        private void UpdateMedia(GameDetailsViewModel detailVm)
        {
            var coverPath = detailVm.CoverPath;
            if (!string.IsNullOrEmpty(coverPath) && File.Exists(coverPath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(coverPath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();

                    BannerCover.Source = bitmap;
                }
                catch
                {
                    BannerCover.Source = null;
                }
            }
            else
            {
                BannerCover.Source = null;
            }

            // Extract associated EXE icon if custom icon is not set
            var iconSource = Components.GameCard.GetGameIcon(detailVm.Game.IconPath, detailVm.Game.ExePath);
            GameIconImage.Source = iconSource;
            GameIconBorder.Visibility = iconSource != null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }

        private void OnOptionsClick(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is GameDetailsViewModel detailVm)
            {
                var game = detailVm.Game;
                if (game == null) return;

                var optionsDialog = new GameOptionsDialog(game.Title);
                if (System.Windows.Application.Current?.MainWindow != null)
                {
                    optionsDialog.Owner = System.Windows.Application.Current.MainWindow;
                }

                if (optionsDialog.ShowDialog() == true)
                {
                    if (optionsDialog.PlaySelected)
                    {
                        detailVm.PlayCommand.Execute(null);
                    }
                    else if (optionsDialog.CustomizeSelected)
                    {
                        var customizeDialog = new GameCustomizeDialog(game);
                        if (System.Windows.Application.Current?.MainWindow != null)
                        {
                            customizeDialog.Owner = System.Windows.Application.Current.MainWindow;
                        }

                        if (customizeDialog.ShowDialog() == true)
                        {
                            // Notify changes on viewmodel to refresh details
                            detailVm.NotifyPropertiesChanged();
                        }
                    }
                    else if (optionsDialog.RemoveSelected)
                    {
                        var confirmDialog = new ModernDialog("Remove Game", $"Are you sure you want to remove '{game.Title}' from your library?", isConfirmation: true);
                        if (System.Windows.Application.Current?.MainWindow != null)
                        {
                            confirmDialog.Owner = System.Windows.Application.Current.MainWindow;
                        }

                        if (confirmDialog.ShowDialog() == true && confirmDialog.Result)
                        {
                            var libraryService = Services.LibraryService.Instance;
                            libraryService.RemoveGame(game.Id);

                            // Navigate back to library
                            detailVm.BackCommand.Execute(null);
                        }
                    }
                }
            }
        }
    }
}
