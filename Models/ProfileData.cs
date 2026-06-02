using System.Text.Json.Serialization;

namespace Lunex.Models
{
    public class ProfileData
    {
        [JsonPropertyName("username")]
        public string Username { get; set; } = "Lunex";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "THE SILENT COMMANDER";

        [JsonPropertyName("dpPath")]
        public string? DpPath { get; set; }
    }
}
