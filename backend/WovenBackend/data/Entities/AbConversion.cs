using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.Data.Entities;

[Table("ab_conversions")]
public class AbConversion
{
    [Key][Column("id")] public long Id { get; set; }
    [Column("user_id")] public int UserId { get; set; }
    [Column("experiment_id")][MaxLength(50)] public string ExperimentId { get; set; } = string.Empty;
    [Column("conversion_type")][MaxLength(100)] public string ConversionType { get; set; } = string.Empty;
    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
