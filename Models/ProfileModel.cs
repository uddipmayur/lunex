using System;
using Postgrest.Attributes;
using Postgrest.Models;

namespace Lunex.Models
{
    [Table("profiles")]
    public class ProfileModel : BaseModel
    {
        [PrimaryKey("id", false)]
        public string Id { get; set; } = string.Empty;

        [Column("email")]
        public string? Email { get; set; }

        [Column("username")]
        public string? Username { get; set; }

        [Column("title")]
        public string? Title { get; set; }

        [Column("avatar_url")]
        public string? AvatarUrl { get; set; }

        [Column("provider_type")]
        public string? ProviderType { get; set; }

        [Column("rank")]
        public string? Rank { get; set; }

        [Column("total_playtime")]
        public int TotalPlaytime { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [Column("rawg_api_key")]
        public string? RawgApiKey { get; set; }
    }
}
