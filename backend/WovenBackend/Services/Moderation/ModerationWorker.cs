namespace WovenBackend.Services.Moderation;

public class ModerationWorker : BackgroundService
{
    private const string LockKey = "lock:moderation-pass";
    private static readonly TimeSpan Interval   = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LockExpiry = TimeSpan.FromMinutes(4); // shorter than interval

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICacheService _cache;
    private readonly ILogger<ModerationWorker> _logger;

    public ModerationWorker(IServiceScopeFactory scopeFactory, ICacheService cache, ILogger<ModerationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _cache        = cache;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[ModerationWorker] Started — polling every {Minutes} minutes", Interval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken);

            if (!await _cache.AcquireLockAsync(LockKey, LockExpiry, stoppingToken))
                continue; // another pod is mid-pass

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
            finally
            {
                await _cache.ReleaseLockAsync(LockKey, stoppingToken);
            }
        }
    }
}
