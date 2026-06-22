using System;
using Postgrest.Attributes;
using Postgrest.Models;

namespace Lunex.Models
{
    [Table("user_games")]
    public class UserGameModel : BaseModel
    {
        [PrimaryKey("id", false)]
        public string Id { get; set; } = string.Empty;

        [Column("user_id")]
        public string UserId { get; set; } = string.Empty;

        [Column("game_id")]
        public string GameId { get; set; } = string.Empty;

        [Column("game_title")]
        public string GameTitle { get; set; } = string.Empty;

        [Column("playtime_minutes")]
        public int PlaytimeMinutes { get; set; }

        [Column("last_played")]
        public DateTime? LastPlayed { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }
}
