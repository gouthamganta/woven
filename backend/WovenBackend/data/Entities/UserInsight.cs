using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.Data.Entities;

[Table("user_insights")]
public class UserInsight
{
    [Key]
    [Column("user_id")]
    public int UserId { get; set; }

    [Column("insights_json")]
    public string InsightsJson { get; set; } = "[]";

    [Column("opinion_text")]
    [MaxLength(300)]
    public string? OpinionText { get; set; }

    [Column("opinion_trigger")]
    [MaxLength(50)]
    public string? OpinionTrigger { get; set; }

    [Column("opinion_submitted_at")]
    public DateTimeOffset? OpinionSubmittedAt { get; set; }

    [Column("computed_at")]
    public DateTimeOffset ComputedAt { get; set; } = DateTimeOffset.UtcNow;
}
