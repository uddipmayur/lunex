using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Lunex.Services;
using Lunex.ViewModels;

namespace Lunex.Views
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;

            if (DataContext is SettingsViewModel vm)
            {
                vm.PropertyChanged += OnVmPropertyChanged;
                vm.AlreadyOnLatestVersion += OnAlreadyOnLatestVersion;
                vm.UpdateAvailableAndDownloading += OnUpdateAvailableAndDownloading;
                vm.UpdateCheckFailed += OnUpdateCheckFailed;
            }
        }

        // ── DataContext wiring ────────────────────────────────────────────────
        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is SettingsViewModel oldVm)
            {
                oldVm.PropertyChanged -= OnVmPropertyChanged;
                oldVm.AlreadyOnLatestVersion -= OnAlreadyOnLatestVersion;
                oldVm.UpdateAvailableAndDownloading -= OnUpdateAvailableAndDownloading;
                oldVm.UpdateCheckFailed -= OnUpdateCheckFailed;
            }
            if (e.NewValue is SettingsViewModel newVm)
            {
                newVm.PropertyChanged += OnVmPropertyChanged;
                newVm.AlreadyOnLatestVersion += OnAlreadyOnLatestVersion;
                newVm.UpdateAvailableAndDownloading += OnUpdateAvailableAndDownloading;
                newVm.UpdateCheckFailed += OnUpdateCheckFailed;
            }
        }

        // ── "Already on latest" dialog ────────────────────────────────────────
        // Called synchronously on the UI thread from the command continuation.
        // Do NOT wrap in Dispatcher.BeginInvoke here — the caller is already on
        // the UI thread and the extra dispatch causes ordering issues.
        private void OnAlreadyOnLatestVersion()
        {
            try
            {
                var dlg = new ModernDialog(
                    "Already on Latest Version",
                    "Already on the latest version");

                // Use the window hosting this control as the owner.
                // Window.GetWindow is safe for any depth of nesting.
                var owner = Window.GetWindow(this);
                if (owner != null)
                    dlg.Owner = owner;
                else
                    dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;

                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                // Fallback: surface any dialog error rather than swallowing it.
                MessageBox.Show(
                    $"Already on the latest version\n\n(Dialog error: {ex.Message})",
                    "Already on Latest Version",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void OnUpdateAvailableAndDownloading()
        {
            try
            {
                var dlg = new ModernDialog(
                    "Downloading New Update",
                    "Downloading new update");

                var owner = Window.GetWindow(this);
                if (owner != null)
                    dlg.Owner = owner;
                else
                    dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;

                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Downloading new update...\n\n(Dialog error: {ex.Message})",
                    "Downloading New Update",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void OnUpdateCheckFailed(string message)
        {
            try
            {
                var dlg = new ModernDialog(
                    "Update Check Failed",
                    message);

                var owner = Window.GetWindow(this);
                if (owner != null)
                    dlg.Owner = owner;
                else
                    dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;

                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"{message}\n\n(Dialog error: {ex.Message})",
                    "Update Check Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ── Spinner start / stop ──────────────────────────────────────────────
        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(SettingsViewModel.IsCheckingForUpdate)) return;

            Dispatcher.Invoke(() =>
            {
                var spinPath = FindVisualChildByTag<Path>(this, "SpinIcon");
                if (spinPath?.RenderTransform is not RotateTransform rt) return;

                if (sender is SettingsViewModel vm && vm.IsCheckingForUpdate)
                {
                    var anim = new DoubleAnimation(0, 360,
                        new Duration(TimeSpan.FromSeconds(0.9)))
                    {
                        RepeatBehavior = RepeatBehavior.Forever
                    };
                    rt.BeginAnimation(RotateTransform.AngleProperty, anim);
                }
                else
                {
                    // null stops the animation and restores the base value (0°)
                    rt.BeginAnimation(RotateTransform.AngleProperty, null);
                }
            });
        }

        // ── Visual-tree search by Tag ─────────────────────────────────────────
        private static T? FindVisualChildByTag<T>(DependencyObject parent, string tag)
            where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed && typed.Tag?.ToString() == tag)
                    return typed;
                var found = FindVisualChildByTag<T>(child, tag);
                if (found != null) return found;
            }
            return null;
        }
    }
}
