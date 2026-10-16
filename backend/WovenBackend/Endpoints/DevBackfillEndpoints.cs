using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Services.Security;

namespace WovenBackend.Endpoints;

public static class DevBackfillEndpoints
{
    public static void MapDevBackfillEndpoints(this WebApplication app)
    {
        // Only enable in Development
        if (!app.Environment.IsDevelopment()) return;

        var group = app.MapGroup("/dev/backfill")
            .WithTags("Dev Backfill");

        // POST /dev/backfill/email-hash — one-time backfill for existing users
        group.MapPost("/email-hash", async (
            WovenDbContext db,
            IEncryptionService encryption,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var usersWithoutHash = await db.Users
                .Where(u => u.EmailHash == "")
                .ToListAsync(ct);

            if (usersWithoutHash.Count == 0)
            {
                logger.LogInformation("[Backfill] No users need EmailHash backfill");
                return Results.Ok(new
                {
                    status = "already_complete",
                    message = "All users already have EmailHash populated",
                    usersUpdated = 0
                });
            }

            logger.LogInformation("[Backfill] Starting EmailHash backfill for {Count} users",
                usersWithoutHash.Count);

            var updated = 0;
            var failed = 0;

            foreach (var user in usersWithoutHash)
            {
                try
                {
                    // Email is automatically decrypted by EF value converter
                    user.EmailHash = encryption.ComputeEmailHash(user.Email);
                    updated++;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[Backfill] Failed to compute EmailHash for user {UserId}", user.Id);
                    failed++;
                }
            }

            await db.SaveChangesAsync(ct);

            logger.LogInformation("[Backfill] EmailHash backfill complete: {Updated} updated, {Failed} failed",
                updated, failed);

            return Results.Ok(new
            {
                status = "completed",
                usersUpdated = updated,
                usersFailed = failed,
                totalProcessed = usersWithoutHash.Count
            });
        })
        .WithName("BackfillEmailHash")
        .WithSummary("One-time backfill: compute EmailHash for existing users")
        .WithDescription("Decrypts each user's email and computes SHA-256 EmailHash for searchable identity lookup. Development only.");
    }
}
