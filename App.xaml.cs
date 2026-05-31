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

            // silent update check on startup - fire-and-forget, hope it doesn't crash on thread pool
            _ = UpdateService.Instance.CheckAndDownloadOnLaunchAsync();

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
            MessageBox.Show(
                $"Unhandled UI Exception:\n\n{e.Exception.GetType().Name}: {e.Exception.Message}\n\nStack Trace:\n{e.Exception.StackTrace}",
                "Lunex - Crash",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            MessageBox.Show(
                $"Fatal Exception:\n\n{ex?.GetType().Name}: {ex?.Message}\n\nStack Trace:\n{ex?.StackTrace}",
                "Lunex - Fatal Crash",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
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
