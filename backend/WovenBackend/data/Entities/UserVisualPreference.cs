using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Pgvector;

namespace WovenBackend.Data.Entities;

[Table("user_visual_preference")]
public class UserVisualPreference
{
    [Key]
    [Column("user_id")]
    public int UserId { get; set; }

    [Column("preference_embedding")]
    public Vector? PreferenceEmbedding { get; set; }

    [Column("aversion_embedding")]
    public Vector? AversionEmbedding { get; set; }

    [Column("yes_sample_count")]
    public int YesSampleCount { get; set; }

    [Column("no_sample_count")]
    public int NoSampleCount { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public User User { get; set; } = default!;
}
