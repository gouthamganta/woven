using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Services.Security;

namespace WovenBackend.Services.Trust;

public class CatfishDetectionService : ICatfishDetectionService
{
    private readonly WovenDbContext _db;
    private readonly ITrustService _trust;
    private readonly ISecurityAuditService _audit;
    private readonly ILogger<CatfishDetectionService> _logger;

    public CatfishDetectionService(
        WovenDbContext db,
        ITrustService trust,
        ISecurityAuditService audit,
        ILogger<CatfishDetectionService> logger)
    {
        _db = db;
        _trust = trust;
        _audit = audit;
        _logger = logger;
    }

    public async Task CheckPhotoAsync(int userId, int photoEmbeddingId, CancellationToken ct = default)
    {
        try
        {
            var photo = await _db.PhotoEmbeddings.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == photoEmbeddingId, ct);

            if (photo?.Embedding == null) return;

            var embedding = photo.Embedding;

            // Signal 1 — Duplicate photo across accounts
            var duplicates = await _db.PhotoEmbeddings.AsNoTracking()
                .Where(p => p.UserId != userId && p.Embedding != null)
                .OrderBy(p => p.Embedding!.CosineDistance(embedding))
                .Take(5)
                .Where(p => p.Embedding!.CosineDistance(embedding) < 0.08)
                .Select(p => p.UserId)
                .Distinct()
                .ToListAsync(ct);

            if (duplicates.Count > 0)
            {
                await _trust.FlagAsync(userId, "CATFISH_SUSPECTED", 0.9f, ct);
                foreach (var matchedUserId in duplicates)
                    await _trust.FlagAsync(matchedUserId, "CATFISH_SUSPECTED", 0.9f, ct);

                _audit.Log("suspicious_pattern", userId: userId,
                    service: "CatfishDetection",
                    resourceType: "duplicate_photo_cross_account",
                    piiStripped: true);

                _logger.LogWarning("[CatfishDetection] Duplicate photo detected for user {UserId}, matched {Count} other accounts",
                    userId, duplicates.Count);
            }

            // Signal 2 — Stock photo reference match
            // moderation_queue.TileId is NOT NULL so we skip that insert; log only.
            var stockMatch = await _db.ReferencePhotoEmbeddings.AsNoTracking()
                .OrderBy(r => r.Embedding.CosineDistance(embedding))
                .Take(1)
                .Where(r => r.Embedding.CosineDistance(embedding) < 0.12)
                .AnyAsync(ct);

            if (stockMatch)
            {
                await _trust.FlagAsync(userId, "CATFISH_SUSPECTED", 0.7f, ct);

                _audit.Log("suspicious_pattern", userId: userId,
                    service: "CatfishDetection",
                    resourceType: "stock_photo_reference_match",
                    piiStripped: true);

                _logger.LogWarning("[CatfishDetection] Stock photo match for user {UserId}", userId);
            }

            // Signal 3 — Inconsistent profile photos
            var allEmbeddings = await _db.PhotoEmbeddings.AsNoTracking()
                .Where(p => p.UserId == userId && p.Embedding != null)
                .Select(p => p.Embedding!)
                .ToListAsync(ct);

            if (allEmbeddings.Count >= 3)
            {
                var minSimilarity = double.MaxValue;
                for (int i = 0; i < allEmbeddings.Count; i++)
                {
                    for (int j = i + 1; j < allEmbeddings.Count; j++)
                    {
                        var dist = allEmbeddings[i].CosineDistance(allEmbeddings[j]);
                        var sim = 1.0 - (double)dist;
                        if (sim < minSimilarity) minSimilarity = sim;
                    }
                }

                if (minSimilarity < 0.30)
                {
                    await _trust.FlagAsync(userId, "CATFISH_SUSPECTED", 0.6f, ct);

                    _audit.Log("suspicious_pattern", userId: userId,
                        service: "CatfishDetection",
                        resourceType: "inconsistent_profile_photos",
                        piiStripped: true);

                    _logger.LogWarning("[CatfishDetection] Inconsistent profile photos for user {UserId}, min_sim={MinSim:F3}",
                        userId, minSimilarity);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CatfishDetection] Failed for user {UserId}, photoEmbeddingId {Id}",
                userId, photoEmbeddingId);
        }
    }
}
