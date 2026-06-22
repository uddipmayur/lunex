using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Lunex.Models
{
    public class Game : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private T SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value)) return value;
            field = value;
            OnPropertyChanged(name);
            return value;
        }

        private string _id = string.Empty;
        [JsonPropertyName("id")]
        public string Id
        {
            get => _id;
            set => SetField(ref _id, value);
        }

        private string _title = string.Empty;
        [JsonPropertyName("title")]
        public string Title
        {
            get => _title;
            set => SetField(ref _title, value);
        }

        private string _exePath = string.Empty;
        [JsonPropertyName("exePath")]
        public string ExePath
        {
            get => _exePath;
            set
            {
                if (SetField(ref _exePath, value) != null)
                {
                    OnPropertyChanged(nameof(IsInstalled));
                }
            }
        }

        private string? _coverPath;
        [JsonPropertyName("coverPath")]
        public string? CoverPath
        {
            get => _coverPath;
            set => SetField(ref _coverPath, value);
        }

        private string? _iconPath;
        [JsonPropertyName("iconPath")]
        public string? IconPath
        {
            get => _iconPath;
            set => SetField(ref _iconPath, value);
        }

        private int _playTimeMinutes;
        [JsonPropertyName("playTimeMinutes")]
        public int PlayTimeMinutes
        {
            get => _playTimeMinutes;
            set => SetField(ref _playTimeMinutes, value);
        }

        private int _cloudPlayTimeMinutes;
        [JsonPropertyName("cloudPlayTimeMinutes")]
        public int CloudPlayTimeMinutes
        {
            get => _cloudPlayTimeMinutes;
            set => SetField(ref _cloudPlayTimeMinutes, value);
        }

        private DateTime? _lastPlayed;
        [JsonPropertyName("lastPlayed")]
        public DateTime? LastPlayed
        {
            get => _lastPlayed;
            set => SetField(ref _lastPlayed, value);
        }

        private string _launchArguments = string.Empty;
        [JsonPropertyName("launchArguments")]
        public string LaunchArguments
        {
            get => _launchArguments;
            set => SetField(ref _launchArguments, value);
        }

        // Transient state properties for UI feedback (ignored by serializer)
        private bool _isInstalling;
        [JsonIgnore]
        public bool IsInstalling
        {
            get => _isInstalling;
            set => SetField(ref _isInstalling, value);
        }

        private int _installProgress;
        [JsonIgnore]
        public int InstallProgress
        {
            get => _installProgress;
            set => SetField(ref _installProgress, value);
        }

        private bool _isHero;
        [JsonIgnore]
        public bool IsHero
        {
            get => _isHero;
            set => SetField(ref _isHero, value);
        }

        private bool _isRunning;
        [JsonIgnore]
        public bool IsRunning
        {
            get => _isRunning;
            set => SetField(ref _isRunning, value);
        }

        private System.Collections.Generic.List<PlaySession> _sessionHistory = new();
        [JsonPropertyName("sessionHistory")]
        public System.Collections.Generic.List<PlaySession> SessionHistory
        {
            get => _sessionHistory;
            set => SetField(ref _sessionHistory, value);
        }

        [JsonIgnore]
        public System.Collections.Generic.Dictionary<DayOfWeek, int> WeeklyActivity { get; set; } = new()
        {
            { DayOfWeek.Monday, 0 },
            { DayOfWeek.Tuesday, 0 },
            { DayOfWeek.Wednesday, 0 },
            { DayOfWeek.Thursday, 0 },
            { DayOfWeek.Friday, 0 },
            { DayOfWeek.Saturday, 0 },
            { DayOfWeek.Sunday, 0 }
        };

        public void PopulateWeeklyActivity()
        {
            WeeklyActivity[DayOfWeek.Monday] = 0;
            WeeklyActivity[DayOfWeek.Tuesday] = 0;
            WeeklyActivity[DayOfWeek.Wednesday] = 0;
            WeeklyActivity[DayOfWeek.Thursday] = 0;
            WeeklyActivity[DayOfWeek.Friday] = 0;
            WeeklyActivity[DayOfWeek.Saturday] = 0;
            WeeklyActivity[DayOfWeek.Sunday] = 0;

            DateTime today = DateTime.Today;
            int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
            DateTime startOfWeek = today.AddDays(-1 * diff).Date;
            DateTime endOfWeek = startOfWeek.AddDays(7);

            if (SessionHistory != null)
            {
                foreach (var session in SessionHistory)
                {
                    if (session.Timestamp >= startOfWeek && session.Timestamp < endOfWeek)
                    {
                        var day = session.Timestamp.DayOfWeek;
                        WeeklyActivity[day] += session.DurationMinutes;
                    }
                }
            }
        }

        [JsonIgnore]
        public bool IsInstalled => !string.IsNullOrEmpty(ExePath) && System.IO.File.Exists(ExePath);

        public Game Clone()
        {
            var cloned = new Game
            {
                Id = this.Id,
                Title = this.Title,
                ExePath = this.ExePath,
                CoverPath = this.CoverPath,
                IconPath = this.IconPath,
                PlayTimeMinutes = this.PlayTimeMinutes,
                CloudPlayTimeMinutes = this.CloudPlayTimeMinutes,
                LastPlayed = this.LastPlayed,
                LaunchArguments = this.LaunchArguments,
                IsInstalling = this.IsInstalling,
                InstallProgress = this.InstallProgress,
                IsHero = this.IsHero
            };
            if (this.SessionHistory != null)
            {
                cloned.SessionHistory = new System.Collections.Generic.List<PlaySession>(this.SessionHistory);
            }
            return cloned;
        }
    }

    public class PlaySession
    {
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("durationMinutes")]
        public int DurationMinutes { get; set; }
    }
}

