using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.Data.Entities;

[Table("cf_scores")]
public class CfScore
{
    [Column("user_id")]
    public int UserId { get; set; }

    [Column("candidate_id")]
    public int CandidateId { get; set; }

    [Column("score")]
    public double Score { get; set; }

    [Column("computed_at")]
    public DateTimeOffset ComputedAt { get; set; } = DateTimeOffset.UtcNow;
}
