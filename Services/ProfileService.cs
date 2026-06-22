using System;
using System.IO;
using System.Text.Json;
using Lunex.Models;

namespace Lunex.Services
{
    public class ProfileService
    {
        public static event EventHandler? ProfileUpdated;

        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
        private readonly string _storageFilePath;

        public ProfileService()
        {
            var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lunex");
            if (!Directory.Exists(appDataDir))
            {
                Directory.CreateDirectory(appDataDir);
            }
            _storageFilePath = Path.Combine(appDataDir, "lunex_profile.json");
        }

        public ProfileData LoadProfile()
        {
            try
            {
                if (File.Exists(_storageFilePath))
                {
                    var jsonContent = File.ReadAllText(_storageFilePath);
                    return JsonSerializer.Deserialize<ProfileData>(jsonContent) ?? GetDefaultProfile();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading profile: {ex.Message}");
            }
            return GetDefaultProfile();
        }

        public void SaveProfile(ProfileData profile)
        {
            try
            {
                var jsonContent = JsonSerializer.Serialize(profile, _jsonOptions);
                File.WriteAllText(_storageFilePath, jsonContent);
                ProfileUpdated?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving profile: {ex.Message}");
            }
        }

        private ProfileData GetDefaultProfile()
        {
            return new ProfileData
            {
                Username = "Lunex",
                Title = "THE SILENT COMMANDER",
                DpPath = null
            };
        }
    }
}
