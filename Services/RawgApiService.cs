using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Lunex.Models;

namespace Lunex.Services
{
    public class RawgApiService
    {
        private static readonly Lazy<RawgApiService> _instance = new(() => new RawgApiService());
        public static RawgApiService Instance => _instance.Value;

        // Use the API key from user settings
        private string? ApiKey => SettingsService.Instance.RawgApiKey;
        private const string BaseUrl = "https://api.rawg.io/api";

        private readonly HttpClient _httpClient;
        
        private RawgApiService()
        {
            _httpClient = new HttpClient();
            // RAWG API prefers requests with User-Agent
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Lunex-Launcher");
        }

        public async Task<RawgGameDetails?> SearchGameAsync(string title)
        {
            if (string.IsNullOrWhiteSpace(ApiKey)) return null;
            if (string.IsNullOrWhiteSpace(title)) return null;

            try
            {
                // Normalize the raw exe/folder title into a human-readable search query.
                var normalizedTitle = NormalizeGameTitle(title);

                // Fetch top 10 candidates — more candidates = better chance of finding the exact game
                var searchUrl = $"{BaseUrl}/games?search={Uri.EscapeDataString(normalizedTitle)}&key={ApiKey}&page_size=10&search_precise=true";
                var response = await _httpClient.GetAsync(searchUrl);

                if (!response.IsSuccessStatusCode) return null;

                var content = await response.Content.ReadAsStringAsync();
                var searchResult = JsonSerializer.Deserialize<RawgSearchResponse>(content);

                if (searchResult?.Results == null || !searchResult.Results.Any()) return null;

                // Score each candidate and pick the best match above confidence threshold
                var bestMatch = FindBestMatch(normalizedTitle, searchResult.Results);
                if (bestMatch == null)
                {
                    Console.WriteLine($"[RAWG] No confident match for '{normalizedTitle}' — skipping to avoid wrong metadata.");
                    return null;
                }

                Console.WriteLine($"[RAWG] Matched '{normalizedTitle}' → '{bestMatch.Name}' (id={bestMatch.Id})");

                // Fetch full details by the confirmed RAWG ID
                var detailsUrl = $"{BaseUrl}/games/{bestMatch.Id}?key={ApiKey}";
                var detailsResponse = await _httpClient.GetAsync(detailsUrl);

                if (!detailsResponse.IsSuccessStatusCode) return null;

                var detailsContent = await detailsResponse.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<RawgGameDetails>(detailsContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching from RAWG: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Converts a raw executable/folder name into a space-separated, human-readable title.
        /// Handles CamelCase, PascalCase, underscores, dashes, and runs of digits.
        /// e.g. "Cyberpunk2077"  → "Cyberpunk 2077"
        ///      "subnautica2"    → "Subnautica 2"
        ///      "red_dead_2"     → "Red Dead 2"
        ///      "GTA_V"          → "GTA V"
        /// </summary>
        private static string NormalizeGameTitle(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;

            // Replace underscores and dashes with spaces
            var s = raw.Replace('_', ' ').Replace('-', ' ');

            // Insert space between a lowercase/digit and an uppercase letter (camelCase/PascalCase boundary)
            s = System.Text.RegularExpressions.Regex.Replace(s, @"([a-z\d])([A-Z])", "$1 $2");

            // Insert space between a letter and a run of digits, or vice versa
            s = System.Text.RegularExpressions.Regex.Replace(s, @"([A-Za-z])(\d)", "$1 $2");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"(\d)([A-Za-z])", "$1 $2");

            // Collapse multiple spaces and trim
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();

            return s;
        }

        /// <summary>
        /// Scores each RAWG search result against the local game title and returns the best
        /// match only if its confidence is above a threshold. Returns null if no result is
        /// confident enough, preventing wrong metadata from being assigned.
        ///
        /// Scoring tiers (highest to lowest priority):
        ///   100 — Exact case-insensitive title match
        ///    80 — RAWG slug matches normalized title (e.g. "cyberpunk-2077" ↔ "Cyberpunk 2077")
        ///    60 — All words in the query appear in the result name (and vice versa)
        ///    40 — Word overlap ≥ 80 % on both sides
        ///   -∞  — Result is a strict subset of query OR query is a strict subset of result
        ///          (prevents "Subnautica" matching "Subnautica 2")
        /// </summary>
        private static RawgGameResult? FindBestMatch(string normalizedTitle, System.Collections.Generic.List<RawgGameResult> candidates)
        {
            const int MinConfidence = 60; // Below this we refuse to assign metadata

            var queryWords = TokenizeTitle(normalizedTitle);
            if (queryWords.Count == 0) return null;

            RawgGameResult? best = null;
            int bestScore = -1;

            foreach (var candidate in candidates)
            {
                int score = ScoreCandidate(normalizedTitle, queryWords, candidate);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return bestScore >= MinConfidence ? best : null;
        }

        private static int ScoreCandidate(string normalizedTitle, System.Collections.Generic.HashSet<string> queryWords, RawgGameResult candidate)
        {
            var candidateName = candidate.Name ?? string.Empty;

            // Tier 1: Exact match (case-insensitive)
            if (string.Equals(candidateName, normalizedTitle, StringComparison.OrdinalIgnoreCase))
                return 100;

            // Tier 2: Slug match — compare RAWG slug against our normalized title turned into a slug
            // e.g. "Cyberpunk 2077" → "cyberpunk-2077"
            var querySlug = normalizedTitle.ToLowerInvariant().Replace(' ', '-');
            var candidateSlug = candidateName.ToLowerInvariant().Replace(' ', '-');
            if (querySlug == candidateSlug)
                return 80;

            // Tier 3: Word-level analysis
            var candidateWords = TokenizeTitle(candidateName);
            if (candidateWords.Count == 0) return 0;

            // Count how many query words appear in the candidate name
            int matchedInCandidate = queryWords.Count(w => candidateWords.Contains(w));
            // Count how many candidate words appear in the query
            int matchedInQuery = candidateWords.Count(w => queryWords.Contains(w));

            double queryCoverage    = (double)matchedInCandidate / queryWords.Count;
            double candidateCoverage = (double)matchedInQuery / candidateWords.Count;

            // Penalize hard: if either side is a strict subset of the other.
            // This prevents "Subnautica" (1 word) from matching "Subnautica 2" (2 words)
            // and "Cyberpunk" from matching "Cyberpunk 2077".
            if (queryWords.Count != candidateWords.Count)
            {
                bool queryIsSubset     = matchedInCandidate == queryWords.Count && queryWords.Count < candidateWords.Count;
                bool candidateIsSubset = matchedInQuery == candidateWords.Count && candidateWords.Count < queryWords.Count;
                if (queryIsSubset || candidateIsSubset)
                    return 0; // Hard reject — one is a proper subset of the other
            }

            // Tier 3a: All words match bidirectionally
            if (queryCoverage >= 1.0 && candidateCoverage >= 1.0)
                return 75;

            // Tier 3b: High overlap (≥ 80%) on both sides
            if (queryCoverage >= 0.8 && candidateCoverage >= 0.8)
                return 60;

            // Tier 3c: Moderate overlap — not confident enough
            return (int)(Math.Min(queryCoverage, candidateCoverage) * 50);
        }

        /// <summary>Splits a title into a set of lowercase word tokens, filtering out noise words.</summary>
        private static System.Collections.Generic.HashSet<string> TokenizeTitle(string title)
        {
            var stopWords = new System.Collections.Generic.HashSet<string> { "the", "a", "an", "of", "and", "in", "for", "to" };
            var words = title.ToLowerInvariant()
                .Split(new[] { ' ', '-', ':', '\'', '.', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 1 && !stopWords.Contains(w))
                .ToHashSet();
            return words;
        }

        
        public async Task<RawgGameDetails?> GetGameByIdAsync(string idOrSlug)
        {
            if (string.IsNullOrWhiteSpace(ApiKey)) return null;
            if (string.IsNullOrWhiteSpace(idOrSlug)) return null;

            try
            {
                var detailsUrl = $"{BaseUrl}/games/{Uri.EscapeDataString(idOrSlug)}?key={ApiKey}";
                var detailsResponse = await _httpClient.GetAsync(detailsUrl);
                
                if (!detailsResponse.IsSuccessStatusCode) return null;

                var detailsContent = await detailsResponse.Content.ReadAsStringAsync();
                var gameDetails = JsonSerializer.Deserialize<RawgGameDetails>(detailsContent);

                return gameDetails;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching from RAWG by ID: {ex.Message}");
                return null;
            }
        }
        
        public async Task<byte[]?> DownloadImageAsync(string? imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl)) return null;

            try
            {
                // Validate URL domain to prevent arbitrary downloads
                if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
                {
                    var host = uri.Host.ToLowerInvariant();
                    if (!host.EndsWith("rawg.io") && !host.EndsWith("rawg.media"))
                    {
                        Console.WriteLine($"[RAWG] Rejected image download from untrusted domain: {host}");
                        return null;
                    }
                }
                else
                {
                    return null;
                }

                // Check content length before downloading (10MB max)
                const long MaxImageSize = 10 * 1024 * 1024;
                using var headResponse = await _httpClient.SendAsync(
                    new HttpRequestMessage(HttpMethod.Head, imageUrl), HttpCompletionOption.ResponseHeadersRead);
                if (headResponse.Content.Headers.ContentLength > MaxImageSize)
                {
                    Console.WriteLine($"[RAWG] Image too large ({headResponse.Content.Headers.ContentLength} bytes), skipping: {imageUrl}");
                    return null;
                }

                var data = await _httpClient.GetByteArrayAsync(imageUrl);
                if (data.Length > MaxImageSize)
                {
                    Console.WriteLine($"[RAWG] Downloaded image exceeded max size ({data.Length} bytes), discarding.");
                    return null;
                }
                return data;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading image from {imageUrl}: {ex.Message}");
                return null;
            }
        }
    }
}
