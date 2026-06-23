using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Lunex.Models
{
    public class RawgSearchResponse
    {
        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("results")]
        public List<RawgGameResult> Results { get; set; } = new();
    }

    public class RawgGameResult
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("released")]
        public string? Released { get; set; }

        [JsonPropertyName("background_image")]
        public string? BackgroundImage { get; set; }

        [JsonPropertyName("rating")]
        public double Rating { get; set; }
    }

    public class RawgGameDetails
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description_raw")]
        public string? DescriptionRaw { get; set; }

        [JsonPropertyName("released")]
        public string? Released { get; set; }

        [JsonPropertyName("background_image")]
        public string? BackgroundImage { get; set; }

        [JsonPropertyName("rating")]
        public double Rating { get; set; }

        [JsonPropertyName("developers")]
        public List<RawgDeveloper> Developers { get; set; } = new();

        [JsonPropertyName("publishers")]
        public List<RawgPublisher> Publishers { get; set; } = new();
    }

    public class RawgDeveloper
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    public class RawgPublisher
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }
}
