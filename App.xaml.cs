using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Lunex.Services;

namespace Lunex
{
    // WPF entry point boilerplate - do not break this class declaration
    public partial class App : Application
    {
        // DO NOT FUCKING TOUCH THESE - single-instance mutex logic is extremely brittle and will break user launches
        private static Mutex? _mutex;
        private static EventWaitHandle? _instanceEvent;
        private const string MutexName = "Local\\LunexSingleInstanceMutex";
        private const string EventName = "Local\\LunexSingleInstanceEvent";

        public App()
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            var pendingInstaller = UpdateService.GetPendingUpdateInstaller();
            if (!string.IsNullOrEmpty(pendingInstaller))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = pendingInstaller,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not launch pending update: {ex.Message}");
                }
                Shutdown();
                return;
            }

            // check if another instance is already running so we don't start duplicate processes like an idiot
            _mutex = new Mutex(true, MutexName, out bool isNewInstance);
            if (!isNewInstance)
            {
                try
                {
                    using var clientEvent = EventWaitHandle.OpenExisting(EventName);
                    clientEvent.Set();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not signal existing instance: {ex.Message}");
                }

                Shutdown();
                return;
            }

            _instanceEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                while (true)
                {
                    try
                    {
                        if (_instanceEvent.WaitOne())
                        {
                            Dispatcher.BeginInvoke(() =>
                            {
                                if (MainWindow is MainWindow mainWindow)
                                {
                                    mainWindow.ShowWindow();
                                }
                            });
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error waiting for instance event: {ex.Message}");
                    }
                }
            });

            // force update listener because devs hate out of date clients
            UpdateService.Instance.ForceUpdateRequired += OnForceUpdateRequired;

            // silent update check on startup - run on background thread to prevent any startup block
            _ = Task.Run(async () => await UpdateService.Instance.CheckAndDownloadOnLaunchAsync());

            // Initialize background cloud sync for gameplay data
            CloudSyncService.Instance.Initialize();

            bool isStartup = false;
            foreach (var arg in e.Args)
            {
                if (arg.Equals("-startup", StringComparison.OrdinalIgnoreCase))
                {
                    isStartup = true;
                    break;
                }
            }

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;

            if (!isStartup)
            {
                if (string.IsNullOrEmpty(SettingsService.Instance.CloudAuthToken) && !SettingsService.Instance.SkipLoginOnStartup)
                {
                    var authWin = new Views.AuthWindow();
                    authWin.ShowDialog();
                }
                mainWindow.Show();
            }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                UpdateService.Instance.CancelDownload();
                _instanceEvent?.Dispose();
                if (_mutex != null)
                {
                    _mutex.Dispose();
                }
            }
            catch { }
            base.OnExit(e);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogCrash("UI", e.Exception);
            MessageBox.Show(
                "An unexpected error occurred. Details have been saved to the crash log.\n\n" +
                "If this keeps happening, please report it with the crash log file.",
                "Lunex — Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            LogCrash("Fatal", ex);
            MessageBox.Show(
                "A fatal error occurred and Lunex needs to close.\n\n" +
                "Details have been saved to the crash log. Please report this issue.",
                "Lunex — Fatal Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private static void LogCrash(string category, Exception? ex)
        {
            try
            {
                var logDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lunex");
                if (!System.IO.Directory.Exists(logDir))
                    System.IO.Directory.CreateDirectory(logDir);
                var logPath = System.IO.Path.Combine(logDir, "crash_log.txt");
                var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{category}] {ex?.GetType().Name}: {ex?.Message}\n{ex?.StackTrace}\n{new string('-', 80)}\n";
                System.IO.File.AppendAllText(logPath, entry);
            }
            catch { /* logging must never throw */ }
        }

        private void OnForceUpdateRequired()
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    var dlg = new Views.ModernDialog(
                        "Update Required",
                        "A new update has been downloaded. Lunex must close and install the update to continue.");

                    dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    dlg.ShowDialog();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"A new update has been downloaded. Lunex must close and install the update to continue.\n\n(Error: {ex.Message})",
                        "Update Required",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                finally
                {
                    UpdateService.Instance.LaunchInstaller();
                    Shutdown();
                }
            });
        }
    }
}
