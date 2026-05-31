using System;
using System.Text.Json.Serialization;

namespace Lunex.Models
{
    public class Game
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("exePath")]
        public string ExePath { get; set; } = string.Empty;

        [JsonPropertyName("coverPath")]
        public string? CoverPath { get; set; }

        [JsonPropertyName("iconPath")]
        public string? IconPath { get; set; }

        [JsonPropertyName("playTimeMinutes")]
        public int PlayTimeMinutes { get; set; }

        [JsonPropertyName("lastPlayed")]
        public DateTime? LastPlayed { get; set; }

        [JsonPropertyName("launchArguments")]
        public string LaunchArguments { get; set; } = string.Empty;

        public Game Clone()
        {
            return new Game
            {
                Id = this.Id,
                Title = this.Title,
                ExePath = this.ExePath,
                CoverPath = this.CoverPath,
                IconPath = this.IconPath,
                PlayTimeMinutes = this.PlayTimeMinutes,
                LastPlayed = this.LastPlayed,
                LaunchArguments = this.LaunchArguments
            };
        }
    }
}
