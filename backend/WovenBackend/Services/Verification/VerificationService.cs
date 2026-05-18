using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.Services.Analytics;
using WovenBackend.Services.Embeddings;
using WovenBackend.Services.Security;

namespace WovenBackend.Services.Verification;

public class VerificationService : IVerificationService
{
    private const double SimilarityThreshold = 0.70;

    private readonly WovenDbContext _db;
    private readonly IPhotoEmbeddingService _photoEmbedding;
    private readonly ISecurityAuditService _audit;
    private readonly IAnalyticsService _analytics;
    private readonly ILogger<VerificationService> _logger;

    public VerificationService(
        WovenDbContext db,
        IPhotoEmbeddingService photoEmbedding,
        ISecurityAuditService audit,
        IAnalyticsService analytics,
        ILogger<VerificationService> logger)
    {
        _db = db;
        _photoEmbedding = photoEmbedding;
        _audit = audit;
        _analytics = analytics;
        _logger = logger;
    }

    public async Task<VerificationResult> SubmitSelfieAsync(int userId, string blobPath, CancellationToken ct = default)
    {
        // Step 1: check existing profile photo embeddings exist
        var existingEmbeddingIds = await _db.PhotoEmbeddings.AsNoTracking()
            .Where(e => e.UserId == userId)
            .Select(e => e.Id)
            .ToListAsync(ct);

        if (existingEmbeddingIds.Count == 0)
            return new VerificationResult(false, null, "Upload profile photos first");

        // Step 2: embed the selfie photo (stored in photo_embeddings)
        var selfieEmbeddingId = await _photoEmbedding.EmbedPhotoAsync(userId, blobPath, ct);
        if (selfieEmbeddingId == null)
            return new VerificationResult(false, null, "Could not process selfie");

        // Load selfie embedding vector
        var selfieEmb = await _db.PhotoEmbeddings.AsNoTracking()
            .Where(e => e.Id == selfieEmbeddingId.Value)
            .Select(e => e.Embedding)
            .FirstOrDefaultAsync(ct);

        if (selfieEmb == null)
            return new VerificationResult(false, null, "Could not process selfie");

        // Step 3: Create pending verification row
        var verification = new UserVerification
        {
            UserId = userId,
            Type = "selfie",
            Status = "pending",
            SubmittedAt = DateTimeOffset.UtcNow
        };
        _db.UserVerifications.Add(verification);
        await _db.SaveChangesAsync(ct);

        // Step 4: Find best cosine similarity against pre-existing profile photo embeddings
        double bestSimilarity = 0;
        try
        {
            var best = await _db.PhotoEmbeddings.AsNoTracking()
                .Where(e => e.UserId == userId && existingEmbeddingIds.Contains(e.Id))
                .Select(e => 1.0 - (double)e.Embedding!.CosineDistance(selfieEmb))
                .OrderByDescending(s => s)
                .FirstOrDefaultAsync(ct);
            bestSimilarity = best;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Verification] Similarity query failed for user {UserId}", userId);
        }

        // Step 5: Apply threshold
        if (bestSimilarity >= SimilarityThreshold)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user != null)
            {
                user.IsVerified = true;
                user.VerifiedAt = DateTimeOffset.UtcNow;
                user.VerificationType = "selfie";
            }

            verification.Status = "verified";
            verification.VerifiedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            _audit.Log("pii_access", userId: userId, service: "VerificationService",
                resourceType: "selfie_verified", piiStripped: true);

            _ = _analytics.TrackAsync(userId, null, AnalyticsEvents.VerificationCompleted,
                new { type = "selfie" });

            _logger.LogInformation("[Verification] User {UserId} verified via selfie (similarity={Sim:F3})", userId, bestSimilarity);
            return new VerificationResult(true, true, null);
        }
        else
        {
            verification.Status = "failed";
            verification.FailureReason = "Selfie did not match profile photos";
            await _db.SaveChangesAsync(ct);

            _ = _analytics.TrackAsync(userId, null, AnalyticsEvents.VerificationFailed,
                new { type = "selfie", failureReason = "no_match" });

            _logger.LogInformation("[Verification] User {UserId} selfie failed (similarity={Sim:F3})", userId, bestSimilarity);
            return new VerificationResult(false, false,
                "We couldn't match your selfie to your profile photos. Try better lighting or update your profile photos.");
        }
    }

    public async Task<VerificationStatusResult> GetVerificationStatusAsync(int userId, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.IsVerified, u.VerifiedAt, u.VerificationType })
            .FirstOrDefaultAsync(ct);

        var latest = await _db.UserVerifications.AsNoTracking()
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.SubmittedAt)
            .Select(v => new LatestAttemptDto(v.Id, v.Type, v.Status, v.SubmittedAt, v.VerifiedAt, v.FailureReason))
            .FirstOrDefaultAsync(ct);

        return new VerificationStatusResult(
            user?.IsVerified ?? false,
            user?.VerifiedAt,
            user?.VerificationType,
            latest);
    }
}
