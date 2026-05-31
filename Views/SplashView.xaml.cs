using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Lunex.ViewModels;

namespace Lunex.Views
{
    public partial class SplashView : UserControl
    {
        private readonly List<string> _bootMessages = new()
        {
            "INITIALIZING SYSTEM CORES...",
            "CONNECTING TO LUNEX SHELL DATABASE...",
            "SYNCING COMPONENT SUBSYSTEMS...",
            "OPTIMIZING DISPLAY RENDERING..."
        };
        private int _messageIndex = 0;
        private DispatcherTimer? _consoleTimer;

        public SplashView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Start spinning loader and pulsing logo storyboards
            var rotateStoryboard = (Storyboard)Resources["RotateLoader"];
            var pulseStoryboard = (Storyboard)Resources["PulseLogo"];
            rotateStoryboard.Begin(this, true);
            pulseStoryboard.Begin(this, true);

            // Typewriter terminal timer loop
            _consoleTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(550)
            };
            _consoleTimer.Tick += (s, ev) =>
            {
                if (_messageIndex < _bootMessages.Count - 1)
                {
                    _messageIndex++;
                    ConsoleBlock.Text = _bootMessages[_messageIndex];
                }
                else
                {
                    _consoleTimer.Stop();
                }
            };
            _consoleTimer.Start();

            // Perform automatic routing boot complete after 2800ms
            Task.Delay(2800).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (DataContext is SplashViewModel splashVm)
                    {
                        splashVm.CompleteBoot();
                    }
                });
            });
        }
    }
}
