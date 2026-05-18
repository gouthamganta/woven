using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.data.Entities.Moments;

[Table("highlights")]
public class Highlight
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("tile_id")]
    public Guid TileId { get; set; }

    public Tile Tile { get; set; } = default!;

    // 1–9: the slot on the user's profile grid this tile occupies
    [Column("slot_number")]
    public int SlotNumber { get; set; }

    [Column("pinned_at")]
    public DateTimeOffset PinnedAt { get; set; } = DateTimeOffset.UtcNow;
}
