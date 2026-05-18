namespace WovenBackend.Services.Recommendations;

public class CfBatchWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CfBatchWorker> _logger;

    public CfBatchWorker(IServiceScopeFactory scopeFactory, ILogger<CfBatchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await SleepUntil3AmUtcAsync(ct);
            if (ct.IsCancellationRequested) break;

            var start = DateTime.UtcNow;
            _logger.LogInformation("[CfBatchWorker] Starting CF batch run at {Time}", start);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ICollaborativeFilteringService>();
                await service.RunAsync(ct);

                _logger.LogInformation("[CfBatchWorker] CF batch completed in {Ms}ms",
                    (int)(DateTime.UtcNow - start).TotalMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CfBatchWorker] CF batch failed after {Ms}ms",
                    (int)(DateTime.UtcNow - start).TotalMilliseconds);
            }
        }
    }

    private static async Task SleepUntil3AmUtcAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var next3Am = now.Date.AddDays(now.Hour >= 3 ? 1 : 0).AddHours(3);
        var delay = next3Am - now;
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, ct);
    }
}
