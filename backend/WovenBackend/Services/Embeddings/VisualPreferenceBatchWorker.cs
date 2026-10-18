using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;

namespace WovenBackend.Services.Embeddings;

/// <summary>
/// Runs nightly at 04:30 UTC. Aggregates UserVisualDecision records (YES/NO choices
/// from Moments responses) into PreferenceEmbedding and AversionEmbedding vectors
/// stored in UserVisualPreference. This is the visual attraction learning pipeline.
/// </summary>
public class VisualPreferenceBatchWorker : BackgroundService
{
    private const string LockKey = "lock:visual-preference-batch";
    private static readonly TimeSpan LockExpiry = TimeSpan.FromHours(3);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICacheService _cache;
    private readonly ILogger<VisualPreferenceBatchWorker> _logger;

    public VisualPreferenceBatchWorker(
        IServiceScopeFactory scopeFactory,
        ICacheService cache,
        ILogger<VisualPreferenceBatchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("[VisualPreference] Started — nightly run at 04:30 UTC");

        while (!ct.IsCancellationRequested)
        {
            await WaitForNextRunAsync(ct);
            if (ct.IsCancellationRequested) break;

            if (!await _cache.AcquireLockAsync(LockKey, LockExpiry, ct))
            {
                _logger.LogInformation("[VisualPreference] Skipping — another pod holds the lock");
                continue;
            }

            var start = DateTime.UtcNow;
            _logger.LogInformation("[VisualPreference] Starting nightly batch at {Time}", start);

            try
            {
                await RunBatchAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[VisualPreference] Batch failed");
            }
            finally
            {
                await _cache.ReleaseLockAsync(LockKey, ct);
                _logger.LogInformation("[VisualPreference] Done in {Ms}ms",
                    (int)(DateTime.UtcNow - start).TotalMilliseconds);
            }
        }
    }

    private static async Task WaitForNextRunAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var next = now.Date.AddHours(4).AddMinutes(30);
        if (now >= next) next = next.AddDays(1);
        await Task.Delay(next - now, ct);
    }

    private async Task RunBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();

        // All users who have made visual decisions
        var userIds = await db.UserVisualDecisions.AsNoTracking()
            .Select(d => d.ViewerUserId)
            .Distinct()
            .ToListAsync(ct);

        _logger.LogInformation("[VisualPreference] Processing {Count} users", userIds.Count);

        int processed = 0, errors = 0;

        foreach (var userId in userIds)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                using var inner = _scopeFactory.CreateScope();
                var svc = inner.ServiceProvider.GetRequiredService<IVisualPreferenceService>();
                await svc.UpdateVisualPreferenceAsync(userId, ct);
                processed++;
            }
            catch (Exception ex)
            {
                errors++;
                _logger.LogWarning(ex, "[VisualPreference] Failed for user {UserId}", userId);
            }
        }

        _logger.LogInformation("[VisualPreference] Completed — processed={P} errors={E}", processed, errors);
    }
}
