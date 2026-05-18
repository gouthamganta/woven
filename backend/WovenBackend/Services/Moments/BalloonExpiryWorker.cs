using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Services.Analytics;

namespace WovenBackend.Services.Moments;

public class BalloonExpiryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WovenBackend.Services.INotificationService _notifications;
    private readonly IAnalyticsService _analytics;
    private readonly ILogger<BalloonExpiryWorker> _logger;

    public BalloonExpiryWorker(
        IServiceScopeFactory scopeFactory,
        WovenBackend.Services.INotificationService notifications,
        IAnalyticsService analytics,
        ILogger<BalloonExpiryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _notifications = notifications;
        _analytics = analytics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BalloonExpiryWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExpireOnce(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BalloonExpiryWorker failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task ExpireOnce(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();

        var now = MomentsRules.NowUtc();

        // ✅ Only expire ACTIVE balloons that have NO 2-way communication
        var expired = await db.Matches
            .Where(m => m.BalloonState == BalloonState.ACTIVE)
            .Where(m => m.ClosedAt == null)
            .Where(m => m.BothMessagedAt == null)     // ✅ key rule
            .Where(m => m.ExpiresAt <= now)
            .ToListAsync(ct);

        if (expired.Count == 0) return;

        foreach (var m in expired)
        {
            m.BalloonState = BalloonState.CLOSED;
            m.ClosedReason = ClosedReason.EXPIRE;
            m.ClosedAt = now;
        }

        await db.SaveChangesAsync(ct);

        // Phase 1C: notify both sides that their balloon expired
        foreach (var m in expired)
            await _notifications.MomentExpiredAsync(m.UserAId, m.UserBId, m.Id, ct);

        // Phase 5C: track match_expired
        foreach (var m in expired)
        {
            _ = _analytics.TrackAsync(m.UserAId, null, AnalyticsEvents.MatchExpired,
                new { matchType = m.MatchType.ToString(), messageCount = 0 });
            _ = _analytics.TrackAsync(m.UserBId, null, AnalyticsEvents.MatchExpired,
                new { matchType = m.MatchType.ToString(), messageCount = 0 });
        }

        // Phase 4C: fire-and-forget insight delivery at match_closed moment
        foreach (var m in expired)
        {
            var matchId = m.Id;
            var userAId = m.UserAId;
            var userBId = m.UserBId;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var s = _scopeFactory.CreateScope();
                    var svc = s.ServiceProvider.GetRequiredService<WovenBackend.Services.Insights.IInsightService>();
                    await svc.DeliverInsightAtMomentAsync(userAId, "match_closed");
                    await svc.DeliverInsightAtMomentAsync(userBId, "match_closed");
                }
                catch { /* non-critical */ }
            });
        }

        _logger.LogInformation("Expired {Count} balloons (no 2-way comm)", expired.Count);
    }
}
