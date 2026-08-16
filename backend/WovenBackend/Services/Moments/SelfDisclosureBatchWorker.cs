using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.data.Entities.Moments;

namespace WovenBackend.Services.Moments;

public class SelfDisclosureBatchWorker : BackgroundService
{
    private const string LockKey = "lock:self-disclosure-batch";
    private static readonly TimeSpan LockExpiry = TimeSpan.FromHours(2);

    // Threads with fewer combined messages don't have stable ratios yet
    private const int MinMessageCount = 4;

    // Only re-score threads that had activity in the last 7 days
    private static readonly TimeSpan ActivityWindow = TimeSpan.FromDays(7);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICacheService _cache;
    private readonly ILogger<SelfDisclosureBatchWorker> _logger;

    public SelfDisclosureBatchWorker(
        IServiceScopeFactory scopeFactory,
        ICacheService cache,
        ILogger<SelfDisclosureBatchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("[SelfDisclosure] Started — nightly run at 03:45 UTC");

        while (!ct.IsCancellationRequested)
        {
            await WaitForNextRunAsync(ct);
            if (ct.IsCancellationRequested) break;

            if (!await _cache.AcquireLockAsync(LockKey, LockExpiry, ct))
            {
                _logger.LogInformation("[SelfDisclosure] Skipping — another pod holds the lock");
                continue;
            }

            try
            {
                await RunBatchAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SelfDisclosure] Error during nightly batch");
            }
            finally
            {
                await _cache.ReleaseLockAsync(LockKey, ct);
            }
        }
    }

    private static async Task WaitForNextRunAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var next = now.Date.AddHours(3).AddMinutes(45);
        if (now >= next) next = next.AddDays(1);
        await Task.Delay(next - now, ct);
    }

    private async Task RunBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();
        var signals = scope.ServiceProvider.GetRequiredService<IMatchSignalService>();

        var cutoff = DateTime.UtcNow - ActivityWindow;

        // Load active threads with recent activity
        var threads = await db.ChatThreads.AsNoTracking()
            .Where(t => t.LastMessageAt != null && t.LastMessageAt >= cutoff)
            .Join(db.Matches.AsNoTracking().Where(m => m.BalloonState == BalloonState.ACTIVE),
                  t => t.MatchId, m => m.Id,
                  (t, m) => new { t.Id, m.UserAId, m.UserBId })
            .ToListAsync(ct);

        _logger.LogInformation("[SelfDisclosure] Processing {Count} active threads", threads.Count);

        int processed = 0, skipped = 0;

        foreach (var thread in threads)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                // Aggregate char counts per sender in a single pass
                var stats = await db.ChatMessages.AsNoTracking()
                    .Where(m => m.ThreadId == thread.Id)
                    .GroupBy(m => m.SenderUserId)
                    .Select(g => new { SenderId = g.Key, TotalChars = g.Sum(m => (int)m.Body.Length), Count = g.Count() })
                    .ToListAsync(ct);

                var totalMessages = stats.Sum(s => s.Count);
                if (totalMessages < MinMessageCount) { skipped++; continue; }

                var totalChars = stats.Sum(s => s.TotalChars);
                if (totalChars == 0) { skipped++; continue; }

                var charsByUser = stats.ToDictionary(s => s.SenderId, s => s.TotalChars);

                var aChars = charsByUser.GetValueOrDefault(thread.UserAId, 0);
                var bChars = charsByUser.GetValueOrDefault(thread.UserBId, 0);

                // Only record if both participants have sent at least one message
                if (aChars == 0 || bChars == 0) { skipped++; continue; }

                var ratioA = (float)aChars / totalChars;
                var ratioB = (float)bChars / totalChars;

                await signals.RecordAsync(thread.UserAId, thread.UserBId,
                    MatchSignalEventTypes.SelfDisclosureRatio, ratioA, ct: ct);
                await signals.RecordAsync(thread.UserBId, thread.UserAId,
                    MatchSignalEventTypes.SelfDisclosureRatio, ratioB, ct: ct);

                processed++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SelfDisclosure] Failed for thread {ThreadId}", thread.Id);
            }
        }

        _logger.LogInformation("[SelfDisclosure] Done — processed={Processed} skipped={Skipped}",
            processed, skipped);
    }
}
