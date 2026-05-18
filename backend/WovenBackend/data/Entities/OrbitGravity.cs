using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.Data.Entities;

[Table("orbit_gravity")]
public class OrbitGravity
{
    [Column("user_id")]
    public int UserId { get; set; }

    [Column("candidate_id")]
    public int CandidateId { get; set; }

    [Column("score")]
    public double Score { get; set; }

    [Column("last_orbit_at")]
    public DateTimeOffset LastOrbitAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
