using System;
using System.Collections.Generic;
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
            "CONNECTING TO LUNEX DATABASE...",
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

            // Use a DispatcherTimer for the boot delay so it stays on the UI thread.
            // Avoids any DataContext timing race that Task.Delay + ContinueWith can hit.
            var bootTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(2800)
            };
            bootTimer.Tick += (s, ev) =>
            {
                bootTimer.Stop();

                // Re-read DataContext at tick time — it is guaranteed to be set by now
                if (DataContext is SplashViewModel splashVm)
                {
                    splashVm.CompleteBoot();
                }
            };
            bootTimer.Start();
        }
    }
}
