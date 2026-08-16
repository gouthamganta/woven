using Microsoft.Extensions.DependencyInjection;
using WovenBackend.Services.Recommendations;

namespace WovenBackend.Services.Matchmaking;

/// <summary>
/// Nightly batch worker that computes collaborative filtering scores.
/// Runs CollaborativeFilteringService to populate CfScores table.
/// Schedule: Daily at 05:00 UTC
/// </summary>
public class CfScoreBatchWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CfScoreBatchWorker> _logger;
    private readonly bool _disabled;

    public CfScoreBatchWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<CfScoreBatchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _disabled = config.GetValue<bool>("WOVEN_DISABLE_BATCH_WORKERS");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_disabled)
        {
            _logger.LogInformation("[CfScoreBatch] Worker disabled via WOVEN_DISABLE_BATCH_WORKERS");
            return;
        }

        _logger.LogInformation("[CfScoreBatch] Worker started, waiting for first run at 05:00 UTC");

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var targetToday = new DateTime(now.Year, now.Month, now.Day, 5, 0, 0, DateTimeKind.Utc);

            // If we've passed 05:00 today, target tomorrow
            var nextRun = now >= targetToday
                ? targetToday.AddDays(1)
                : targetToday;

            var delay = nextRun - now;
            _logger.LogInformation("[CfScoreBatch] Next run scheduled for {NextRun} UTC (in {Hours}h {Minutes}m)",
                nextRun, delay.Hours, delay.Minutes);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // Run collaborative filtering
            try
            {
                _logger.LogInformation("[CfScoreBatch] Starting CF batch run");
                var startTime = DateTime.UtcNow;

                using var scope = _scopeFactory.CreateScope();
                var cfService = scope.ServiceProvider.GetRequiredService<ICollaborativeFilteringService>();

                await cfService.RunAsync(stoppingToken);

                var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                _logger.LogInformation("[CfScoreBatch] Completed in {Elapsed:F1}s", elapsed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CfScoreBatch] Batch run failed");
            }
        }

        _logger.LogInformation("[CfScoreBatch] Worker stopped");
    }
}
