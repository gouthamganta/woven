using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.Data.Entities;

[Table("ab_assignments")]
public class AbAssignment
{
    [Key][Column("id")] public long Id { get; set; }
    [Column("user_id")] public int UserId { get; set; }
    [Column("experiment_id")][MaxLength(50)] public string ExperimentId { get; set; } = string.Empty;
    [Column("variant")][MaxLength(50)] public string Variant { get; set; } = string.Empty;
    [Column("assigned_at")] public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;
}
