using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.data.Entities.Moments;

[Table("date_feedback")]
public class DateFeedback
{
    [Key]
    [Column("Id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("MatchId")]
    public Guid MatchId { get; set; }

    [Column("UserId")]
    public int UserId { get; set; }

    [Column("MetInPerson")]
    public bool MetInPerson { get; set; } = false;

    [Column("Stars")]
    public int? Stars { get; set; }

    [Column("FeltRightText")]
    [MaxLength(300)]
    public string? FeltRightText { get; set; }

    [Column("FeltOffText")]
    [MaxLength(300)]
    public string? FeltOffText { get; set; }

    [Column("MeetAgain")]
    [MaxLength(10)]
    public string? MeetAgain { get; set; }

    [Column("CreatedAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
