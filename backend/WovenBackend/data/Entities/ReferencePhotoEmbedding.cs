using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Pgvector;

namespace WovenBackend.Data.Entities;

[Table("reference_photo_embeddings")]
public class ReferencePhotoEmbedding
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("label")]
    [MaxLength(100)]
    public string Label { get; set; } = string.Empty;

    [Column("embedding")]
    public Vector Embedding { get; set; } = default!;

    [Column("added_at")]
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}
