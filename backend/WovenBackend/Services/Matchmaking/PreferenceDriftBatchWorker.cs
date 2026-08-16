using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;

namespace WovenBackend.Services.Matchmaking;

public class PreferenceDriftBatchWorker : BackgroundService
{
    private const string LockKey = "lock:preference-drift-batch";
    private static readonly TimeSpan LockExpiry = TimeSpan.FromHours(3);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICacheService _cache;
    private readonly ILogger<PreferenceDriftBatchWorker> _logger;

    public PreferenceDriftBatchWorker(
        IServiceScopeFactory scopeFactory,
        ICacheService cache,
        ILogger<PreferenceDriftBatchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("[PreferenceDrift] Started — nightly run at 04:15 UTC");

        while (!ct.IsCancellationRequested)
        {
            await WaitForNextRunAsync(ct);
            if (ct.IsCancellationRequested) break;

            if (!await _cache.AcquireLockAsync(LockKey, LockExpiry, ct))
            {
                _logger.LogInformation("[PreferenceDrift] Skipping — another pod holds the lock");
                continue;
            }

            var start = DateTime.UtcNow;
            _logger.LogInformation("[PreferenceDrift] Starting nightly batch at {Time}", start);

            try
            {
                await RunBatchAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PreferenceDrift] Batch failed");
            }
            finally
            {
                await _cache.ReleaseLockAsync(LockKey, ct);
                _logger.LogInformation("[PreferenceDrift] Done in {Ms}ms",
                    (int)(DateTime.UtcNow - start).TotalMilliseconds);
            }
        }
    }

    private static async Task WaitForNextRunAsync(CancellationToken ct)
    {
        var now  = DateTime.UtcNow;
        var next = now.Date.AddHours(4).AddMinutes(15);
        if (now >= next) next = next.AddDays(1);
        await Task.Delay(next - now, ct);
    }

    private async Task RunBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db    = scope.ServiceProvider.GetRequiredService<WovenDbContext>();

        // Only drift for users who have at least one qualifying ConnectionScore
        var userIds = await db.ConnectionScores.AsNoTracking()
            .Where(c => c.Score >= 0.15f)
            .Select(c => c.ViewerId)
            .Distinct()
            .ToListAsync(ct);

        _logger.LogInformation("[PreferenceDrift] Processing {Count} users", userIds.Count);

        int processed = 0, errors = 0;

        foreach (var userId in userIds)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                using var inner = _scopeFactory.CreateScope();
                var svc = inner.ServiceProvider.GetRequiredService<IPreferenceDriftService>();
                await svc.DriftForUserAsync(userId, ct);
                processed++;
            }
            catch (Exception ex)
            {
                errors++;
                _logger.LogWarning(ex, "[PreferenceDrift] Failed for user {UserId}", userId);
            }
        }

        _logger.LogInformation("[PreferenceDrift] Completed — processed={P} errors={E}", processed, errors);
    }
}
