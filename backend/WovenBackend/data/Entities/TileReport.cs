using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WovenBackend.data.Entities.Moments;

namespace WovenBackend.Data.Entities;

[Table("tile_reports")]
public class TileReport
{
    [Key][Column("id")] public Guid Id { get; set; } = Guid.NewGuid();
    [Column("tile_id")] public Guid TileId { get; set; }
    public Tile Tile { get; set; } = default!;
    [Column("reporter_id")] public int ReporterId { get; set; }
    [Column("reason")][MaxLength(100)] public string Reason { get; set; } = default!;
    [Column("reported_at")] public DateTimeOffset ReportedAt { get; set; } = DateTimeOffset.UtcNow;
}
