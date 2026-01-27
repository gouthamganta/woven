using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.data.Entities.Moments;

[Table("blocks")]
public class Block
{
    [Column("blocker_id")]
    public int BlockerId { get; set; }

    [Column("blocked_id")]
    public int BlockedId { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}