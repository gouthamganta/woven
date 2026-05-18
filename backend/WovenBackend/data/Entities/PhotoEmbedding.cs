using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Pgvector;

namespace WovenBackend.Data.Entities;

[Table("photo_embeddings")]
public class PhotoEmbedding
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("photo_url")]
    [MaxLength(2048)]
    public string PhotoUrl { get; set; } = default!;

    [Column("embedding")]
    public Vector? Embedding { get; set; }

    [Column("embedded_at")]
    public DateTimeOffset EmbeddedAt { get; set; } = DateTimeOffset.UtcNow;

    public User User { get; set; } = default!;
}
