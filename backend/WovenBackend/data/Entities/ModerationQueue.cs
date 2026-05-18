using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WovenBackend.data.Entities.Moments;

namespace WovenBackend.Data.Entities;

[Table("moderation_queue")]
public class ModerationQueue
{
    [Key][Column("id")] public Guid Id { get; set; } = Guid.NewGuid();
    [Column("tile_id")] public Guid TileId { get; set; }
    public Tile Tile { get; set; } = default!;
    [Column("user_id")] public int UserId { get; set; }
    [Column("queued_at")] public DateTimeOffset QueuedAt { get; set; } = DateTimeOffset.UtcNow;
    [Column("reviewed_at")] public DateTimeOffset? ReviewedAt { get; set; }
    [Column("reviewer_id")] public int? ReviewerId { get; set; }
    [Column("decision")][MaxLength(20)] public string? Decision { get; set; }
    [Column("reject_reason")][MaxLength(200)] public string? RejectReason { get; set; }
}
