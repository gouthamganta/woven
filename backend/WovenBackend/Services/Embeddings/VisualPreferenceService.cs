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

    private static double CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        double dot = 0, normA = 0, normB = 0;
        int len = Math.Min(a.Length, b.Length);
        for (int i = 0; i < len; i++)
        {
            dot   += a[i] * b[i];
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
