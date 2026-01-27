using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.data.Entities.Moments;

public enum MomentChoice { YES = 1, NO = 2, PENDING = 3 }

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

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
