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

                    // Extract and set dominant color brush
                    var dominantBrush = ExtractDominantColorBrush(bitmap);
                    detailVm.SetDominantColorBrush(dominantBrush);
                }
                catch
                {
                    BannerCover.Source = null;
                    detailVm.SetDominantColorBrush(null);
                }
            }
            else
            {
                BannerCover.Source = null;
                detailVm.SetDominantColorBrush(null);
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

        private System.Windows.Media.SolidColorBrush ExtractDominantColorBrush(BitmapSource bitmap)
        {
            System.Windows.Media.SolidColorBrush result = (System.Windows.Media.SolidColorBrush)System.Windows.Application.Current.Resources["PrimaryBrush"];
            try
            {
                if (bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0)
                {
                    int width = bitmap.PixelWidth;
                    int height = bitmap.PixelHeight;

                    int startX = 0;
                    int endX = width / 2;
                    int startY = height / 2;
                    int endY = height;
                    int qWidth = endX - startX;
                    int qHeight = endY - startY;

                    if (qWidth > 0 && qHeight > 0)
                    {
                        var converted = new FormatConvertedBitmap();
                        converted.BeginInit();
                        converted.Source = bitmap;
                        converted.DestinationFormat = System.Windows.Media.PixelFormats.Bgra32;
                        converted.EndInit();

                        int stride = qWidth * 4;
                        byte[] pixels = new byte[qHeight * stride];
                        var rect = new System.Windows.Int32Rect(startX, startY, qWidth, qHeight);
                        converted.CopyPixels(rect, pixels, stride, 0);

                        long totalR = 0;
                        long totalG = 0;
                        long totalB = 0;
                        int sampleCount = 0;

                        for (int row = 0; row < 5; row++)
                        {
                            int y = row * (qHeight - 1) / 4;
                            for (int col = 0; col < 5; col++)
                            {
                                int x = col * (qWidth - 1) / 4;
                                int index = (y * stride) + (x * 4);

                                totalB += pixels[index];
                                totalG += pixels[index + 1];
                                totalR += pixels[index + 2];
                                sampleCount++;
                            }
                        }

                        double avgR = (double)totalR / sampleCount;
                        double avgG = (double)totalG / sampleCount;
                        double avgB = (double)totalB / sampleCount;

                        RgbToHsl(avgR, avgG, avgB, out double h, out double s, out double l);
                        s = Math.Clamp(s * 1.3, 0.0, 1.0);
                        HslToRgb(h, s, l, out byte r, out byte g, out byte b);

                        result = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
                        result.Freeze();
                    }
                }
            }
            catch
            {
                // Fallback to PrimaryBrush
            }
            return result;
        }

        private static void RgbToHsl(double r, double g, double b, out double h, out double s, out double l)
        {
            r /= 255.0;
            g /= 255.0;
            b /= 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            h = s = l = (max + min) / 2.0;

            if (max == min)
            {
                h = s = 0; // achromatic
            }
            else
            {
                double d = max - min;
                s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);

                if (max == r)
                {
                    h = (g - b) / d + (g < b ? 6.0 : 0.0);
                }
                else if (max == g)
                {
                    h = (b - r) / d + 2.0;
                }
                else if (max == b)
                {
                    h = (r - g) / d + 4.0;
                }

                h /= 6.0;
            }
        }

        private static void HslToRgb(double h, double s, double l, out byte r, out byte g, out byte b)
        {
            double rTemp, gTemp, bTemp;

            if (s == 0)
            {
                rTemp = gTemp = bTemp = l; // achromatic
            }
            else
            {
                double q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
                double p = 2.0 * l - q;

                rTemp = HueToRgb(p, q, h + 1.0 / 3.0);
                gTemp = HueToRgb(p, q, h);
                bTemp = HueToRgb(p, q, h - 1.0 / 3.0);
            }

            r = (byte)Math.Clamp(Math.Round(rTemp * 255.0), 0, 255);
            g = (byte)Math.Clamp(Math.Round(gTemp * 255.0), 0, 255);
            b = (byte)Math.Clamp(Math.Round(bTemp * 255.0), 0, 255);
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1.0;
            if (t > 1) t -= 1.0;
            if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
            return p;
        }
    }
}
