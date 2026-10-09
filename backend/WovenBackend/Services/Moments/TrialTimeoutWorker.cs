using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Services.Analytics;

namespace WovenBackend.Services.Moments;

public class TrialTimeoutWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WovenBackend.Services.INotificationService _notifications;
    private readonly IAnalyticsService _analytics;
    private readonly ILogger<TrialTimeoutWorker> _logger;

    // If second user never opens thread, trial times out after 24 hours
    private static readonly TimeSpan AbandonedTrialTimeout = TimeSpan.FromHours(24);

    public TrialTimeoutWorker(
        IServiceScopeFactory scopeFactory,
        WovenBackend.Services.INotificationService notifications,
        IAnalyticsService analytics,
        ILogger<TrialTimeoutWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _notifications = notifications;
        _analytics = analytics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[TrialTimeoutWorker] Started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TimeoutTrialsOnce(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TrialTimeoutWorker] Failed");
            }

            // Check every minute for trials that have timed out
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task TimeoutTrialsOnce(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();

        var now = MomentsRules.NowUtc();
        var abandonCutoff = now.Subtract(AbandonedTrialTimeout);

        // Find trials that have timed out:
        // 1. TrialEndsAt is set and has passed
        // 2. TrialEndsAt is null but trial started > 24h ago (abandoned)
        var timedOut = await db.Matches
            .Where(m => m.BalloonState == BalloonState.ACTIVE)
            .Where(m => m.IsTrial == true)
            .Where(m =>
                (m.TrialEndsAt != null && m.TrialEndsAt <= now) || // 3-min window passed
                (m.TrialEndsAt == null && m.TrialStartedAt != null && m.TrialStartedAt <= abandonCutoff) // never opened
            )
            .ToListAsync(ct);

        if (timedOut.Count == 0) return;

        foreach (var m in timedOut)
        {
            m.BalloonState = BalloonState.CLOSED;
            m.ClosedReason = ClosedReason.TRIAL_TIMEOUT;
            m.ClosedAt = now;
            m.IsTrial = false; // clear trial flag
        }

        await db.SaveChangesAsync(ct);

        // Notify both users
        foreach (var m in timedOut)
        {
            await _notifications.TrialTimeoutAsync(m.UserAId, m.UserBId, m.Id, ct);
        }

        // Track analytics
        foreach (var m in timedOut)
        {
            var wasAbandoned = m.TrialEndsAt == null;
            _ = _analytics.TrackAsync(m.UserAId, null, AnalyticsEvents.TrialTimedOut,
                new { matchType = m.MatchType.ToString(), abandoned = wasAbandoned });
            _ = _analytics.TrackAsync(m.UserBId, null, AnalyticsEvents.TrialTimedOut,
                new { matchType = m.MatchType.ToString(), abandoned = wasAbandoned });
        }

        _logger.LogInformation("[TrialTimeoutWorker] Closed {Count} timed-out trials", timedOut.Count);
    }
}
