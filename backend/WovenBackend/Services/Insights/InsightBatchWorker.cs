using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;

namespace WovenBackend.Services.Insights;

public class InsightBatchWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InsightBatchWorker> _logger;

    private static readonly TimeSpan NightlyTarget = new(4, 30, 0); // 04:30 UTC

    public InsightBatchWorker(IServiceScopeFactory scopeFactory, ILogger<InsightBatchWorker> logger)
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

            if (nowUtc.TimeOfDay >= NightlyTarget && today > lastRunDate)
            {
                lastRunDate = today;
                await RunBatchPassAsync(stoppingToken);
            }

            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
        }
    }

    private async Task RunBatchPassAsync(CancellationToken ct)
    {
        _logger.LogInformation("[InsightBatch] Starting nightly pass");
        int processed = 0, opinionPrompts = 0, errors = 0;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();
        var notify = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var insightSvc = scope.ServiceProvider.GetRequiredService<IInsightService>();

        var cutoff = DateTimeOffset.UtcNow.AddDays(-14);
        var activeUsers = await db.Users.AsNoTracking()
            .Where(u => u.LastActiveAt >= cutoff)
            .Select(u => u.Id)
            .ToListAsync(ct);

        foreach (var userId in activeUsers)
        {
            try
            {
                var (shouldAsk, _, prompt) = await insightSvc.ShouldAskOpinionAsync(userId, ct);

                if (shouldAsk && !string.IsNullOrEmpty(prompt))
                {
                    await notify.SendPushAsync(userId, prompt, ct);
                    opinionPrompts++;
                }

                await insightSvc.ComputeInsightsAsync(userId, ct);
                processed++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[InsightBatch] Failed for user {UserId}", userId);
                errors++;
            }
        }

        _logger.LogInformation("[InsightBatch] Done — processed={P} opinion_prompts_sent={O} errors={E}",
            processed, opinionPrompts, errors);
    }
}
