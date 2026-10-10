using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.Services;
using WovenBackend.Services.Security;

namespace WovenBackend.Endpoints;

public static class UserDataEndpoints
{
    public static void MapUserDataEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/me").RequireAuthorization();

        // GET /me/data-summary — lightweight overview of what we hold
        group.MapGet("/data-summary", async (
            ClaimsPrincipal principal,
            WovenDbContext db,
            CancellationToken ct) =>
        {
            var userId = GetUserId(principal);

            var tileCount = await db.Tiles.CountAsync(t => t.UserId == userId, ct);
            var momentCount = await db.MomentResponses.CountAsync(m => m.FromUserId == userId, ct);
            var chatCount = await db.ChatMessages.CountAsync(m => m.SenderUserId == userId, ct);
            var photoCount = await db.PhotoEmbeddings.CountAsync(p => p.UserId == userId, ct);

            return Results.Ok(new
            {
                userId,
                tiles = tileCount,
                momentResponses = momentCount,
                chatMessages = chatCount,
                photos = photoCount,
                thirdPartyProcessors = new[] { "OpenAI (semantic embeddings)", "Replicate (photo embeddings)" }
            });
        });

        // GET /me/data-export — full export; rate-limited to 1 per 30 days (string flag + TTL)
        group.MapGet("/data-export", async (
            ClaimsPrincipal principal,
            WovenDbContext db,
            ICacheService cache,
            ISecurityAuditService audit,
            CancellationToken ct) =>
        {
            var userId = GetUserId(principal);
            var rateLimitKey = $"data-export:{userId}";

            var flagged = await cache.GetAsync<string>(rateLimitKey, ct);
            if (flagged != null)
                return Results.StatusCode(429);

            var user = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.Email, u.FullName, u.CreatedAt })
                .FirstOrDefaultAsync(ct);

            if (user is null) return Results.NotFound();

            var tiles = await db.Tiles
                .AsNoTracking()
                .Where(t => t.UserId == userId)
                .Select(t => new { t.Id, t.MediaUrl, t.CreatedAt })
                .ToListAsync(ct);

            var messages = await db.ChatMessages
                .AsNoTracking()
                .Where(m => m.SenderUserId == userId)
                .Select(m => new { m.Id, m.ThreadId, m.Body, m.CreatedAt })
                .ToListAsync(ct);

            var visualPrefs = await db.UserVisualPreferences
                .AsNoTracking()
                .Where(p => p.UserId == userId)
                .Select(p => new { p.YesSampleCount, p.NoSampleCount, p.UpdatedAt })
                .FirstOrDefaultAsync(ct);

            await cache.SetAsync(rateLimitKey, "exported", TimeSpan.FromDays(30), ct);

            audit.Log("bulk_data_export", userId: userId, resourceType: "User", resourceId: userId.ToString());

            return Results.Ok(new
            {
                exportedAt = DateTimeOffset.UtcNow,
                note = "AI processors (OpenAI, Replicate) may retain data per their own retention policies.",
                profile = user,
                tiles,
                chatMessages = messages,
                visualPreferences = visualPrefs
            });
        });

        // POST /me/visual-preference/reset
        group.MapPost("/visual-preference/reset", async (
            ClaimsPrincipal principal,
            WovenDbContext db,
            ISecurityAuditService audit,
            CancellationToken ct) =>
        {
            var userId = GetUserId(principal);

            var pref = await db.UserVisualPreferences.FindAsync([userId], ct);
            if (pref is not null)
            {
                db.UserVisualPreferences.Remove(pref);
                await db.UserVisualDecisions
                    .Where(d => d.ViewerUserId == userId)
                    .ExecuteDeleteAsync(ct);
                await db.SaveChangesAsync(ct);
            }

            audit.Log("preference_reset", userId: userId, resourceType: "VisualPreference", resourceId: userId.ToString());

            return Results.Ok(new { reset = true });
        });

        // POST /me/voice-preference/reset
        group.MapPost("/voice-preference/reset", async (
            ClaimsPrincipal principal,
            WovenDbContext db,
            ISecurityAuditService audit,
            CancellationToken ct) =>
        {
            var userId = GetUserId(principal);

            var pref = await db.UserVoicePreferences.FindAsync([userId], ct);
            if (pref is not null)
            {
                db.UserVoicePreferences.Remove(pref);
                await db.SaveChangesAsync(ct);
            }

            audit.Log("preference_reset", userId: userId, resourceType: "VoicePreference", resourceId: userId.ToString());

            return Results.Ok(new { reset = true });
        });

        // GET /me/blocks — list of users the caller has blocked
        group.MapGet("/blocks", async (
            ClaimsPrincipal principal,
            WovenDbContext db,
            CancellationToken ct) =>
        {
            var userId = GetUserId(principal);

            var blocked = await db.Blocks
                .Where(b => b.BlockerId == userId)
                .Join(db.Users,
                    b => b.BlockedId,
                    u => u.Id,
                    (b, u) => new
                    {
                        userId = u.Id,
                        name = u.FullName ?? "Unknown",
                        photo = u.ProfilePhoto,
                        blockedAt = b.CreatedAt
                    })
                .OrderByDescending(x => x.blockedAt)
                .ToListAsync(ct);

            return Results.Ok(blocked);
        });

        // DELETE /me/blocks/{targetUserId} — unblock a user
        group.MapDelete("/blocks/{targetUserId:int}", async (
            int targetUserId,
            ClaimsPrincipal principal,
            WovenDbContext db,
            CancellationToken ct) =>
        {
            var userId = GetUserId(principal);
            var deleted = await db.Blocks
                .Where(b => b.BlockerId == userId && b.BlockedId == targetUserId)
                .ExecuteDeleteAsync(ct);
            return deleted > 0 ? Results.Ok(new { unblocked = true }) : Results.NotFound();
        });

        // DELETE /me/account — hard delete; anonymizes matches and removes all media
        group.MapDelete("/account", async (
            ClaimsPrincipal principal,
            WovenDbContext db,
            IMediaService media,
            ISecurityAuditService audit,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var userId = GetUserId(principal);

            // Log before delete so we still have an audit trail.
            audit.Log("account_deletion", userId: userId, resourceType: "User", resourceId: userId.ToString());

            // Wrap all database operations in a transaction
            // Blobs deleted AFTER DB commits to avoid orphaned data on DB failure
            using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                // 1. Anonymize matches: preserve the record for the other participant
                // Save immediately so changes aren't lost if bulk deletes fail
                var matchesAsA = await db.Matches.Where(m => m.UserAId == userId).ToListAsync(ct);
                var matchesAsB = await db.Matches.Where(m => m.UserBId == userId).ToListAsync(ct);
                foreach (var m in matchesAsA) m.UserAId = 0;
                foreach (var m in matchesAsB) m.UserBId = 0;
                await db.SaveChangesAsync(ct);

                // 2. Bulk-delete owned data in dependency order (children before parents)
                // Auth & Identity
                await db.AuthIdentities.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.PushSubscriptions.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.IdempotencyRecords.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);

                // Profile & Onboarding
                await db.UserProfiles.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.UserPreferences.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.UserIntents.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.UserPhotos.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.UserOptionalFields.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.UserWeeklyVibes.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);

                // Foundational & Dynamic Intake
                await db.UserFoundationalQuestionSets.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.UserFoundationalV1s.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.UserDynamicIntakeSets.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);

                // Verification & Trust
                await db.UserVerifications.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);

                // Sparks & Wallets
                await db.SparkWallets.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);

                // Moments & Matches
                await db.DailyDecks.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.DailyInteractions.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.MomentResponses.Where(x => x.FromUserId == userId || x.ToUserId == userId).ExecuteDeleteAsync(ct);
                await db.PendingMatches.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.MatchExplanations.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.MatchOutcomes.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.DateFeedbacks.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.DateFeedbackPrompts.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.ChatAvailabilitySignals.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);

                // Commons & Content
                await db.Tiles.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.Highlights.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.TileViews.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.TileEngagements.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.OrbitGravities.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.UserEnergyMeters.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);

                // Chat & Messages
                await db.ChatMessages.Where(x => x.SenderUserId == userId).ExecuteDeleteAsync(ct);

                // Ratings (user is the rater)
                await db.UserRatings.Where(x => x.RaterUserId == userId).ExecuteDeleteAsync(ct);

                // ECHO & Matching
                await db.UserVectors.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.UserVectorTags.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.UserMatchingWeights.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.UserBehavioralFingerprints.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.CfScores.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.LinUcbUserModels.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);

                // Embeddings & Preferences
                await db.PhotoEmbeddings.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.UserVisualPreferences.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.UserVoicePreferences.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.UserVisualDecisions.Where(x => x.ViewerUserId == userId).ExecuteDeleteAsync(ct);

                // Insights & Analytics
                await db.UserInsights.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.UserInteractionLogs.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);

                // Coaching & Seasons
                await db.CoachingSummaries.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.UserSeasonResponses.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);

                // A/B Testing
                await db.AbAssignments.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
                await db.AbConversions.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);

                // Moderation (user as reporter or subject - keep for audit trail)
                // NOT deleted: ModerationQueue items where user is flagged/reporter

                // Finally, delete the user row
                await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(ct);

                // Commit database transaction
                await transaction.CommitAsync(ct);

                // Only after DB commit succeeds, delete blobs
                // If this throws, DB is already committed but blobs remain (acceptable orphan)
                try
                {
                    await media.DeleteAllForUserAsync(userId, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[UserData] Blob deletion failed after account deletion for UserId={UserId}. Database deleted, blobs orphaned.", userId);
                    // Don't fail the request - account is deleted from DB
                }

                return Results.Ok(new
                {
                    deleted = true,
                    note = "AI processors (OpenAI, Replicate) may retain embeddings per their own retention policies. Contact support to submit deletion requests to those providers."
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[UserData] Account deletion failed for UserId={UserId}", userId);
                await transaction.RollbackAsync(ct);
                throw; // 500 with global exception handler
            }
        });
    }

    private static int GetUserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue("uid")
               ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? throw new UnauthorizedAccessException("No user ID claim");
        return int.Parse(raw);
    }
}

// ── Push subscription endpoints ────────────────────────────────────────────────
public static class PushEndpoints
{
    public static void MapPushEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/me").RequireAuthorization();

        // GET /me/vapid-public-key — frontend needs this to subscribe
        group.MapGet("/vapid-public-key", (WovenBackend.Services.PushNotifications.IWebPushService push) =>
            Results.Ok(new { publicKey = push.GetPublicVapidKey() }));

        // POST /me/push-subscription — register a browser push subscription
        group.MapPost("/push-subscription", async (
            PushSubscriptionRequest req,
            ClaimsPrincipal principal,
            WovenDbContext db,
            CancellationToken ct) =>
        {
            var userId = GetPushUserId(principal);

            // Upsert by endpoint — one browser registers one endpoint
            var existing = await db.PushSubscriptions
                .FirstOrDefaultAsync(s => s.UserId == userId && s.Endpoint == req.Endpoint, ct);

            if (existing is null)
            {
                db.PushSubscriptions.Add(new WovenBackend.Data.Entities.UserPushSubscription
                {
                    UserId = userId,
                    Endpoint = req.Endpoint,
                    P256dh = req.P256dh,
                    Auth = req.Auth,
                    UserAgent = req.UserAgent,
                });
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new { registered = true });
        });

        // DELETE /me/push-subscription — unregister (on toggle off or logout)
        group.MapDelete("/push-subscription", async (
            PushUnsubscribeRequest req,
            ClaimsPrincipal principal,
            WovenDbContext db,
            CancellationToken ct) =>
        {
            var userId = GetPushUserId(principal);
            await db.PushSubscriptions
                .Where(s => s.UserId == userId && s.Endpoint == req.Endpoint)
                .ExecuteDeleteAsync(ct);
            return Results.Ok(new { unregistered = true });
        });
    }

    private static int GetPushUserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue("uid")
               ?? principal.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)
               ?? throw new UnauthorizedAccessException("No user ID claim");
        return int.Parse(raw);
    }
}

internal record PushSubscriptionRequest(string Endpoint, string P256dh, string Auth, string? UserAgent);
internal record PushUnsubscribeRequest(string Endpoint);
