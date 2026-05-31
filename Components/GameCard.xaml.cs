using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lunex.Models;
using Lunex.ViewModels;

namespace Lunex.Components
{
    public partial class GameCard : UserControl
    {
        private Game? _game;

        public GameCard()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        public static ImageSource? GetGameIcon(string? iconPath, string? exePath)
        {
            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(iconPath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    return bitmap;
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                try
                {
                    using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath))
                    {
                        if (icon != null)
                        {
                            return System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                icon.Handle,
                                Int32Rect.Empty,
                                BitmapSizeOptions.FromEmptyOptions());
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is Game game)
            {
                _game = game;
                TxtTitle.Text = game.Title;
                TxtPlaytime.Text = FormatPlayTime(game.PlayTimeMinutes);

                // Try to load custom cover art
                if (!string.IsNullOrEmpty(game.CoverPath) && File.Exists(game.CoverPath))
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(game.CoverPath);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();

                        CoverImage.Source = bitmap;
                        CoverImage.Visibility = Visibility.Visible;
                        FallbackBanner.Visibility = Visibility.Collapsed;
                    }
                    catch
                    {
                        CoverImage.Visibility = Visibility.Collapsed;
                        FallbackBanner.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    CoverImage.Visibility = Visibility.Collapsed;
                    FallbackBanner.Visibility = Visibility.Visible;
                }

                // Load game icon
                var iconSource = GetGameIcon(game.IconPath, game.ExePath);
                CardIconImage.Source = iconSource;
                CardIconBorder.Visibility = iconSource != null ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private string FormatPlayTime(int minutes)
        {
            if (minutes == 0) return "0 hours";
            if (minutes < 60) return $"{minutes} mins";
            int hours = minutes / 60;
            int remaining = minutes % 60;
            if (remaining == 0) return $"{hours} hours";
            return $"{hours}h {remaining}m";
        }

        private void OnCardClick(object sender, MouseButtonEventArgs e)
        {
            if (_game == null) return;

            // Look up parent LibraryViewModel to trigger click navigation
            var parent = FindParentViewModel();
            if (parent != null)
            {
                parent.SelectGameCommand.Execute(_game);
            }
        }

        private LibraryViewModel? FindParentViewModel()
        {
            DependencyObject parent = VisualTreeHelper.GetParent(this);
            while (parent != null)
            {
                if (parent is FrameworkElement element && element.DataContext is LibraryViewModel libraryVm)
                {
                    return libraryVm;
                }
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }
    }
}
