namespace WovenBackend.Services.AntiGhosting;

public class GhostDetectionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GhostDetectionWorker> _logger;

    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private static readonly TimeSpan NightlyTarget = new(3, 30, 0); // 03:30 UTC

    public GhostDetectionWorker(IServiceScopeFactory scopeFactory, ILogger<GhostDetectionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastNightlyRun = DateOnly.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<IGhostDetectionService>();

                await svc.ProcessSilentThreadsAsync(stoppingToken);
                await svc.ProcessExpiringBalloonsAsync(stoppingToken);

                var nowUtc = DateTimeOffset.UtcNow;
                var today = DateOnly.FromDateTime(nowUtc.UtcDateTime);
                if (today > lastNightlyRun && nowUtc.TimeOfDay >= NightlyTarget)
                {
                    await svc.UpdateGhostScoresAsync(stoppingToken);
                    lastNightlyRun = today;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GhostWorker] Pass failed");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
