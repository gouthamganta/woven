using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.Services.Moderation;
using WovenBackend.Services.Trust;

namespace WovenBackend.Endpoints;

public static class AdminEndpoints
{
    private record RejectRequest(string Reason);

    public static void MapAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/admin").RequireAuthorization("Admin");

        // 1. GET /admin/moderation/queue — pending items (50 max)
        group.MapGet("/moderation/queue", async (
            IModerationService moderation,
            CancellationToken ct) =>
        {
            var items = await moderation.GetPendingAsync(50, ct);
            return Results.Ok(new { count = items.Count, items });
        });

        // 2. POST /admin/moderation/{itemId}/approve
        group.MapPost("/moderation/{itemId:guid}/approve", async (
            Guid itemId,
            IModerationService moderation,
            HttpContext http,
            CancellationToken ct) =>
        {
            var reviewerId = GetUserId(http.User);
            var ok = await moderation.ApproveAsync(itemId, reviewerId, ct);
            return ok
                ? Results.Ok(new { approved = itemId })
                : Results.NotFound(new { error = "QUEUE_ITEM_NOT_FOUND_OR_ALREADY_REVIEWED" });
        });

        // 3. POST /admin/moderation/{itemId}/reject
        group.MapPost("/moderation/{itemId:guid}/reject", async (
            Guid itemId,
            RejectRequest req,
            IModerationService moderation,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Reason))
                return Results.BadRequest(new { error = "REASON_REQUIRED" });

            var reviewerId = GetUserId(http.User);
            var ok = await moderation.RejectAsync(itemId, reviewerId, req.Reason, ct);
            return ok
                ? Results.Ok(new { rejected = itemId })
                : Results.NotFound(new { error = "QUEUE_ITEM_NOT_FOUND_OR_ALREADY_REVIEWED" });
        });

        // 4. GET /admin/tiles/{tileId}/reports
        group.MapGet("/tiles/{tileId:guid}/reports", async (
            Guid tileId,
            IModerationService moderation,
            CancellationToken ct) =>
        {
            var reports = await moderation.GetReportsAsync(tileId, ct);
            return Results.Ok(new { count = reports.Count, reports });
        });

        // 5. GET /admin/trust/{userId}
        group.MapGet("/trust/{userId:int}", async (
            int userId,
            ITrustService trust,
            WovenDbContext db,
            CancellationToken ct) =>
        {
            var user = await db.Users.AsNoTracking()
                .Select(u => new { u.Id, u.Email, u.TrustScore, u.TrustUpdatedAt })
                .FirstOrDefaultAsync(u => u.Id == userId, ct);

            if (user is null)
                return Results.NotFound(new { error = "USER_NOT_FOUND" });

            return Results.Ok(new
            {
                userId      = user.Id,
                email       = user.Email,
                trustScore  = user.TrustScore,
                updatedAt   = user.TrustUpdatedAt
            });
        });

        // 6. POST /admin/trust/{userId}/reset — restore to default 0.5
        group.MapPost("/trust/{userId:int}/reset", async (
            int userId,
            WovenDbContext db,
            CancellationToken ct) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null)
                return Results.NotFound(new { error = "USER_NOT_FOUND" });

            user.TrustScore     = 0.5f;
            user.TrustUpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { userId, trustScore = user.TrustScore });
        });
    }

    private static int GetUserId(ClaimsPrincipal user)
    {
        var uid = user.FindFirstValue("uid");
        if (int.TryParse(uid, out var id)) return id;
        var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(sub, out id)) return id;
        throw new UnauthorizedAccessException("Missing user id claim");
    }
}
