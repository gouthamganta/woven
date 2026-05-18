using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WovenBackend.data.Entities.Moments;

namespace WovenBackend.Data.Entities;

[Table("tile_engagement")]
public class TileEngagement
{
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("tile_id")]
    public Guid TileId { get; set; }

    [Column("engagement_type")]
    [MaxLength(20)]
    public string EngagementType { get; set; } = default!;

    [Column("duration_ms")]
    public int? DurationMs { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Tile Tile { get; set; } = default!;
}
