namespace WovenBackend.Services.Moderation;

public class ModerationWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ModerationWorker> _logger;

    public ModerationWorker(IServiceScopeFactory scopeFactory, ILogger<ModerationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[ModerationWorker] Started — polling every {Minutes} minutes", Interval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var moderation = scope.ServiceProvider.GetRequiredService<IModerationService>();
                await moderation.ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ModerationWorker] Error during moderation pass");
            }
        }
    }
}
