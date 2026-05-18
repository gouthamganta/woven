using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Services.Security;

namespace WovenBackend.Services.Trust;

public class TrustService : ITrustService
{
    private const float TrustThreshold   = 0.25f;
    private const float DefaultTrust     = 0.50f;
    private const float MinTrust         = 0.00f;
    private const float MaxTrust         = 1.00f;

    // Velocity: >10 tiles/hour is suspicious
    private const int   VelocityTileLimit = 10;
    private const float VelocityPenalty   = 0.10f;

    // Multi-account: same device fingerprint used by two different accounts
    private const float MultiAccountPenalty = 0.20f;

    // Bot detection rewards: points toward a healthy 0.8 score
    private const float ProfileCompletionBonus = 0.15f;
    private const float AnsweredQuestionsBonus  = 0.10f;
    private const float HasPhotoBonus           = 0.05f;

    private readonly WovenDbContext _db;
    private readonly ICacheService _cache;
    private readonly ISecurityAuditService _audit;
    private readonly ILogger<TrustService> _logger;

    public TrustService(WovenDbContext db, ICacheService cache, ISecurityAuditService audit, ILogger<TrustService> logger)
    {
        _db = db;
        _cache = cache;
        _audit = audit;
        _logger = logger;
    }

    public async Task CheckDeviceFingerprintAsync(int userId, string? fingerprint, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fingerprint)) return;

        // Hash the raw fingerprint before storing — never cache PII in plaintext.
        var hash = PiiSanitizer.HashForAudit(fingerprint, "fp-v1");
        var cacheKey = $"fp:{hash}";

        var recorded = await _cache.GetAsync<string>(cacheKey, ct);
        if (recorded != null && int.TryParse(recorded, out var existingUserId) && existingUserId != userId)
        {
            // Same device fingerprint, different account → multi-account signal.
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user != null)
            {
                user.TrustScore = Math.Max(MinTrust, user.TrustScore - MultiAccountPenalty);
                user.TrustUpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                _audit.Log("suspicious_pattern", userId: userId, resourceType: "DeviceFingerprint",
                    resourceId: hash[..8]);
                _logger.LogWarning("[Trust] Multi-account flag: user {UserId} shares fingerprint with user {OtherId} → TrustScore={Score:F2}",
                    userId, existingUserId, user.TrustScore);
            }
            return;
        }

        // Record this user's claim on the fingerprint (30-day TTL; renews on each login).
        await _cache.SetAsync(cacheKey, userId.ToString(), TimeSpan.FromDays(30), ct);
        _logger.LogDebug("[Trust] Fingerprint recorded for user {UserId}", userId);
    }

    public async Task CheckVelocityAsync(int userId, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-1);
        var recentTiles = await _db.Tiles
            .CountAsync(t => t.UserId == userId && t.CreatedAt >= cutoff, ct);

        if (recentTiles <= VelocityTileLimit) return;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return;

        user.TrustScore    = Math.Max(MinTrust, user.TrustScore - VelocityPenalty);
        user.TrustUpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogWarning("[Trust] Velocity flag: user {UserId} posted {Count} tiles in 1h → TrustScore={Score:F2}",
            userId, recentTiles, user.TrustScore);
    }

    public async Task RunBotDetectionAsync(int userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return;

        float score = DefaultTrust;

        var hasProfile = await _db.UserProfiles.AnyAsync(p => p.UserId == userId, ct);
        if (hasProfile) score += ProfileCompletionBonus;

        var hasAnswers = await _db.UserFoundationalQuestionSets
            .AnyAsync(q => q.UserId == userId && q.AnsweredAt != null, ct);
        if (hasAnswers) score += AnsweredQuestionsBonus;

        var hasPhoto = await _db.UserPhotos.AnyAsync(p => p.UserId == userId, ct);
        if (hasPhoto) score += HasPhotoBonus;

        user.TrustScore     = Math.Clamp(score, MinTrust, MaxTrust);
        user.TrustUpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogDebug("[Trust] Bot detection for user {UserId}: score={Score:F2}", userId, score);
    }

    public async Task FlagAsync(int userId, string reason, float penaltyScore, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return;

        user.TrustScore = Math.Min(user.TrustScore, penaltyScore);
        user.TrustUpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _audit.Log("trust_flag", userId: userId, resourceType: "TrustScore", resourceId: reason);
        _logger.LogWarning("[Trust] FlagAsync: user {UserId} reason={Reason} → TrustScore={Score:F2}",
            userId, reason, user.TrustScore);
    }

    public async Task<float> GetTrustScoreAsync(int userId, CancellationToken ct = default)
    {
        return await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.TrustScore)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> IsTrustedEnoughAsync(int userId, CancellationToken ct = default)
    {
        var score = await GetTrustScoreAsync(userId, ct);
        return score >= TrustThreshold;
    }
}
