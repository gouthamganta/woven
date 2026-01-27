using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.data.Entities.Moments;

namespace WovenBackend.Services.Moments;

public class BalloonExpiryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BalloonExpiryWorker> _logger;

    public BalloonExpiryWorker(IServiceScopeFactory scopeFactory, ILogger<BalloonExpiryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BalloonExpiryWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExpireOnce(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BalloonExpiryWorker failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task ExpireOnce(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();

        var now = MomentsRules.NowUtc();

        // ✅ Only expire ACTIVE balloons that have NO 2-way communication
        var expired = await db.Matches
            .Where(m => m.BalloonState == BalloonState.ACTIVE)
            .Where(m => m.ClosedAt == null)
            .Where(m => m.BothMessagedAt == null)     // ✅ key rule
            .Where(m => m.ExpiresAt <= now)
            .ToListAsync(ct);

        if (expired.Count == 0) return;

        foreach (var m in expired)
        {
            m.BalloonState = BalloonState.CLOSED;
            m.ClosedReason = ClosedReason.EXPIRE;
            m.ClosedAt = now;
        }

        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Expired {Count} balloons (no 2-way comm)", expired.Count);
    }
}
