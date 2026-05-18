using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.Services;
using WovenBackend.Services.Analytics;
using WovenBackend.Services.Insights;

namespace WovenBackend.Endpoints;

public static class MeEndpoints
{
    private record OpinionRequest(string Text, string Trigger);
    private record AccessibilityRequest(bool? ReduceMotion, bool? HighContrast, string? DisplayPronouns);

    private static readonly HashSet<string> KnownTriggers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "no_dates_yet", "pattern_shift", "high_rejection", "low_depth"
        };

    public static void MapMeEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/me");
        group.RequireAuthorization();

        // GET /me/insights
        group.MapGet("/insights", async (
            IInsightService insights,
            IAnalyticsService analytics,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);

            var db = http.RequestServices.GetRequiredService<WovenDbContext>();
            var row = await db.UserInsights.AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == userId, ct);

            var insightList = JsonSerializer.Deserialize<List<string>>(row?.InsightsJson ?? "[]")
                              ?? new List<string>();

            var (shouldAsk, trigger, prompt) = await insights.ShouldAskOpinionAsync(userId, ct);

            _ = analytics.TrackAsync(userId, null, AnalyticsEvents.InsightViewed,
                new { hasInsights = insightList.Count > 0, shouldAskOpinion = shouldAsk });

            return Results.Ok(new
            {
                insights = insightList,
                shouldAskOpinion = shouldAsk,
                opinionTrigger = trigger,
                opinionPrompt = prompt
            });
        });

        // POST /me/insights/opinion
        group.MapPost("/insights/opinion", async (
            OpinionRequest req,
            IInsightService insights,
            ICacheService cache,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);

            // Rate limit: 1 opinion per user per calendar month
            var monthYear = DateTime.UtcNow.ToString("yyyy-MM");
            var rlKey = $"rl:opinion:{userId}:{monthYear}";
            var allowed = await cache.CheckRateLimitAsync(rlKey, 1, TimeSpan.FromDays(31), ct);
            if (!allowed)
            {
                var now = DateTime.UtcNow;
                var nextMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
                http.Response.Headers["Retry-After"] = ((int)(nextMonth - now).TotalSeconds).ToString();
                return Results.StatusCode(429);
            }

            if (string.IsNullOrWhiteSpace(req.Text))
                return Results.BadRequest(new { error = "TEXT_REQUIRED" });

            if (req.Text.Length > 300)
                return Results.BadRequest(new { error = "TEXT_TOO_LONG" });

            if (string.IsNullOrWhiteSpace(req.Trigger) || !KnownTriggers.Contains(req.Trigger))
                return Results.BadRequest(new { error = "INVALID_TRIGGER" });

            await insights.SubmitOpinionAsync(userId, req.Text, req.Trigger, ct);

            return Results.Ok(new { submitted = true });
        });

        // PUT /me/accessibility
        group.MapPut("/accessibility", async (
            AccessibilityRequest req,
            WovenDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);

            if (req.DisplayPronouns != null && req.DisplayPronouns.Length > 50)
                return Results.BadRequest(new { error = "PRONOUNS_TOO_LONG" });

            var pref = await db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);
            if (pref != null)
            {
                if (req.ReduceMotion.HasValue) pref.ReduceMotion = req.ReduceMotion.Value;
                if (req.HighContrast.HasValue) pref.HighContrast = req.HighContrast.Value;
            }

            var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
            if (profile != null && req.DisplayPronouns != null)
                profile.DisplayPronouns = req.DisplayPronouns;

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { updated = true });
        });

        // GET /me/accessibility
        group.MapGet("/accessibility", async (
            WovenDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);

            var pref = await db.UserPreferences.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId, ct);
            var profile = await db.UserProfiles.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId, ct);

            return Results.Ok(new
            {
                reduceMotion = pref?.ReduceMotion ?? false,
                highContrast = pref?.HighContrast ?? false,
                displayPronouns = profile?.DisplayPronouns
            });
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
