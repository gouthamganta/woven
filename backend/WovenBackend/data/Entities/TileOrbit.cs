using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WovenBackend.data.Entities.Moments;

namespace WovenBackend.Data.Entities;

[Table("tile_orbits")]
public class TileOrbit
{
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("orbiter_id")]
    public int OrbiterId { get; set; }

    [Column("tile_id")]
    public Guid TileId { get; set; }

    [Column("tile_owner_id")]
    public int TileOwnerId { get; set; }

    [Column("relationship_type")]
    [MaxLength(10)]
    public string RelationshipType { get; set; } = default!;

    [Column("orbited_at")]
    public DateTimeOffset OrbitedAt { get; set; } = DateTimeOffset.UtcNow;

    public Tile Tile { get; set; } = default!;
}
