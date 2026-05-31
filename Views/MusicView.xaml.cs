using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Lunex.ViewModels;

namespace Lunex.Views
{
    public partial class MusicView : UserControl
    {
        private readonly List<Border> _eqBars = new();
        private readonly List<Border> _eqPeaks = new();
        private readonly double[] _eqBarHeights = new double[32];
        private readonly double[] _eqPeakHeights = new double[32];
        private readonly double[] _eqPeakVelocities = new double[32];
        private readonly DispatcherTimer _eqTimer;
        private readonly Random _rand = new();
        private double _eqAnimationPhase = 0;

        public MusicView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;

            // Equalizer timer loop - runs at ~30 FPS
            _eqTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _eqTimer.Tick += OnEqTimerTick;
        }

        private bool _webViewReady = false;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            GenerateEqualizerColumns();
            UpdatePlaylistVisibility();

            if (DataContext is MusicViewModel musicVm)
            {
                musicVm.PropertyChanged += OnViewModelPropertyChanged;
                musicVm.LocalSongs.CollectionChanged += OnLocalSongsCollectionChanged;
                ToggleEqualizerTimer(musicVm.IsPlaying);

                // slide-in animation for list items
                AnimatePlaylistCards();

                // webview lazy loader
                if (!_webViewReady)
                {
                    _ = InitializeWebViewAsync(musicVm);
                }
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _eqTimer.Stop();
            if (DataContext is MusicViewModel musicVm)
            {
                musicVm.PropertyChanged -= OnViewModelPropertyChanged;
                musicVm.LocalSongs.CollectionChanged -= OnLocalSongsCollectionChanged;
            }
        }

        private async System.Threading.Tasks.Task InitializeWebViewAsync(MusicViewModel musicVm)
        {
            try
            {
                // use custom appdata folder to store web cookies, otherwise logins get lost
                var userDataDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Lunex", "WebView2Data");

                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                    userDataFolder: userDataDir);

                await WebBrowser.EnsureCoreWebView2Async(env);

                var settings = WebBrowser.CoreWebView2.Settings;

                // DO NOT FUCKING TOUCH THIS UA STRING. Google and Spotify will block our ass if they detect WebView2, so we spoof Chrome.
                settings.UserAgent =
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                    "AppleWebKit/537.36 (KHTML, like Gecko) " +
                    "Chrome/125.0.0.0 Safari/537.36";

                // block context menus to make it feel like a native app
                settings.AreDefaultContextMenusEnabled = false;

                // toggle loading screen while page loads
                WebBrowser.NavigationStarting += (_, _) => ShowLoadingOverlay();
                WebBrowser.NavigationCompleted += (_, _) => HideLoadingOverlay();

                _webViewReady = true;

                // if we are web-bound on init, boot the browser
                if (musicVm.ActiveSource != MusicSource.Local)
                    NavigateWebView(musicVm.WebPortalUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WebView2 init error: {ex.Message}");
            }
        }

        private void ShowLoadingOverlay()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(ShowLoadingOverlay));
                return;
            }

            WebBrowser.Visibility = Visibility.Hidden;
            WebLoadingOverlay.Visibility = Visibility.Visible;

            // trigger continuous spin animation on spinner
            var spin = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(0.85),
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
            };
            SpinnerRotate.BeginAnimation(
                System.Windows.Media.RotateTransform.AngleProperty, spin);
        }

        private void HideLoadingOverlay()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(HideLoadingOverlay));
                return;
            }

            // kill animation so we don't hog CPU cycles
            SpinnerRotate.BeginAnimation(
                System.Windows.Media.RotateTransform.AngleProperty, null);
            WebLoadingOverlay.Visibility = Visibility.Collapsed;
            WebBrowser.Visibility = Visibility.Visible;
        }

        private void NavigateWebView(string url)
        {
            if (!_webViewReady) return;

            try
            {
                // run JS hack to mute previous page videos/audio so they don't overlap playback
                _ = WebBrowser.CoreWebView2.ExecuteScriptAsync(
                    "document.querySelectorAll('video, audio').forEach(media => media.pause());");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error pausing media: {ex.Message}");
            }

            if (string.IsNullOrEmpty(url))
            {
                WebBrowser.CoreWebView2.Navigate("about:blank");
                return;
            }

            ShowLoadingOverlay();
            WebBrowser.CoreWebView2.Navigate(url);
        }

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender is MusicViewModel musicVm)
            {
                if (e.PropertyName == nameof(MusicViewModel.IsPlaying))
                {
                    ToggleEqualizerTimer(musicVm.IsPlaying);
                }
                else if (e.PropertyName == nameof(MusicViewModel.LocalSongs))
                {
                    UpdatePlaylistVisibility();
                }
                else if (e.PropertyName == nameof(MusicViewModel.WebPortalUrl))
                {
                    // user clicked a different tab, send browser to new endpoint
                    NavigateWebView(musicVm.WebPortalUrl);
                }
            }
        }

        private void UpdatePlaylistVisibility()
        {
            if (DataContext is MusicViewModel musicVm)
            {
                if (musicVm.LocalSongs.Count > 0)
                {
                    SongsList.Visibility = Visibility.Visible;
                    PlaylistEmptyPanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    SongsList.Visibility = Visibility.Collapsed;
                    PlaylistEmptyPanel.Visibility = Visibility.Visible;
                }
            }
        }

        // fake equalizer physics simulation since we don't actualy decode raw audio buffer
        private void GenerateEqualizerColumns()
        {
            EqGrid.Children.Clear();
            _eqBars.Clear();
            _eqPeaks.Clear();

            int barCount = 32;
            Array.Clear(_eqBarHeights, 0, barCount);
            Array.Clear(_eqPeakHeights, 0, barCount);
            Array.Clear(_eqPeakVelocities, 0, barCount);

            var primaryBrush = (Brush)Application.Current.Resources["PrimaryBrush"];
            var secondaryBrush = (Brush)Application.Current.Resources["SecondaryBrush"];
            var tertiaryBrush = (Brush)Application.Current.Resources["TertiaryBrush"];

            // fancy gradient stops for neon vibe
            var brushGradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            brushGradient.GradientStops.Add(new GradientStop(((SolidColorBrush)tertiaryBrush).Color, 0.0));
            brushGradient.GradientStops.Add(new GradientStop(((SolidColorBrush)secondaryBrush).Color, 0.5));
            brushGradient.GradientStops.Add(new GradientStop(Color.FromRgb(79, 70, 229), 1.0)); // primaryContainer color

            for (int i = 0; i < barCount; i++)
            {
                var colGrid = new Grid
                {
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Margin = new Thickness(1.5, 0, 1.5, 0)
                };

                // faint background guide line
                var track = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(8, 255, 255, 255)),
                    Width = 2,
                    CornerRadius = new CornerRadius(1),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                colGrid.Children.Add(track);

                // actual equalizer bar
                var bar = new Border
                {
                    Background = brushGradient,
                    Width = 6,
                    CornerRadius = new CornerRadius(3, 3, 0, 0),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Height = 0 // init empty
                };
                colGrid.Children.Add(bar);
                _eqBars.Add(bar);

                // peak decay dot
                var peak = new Border
                {
                    Background = tertiaryBrush, // neon cyan
                    Width = 6,
                    Height = 2,
                    CornerRadius = new CornerRadius(1),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 0)
                };
                colGrid.Children.Add(peak);
                _eqPeaks.Add(peak);

                EqGrid.Children.Add(colGrid);
            }
        }

        private void ToggleEqualizerTimer(bool isPlaying)
        {
            // run timer non-stop so the wave decays smoothly instead of snapping to 0
            _eqTimer.Start();
        }

        private void OnEqTimerTick(object? sender, EventArgs e)
        {
            if (_eqBars.Count == 0 || _eqPeaks.Count == 0) return;

            bool isPlaying = false;
            double volume = 0.75;

            if (DataContext is MusicViewModel musicVm)
            {
                isPlaying = musicVm.IsPlaying;
                volume = musicVm.Volume;
            }

            _eqAnimationPhase += 0.12;

            int barCount = _eqBars.Count;
            double maxHeight = EqGrid.ActualHeight;
            if (maxHeight <= 0) maxHeight = 140.0; // fallback

            for (int i = 0; i < barCount; i++)
            {
                double targetHeight = 0;

                if (!isPlaying)
                {
                    // standby mode - breathing sinus wave from center outwards
                    double distanceFromCenter = Math.Abs(i - (barCount / 2.0));
                    double breathingWave = Math.Sin(_eqAnimationPhase * 1.5 - distanceFromCenter * 0.25);
                    targetHeight = 5 + (3 + 3 * breathingWave) * (1 - (distanceFromCenter / (barCount / 1.5)));
                    if (targetHeight < 4) targetHeight = 4;
                }
                else
                {
                    // simulate fake audio spectrum bands (bass, mids, highs)
                    double frequencyIntensity = 0;

                    // bass: make the left side bounce hard on the beat
                    if (i < 8)
                    {
                        double beatSpeed = 2.4;
                        double beatVal = Math.Max(0, Math.Sin(_eqAnimationPhase * beatSpeed));
                        double subBassNoise = _rand.NextDouble() * 0.4;
                        frequencyIntensity = (0.5 * beatVal + subBassNoise) * 1.2;
                    }
                    // mids: noise combined with smooth sin waves
                    else if (i >= 8 && i <= 22)
                    {
                        double waveA = Math.Sin(_eqAnimationPhase * 3.0 + i * 0.5);
                        double waveB = Math.Cos(_eqAnimationPhase * 1.8 - i * 0.3);
                        double midNoise = _rand.NextDouble() * 0.35;
                        frequencyIntensity = 0.35 + 0.3 * waveA + 0.2 * waveB + midNoise;
                    }
                    // high treble: jittery noise
                    else
                    {
                        double trebleNoise = _rand.NextDouble() * 0.65;
                        double waveHigh = Math.Sin(_eqAnimationPhase * 5.0 + i * 0.8);
                        frequencyIntensity = 0.1 + 0.2 * waveHigh + trebleNoise;
                    }

                    // scale by slider volume
                    frequencyIntensity = Math.Max(0.05, frequencyIntensity * volume);
                    targetHeight = frequencyIntensity * (maxHeight - 10);
                }

                if (targetHeight < 2) targetHeight = 2;
                if (targetHeight > maxHeight - 5) targetHeight = maxHeight - 5;

                // linear interpolation so bars don't jitter like crazy
                double currentHeight = _eqBarHeights[i];
                if (targetHeight > currentHeight)
                {
                    currentHeight = currentHeight * 0.25 + targetHeight * 0.75;
                }
                else
                {
                    currentHeight = currentHeight * 0.6 + targetHeight * 0.4;
                }
                _eqBarHeights[i] = currentHeight;
                _eqBars[i].Height = currentHeight;

                // fake gravity physics for peak dots
                double peakHeight = _eqPeakHeights[i];
                double velocity = _eqPeakVelocities[i];

                if (currentHeight >= peakHeight)
                {
                    peakHeight = currentHeight;
                    velocity = 0;
                }
                else
                {
                    double gravity = 0.4;
                    velocity += gravity;
                    peakHeight -= velocity;

                    if (peakHeight < currentHeight)
                    {
                        peakHeight = currentHeight;
                        velocity = 0;
                    }
                }

                _eqPeakHeights[i] = peakHeight;
                _eqPeakVelocities[i] = velocity;

                // render the peak dot above the bar
                double peakMargin = peakHeight + 1.0;
                if (peakMargin > maxHeight - 3) peakMargin = maxHeight - 3;
                _eqPeaks[i].Margin = new Thickness(0, 0, 0, peakMargin);
            }
        }

        private void PlaySelectedSong(object sender, MouseButtonEventArgs e)
        {
            if (SongsList.SelectedItem is string trackName && DataContext is MusicViewModel musicVm)
            {
                musicVm.ActiveTrackTitle = trackName;
                musicVm.IsPlaying = true;
            }
        }

        // list animations

        private void OnLocalSongsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdatePlaylistVisibility();
            if (e.Action == NotifyCollectionChangedAction.Add ||
                e.Action == NotifyCollectionChangedAction.Reset)
            {
                AnimatePlaylistCards();
            }
        }

        private void AnimatePlaylistCards()
        {
            if (SongsList.Items.Count == 0) return;

            // DO NOT TOUCH THIS - priority must be Background so WPF finishes rendering list before we animate them
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                int count = SongsList.Items.Count;
                if (count == 0) return;

                // dynamic stagger layout timing
                double maxStaggerTotalSeconds = 0.6; // Stagger start times across 600ms
                double staggerDelaySeconds = Math.Min(0.12, maxStaggerTotalSeconds / count);
                double animationDurationSeconds = 0.35; // 350ms duration per card

                for (int i = 0; i < count; i++)
                {
                    var container = SongsList.ItemContainerGenerator.ContainerFromIndex(i) as ListViewItem;
                    if (container != null)
                    {
                        // hide initially for fade-in
                        container.Opacity = 0;
                        
                        var translate = new TranslateTransform(40, 0);
                        container.RenderTransform = translate;
                        container.RenderTransformOrigin = new Point(0.5, 0.5);

                        double beginTimeSeconds = i * staggerDelaySeconds;

                        // fade animation
                        var fadeIn = new DoubleAnimation
                        {
                            From = 0,
                            To = 1,
                            Duration = TimeSpan.FromSeconds(animationDurationSeconds),
                            BeginTime = TimeSpan.FromSeconds(beginTimeSeconds),
                            DecelerationRatio = 0.8
                        };

                        // slide translation
                        var slideIn = new DoubleAnimation
                        {
                            From = 40,
                            To = 0,
                            Duration = TimeSpan.FromSeconds(animationDurationSeconds),
                            BeginTime = TimeSpan.FromSeconds(beginTimeSeconds),
                            DecelerationRatio = 0.8
                        };

                        container.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                        translate.BeginAnimation(TranslateTransform.XProperty, slideIn);
                    }
                }
            }));
        }
    }
}
