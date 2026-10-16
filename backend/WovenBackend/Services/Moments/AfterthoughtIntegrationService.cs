using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Services.Analytics;
using WovenBackend.Services.Trust;

namespace WovenBackend.Services.Moments;

/// <summary>
/// Processes MatchAfterthought records and routes them to appropriate systems:
/// - Non-safety feedback → MatchSignalLogs (ExplicitFeedback) for ECHO learning
/// - Safety flags → TrustService for trust scoring (never mixed into matching)
///
/// Called immediately after Afterthought submission to ensure real-time integration.
/// </summary>
public interface IAfterthoughtIntegrationService
{
    Task ProcessAfterthoughtAsync(int afterthoughtId, CancellationToken ct = default);
}

public class AfterthoughtIntegrationService : IAfterthoughtIntegrationService
{
    private readonly WovenDbContext _db;
    private readonly IMatchSignalService _signals;
    private readonly ITrustService _trust;
    private readonly ILogger<AfterthoughtIntegrationService> _logger;

    // Trust score caps for safety-flagged Afterthoughts
    // Note: FlagAsync caps trust at the penalty value (Math.Min), not reduces by it
    private const float BlockTrustCap = 0.15f;       // Severe (block path) → cap at 15%
    private const float SafetyFlagTrustCap = 0.30f;  // Moderate flag → cap at 30%

    public AfterthoughtIntegrationService(
        WovenDbContext db,
        IMatchSignalService signals,
        ITrustService trust,
        ILogger<AfterthoughtIntegrationService> logger)
    {
        _db = db;
        _signals = signals;
        _trust = trust;
        _logger = logger;
    }

    public async Task ProcessAfterthoughtAsync(int afterthoughtId, CancellationToken ct = default)
    {
        var afterthought = await _db.MatchAfterthoughts.AsNoTracking()
            .Include(a => a.Match)
            .FirstOrDefaultAsync(a => a.Id == afterthoughtId, ct);

        if (afterthought == null)
        {
            _logger.LogWarning("[AfterthoughtIntegration] Afterthought {Id} not found", afterthoughtId);
            return;
        }

        // Route based on safety flag
        if (afterthought.IsSafety)
        {
            await ProcessSafetyFlagAsync(afterthought, ct);
        }
        else
        {
            await ProcessMatchingFeedbackAsync(afterthought, ct);
        }

        _logger.LogInformation(
            "[AfterthoughtIntegration] Processed {Type} afterthought | Id={Id} From={From} About={About} Rating={Rating}",
            afterthought.IsSafety ? "SAFETY" : "FEEDBACK",
            afterthought.Id,
            afterthought.FromUserId,
            afterthought.AboutUserId,
            afterthought.Rating);
    }

    /// <summary>
    /// Safety-flagged Afterthoughts → TrustService ONLY (never to matching)
    /// </summary>
    private async Task ProcessSafetyFlagAsync(MatchAfterthought afterthought, CancellationToken ct)
    {
        // Determine trust cap based on close path severity
        // FlagAsync caps trust at the penalty value (Math.Min), not reduces by it
        float trustCap = afterthought.ClosePath == "block" ? BlockTrustCap : SafetyFlagTrustCap;

        string reason = afterthought.ClosePath == "block"
            ? $"blocked_by_user:{afterthought.ReasonTag}"
            : $"safety_flag:{afterthought.ReasonTag}";

        await _trust.FlagAsync(afterthought.AboutUserId, reason, trustCap, ct);

        _logger.LogWarning(
            "[AfterthoughtIntegration] Safety flag applied | AboutUser={About} Reason={Reason} TrustCap={Cap:F2} Path={Path}",
            afterthought.AboutUserId,
            afterthought.ReasonTag,
            trustCap,
            afterthought.ClosePath);
    }

    /// <summary>
    /// Non-safety Afterthoughts → MatchSignalLogs as ExplicitFeedback for ECHO
    /// Rating (1-5 stars) normalized to [0,1]: (rating - 1) / 4
    /// </summary>
    private async Task ProcessMatchingFeedbackAsync(MatchAfterthought afterthought, CancellationToken ct)
    {
        // Only process if rating was provided
        if (!afterthought.Rating.HasValue)
        {
            _logger.LogDebug(
                "[AfterthoughtIntegration] Skipping — no rating provided | Id={Id}",
                afterthought.Id);
            return;
        }

        // Normalize 1-5 star rating to [0,1] scale
        // 1 star → 0.00 (worst match)
        // 3 stars → 0.50 (neutral)
        // 5 stars → 1.00 (best match)
        float normalizedScore = (afterthought.Rating.Value - 1) / 4f;

        // Record as ExplicitFeedback signal
        // ConnectionScoreBatchWorker will pick this up in its nightly run
        await _signals.RecordAsync(
            viewerId: afterthought.FromUserId,
            candidateId: afterthought.AboutUserId,
            eventType: MatchSignalEventTypes.ExplicitFeedback,
            eventValue: normalizedScore,
            ct: ct);

        _logger.LogInformation(
            "[AfterthoughtIntegration] Recorded ExplicitFeedback | From={From} About={About} Stars={Stars} Normalized={Norm:F2}",
            afterthought.FromUserId,
            afterthought.AboutUserId,
            afterthought.Rating.Value,
            normalizedScore);
    }
}
