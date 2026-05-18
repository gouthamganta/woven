using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;

namespace WovenBackend.Services.Insights;

public class WeeklyDigestWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WeeklyDigestWorker> _logger;

    // Sunday 06:00 UTC
    private static readonly DayOfWeek TargetDay = DayOfWeek.Sunday;
    private static readonly TimeSpan TargetTime = new(6, 0, 0);

    public WeeklyDigestWorker(IServiceScopeFactory scopeFactory, ILogger<WeeklyDigestWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastRunDate = DateOnly.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            var nowUtc = DateTimeOffset.UtcNow;
            var today = DateOnly.FromDateTime(nowUtc.UtcDateTime);

            if (nowUtc.DayOfWeek == TargetDay && nowUtc.TimeOfDay >= TargetTime && today > lastRunDate)
            {
                lastRunDate = today;
                await RunDigestPassAsync(stoppingToken);
            }

            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
        }
    }

    private async Task RunDigestPassAsync(CancellationToken ct)
    {
        _logger.LogInformation("[WeeklyDigest] Starting weekly digest pass");
        int processed = 0, skipped = 0, errors = 0;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();
        var ai = scope.ServiceProvider.GetRequiredService<IOpenAiResilientClient>();
        var notify = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var insightSvc = scope.ServiceProvider.GetRequiredService<IInsightService>();

        var weekStart = DateTimeOffset.UtcNow.AddDays(-7);

        var activeUsers = await db.Users.AsNoTracking()
            .Where(u => u.LastActiveAt >= weekStart)
            .Select(u => u.Id)
            .ToListAsync(ct);

        foreach (var userId in activeUsers)
        {
            try
            {
                // Week summary stats
                var newMatches = await db.Matches.AsNoTracking()
                    .CountAsync(m => (m.UserAId == userId || m.UserBId == userId)
                                  && m.CreatedAt >= weekStart, ct);

                var newThreadIds = await (
                    from t in db.ChatThreads.AsNoTracking()
                    join m in db.Matches.AsNoTracking() on t.MatchId equals m.Id
                    where (m.UserAId == userId || m.UserBId == userId) && t.CreatedAt >= weekStart
                    select t.Id
                ).ToListAsync(ct);

                var convsStarted = newThreadIds.Count;

                // Skip if nothing to report
                if (newMatches == 0 && convsStarted == 0)
                {
                    skipped++;
                    continue;
                }

                // Best connection pillar: average pillar scores of positive matches this week
                var weekMatchIds = await db.Matches.AsNoTracking()
                    .Where(m => (m.UserAId == userId || m.UserBId == userId)
                                && m.CreatedAt >= weekStart)
                    .Select(m => new { m.Id, OtherUserId = m.UserAId == userId ? m.UserBId : m.UserAId })
                    .ToListAsync(ct);

                var otherIds = weekMatchIds.Select(m => m.OtherUserId).ToList();
                var pillarData = await db.UserVectors.AsNoTracking()
                    .Where(v => otherIds.Contains(v.UserId))
                    .GroupBy(v => v.UserId)
                    .Select(g => g.OrderByDescending(v => v.Version).First())
                    .Select(v => new { v.PillarScoresJson })
                    .ToListAsync(ct);

                var bestPillar = "varied interests";
                if (pillarData.Count > 0)
                {
                    var pillarSums = new Dictionary<string, double>();
                    foreach (var pd in pillarData)
                    {
                        try
                        {
                            var scores = System.Text.Json.JsonSerializer
                                .Deserialize<Dictionary<string, double>>(pd.PillarScoresJson);
                            if (scores == null) continue;
                            foreach (var kv in scores)
                            {
                                pillarSums.TryGetValue(kv.Key, out var s);
                                pillarSums[kv.Key] = s + kv.Value;
                            }
                        }
                        catch { /* skip */ }
                    }
                    if (pillarSums.Count > 0)
                        bestPillar = pillarSums.MaxBy(kv => kv.Value).Key;
                }

                var digestPrompt =
                    "Generate a warm 2-sentence weekly dating app summary. " +
                    $"Matches: {newMatches}. Conversations: {convsStarted}. Pattern: {bestPillar}. " +
                    "Max 200 chars total.";

                var digest = await ai.ExecuteAsync("weekly_digest", digestPrompt, useJsonMode: false, ct);
                if (string.IsNullOrWhiteSpace(digest))
                {
                    skipped++;
                    continue;
                }

                await notify.SendPushAsync(userId, digest.Trim(), ct);
                await insightSvc.ComputeInsightsAsync(userId, ct);

                processed++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[WeeklyDigest] Failed for user {UserId}", userId);
                errors++;
            }
        }

        _logger.LogInformation("[WeeklyDigest] Done — processed={P} skipped={S} errors={E}",
            processed, skipped, errors);
    }
}
