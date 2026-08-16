using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.data.Entities.Moments;

public enum MomentChoice
{
    YES = 1,      // legacy — treated as MAGICAL
    NO = 2,       // legacy — treated as LOGICAL
    PENDING = 3,  // legacy — Save (removed, kept for old rows)
    MAGICAL = 4,  // felt/emotional yes
    LOGICAL = 5,  // reasoned/compatibility yes
    PASS = 6,     // soft skip — never triggers match
}

[Table("moment_responses")]
public class MomentResponse
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("date_utc")]
    public DateOnly DateUtc { get; set; }

    [Column("from_user_id")]
    public int FromUserId { get; set; }

    [Column("to_user_id")]
    public int ToUserId { get; set; }

    [Column("choice")]
    public MomentChoice Choice { get; set; }

    [Column("time_on_card_ms")]
    public int? TimeOnCardMs { get; set; }

    [Column("source")]
    [MaxLength(20)]
    public string? Source { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
