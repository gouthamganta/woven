using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.Data.Entities;

[Table("user_visual_decisions")]
public class UserVisualDecision
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("viewer_user_id")]
    public int ViewerUserId { get; set; }

    [Column("target_user_id")]
    public int TargetUserId { get; set; }

    [Column("photo_embedding_id")]
    public int? PhotoEmbeddingId { get; set; }

    [Column("choice")]
    [MaxLength(10)]
    public string Choice { get; set; } = default!;

    [Column("decided_at")]
    public DateTimeOffset DecidedAt { get; set; } = DateTimeOffset.UtcNow;

    public PhotoEmbedding? PhotoEmbedding { get; set; }
}
