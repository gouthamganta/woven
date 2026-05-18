using System.ComponentModel.DataAnnotations.Schema;
using WovenBackend.data.Entities.Moments;

namespace WovenBackend.Data.Entities;

[Table("tile_views")]
public class TileView
{
    [Column("user_id")]
    public int UserId { get; set; }

    [Column("tile_id")]
    public Guid TileId { get; set; }

    [Column("viewed_at")]
    public DateTimeOffset ViewedAt { get; set; } = DateTimeOffset.UtcNow;

    [Column("duration_ms")]
    public int? DurationMs { get; set; }

    public Tile Tile { get; set; } = default!;
}
