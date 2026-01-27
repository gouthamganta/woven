using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WovenBackend.Data.Entities;
using WovenBackend.data.Entities.Moments;

namespace WovenBackend.Data.Entities.Games;

/// <summary>
/// Tracks the outcome of game sessions for analytics and feedback loops.
/// </summary>
[Table("game_outcomes")]
public class GameOutcome
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("session_id")]
    public Guid SessionId { get; set; }

    [Column("game_type")]
    [MaxLength(50)]
    public string GameType { get; set; } = string.Empty;

    [Column("initiator_user_id")]
    public int InitiatorUserId { get; set; }

    [Column("partner_user_id")]
    public int PartnerUserId { get; set; }

    [Column("match_id")]
    public Guid MatchId { get; set; }

    // Game configuration
    [Column("difficulty")]
    [MaxLength(20)]
    public string Difficulty { get; set; } = "MEDIUM";

    [Column("tone")]
    [MaxLength(20)]
    public string Tone { get; set; } = "BALANCED";

    [Column("bucket")]
    [MaxLength(30)]
    public string Bucket { get; set; } = "EXPLORER";

    [Column("intent_alignment")]
    public double IntentAlignment { get; set; } = 0.5;

    // Game results
    [Column("total_rounds")]
    public int TotalRounds { get; set; }

    [Column("completed_rounds")]
    public int CompletedRounds { get; set; }

    [Column("initiator_score")]
    public int InitiatorScore { get; set; }

    [Column("partner_score")]
    public int PartnerScore { get; set; }

    [Column("average_response_time_ms")]
    public double AverageResponseTimeMs { get; set; }

    // Completion status
    [Column("completion_status")]
    [MaxLength(30)]
    public string CompletionStatus { get; set; } = "COMPLETED"; // COMPLETED, ABANDONED, EXPIRED

    [Column("user_feedback")]
    public string? UserFeedback { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public GameSession? Session { get; set; }
    public User? InitiatorUser { get; set; }
    public User? PartnerUser { get; set; }
    public Match? Match { get; set; }
}
