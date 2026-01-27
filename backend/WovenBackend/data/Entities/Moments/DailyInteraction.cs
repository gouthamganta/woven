using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.data.Entities.Moments;

[Table("daily_interactions")]
public class DailyInteraction
{
    [Column("user_id")]
    public int UserId { get; set; }

    // UTC day key
    [Column("date_utc")]
    public DateOnly DateUtc { get; set; }

    [Column("total_used")]
    public short TotalUsed { get; set; } = 0;

    [Column("pending_used")]
    public short PendingUsed { get; set; } = 0;

    [Column("games_initiated")]
    public int GamesInitiated { get; set; } = 0;

    [Column("games_played")]
    public int GamesPlayed { get; set; } = 0;

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}