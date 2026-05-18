using Pgvector;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.data.Entities.Moments;

[Table("tiles")]
public class Tile
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("content_type")]
    [MaxLength(20)]
    public string ContentType { get; set; } = default!;

    [Column("content_text")]
    public string? ContentText { get; set; }

    [Column("media_url")]
    [MaxLength(2048)]
    public string? MediaUrl { get; set; }

    // 1536-dim embedding from text-embedding-3-small. Null until TileEmbeddingService runs.
    [Column("embedding")]
    public Vector? Embedding { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Always created_at + 48h
    [Column("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }

    [Column("is_expired")]
    public bool IsExpired { get; set; }

    // True once the tile has been pinned to at least one highlight slot.
    // Prevents media blob cleanup by MediaLifecycleWorker.
    [Column("is_highlighted")]
    public bool IsHighlighted { get; set; }

    // Set by ModerationService (Phase 2B). False = tile is hidden from all feeds.
    // Dev: auto-set to true for text tiles on creation; media tiles stay false until Phase 2B.
    [Column("is_moderated")]
    public bool IsModerated { get; set; }

    // Phase 3D: 192-dim voice embedding via SpeechBrain ECAPA-TDNN. Null until VoiceEmbeddingService runs.
    [Column("voice_embedding")]
    public Vector? VoiceEmbedding { get; set; }
}
