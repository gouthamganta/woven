using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.Data.Entities;

[Table("ab_experiments")]
public class AbExperiment
{
    [Key][Column("id")][MaxLength(50)] public string Id { get; set; } = string.Empty;
    [Column("variants", TypeName = "jsonb")] public string Variants { get; set; } = "[\"control\",\"treatment\"]";
    [Column("is_active")] public bool IsActive { get; set; } = true;
    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
