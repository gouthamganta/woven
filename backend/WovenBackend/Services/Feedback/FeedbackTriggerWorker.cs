namespace WovenBackend.Services.Feedback;

public class FeedbackTriggerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FeedbackTriggerWorker> _logger;

    private static readonly TimeSpan DailyTarget = new(8, 0, 0); // 08:00 UTC

    public FeedbackTriggerWorker(IServiceScopeFactory scopeFactory, ILogger<FeedbackTriggerWorker> logger)
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

            if (nowUtc.TimeOfDay >= DailyTarget && today > lastRunDate)
            {
                lastRunDate = today;
                await RunPassAsync(stoppingToken);
            }

            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
        }
    }

    private async Task RunPassAsync(CancellationToken ct)
    {
        _logger.LogInformation("[FeedbackTrigger] Starting daily pass");

        await using var scope = _scopeFactory.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDateFeedbackService>();

        try { await svc.QueueFeedbackPromptsAsync(ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "[FeedbackTrigger] QueueFeedbackPromptsAsync failed"); }

        try { await svc.SendDuePromptsAsync(ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "[FeedbackTrigger] SendDuePromptsAsync failed"); }

        try { await svc.ReschedulePendingAsync(ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "[FeedbackTrigger] ReschedulePendingAsync failed"); }

        _logger.LogInformation("[FeedbackTrigger] Daily pass complete");
    }
}
