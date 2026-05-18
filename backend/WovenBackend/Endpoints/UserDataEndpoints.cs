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

        // DELETE /me/account — hard delete; anonymizes matches and removes all media
        group.MapDelete("/account", async (
            ClaimsPrincipal principal,
            WovenDbContext db,
            IMediaService media,
            ISecurityAuditService audit,
            CancellationToken ct) =>
        {
            var userId = GetUserId(principal);

            // Log before delete so we still have an audit trail.
            audit.Log("account_deletion", userId: userId, resourceType: "User", resourceId: userId.ToString());

            // Delete all blobs across all containers.
            await media.DeleteAllForUserAsync(userId, ct);

            // Anonymize matches: preserve the record for the other participant.
            var matchesAsA = await db.Matches.Where(m => m.UserAId == userId).ToListAsync(ct);
            var matchesAsB = await db.Matches.Where(m => m.UserBId == userId).ToListAsync(ct);
            foreach (var m in matchesAsA) m.UserAId = 0;
            foreach (var m in matchesAsB) m.UserBId = 0;

            // Bulk-delete owned data before removing the user row.
            await db.Tiles.Where(t => t.UserId == userId).ExecuteDeleteAsync(ct);
            await db.ChatMessages.Where(m => m.SenderUserId == userId).ExecuteDeleteAsync(ct);
            await db.PhotoEmbeddings.Where(p => p.UserId == userId).ExecuteDeleteAsync(ct);
            await db.UserVisualDecisions.Where(d => d.ViewerUserId == userId).ExecuteDeleteAsync(ct);
            await db.UserVectors.Where(v => v.UserId == userId).ExecuteDeleteAsync(ct);
            await db.UserVisualPreferences.Where(p => p.UserId == userId).ExecuteDeleteAsync(ct);
            await db.UserVoicePreferences.Where(p => p.UserId == userId).ExecuteDeleteAsync(ct);
            await db.UserMatchingWeights.Where(w => w.UserId == userId).ExecuteDeleteAsync(ct);
            await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(ct);

            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                deleted = true,
                note = "AI processors (OpenAI, Replicate) may retain embeddings per their own retention policies. Contact support to submit deletion requests to those providers."
            });
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
