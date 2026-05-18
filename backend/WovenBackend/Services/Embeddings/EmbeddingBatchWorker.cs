using WovenBackend.Data;
using WovenBackend.Data.Entities;

namespace WovenBackend.Services.Embeddings;

public class EmbeddingBatchWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmbeddingBatchWorker> _logger;

    public EmbeddingBatchWorker(IServiceScopeFactory scopeFactory, ILogger<EmbeddingBatchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await SleepUntil2h30AmUtcAsync(ct);
            if (ct.IsCancellationRequested) break;

            var start = DateTime.UtcNow;
            _logger.LogInformation("[EmbeddingBatchWorker] Starting embedding batch at {Time}", start);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();

                var userIds = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                    .ToListAsync(
                        db.Users.Where(u => u.ProfileStatus == ProfileStatus.COMPLETE).Select(u => u.Id),
                        ct);

                int processed = 0, skipped = 0, errors = 0;

                foreach (var userId in userIds)
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        using var innerScope = _scopeFactory.CreateScope();
                        var sp = innerScope.ServiceProvider;

                        await sp.GetRequiredService<IStyleEmbeddingService>().ComputeStyleEmbeddingAsync(userId, ct);
                        await sp.GetRequiredService<IHumorEmbeddingService>().ComputeHumorEmbeddingAsync(userId, ct);
                        await sp.GetRequiredService<ILifestyleEmbeddingService>().ComputeLifestyleEmbeddingAsync(userId, ct);
                        await sp.GetRequiredService<IEmotionalRhythmService>().ComputeEmotionalRhythmAsync(userId, ct);
                        await sp.GetRequiredService<IAttachmentProxyService>().ComputeAttachmentProxyAsync(userId, ct);
                        await sp.GetRequiredService<IVisualPreferenceService>().UpdateVisualPreferenceAsync(userId, ct);

                        processed++;
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        _logger.LogError(ex, "[EmbeddingBatchWorker] Error processing user {UserId}", userId);
                    }
                }

                _logger.LogInformation(
                    "[EmbeddingBatchWorker] Batch completed in {Ms}ms — processed={P}, skipped={S}, errors={E}",
                    (int)(DateTime.UtcNow - start).TotalMilliseconds, processed, skipped, errors);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EmbeddingBatchWorker] Batch failed");
            }
        }
    }

    private static async Task SleepUntil2h30AmUtcAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var targetHour = 2;
        var targetMinute = 30;
        var next = now.Date.AddDays(
            (now.Hour > targetHour || (now.Hour == targetHour && now.Minute >= targetMinute)) ? 1 : 0)
            .AddHours(targetHour)
            .AddMinutes(targetMinute);
        var delay = next - now;
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, ct);
    }
}
