using System.ComponentModel.DataAnnotations.Schema;


[Table("match_outcomes")]
public class MatchOutcome
{
    [Column("id")]
    public int Id { get; set; }

    // Optional: link to Match if one was created
    [Column("match_id")]
    public Guid? MatchId { get; set; }

    // Viewer (who saw the candidate)
    [Column("user_id")]
    public int UserId { get; set; }

    // Candidate shown
    [Column("candidate_id")]
    public int CandidateId { get; set; }

    // Date shown in daily deck
    [Column("date_utc")]
    public DateOnly DateUtc { get; set; }

    // Learning signals
    [Column("chat_started")]
    public bool ChatStarted { get; set; } = false;

    [Column("messages_24h")]
    public int Messages24h { get; set; } = 0;

    [Column("expired")]
    public bool Expired { get; set; } = false;

    [Column("unmatched")]
    public bool Unmatched { get; set; } = false;

    [Column("blocked")]
    public bool Blocked { get; set; } = false;

    [Column("recorded_at")]
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}