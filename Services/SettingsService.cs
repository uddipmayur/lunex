using System;
using System.IO;
using System.Text.Json;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Lunex.Services
{
    public class SettingsData
    {
        public bool LaunchAtStartup { get; set; } = true;
        public bool MinimizeToTray { get; set; } = true;
        public bool EnableGlowBorders { get; set; } = true;
        public bool DefaultsUpgraded { get; set; } = false;
    }

    public class SettingsService : INotifyPropertyChanged
    {
        private static readonly Lazy<SettingsService> _instance = new(() => new SettingsService());
        public static SettingsService Instance => _instance.Value;

        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
        private readonly string _storageFilePath;
        private SettingsData _data;

        public event PropertyChangedEventHandler? PropertyChanged;

        private SettingsService()
        {
            var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lunex");
            if (!Directory.Exists(appDataDir))
            {
                Directory.CreateDirectory(appDataDir);
            }
            _storageFilePath = Path.Combine(appDataDir, "lunex_settings.json");
            _data = LoadSettings();

            if (!_data.DefaultsUpgraded)
            {
                _data.MinimizeToTray = true;
                _data.DefaultsUpgraded = true;
                SaveSettings();
            }

            // Always read the real startup state from the registry so the app
            // toggle reflects what the installer (or user) set externally.
            _data.LaunchAtStartup = ReadStartupFromRegistry();
        }

        private SettingsData LoadSettings()
        {
            try
            {
                if (File.Exists(_storageFilePath))
                {
                    var jsonContent = File.ReadAllText(_storageFilePath);
                    var loaded = JsonSerializer.Deserialize<SettingsData>(jsonContent);
                    if (loaded != null)
                    {
                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading settings: {ex.Message}");
            }
            return new SettingsData();
        }

        public void SaveSettings()
        {
            try
            {
                var jsonContent = JsonSerializer.Serialize(_data, _jsonOptions);
                File.WriteAllText(_storageFilePath, jsonContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving settings: {ex.Message}");
            }
        }

        public bool LaunchAtStartup
        {
            get => _data.LaunchAtStartup;
            set
            {
                if (_data.LaunchAtStartup != value)
                {
                    _data.LaunchAtStartup = value;
                    OnPropertyChanged();
                    SaveSettings();
                    ApplyLaunchAtStartup(value);
                }
            }
        }

        public bool MinimizeToTray
        {
            get => _data.MinimizeToTray;
            set
            {
                if (_data.MinimizeToTray != value)
                {
                    _data.MinimizeToTray = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public bool EnableGlowBorders
        {
            get => _data.EnableGlowBorders;
            set
            {
                if (_data.EnableGlowBorders != value)
                {
                    _data.EnableGlowBorders = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void ApplyLaunchAtStartup(bool enable)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null)
                {
                    if (enable)
                    {
                        var path = Environment.ProcessPath;
                        if (!string.IsNullOrEmpty(path))
                        {
                            key.SetValue("Lunex", $"\"{path}\"");
                        }
                    }
                    else
                    {
                        key.DeleteValue("Lunex", false);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to set startup registry key: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads the actual Windows startup registry entry to determine if Lunex
        /// is registered to launch on login — the ground truth regardless of JSON.
        /// </summary>
        private static bool ReadStartupFromRegistry()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
                return key?.GetValue("Lunex") != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
