using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Services.Moments;

namespace WovenBackend.Services.AntiGhosting;

public class GhostDetectionService : IGhostDetectionService
{
    private readonly WovenDbContext _db;
    private readonly ICacheService _cache;
    private readonly INotificationService _notify;
    private readonly InteractionBudgetService _budget;
    private readonly ILogger<GhostDetectionService> _logger;

    private const int SilentThresholdHours = 24;
    private const int RefundKeyTtlDays = 3;

    public GhostDetectionService(
        WovenDbContext db,
        ICacheService cache,
        INotificationService notify,
        InteractionBudgetService budget,
        ILogger<GhostDetectionService> logger)
    {
        _db = db;
        _cache = cache;
        _notify = notify;
        _budget = budget;
        _logger = logger;
    }

    public async Task ProcessSilentThreadsAsync(CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-SilentThresholdHours);

        var activeThreads = await (
            from t in _db.ChatThreads.AsNoTracking()
            join m in _db.Matches.AsNoTracking() on t.MatchId equals m.Id
            where m.BalloonState == BalloonState.ACTIVE
            select new { ThreadId = t.Id, m.UserAId, m.UserBId }
        ).ToListAsync(ct);

        foreach (var item in activeThreads)
        {
            try
            {
                var lastMsg = await _db.ChatMessages.AsNoTracking()
                    .Where(m => m.ThreadId == item.ThreadId)
                    .OrderByDescending(m => m.CreatedAt)
                    .FirstOrDefaultAsync(ct);

                if (lastMsg == null || lastMsg.CreatedAt > cutoff) continue;

                var waitingUserId = lastMsg.SenderUserId;

                var refundKey = $"ghost:refunded:{item.ThreadId}:{waitingUserId}";
                var alreadyRefunded = await _cache.GetAsync<string>(refundKey, ct);
                if (alreadyRefunded != null) continue;

                await _budget.RefundSparkAsync(waitingUserId, ct);
                await _notify.SendPushAsync(waitingUserId,
                    "You haven’t heard back — we’ve refunded your spark.", ct);

                await _cache.SetAsync(refundKey, "1", TimeSpan.FromDays(RefundKeyTtlDays), ct);

                _logger.LogInformation(
                    "[Ghost] Refunded spark for user {WaitingUserId} in thread {ThreadId}",
                    waitingUserId, item.ThreadId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Ghost] ProcessSilentThreads failed for thread {ThreadId}", item.ThreadId);
            }
        }
    }

    public async Task ProcessExpiringBalloonsAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var window = now.AddHours(24);

        var expiring = await _db.Matches.AsNoTracking()
            .Where(m => m.BalloonState == BalloonState.ACTIVE
                        && m.ExpiresAt > now
                        && m.ExpiresAt <= window)
            .ToListAsync(ct);

        foreach (var match in expiring)
        {
            try
            {
                var notifyKey = $"ghost:expiry-notified:{match.Id}";
                var alreadyNotified = await _cache.GetAsync<string>(notifyKey, ct);
                if (alreadyNotified != null) continue;

                var hoursLeft = (int)Math.Ceiling((match.ExpiresAt - now).TotalHours);
                var msg = $"Your balloon expires in ~{hoursLeft}h. Don’t let the moment slip away.";

                await Task.WhenAll(
                    _notify.SendPushAsync(match.UserAId, msg, ct),
                    _notify.SendPushAsync(match.UserBId, msg, ct));

                await _cache.SetAsync(notifyKey, "1", TimeSpan.FromHours(25), ct);

                _logger.LogInformation("[Ghost] Expiry nudge sent for match {MatchId} (expires {ExpiresAt})",
                    match.Id, match.ExpiresAt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Ghost] ProcessExpiringBalloons failed for match {MatchId}", match.Id);
            }
        }
    }

    public async Task UpdateGhostScoresAsync(CancellationToken ct = default)
    {
        var lookback = DateTimeOffset.UtcNow.AddDays(-90);

        var userIds = await _db.Users.AsNoTracking()
            .Select(u => u.Id)
            .ToListAsync(ct);

        foreach (var userId in userIds)
        {
            try
            {
                var participantThreadIds = await (
                    from t in _db.ChatThreads.AsNoTracking()
                    join m in _db.Matches.AsNoTracking() on t.MatchId equals m.Id
                    where (m.UserAId == userId || m.UserBId == userId)
                          && m.CreatedAt >= lookback
                    select t.Id
                ).ToListAsync(ct);

                if (participantThreadIds.Count == 0) continue;

                int totalReceived = 0;
                int replied = 0;

                foreach (var threadId in participantThreadIds)
                {
                    var firstMsg = await _db.ChatMessages.AsNoTracking()
                        .Where(m => m.ThreadId == threadId)
                        .OrderBy(m => m.CreatedAt)
                        .FirstOrDefaultAsync(ct);

                    if (firstMsg == null || firstMsg.SenderUserId == userId) continue;

                    totalReceived++;

                    var userReplied = await _db.ChatMessages.AsNoTracking()
                        .AnyAsync(m => m.ThreadId == threadId && m.SenderUserId == userId, ct);

                    if (userReplied) replied++;
                }

                if (totalReceived == 0) continue;

                var newScore = (float)((replied + 1.0) / (totalReceived + 2.0));
                newScore = Math.Clamp(newScore, 0.1f, 1.0f);

                await _db.Users
                    .Where(u => u.Id == userId)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.GhostScore, newScore), ct);

                _logger.LogDebug("[Ghost] User {UserId}: ghostScore={Score:F2} ({Replied}/{Total})",
                    userId, newScore, replied, totalReceived);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Ghost] UpdateGhostScores failed for user {UserId}", userId);
            }
        }
    }
}
