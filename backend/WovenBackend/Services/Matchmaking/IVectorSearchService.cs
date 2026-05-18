namespace WovenBackend.Services.Matchmaking;

public interface IVectorSearchService
{
    /// <summary>
    /// Cosine similarity between the latest PillarEmbeddings of two users.
    /// Returns 0 if either user has no embedding yet.
    /// Range: -1 (opposite) to 1 (identical). Typical useful range: 0.5–1.0.
    /// </summary>
    Task<double> CosineSimilarityAsync(int userId, int candidateId, CancellationToken ct = default);

    /// <summary>
    /// Returns up to <paramref name="topK"/> candidates ranked by pillar embedding
    /// cosine similarity to <paramref name="userId"/>. Uses the pgvector HNSW index
    /// for approximate nearest-neighbor search — safe to call at scale.
    /// Users without a PillarEmbedding are excluded.
    /// </summary>
    Task<List<(int CandidateId, double Similarity)>> GetTopSimilarByPillarAsync(
        int userId, int topK, CancellationToken ct = default);

    /// <summary>
    /// Returns cosine similarity between <paramref name="userId"/>'s PillarEmbedding
    /// and each candidate in <paramref name="candidateIds"/> in a single DB round-trip.
    /// Candidates without an embedding are omitted from the result.
    /// </summary>
    Task<Dictionary<int, double>> GetCosineSimilarityBatchAsync(
        int userId, List<int> candidateIds, CancellationToken ct = default);
}
