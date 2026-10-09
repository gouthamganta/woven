using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Services;

namespace WovenBackend.Endpoints;

public static class InteractionEndpoints
{
    public static void MapInteractionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/me").RequireAuthorization();

        // GET /me/pending-tasks
        group.MapGet("/pending-tasks", GetPendingTasks);

        // POST /me/interaction-log
        group.MapPost("/interaction-log", LogInteraction);
    }

    // ========================================
    // GET /me/pending-tasks
    // ========================================
    private static async Task<IResult> GetPendingTasks(
        HttpContext http,
        WovenDbContext db,
        CancellationToken ct)
    {
        var userId = EndpointHelper.GetUserId(http.User);

        var tasks = new List<object>();

        // 1. Check for Date Feedback pending (highest priority)
        var hasFeedbackPending = await db.Matches
            .Where(m => (m.UserAId == userId || m.UserBId == userId)
                && m.BalloonState == BalloonState.CLOSED
                && !db.Set<DateFeedback>()
                    .Any(f => f.MatchId == m.Id && f.UserId == userId))
            .AnyAsync(ct);

        if (hasFeedbackPending)
        {
            var match = await db.Matches
                .Where(m => (m.UserAId == userId || m.UserBId == userId)
                    && m.BalloonState == BalloonState.CLOSED
                    && !db.Set<DateFeedback>()
                        .Any(f => f.MatchId == m.Id && f.UserId == userId))
                .OrderByDescending(m => m.ClosedAt)
                .FirstAsync(ct);

            // Get partner name
            var partnerId = match.UserAId == userId ? match.UserBId : match.UserAId;
            var partner = await db.Users
                .Where(u => u.Id == partnerId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync(ct);

            tasks.Add(new
            {
                type = "date_feedback",
                priority = 1,
                data = new
                {
                    matchId = match.Id,
                    partnerName = partner ?? "Someone"
                }
            });
        }

        // 2. Check for Weekly Coaching pending
        var coachingSummary = await db.Set<WovenBackend.Data.Entities.CoachingSummary>()
            .Where(c => c.UserId == userId && !c.DismissedAt.HasValue && c.DeliveredAt.HasValue)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (coachingSummary != null)
        {
            tasks.Add(new
            {
                type = "weekly_coaching",
                priority = 2,
                data = new
                {
                    summaryId = coachingSummary.Id,
                    summaryText = coachingSummary.SummaryText
                }
            });
        }

        // 3. Check for Daily Pulse pending
        var currentCycle = await db.UserDynamicIntakeSets
            .Where(d => d.UserId == userId
                && d.CycleEndUtc > DateTimeOffset.UtcNow
                && !d.AnsweredAtUtc.HasValue)
            .OrderByDescending(d => d.CycleStartUtc)
            .FirstOrDefaultAsync(ct);

        if (currentCycle != null)
        {
            tasks.Add(new
            {
                type = "daily_pulse",
                priority = 3,
                data = new
                {
                    cycleId = currentCycle.CycleId
                }
            });
        }

        // Sort by priority
        var sorted = tasks.OrderBy(t => ((dynamic)t).priority).ToList();

        return Results.Ok(new { tasks = sorted });
    }

    // ========================================
    // POST /me/interaction-log
    // ========================================
    private record LogInteractionRequest(
        string EventType,
        Dictionary<string, object>? Context
    );

    private static async Task<IResult> LogInteraction(
        [FromBody] LogInteractionRequest req,
        HttpContext http,
        IInteractionLogService interactionLog,
        CancellationToken ct)
    {
        var userId = EndpointHelper.GetUserId(http.User);

        await interactionLog.LogAsync(userId, req.EventType, req.Context, ct);

        return Results.Ok(new { logged = true });
    }
}
