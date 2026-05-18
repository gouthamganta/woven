using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;

namespace WovenBackend.Services.Matchmaking;

public class WeightLearningBatchWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WeightLearningBatchWorker> _logger;

    public WeightLearningBatchWorker(IServiceScopeFactory scopeFactory, ILogger<WeightLearningBatchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await SleepUntilSunday4AmUtcAsync(ct);
            if (ct.IsCancellationRequested) break;

            var start = DateTime.UtcNow;
            _logger.LogInformation("[WeightLearningBatchWorker] Starting weekly weight learning at {Time}", start);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();

                // Active users with >=5 outcomes
                var eligibleUserIds = await db.MatchOutcomes.AsNoTracking()
                    .GroupBy(o => o.UserId)
                    .Where(g => g.Count() >= 5)
                    .Select(g => g.Key)
                    .ToListAsync(ct);

                int count = 0;
                foreach (var userId in eligibleUserIds)
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        using var innerScope = _scopeFactory.CreateScope();
                        var svc = innerScope.ServiceProvider.GetRequiredService<IWeightLearningService>();
                        await svc.LearnWeightsAsync(userId, ct);
                        count++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[WeightLearningBatchWorker] Failed for user {UserId}", userId);
                    }
                }

                _logger.LogInformation("[WeightLearningBatchWorker] Completed in {Ms}ms — {Count} users updated",
                    (int)(DateTime.UtcNow - start).TotalMilliseconds, count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WeightLearningBatchWorker] Batch failed");
            }
        }
    }

    private static async Task SleepUntilSunday4AmUtcAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        int daysUntilSunday = ((int)DayOfWeek.Sunday - (int)now.DayOfWeek + 7) % 7;
        if (daysUntilSunday == 0 && now.Hour >= 4) daysUntilSunday = 7;

        var next = now.Date.AddDays(daysUntilSunday).AddHours(4);
        var delay = next - now;
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, ct);
    }
}
