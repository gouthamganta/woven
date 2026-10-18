using Microsoft.EntityFrameworkCore;
using Pgvector;
using WovenBackend.Data;
using WovenBackend.Data.Entities;

namespace WovenBackend.Services.Embeddings;

public class VisualPreferenceService : IVisualPreferenceService
{
    private const int MinSamples = 10;
    private readonly WovenDbContext _db;
    private readonly ILogger<VisualPreferenceService> _logger;

    public VisualPreferenceService(WovenDbContext db, ILogger<VisualPreferenceService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task UpdateVisualPreferenceAsync(int userId, CancellationToken ct = default)
    {
        // YES decisions that have a linked photo embedding
        var yesEmbeddings = await _db.UserVisualDecisions.AsNoTracking()
            .Where(d => d.ViewerUserId == userId && d.Choice == "YES" && d.PhotoEmbeddingId != null)
            .Join(_db.PhotoEmbeddings,
                d => d.PhotoEmbeddingId,
                e => e.Id,
                (d, e) => e.Embedding)
            .Where(emb => emb != null)
            .ToListAsync(ct);

        // NO decisions
        var noEmbeddings = await _db.UserVisualDecisions.AsNoTracking()
            .Where(d => d.ViewerUserId == userId && d.Choice == "NO" && d.PhotoEmbeddingId != null)
            .Join(_db.PhotoEmbeddings,
                d => d.PhotoEmbeddingId,
                e => e.Id,
                (d, e) => e.Embedding)
            .Where(emb => emb != null)
            .ToListAsync(ct);

        if (yesEmbeddings.Count < MinSamples && noEmbeddings.Count < MinSamples)
        {
            _logger.LogInformation(
                "[VisualPreference] Skipping user {UserId} — YES={Y}, NO={N} (min {Min})",
                userId, yesEmbeddings.Count, noEmbeddings.Count, MinSamples);
            return;
        }

        Vector? preferenceVec = yesEmbeddings.Count >= MinSamples
            ? new Vector(ElementWiseMean(yesEmbeddings!))
            : null;

        Vector? aversionVec = noEmbeddings.Count >= MinSamples
            ? new Vector(ElementWiseMean(noEmbeddings!))
            : null;

        var pref = await _db.UserVisualPreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (pref == null)
        {
            _db.UserVisualPreferences.Add(new UserVisualPreference
            {
                UserId = userId,
                PreferenceEmbedding = preferenceVec,
                AversionEmbedding = aversionVec,
                YesSampleCount = yesEmbeddings.Count,
                NoSampleCount = noEmbeddings.Count,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            if (preferenceVec != null) pref.PreferenceEmbedding = preferenceVec;
            if (aversionVec != null) pref.AversionEmbedding = aversionVec;
            pref.YesSampleCount = yesEmbeddings.Count;
            pref.NoSampleCount = noEmbeddings.Count;
            pref.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("[VisualPreference] Updated for user {UserId} (YES={Y}, NO={N})",
            userId, yesEmbeddings.Count, noEmbeddings.Count);
    }

    public async Task<string?> GetBestPhotoUrlAsync(int viewerUserId, int candidateUserId, CancellationToken ct = default)
    {
        var pref = await _db.UserVisualPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == viewerUserId, ct);

        if (pref?.PreferenceEmbedding == null)
            return null;

        var photos = await _db.PhotoEmbeddings.AsNoTracking()
            .Where(e => e.UserId == candidateUserId && e.Embedding != null)
            .Select(e => new { e.PhotoUrl, e.Embedding })
            .ToListAsync(ct);

        if (photos.Count == 0) return null;
        if (photos.Count == 1) return photos[0].PhotoUrl;

        var prefSpan = pref.PreferenceEmbedding.Memory.Span;
        string? bestUrl = null;
        double bestSim = double.MinValue;

        foreach (var p in photos)
        {
            var sim = CosineSimilarity(prefSpan, p.Embedding!.Memory.Span);
            if (sim > bestSim) { bestSim = sim; bestUrl = p.PhotoUrl; }
        }

        return bestUrl;
    }

    public async Task<string[]> GetPersonalizedPhotosAsync(
        int viewerUserId,
        int candidateUserId,
        int count = 3,
        CancellationToken ct = default)
    {
        var pref = await _db.UserVisualPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == viewerUserId, ct);

        // Query UserPhotos (all photos) with LEFT JOIN to PhotoEmbeddings
        // This ensures photos without embeddings are still included
        var photos = await (
            from photo in _db.UserPhotos.AsNoTracking()
            where photo.UserId == candidateUserId
            join embedding in _db.PhotoEmbeddings.AsNoTracking()
                on photo.Url equals embedding.PhotoUrl into embeddingGroup
            from embedding in embeddingGroup.DefaultIfEmpty()
            select new
            {
                PhotoUrl = photo.Url,
                SortOrder = photo.SortOrder,
                Embedding = embedding != null ? embedding.Embedding : null,
                AttributesJson = embedding != null ? embedding.AttributesJson : null,
                ConfidenceScoresJson = embedding != null ? embedding.ConfidenceScoresJson : null
            }
        ).ToListAsync(ct);

        if (photos.Count == 0)
            return Array.Empty<string>();

        // Fallback: no preference data → respect user's chosen sortOrder
        if (pref?.PreferenceEmbedding == null)
        {
            return photos
                .OrderBy(p => p.SortOrder)
                .Take(count)
                .Select(p => p.PhotoUrl)
                .ToArray();
        }

        // Copy preference embedding to avoid ref local in lambda
        var prefEmbedding = pref.PreferenceEmbedding;

        // Score each photo (photos without embeddings get base quality score only)
        var scored = photos.Select(p => new
        {
            p.PhotoUrl,
            p.SortOrder,
            Score = p.Embedding != null
                ? ComputePhotoScore(prefEmbedding, p.Embedding, p.AttributesJson, p.ConfidenceScoresJson)
                : 0.0 // No embedding → lowest score, will sort by sortOrder as tiebreaker
        }).ToList();

        // Return top N by score (with sortOrder as tiebreaker for equal scores)
        return scored
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.SortOrder)
            .Take(count)
            .Select(x => x.PhotoUrl)
            .ToArray();
    }

    private static double ComputePhotoScore(
        Vector viewerPrefEmbedding,
        Vector photoEmbedding,
        string? attributesJson,
        string? confidenceScoresJson)
    {
        // 1. Visual similarity (cosine similarity) — weight 0.70
        var visualSim = CosineSimilarity(viewerPrefEmbedding.Memory.Span, photoEmbedding.Memory.Span);
        var visualScore = Math.Max(0, visualSim) * 0.70;

        // 2. Photo quality heuristics — weight 0.30
        var qualityScore = 0.0;
        if (!string.IsNullOrWhiteSpace(attributesJson))
        {
            try
            {
                var attributes = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(attributesJson);
                var confidenceScores = string.IsNullOrWhiteSpace(confidenceScoresJson)
                    ? null
                    : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(confidenceScoresJson);

                // Basic quality signals:
                // - Has clear attributes detected (not empty)
                // - High confidence scores
                if (attributes != null && attributes.Count > 0)
                {
                    qualityScore += 0.15; // Has attributes

                    if (confidenceScores != null && confidenceScores.Count > 0)
                    {
                        var avgConfidence = confidenceScores.Values.Average();
                        qualityScore += avgConfidence * 0.15; // Avg confidence [0-1]
                    }
                }
            }
            catch
            {
                // Ignore JSON parse errors
            }
        }

        return visualScore + qualityScore;
    }

    private static double CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        double dot = 0, normA = 0, normB = 0;
        int len = Math.Min(a.Length, b.Length);
        for (int i = 0; i < len; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return (normA == 0 || normB == 0) ? 0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    private static float[] ElementWiseMean(List<Vector?> vectors)
    {
        var dim = vectors.First()!.Memory.Length;
        var sum = new float[dim];
        foreach (var v in vectors)
        {
            var span = v!.Memory.Span;
            for (int i = 0; i < dim; i++)
                sum[i] += span[i];
        }
        for (int i = 0; i < dim; i++)
            sum[i] /= vectors.Count;
        return sum;
    }
}
