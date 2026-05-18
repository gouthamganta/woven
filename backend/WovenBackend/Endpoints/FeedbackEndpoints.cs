using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Services.Feedback;

namespace WovenBackend.Endpoints;

public static class FeedbackEndpoints
{
    private record FeedbackRequest(
        bool MetInPerson,
        int? Stars,
        string? FeltRightText,
        string? FeltOffText,
        string? MeetAgain);

    private static readonly HashSet<string> ValidMeetAgain =
        new(StringComparer.OrdinalIgnoreCase) { "yes", "no", "maybe" };

    public static void MapFeedbackEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/me");
        group.RequireAuthorization();

        // GET /me/feedback-prompt
        group.MapGet("/feedback-prompt", async (
            HttpContext http,
            WovenDbContext db,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);

            var prompt = await db.DateFeedbackPrompts.AsNoTracking()
                .Where(p => p.UserId == userId && p.SentAt != null && p.RespondedAt == null)
                .OrderByDescending(p => p.SentAt)
                .FirstOrDefaultAsync(ct);

            if (prompt == null)
                return Results.Ok(new { hasPendingPrompt = false });

            var match = await db.Matches.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == prompt.MatchId, ct);
            if (match == null)
                return Results.Ok(new { hasPendingPrompt = false });

            var partnerId = match.UserAId == userId ? match.UserBId : match.UserAId;
            var partnerName = await db.Users.AsNoTracking()
                .Where(u => u.Id == partnerId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync(ct) ?? "your match";
            var firstName = partnerName.Split(' ')[0];

            return Results.Ok(new
            {
                hasPendingPrompt = true,
                matchId = prompt.MatchId,
                partnerFirstName = firstName,
                triggerType = prompt.TriggerType
            });
        });

        var matchGroup = app.MapGroup("/matches");
        matchGroup.RequireAuthorization();

        // POST /matches/{matchId}/feedback
        matchGroup.MapPost("/{matchId:guid}/feedback", async (
            Guid matchId,
            FeedbackRequest req,
            IDateFeedbackService feedbackSvc,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);

            if (req.Stars.HasValue && (req.Stars < 1 || req.Stars > 5))
                return Results.BadRequest(new { error = "INVALID_STARS" });

            if (req.MeetAgain != null && !ValidMeetAgain.Contains(req.MeetAgain))
                return Results.BadRequest(new { error = "INVALID_MEET_AGAIN" });

            if (req.FeltRightText?.Length > 300 || req.FeltOffText?.Length > 300)
                return Results.BadRequest(new { error = "TEXT_TOO_LONG" });

            try
            {
                await feedbackSvc.SubmitFeedbackAsync(userId, matchId, new DateFeedbackDto(
                    req.MetInPerson, req.Stars, req.FeltRightText, req.FeltOffText, req.MeetAgain), ct);
            }
            catch (InvalidOperationException)
            {
                return Results.NotFound(new { error = "NO_PROMPT_FOUND" });
            }

            return Results.Ok(new { submitted = true });
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
