using Pgvector;

namespace WovenBackend.Data.Entities;
using WovenBackend.Data;
public class UserVector
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = default!;

    // Version 1, 2, 3... (incremented on major profile changes)
    public int Version { get; set; }

    // Complete matchable state as JSON:
    // {
    //   "intent": { "style": {...}, "tags": [...] },
    //   "foundational": { "pillars": {...}, "tags": {...} },
    //   "lifestyle": { "kids": "...", "smoking": "...", ... },
    //   "pulse": { "features": {...} }
    // }
    public string VectorJson { get; set; } = "{}";

    // 8 pillar scores (0.0-1.0) as JSON:
    // {
    //   "Lifestyle": 0.78,
    //   "Energy": 0.62,
    //   "Values": 0.85,
    //   "Communication": 0.55,
    //   "Ambition": 0.43,
    //   "Stability": 0.70,
    //   "Curiosity": 0.66,
    //   "Affection": 0.58
    // }
    public string PillarScoresJson { get; set; } = "{}";

    // 8-dim pillar embedding for pgvector cosine similarity ANN queries.
    // Order: [Lifestyle, Energy, Values, Communication, Ambition, Stability, Curiosity, Affection]
    // NULL for users built before Phase 1A — populated on next vector build.
    // Phase 3B replaces this with a 1536-dim text embedding if richer separation is needed.
    public Vector? PillarEmbedding { get; set; }

    // 1536-dim expression embedding: aggregated mean of last 30 tile embeddings.
    // Null until user posts their first Tile. Updated by TileEmbeddingService after each Tile creation.
    public Vector? ExpressionEmbedding { get; set; }

    // Phase 3D: enhanced embedding columns
    public Vector? IntentEmbedding { get; set; }
    public Vector? StyleEmbedding { get; set; }
    public Vector? HumorEmbedding { get; set; }
    public Vector? LifestyleEmbedding { get; set; }
    public Vector? EmotionalRhythmEmbedding { get; set; }
    public Vector? AttachmentProxyEmbedding { get; set; }
    public string? BehavioralLifestyleJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}