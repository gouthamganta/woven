using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.Data.Entities;

[Table("user_matching_weights")]
public class UserMatchingWeight
{
    [Column("user_id")]
    public int UserId { get; set; }

    [Column("component")]
    [MaxLength(50)]
    public string Component { get; set; } = default!;

    [Column("learned_weight")]
    public float LearnedWeight { get; set; }

    [Column("sample_count")]
    public int SampleCount { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public User User { get; set; } = default!;
}
