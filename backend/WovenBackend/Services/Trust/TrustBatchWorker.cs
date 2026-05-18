using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;

namespace WovenBackend.Services.Trust;

public class TrustBatchWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TrustBatchWorker> _logger;

    public TrustBatchWorker(IServiceScopeFactory scopeFactory, ILogger<TrustBatchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[TrustBatch] Started — nightly run at 02:00 UTC");

        while (!stoppingToken.IsCancellationRequested)
        {
            await WaitForNextRunAsync(stoppingToken);
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                await RunBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TrustBatch] Error during nightly trust batch");
            }
        }
    }

    private static async Task WaitForNextRunAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var next2Am = now.Date.AddHours(2);
        if (now >= next2Am) next2Am = next2Am.AddDays(1);

        var delay = next2Am - now;
        await Task.Delay(delay, ct);
    }

    private async Task RunBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var trust = scope.ServiceProvider.GetRequiredService<ITrustService>();
        var db    = scope.ServiceProvider.GetRequiredService<WovenDbContext>();

        // Process users active in the last 30 days
        var cutoff = DateTime.UtcNow.AddDays(-30);
        var activeUserIds = await db.Users.AsNoTracking()
            .Where(u => u.UpdatedAt >= cutoff)
            .Select(u => u.Id)
            .ToListAsync(ct);

        _logger.LogInformation("[TrustBatch] Running bot detection for {Count} active users", activeUserIds.Count);

        foreach (var userId in activeUserIds)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await trust.RunBotDetectionAsync(userId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TrustBatch] Failed bot detection for user {UserId}", userId);
            }
        }

        _logger.LogInformation("[TrustBatch] Completed nightly trust pass for {Count} users", activeUserIds.Count);
    }
}
