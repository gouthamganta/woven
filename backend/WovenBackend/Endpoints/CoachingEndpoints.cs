using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;

namespace WovenBackend.Endpoints;

public static class CoachingEndpoints
{
    public static void MapCoachingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/coaching");
        group.RequireAuthorization();

        // GET /coaching/current-summary → latest undelivered/unread summary or 204
        group.MapGet("/current-summary", async (
            WovenDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);

            var summary = await db.CoachingSummaries
                .Where(c => c.UserId == userId && c.DismissedAt == null && c.OptedOutAt == null)
                .OrderByDescending(c => c.DeliveredAt)
                .FirstOrDefaultAsync(ct);

            if (summary == null) return Results.NoContent();

            return Results.Ok(new
            {
                id          = summary.Id,
                summaryText = summary.SummaryText,
                deliveredAt = summary.DeliveredAt,
                weekStart   = summary.WeekStartDate.ToString("yyyy-MM-dd")
            });
        });

        // POST /coaching/{id}/dismiss → sets dismissed_at
        group.MapPost("/{id:long}/dismiss", async (
            long id,
            WovenDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);

            var summary = await db.CoachingSummaries
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);

            if (summary == null) return Results.NotFound();

            summary.DismissedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { dismissed = true });
        });

        // POST /coaching/opt-out → sets coaching_opted_out = true + dismisses unread
        group.MapPost("/opt-out", async (
            WovenDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);
            var now    = DateTimeOffset.UtcNow;

            await db.Users
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.CoachingOptedOut, true), ct);

            // Dismiss all unread summaries
            await db.CoachingSummaries
                .Where(c => c.UserId == userId && c.DismissedAt == null)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(c => c.OptedOutAt, now)
                     .SetProperty(c => c.DismissedAt, now), ct);

            return Results.Ok(new { optedOut = true });
        });

        // POST /coaching/opt-in → sets coaching_opted_out = false
        group.MapPost("/opt-in", async (
            WovenDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);

            await db.Users
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.CoachingOptedOut, false), ct);

            return Results.Ok(new { optedIn = true });
        });
    }

    private static int GetUserId(ClaimsPrincipal user) => EndpointHelper.GetUserId(user);
}
