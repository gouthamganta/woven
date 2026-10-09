using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;

namespace WovenBackend.Services.Security;

/// <summary>
/// One-time worker to backfill EmailHash for existing users.
/// Once all users have EmailHash, this can be removed.
/// </summary>
public class EmailHashBackfillWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailHashBackfillWorker> _logger;

    public EmailHashBackfillWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<EmailHashBackfillWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[EmailHashBackfill] Starting one-time backfill");

        try
        {
            await BackfillOnce(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EmailHashBackfill] Failed");
        }

        _logger.LogInformation("[EmailHashBackfill] Completed, worker will exit");
    }

    private async Task BackfillOnce(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();
        var enc = scope.ServiceProvider.GetRequiredService<IEncryptionService>();

        // Find all users with null EmailHash
        var users = await db.Users
            .Where(u => u.EmailHash == null)
            .ToListAsync(ct);

        if (users.Count == 0)
        {
            _logger.LogInformation("[EmailHashBackfill] No users need backfilling");
            return;
        }

        _logger.LogInformation("[EmailHashBackfill] Backfilling {Count} users", users.Count);

        int processed = 0;
        foreach (var user in users)
        {
            try
            {
                // Decrypt email, compute hash, store
                var plainEmail = enc.Decrypt(user.Email);
                user.EmailHash = enc.ComputeEmailHash(plainEmail);
                processed++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[EmailHashBackfill] Failed to backfill user {UserId}", user.Id);
            }
        }

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[EmailHashBackfill] Successfully backfilled {Processed}/{Total} users",
            processed, users.Count);
    }
}
