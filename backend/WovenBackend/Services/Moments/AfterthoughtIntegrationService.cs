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
        // Atomic claim via UPDATE to avoid retry strategy conflict
        // Sets ProcessedAt first, then processes (rollback on failure)
        var claimed = await _db.Database.ExecuteSqlRawAsync(
            @"UPDATE ""match_afterthoughts""
              SET ""processed_at"" = {0}
              WHERE ""id"" = {1} AND ""processed_at"" IS NULL",
            new object[] { DateTimeOffset.UtcNow, afterthoughtId },
            ct);

        // Load the row to check state
        var afterthought = await _db.MatchAfterthoughts
            .AsNoTracking()
            .Include(a => a.Match)
            .FirstOrDefaultAsync(a => a.Id == afterthoughtId, ct);

        if (afterthought == null)
        {
            _logger.LogDebug("[AfterthoughtIntegration] Afterthought {Id} not found", afterthoughtId);
            return;
        }

        if (claimed == 0)
        {
            // Claim failed - either already processed or interrupted
            // Check if it's truly complete (has signal) or interrupted (no signal)
            if (!afterthought.ProcessedAt.HasValue)
            {
                // Strange case: ProcessedAt is NULL but UPDATE returned 0
                _logger.LogWarning("[AfterthoughtIntegration] Unexpected state for {Id}", afterthoughtId);
                return;
            }

            // ProcessedAt is set - check if signal exists
            var signalExists = await _db.MatchSignalLogs
                .FromSqlRaw(
                    @"SELECT * FROM ""MatchSignalLogs""
                      WHERE ""EventType"" = {0}
                        AND (""MetadataJson""::jsonb->>'afterthoughtId')::int = {1}
                      LIMIT 1",
                    MatchSignalEventTypes.ExplicitFeedback, afterthought.Id)
                .AsNoTracking()
                .AnyAsync(ct);

            if (signalExists)
            {
                _logger.LogDebug("[AfterthoughtIntegration] Already complete | Id={Id}", afterthoughtId);
                return;
            }

            // Interrupted claim - only recover if abandoned (ProcessedAt > 30s old)
            // This prevents race where active processor hasn't created signal yet
            var abandonedThreshold = DateTimeOffset.UtcNow.AddSeconds(-30);
            _logger.LogInformation("[AfterthoughtIntegration] Interrupted claim | Id={Id}, attempting recovery (threshold={Threshold})",
                afterthoughtId, abandonedThreshold);

            claimed = await _db.Database.ExecuteSqlRawAsync(
                @"UPDATE ""match_afterthoughts""
                  SET ""processed_at"" = {0}
                  WHERE ""id"" = {1}
                    AND ""processed_at"" IS NOT NULL
                    AND ""processed_at"" < {2}
                    AND NOT EXISTS (
                      SELECT 1 FROM ""MatchSignalLogs""
                      WHERE ""EventType"" = {3}
                        AND (""MetadataJson""::jsonb->>'afterthoughtId')::int = {1}
                    )",
                new object[] { DateTimeOffset.UtcNow, afterthoughtId, abandonedThreshold, MatchSignalEventTypes.ExplicitFeedback },
                ct);

            if (claimed == 0)
            {
                _logger.LogDebug("[AfterthoughtIntegration] Recovery failed - completed or claimed by another processor | Id={Id}", afterthoughtId);
                return;
            }
        }

        // Process (signal insertion is idempotent via metadata check)
        try
        {
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
        catch (Exception ex)
        {
            // Reset ProcessedAt for retry
            await _db.Database.ExecuteSqlRawAsync(
                @"UPDATE ""match_afterthoughts"" SET ""processed_at"" = NULL WHERE ""id"" = {0}",
                new object[] { afterthoughtId },
                ct);

            _logger.LogError(ex,
                "[AfterthoughtIntegration] Processing failed for {Id}, reset ProcessedAt for retry",
                afterthoughtId);
            throw;
        }
    }

    /// <summary>
    /// Safety-flagged Afterthoughts → TrustService ONLY (never to matching)
    /// </summary>
    private async Task ProcessSafetyFlagAsync(MatchAfterthought afterthought, CancellationToken ct)
    {
        // Check current trust score to avoid redundant flags
        var currentTrust = await _trust.GetTrustScoreAsync(afterthought.AboutUserId, ct);
        float trustCap = afterthought.ClosePath == "block" ? BlockTrustCap : SafetyFlagTrustCap;

        if (currentTrust <= trustCap)
        {
            _logger.LogDebug(
                "[AfterthoughtIntegration] Trust already at or below cap | AboutUser={About} Current={Current:F2} Cap={Cap:F2}",
                afterthought.AboutUserId, currentTrust, trustCap);
            return; // Already flagged or lower
        }

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
        // Use PostgreSQL jsonb operator for exact match, avoiding ID confusion (1 vs 10)
        var existingSignal = await _db.MatchSignalLogs
            .FromSqlRaw(
                @"SELECT * FROM ""MatchSignalLogs""
                  WHERE ""EventType"" = {0}
                    AND ""ViewerId"" = {1}
                    AND ""CandidateId"" = {2}
                    AND (""MetadataJson""::jsonb->>'afterthoughtId')::int = {3}
                  LIMIT 1",
                MatchSignalEventTypes.ExplicitFeedback,
                afterthought.FromUserId,
                afterthought.AboutUserId,
                afterthought.Id)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        if (existingSignal != null)
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
