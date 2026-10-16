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
        // Use explicit transaction to ensure atomicity of signal + ProcessedAt
        // Prevents duplicate signals if ProcessedAt save fails after signal insertion
        using var transaction = await _db.Database.BeginTransactionAsync(ct);

        try
        {
            // Load with row-level lock (SELECT FOR UPDATE) to prevent concurrent processing
            // FromSqlRaw with FOR UPDATE ensures only one processor claims the row
            var afterthought = await _db.MatchAfterthoughts
                .FromSqlRaw(
                    @"SELECT * FROM ""match_afterthoughts""
                      WHERE ""id"" = {0}
                      FOR UPDATE SKIP LOCKED",
                    afterthoughtId)
                .Include(a => a.Match)
                .FirstOrDefaultAsync(ct);

            if (afterthought == null)
            {
                // Either doesn't exist or locked by another processor (SKIP LOCKED)
                _logger.LogDebug(
                    "[AfterthoughtIntegration] Afterthought {Id} not found or locked by another processor",
                    afterthoughtId);
                return;
            }

            // Idempotency: skip if already processed
            if (afterthought.ProcessedAt.HasValue)
            {
                _logger.LogDebug(
                    "[AfterthoughtIntegration] Already processed | Id={Id} ProcessedAt={ProcessedAt}",
                    afterthought.Id,
                    afterthought.ProcessedAt.Value);
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

            // Mark as processed within the same transaction as signal insertion
            afterthought.ProcessedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            // Commit transaction - both signal and ProcessedAt are now durable
            await transaction.CommitAsync(ct);

            _logger.LogInformation(
                "[AfterthoughtIntegration] Processed {Type} afterthought | Id={Id} From={From} About={About} Rating={Rating}",
                afterthought.IsSafety ? "SAFETY" : "FEEDBACK",
                afterthought.Id,
                afterthought.FromUserId,
                afterthought.AboutUserId,
                afterthought.Rating);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AfterthoughtIntegration] Processing failed for {Id}, rolling back transaction",
                afterthoughtId);
            throw;
        }
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

        // Check for existing signal from this Afterthought (idempotency)
        // This prevents duplicates even if ProcessedAt was not yet set
        var existingSignal = await _db.MatchSignalLogs
            .Where(s => s.EventType == MatchSignalEventTypes.ExplicitFeedback
                     && s.ViewerId == afterthought.FromUserId
                     && s.CandidateId == afterthought.AboutUserId
                     && s.MetadataJson != null
                     && s.MetadataJson.Contains($"\"afterthoughtId\":{afterthought.Id}"))
            .AnyAsync(ct);

        if (existingSignal)
        {
            _logger.LogDebug(
                "[AfterthoughtIntegration] Signal already exists for Afterthought {Id}, skipping duplicate",
                afterthought.Id);
            return;
        }

        // Normalize 1-5 star rating to [0,1] scale
        // 1 star → 0.00 (worst match)
        // 3 stars → 0.50 (neutral)
        // 5 stars → 1.00 (best match)
        float normalizedScore = (afterthought.Rating.Value - 1) / 4f;

        // Record as ExplicitFeedback signal with source metadata
        // ConnectionScoreBatchWorker will pick this up in its nightly run
        // Metadata includes source ID for traceability and idempotent detection
        var metadata = System.Text.Json.JsonSerializer.Serialize(new
        {
            source = "afterthought",
            afterthoughtId = afterthought.Id,
            closePath = afterthought.ClosePath
        });

        await _signals.RecordAsync(
            viewerId: afterthought.FromUserId,
            candidateId: afterthought.AboutUserId,
            eventType: MatchSignalEventTypes.ExplicitFeedback,
            eventValue: normalizedScore,
            metadataJson: metadata,
            ct: ct);

        _logger.LogInformation(
            "[AfterthoughtIntegration] Recorded ExplicitFeedback | From={From} About={About} Stars={Stars} Normalized={Norm:F2}",
            afterthought.FromUserId,
            afterthought.AboutUserId,
            afterthought.Rating.Value,
            normalizedScore);
    }
}
