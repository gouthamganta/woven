using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.data.Entities.Moments;

[Table("date_feedback_prompts")]
public class DateFeedbackPrompt
{
    [Key]
    [Column("Id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("MatchId")]
    public Guid MatchId { get; set; }

    [Column("UserId")]
    public int UserId { get; set; }

    [Column("TriggerType")]
    [MaxLength(20)]
    public string TriggerType { get; set; } = string.Empty;

    [Column("ScheduledFor")]
    public DateTimeOffset ScheduledFor { get; set; }

    [Column("RescheduleCount")]
    public int RescheduleCount { get; set; } = 0;

    [Column("SentAt")]
    public DateTimeOffset? SentAt { get; set; }

    [Column("RespondedAt")]
    public DateTimeOffset? RespondedAt { get; set; }
}
