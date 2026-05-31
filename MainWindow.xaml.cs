using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Lunex.ViewModels;

namespace Lunex
{
    public partial class MainWindow : Window
    {
        private bool _isExiting = false;
        private System.Windows.Forms.NotifyIcon? _notifyIcon;

        public MainWindow()
        {
            InitializeComponent();
            
            // Initialize main MVVM data binding context
            var mainVm = new MainViewModel();
            DataContext = mainVm;

            // Share visual context to sidebar navigation panel
            SidebarNavPanel.DataContext = mainVm;

            // Handle border corners when maximized/restored
            StateChanged += (s, e) =>
            {
                if (WindowState == WindowState.Maximized)
                {
                    MainShellBorder.CornerRadius = new CornerRadius(0);
                    MainShellBorder.BorderThickness = new Thickness(0);
                }
                else
                {
                    MainShellBorder.CornerRadius = new CornerRadius(16);
                    MainShellBorder.BorderThickness = new Thickness(1);
                }
            };

            // Maximize by default on startup
            WindowState = WindowState.Maximized;

            // Bind closing and setup tray icon
            Closing += MainWindow_Closing;
            InitializeTrayIcon();

            // Handle minimizing/restoring window when a game runs
            Services.LibraryService.Instance.GameRunningStateChanged += (gameId, isRunning) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (isRunning)
                    {
                        Hide();
                    }
                    else
                    {
                        ShowWindow();
                    }
                });
            };
        }

        private void InitializeTrayIcon()
        {
            try
            {
                _notifyIcon = new System.Windows.Forms.NotifyIcon();
                _notifyIcon.Text = "Lunex";
                
                var iconUri = new Uri("pack://application:,,,/app.ico", UriKind.RelativeOrAbsolute);
                var streamInfo = Application.GetResourceStream(iconUri);
                if (streamInfo != null)
                {
                    _notifyIcon.Icon = new System.Drawing.Icon(streamInfo.Stream);
                }
                else
                {
                    _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
                }
                
                _notifyIcon.Visible = true;
                _notifyIcon.DoubleClick += (s, e) => ShowWindow();

                var contextMenu = new System.Windows.Forms.ContextMenuStrip();
                
                var showItem = new System.Windows.Forms.ToolStripMenuItem("Show Lunex");
                showItem.Click += (s, e) => ShowWindow();
                contextMenu.Items.Add(showItem);

                var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit");
                exitItem.Click += (s, e) => ExitApplication();
                contextMenu.Items.Add(exitItem);

                _notifyIcon.ContextMenuStrip = contextMenu;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize tray icon: {ex.Message}");
            }
        }

        public void ShowWindow()
        {
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Maximized;
            }
            Activate();
        }

        private void ExitApplication()
        {
            _isExiting = true;
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            Application.Current.Shutdown();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExiting && Services.SettingsService.Instance.MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
            }
            else
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                }
            }
        }

        private void SuppressDrag(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private void CloseWindow(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MinimizeWindow(object sender, RoutedEventArgs e)
        {
            SystemCommands.MinimizeWindow(this);
        }

        private void MaximizeWindow(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                SystemCommands.RestoreWindow(this);
            }
            else
            {
                SystemCommands.MaximizeWindow(this);
            }
        }
    }
}