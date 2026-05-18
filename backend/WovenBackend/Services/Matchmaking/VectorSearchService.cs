using Microsoft.EntityFrameworkCore;
using Pgvector;
using WovenBackend.Data;

namespace WovenBackend.Services.Matchmaking;

public class VectorSearchService : IVectorSearchService
{
    private readonly WovenDbContext _db;
    private readonly ILogger<VectorSearchService> _logger;

    public VectorSearchService(WovenDbContext db, ILogger<VectorSearchService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<double> CosineSimilarityAsync(int userId, int candidateId, CancellationToken ct = default)
    {
        // Fetch both latest embeddings in one round-trip
        var embeddings = await _db.UserVectors
            .Where(v => (v.UserId == userId || v.UserId == candidateId) && v.PillarEmbedding != null)
            .GroupBy(v => v.UserId)
            .Select(g => g.OrderByDescending(v => v.Version).First())
            .Select(v => new { v.UserId, v.PillarEmbedding })
            .ToListAsync(ct);

        var userEmb = embeddings.FirstOrDefault(e => e.UserId == userId)?.PillarEmbedding;
        var candEmb = embeddings.FirstOrDefault(e => e.UserId == candidateId)?.PillarEmbedding;

        if (userEmb == null || candEmb == null)
            return 0.0;

        // 8 floats — compute in-process, no round-trip needed
        return ComputeCosineSimilarity(userEmb.Memory.Span, candEmb.Memory.Span);
    }

    /// <inheritdoc/>
    public async Task<List<(int CandidateId, double Similarity)>> GetTopSimilarByPillarAsync(
        int userId, int topK, CancellationToken ct = default)
    {
        var userEmbedding = await _db.UserVectors
            .Where(v => v.UserId == userId && v.PillarEmbedding != null)
            .OrderByDescending(v => v.Version)
            .Select(v => v.PillarEmbedding)
            .FirstOrDefaultAsync(ct);

        if (userEmbedding == null)
        {
            _logger.LogWarning("[VectorSearch] User {UserId} has no PillarEmbedding — skipping ANN search", userId);
            return [];
        }

        // ANN via HNSW index: ORDER BY embedding <=> query LIMIT k
        // CTE isolates the latest version per candidate before the distance sort
        // so the planner can push the <=> ordering into the index scan.
        var rows = await _db.Database
            .SqlQuery<SimilarCandidateRow>($"""
                WITH latest AS (
                    SELECT DISTINCT ON ("UserId")
                        "UserId",
                        "PillarEmbedding"
                    FROM "UserVectors"
                    WHERE "UserId" <> {userId}
                      AND "PillarEmbedding" IS NOT NULL
                    ORDER BY "UserId", "Version" DESC
                )
                SELECT
                    "UserId"                                                          AS "CandidateId",
                    (1.0 - ("PillarEmbedding" <=> {userEmbedding}))::float8          AS "Similarity"
                FROM latest
                ORDER BY "PillarEmbedding" <=> {userEmbedding}
                LIMIT {topK}
                """)
            .ToListAsync(ct);

        return rows.Select(r => (r.CandidateId, r.Similarity)).ToList();
    }

    /// <inheritdoc/>
    public async Task<Dictionary<int, double>> GetCosineSimilarityBatchAsync(
        int userId, List<int> candidateIds, CancellationToken ct = default)
    {
        if (candidateIds.Count == 0) return new Dictionary<int, double>();

        var userEmbedding = await _db.UserVectors
            .Where(v => v.UserId == userId && v.PillarEmbedding != null)
            .OrderByDescending(v => v.Version)
            .Select(v => v.PillarEmbedding)
            .FirstOrDefaultAsync(ct);

        if (userEmbedding == null) return new Dictionary<int, double>();

        // Load candidate embeddings in one query; compute in-process to avoid N round-trips
        var candidateEmbeddings = await _db.UserVectors
            .Where(v => candidateIds.Contains(v.UserId) && v.PillarEmbedding != null)
            .GroupBy(v => v.UserId)
            .Select(g => g.OrderByDescending(v => v.Version).First())
            .Select(v => new { v.UserId, v.PillarEmbedding })
            .ToListAsync(ct);

        var result = new Dictionary<int, double>(candidateEmbeddings.Count);
        foreach (var c in candidateEmbeddings)
        {
            if (c.PillarEmbedding != null)
                result[c.UserId] = ComputeCosineSimilarity(userEmbedding.Memory.Span, c.PillarEmbedding.Memory.Span);
        }
        return result;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static double ComputeCosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot   += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return (normA == 0 || normB == 0) ? 0.0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    private record SimilarCandidateRow(int CandidateId, double Similarity);
}
