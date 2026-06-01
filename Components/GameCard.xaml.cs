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
        private static readonly System.Collections.Generic.Dictionary<string, SolidColorBrush> _dominantColorCache = new();

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
                    bitmap.Freeze();
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
            if (e.OldValue is Game oldGame)
            {
                oldGame.PropertyChanged -= OnGamePropertyChanged;
            }

            if (e.NewValue is Game game)
            {
                _game = game;
                game.PropertyChanged += OnGamePropertyChanged;
                UpdateVisuals();
            }
        }

        private void OnGamePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Dispatcher.Invoke(UpdateVisuals);
        }

        private void UpdateVisuals()
        {
            if (_game == null) return;

            TxtTitle.Text = _game.Title.ToUpper();
            PlaytimeText.Text = FormatPlayTime(_game.PlayTimeMinutes);

            // Handle playtime progress bar value
            double hours = _game.PlayTimeMinutes / 60.0;
            double progressVal = Math.Min(Math.Log(hours + 1.0) / Math.Log(51.0) * 100.0, 100.0);
            PlaytimeProgressBar.Value = progressVal;

            // Apply dominant color to hover visuals
            var dominantBrush = GetDominantColorBrush(_game);
            HoverLeftBorder.Background = dominantBrush;
            InnerGlowBorder.BorderBrush = dominantBrush;

            // Handle installation state visually
            if (_game.IsInstalling)
            {
                StatusBadge.Visibility = Visibility.Visible;
                TxtStatusBadge.Text = $"Installing {_game.InstallProgress}%";
                InstallProgressBar.Value = _game.InstallProgress;
                InstallProgressBar.Visibility = Visibility.Visible;
            }
            else
            {
                InstallProgressBar.Visibility = Visibility.Collapsed;
                if (_game.IsInstalled)
                {
                    StatusBadge.Visibility = Visibility.Collapsed;
                }
                else
                {
                    StatusBadge.Visibility = Visibility.Visible;
                    TxtStatusBadge.Text = "Not Installed";
                }
            }

            // Try to load custom cover art
            if (!string.IsNullOrEmpty(_game.CoverPath))
            {
                bool isUrl = _game.CoverPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                             _game.CoverPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
                if (isUrl || File.Exists(_game.CoverPath))
                {
                    if (isUrl)
                    {
                        var coverPath = _game.CoverPath;
                        System.Threading.Tasks.Task.Run(() =>
                        {
                            try
                            {
                                var bitmap = new BitmapImage();
                                bitmap.BeginInit();
                                bitmap.UriSource = new Uri(coverPath);
                                bitmap.DecodePixelWidth = 284;
                                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                bitmap.EndInit();
                                bitmap.Freeze();
                                return bitmap;
                            }
                            catch
                            {
                                return null;
                            }
                        }).ContinueWith(t =>
                        {
                            if (t.Result != null && _game != null && _game.CoverPath == coverPath)
                            {
                                CoverImage.Source = t.Result;
                                CoverImage.Visibility = Visibility.Visible;
                                FallbackBanner.Visibility = Visibility.Collapsed;
                            }
                            else if (t.Result == null)
                            {
                                CoverImage.Source = null;
                                CoverImage.Visibility = Visibility.Collapsed;
                                FallbackBanner.Visibility = Visibility.Visible;
                            }
                        }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
                    }
                    else
                    {
                        try
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.UriSource = new Uri(_game.CoverPath);
                            bitmap.DecodePixelWidth = 284;
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.EndInit();
                            bitmap.Freeze();

                            CoverImage.Source = bitmap;
                            CoverImage.Visibility = Visibility.Visible;
                            FallbackBanner.Visibility = Visibility.Collapsed;
                        }
                        catch
                        {
                            CoverImage.Source = null;
                            CoverImage.Visibility = Visibility.Collapsed;
                            FallbackBanner.Visibility = Visibility.Visible;
                        }
                    }
                }
                else
                {
                    CoverImage.Source = null;
                    CoverImage.Visibility = Visibility.Collapsed;
                    FallbackBanner.Visibility = Visibility.Visible;
                }
            }
            else
            {
                CoverImage.Source = null;
                CoverImage.Visibility = Visibility.Collapsed;
                FallbackBanner.Visibility = Visibility.Visible;
            }
        }

        private SolidColorBrush GetDominantColorBrush(Game game)
        {
            if (_dominantColorCache.TryGetValue(game.Id, out var cachedBrush))
            {
                return cachedBrush;
            }

            SolidColorBrush result = (SolidColorBrush)Application.Current.Resources["PrimaryBrush"];

            if (!string.IsNullOrEmpty(game.CoverPath) && File.Exists(game.CoverPath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(game.CoverPath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 100;
                    bitmap.EndInit();

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
                            converted.DestinationFormat = PixelFormats.Bgra32;
                            converted.EndInit();

                            int stride = qWidth * 4;
                            byte[] pixels = new byte[qHeight * stride];
                            var rect = new Int32Rect(startX, startY, qWidth, qHeight);
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

                            result = new SolidColorBrush(Color.FromRgb(r, g, b));
                            result.Freeze();
                        }
                    }
                }
                catch
                {
                    // Fallback to PrimaryBrush
                }
            }

            _dominantColorCache[game.Id] = result;
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
