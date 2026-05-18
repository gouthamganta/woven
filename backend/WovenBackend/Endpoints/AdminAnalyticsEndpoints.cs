using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.Services.Analytics;

namespace WovenBackend.Endpoints;

public static class AdminAnalyticsEndpoints
{
    public static void MapAdminAnalyticsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/admin/analytics")
            .RequireAuthorization("Admin");

        // GET /admin/analytics/overview
        group.MapGet("/overview", async (WovenDbContext db, CancellationToken ct) =>
        {
            var thirtyDaysAgo = DateTimeOffset.UtcNow.AddDays(-30);
            var sevenDaysAgo  = DateTimeOffset.UtcNow.AddDays(-7);
            var oneDayAgo     = DateTimeOffset.UtcNow.AddDays(-1);

            var totalRegistrations = await db.AnalyticsEvents
                .CountAsync(e => e.EventType == AnalyticsEvents.UserRegistered, ct);

            var appOpens30d = await db.AnalyticsEvents
                .CountAsync(e => e.EventType == AnalyticsEvents.AppOpened && e.CreatedAt >= thirtyDaysAgo, ct);

            var matchesCreated30d = await db.AnalyticsEvents
                .CountAsync(e => e.EventType == AnalyticsEvents.MatchCreated && e.CreatedAt >= thirtyDaysAgo, ct);

            var dau = await db.AnalyticsEvents
                .Where(e => e.CreatedAt >= oneDayAgo && e.UserIdHash != null)
                .Select(e => e.UserIdHash)
                .Distinct()
                .CountAsync(ct);

            var wau = await db.AnalyticsEvents
                .Where(e => e.CreatedAt >= sevenDaysAgo && e.UserIdHash != null)
                .Select(e => e.UserIdHash)
                .Distinct()
                .CountAsync(ct);

            var mau = await db.AnalyticsEvents
                .Where(e => e.CreatedAt >= thirtyDaysAgo && e.UserIdHash != null)
                .Select(e => e.UserIdHash)
                .Distinct()
                .CountAsync(ct);

            return Results.Ok(new
            {
                totalRegistrations,
                appOpens30d,
                matchesCreated30d,
                dau,
                wau,
                mau,
                asOf = DateTimeOffset.UtcNow
            });
        }).WithName("AdminAnalyticsOverview");

        // GET /admin/analytics/funnel
        group.MapGet("/funnel", async (WovenDbContext db, CancellationToken ct) =>
        {
            var registered = await db.AnalyticsEvents
                .CountAsync(e => e.EventType == AnalyticsEvents.UserRegistered, ct);

            var steps = new[] { "welcome", "basics", "intent", "foundational", "details", "complete" };
            var stepCounts = new Dictionary<string, int>();

            foreach (var step in steps)
            {
                var pattern = $"\"step\":\"{step}\"";
                stepCounts[step] = await db.AnalyticsEvents
                    .CountAsync(e => e.EventType == AnalyticsEvents.OnboardingStepCompleted
                                  && e.Properties != null
                                  && e.Properties.Contains(pattern), ct);
            }

            return Results.Ok(new
            {
                registered,
                onboarding = stepCounts,
                asOf = DateTimeOffset.UtcNow
            });
        }).WithName("AdminAnalyticsFunnel");

        // GET /admin/analytics/content
        group.MapGet("/content", async (WovenDbContext db, CancellationToken ct) =>
        {
            var thirtyDaysAgo = DateTimeOffset.UtcNow.AddDays(-30);

            var tilesPosted = await db.AnalyticsEvents
                .CountAsync(e => e.EventType == AnalyticsEvents.TilePosted && e.CreatedAt >= thirtyDaysAgo, ct);

            var tilesOrbited = await db.AnalyticsEvents
                .CountAsync(e => e.EventType == AnalyticsEvents.TileOrbited && e.CreatedAt >= thirtyDaysAgo, ct);

            var seasonResponses = await db.AnalyticsEvents
                .CountAsync(e => e.EventType == AnalyticsEvents.SeasonResponseSubmitted && e.CreatedAt >= thirtyDaysAgo, ct);

            var weeklyPulses = await db.AnalyticsEvents
                .CountAsync(e => e.EventType == AnalyticsEvents.WeeklyPulseSubmitted && e.CreatedAt >= thirtyDaysAgo, ct);

            var dateFeedbacks = await db.AnalyticsEvents
                .CountAsync(e => e.EventType == AnalyticsEvents.DateFeedbackSubmitted && e.CreatedAt >= thirtyDaysAgo, ct);

            return Results.Ok(new
            {
                last30Days = new
                {
                    tilesPosted,
                    tilesOrbited,
                    seasonResponses,
                    weeklyPulses,
                    dateFeedbacks
                },
                asOf = DateTimeOffset.UtcNow
            });
        }).WithName("AdminAnalyticsContent");

        // GET /admin/analytics/ab/{experimentId}
        group.MapGet("/ab/{experimentId}", async (
            string experimentId,
            WovenDbContext db,
            CancellationToken ct) =>
        {
            var experiment = await db.AbExperiments.AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == experimentId, ct);

            if (experiment == null)
                return Results.NotFound(new { error = "EXPERIMENT_NOT_FOUND" });

            var assignmentCounts = await db.AbAssignments.AsNoTracking()
                .Where(a => a.ExperimentId == experimentId)
                .GroupBy(a => a.Variant)
                .Select(g => new { variant = g.Key, count = g.Count() })
                .ToListAsync(ct);

            var conversionCounts = await db.AbConversions.AsNoTracking()
                .Where(c => c.ExperimentId == experimentId)
                .GroupBy(c => new { c.ExperimentId, conversionType = c.ConversionType })
                .Select(g => new { g.Key.conversionType, count = g.Count() })
                .ToListAsync(ct);

            var totalAssigned = assignmentCounts.Sum(a => a.count);
            var totalConverted = conversionCounts.Sum(c => c.count);

            return Results.Ok(new
            {
                experimentId,
                isActive = experiment.IsActive,
                totalAssigned,
                totalConverted,
                conversionRate = totalAssigned > 0 ? Math.Round((double)totalConverted / totalAssigned, 4) : 0,
                variants = assignmentCounts,
                conversions = conversionCounts,
                asOf = DateTimeOffset.UtcNow
            });
        }).WithName("AdminAnalyticsAb");

        // GET /admin/analytics/retention
        group.MapGet("/retention", async (WovenDbContext db, CancellationToken ct) =>
        {
            var thirtyDaysAgo = DateTimeOffset.UtcNow.AddDays(-30);

            var dailyCounts = await db.AnalyticsEvents
                .Where(e => e.CreatedAt >= thirtyDaysAgo)
                .GroupBy(e => e.CreatedAt.Date)
                .Select(g => new { date = g.Key, eventCount = g.Count(), uniqueUsers = g.Select(e => e.UserIdHash).Where(h => h != null).Distinct().Count() })
                .OrderBy(x => x.date)
                .ToListAsync(ct);

            return Results.Ok(new
            {
                last30Days = dailyCounts.Select(d => new
                {
                    date = d.date.ToString("yyyy-MM-dd"),
                    d.eventCount,
                    d.uniqueUsers
                }),
                asOf = DateTimeOffset.UtcNow
            });
        }).WithName("AdminAnalyticsRetention");

        // GET /admin/analytics/scoring
        group.MapGet("/scoring", async (WovenDbContext db, CancellationToken ct) =>
        {
            var thirtyDaysAgo = DateTimeOffset.UtcNow.AddDays(-30);

            var weightLearningRuns = await db.AnalyticsEvents
                .CountAsync(e => e.EventType == AnalyticsEvents.WeightLearningRun && e.CreatedAt >= thirtyDaysAgo, ct);

            var opinionsSubmitted = await db.AnalyticsEvents
                .CountAsync(e => e.EventType == AnalyticsEvents.OpinionSubmitted && e.CreatedAt >= thirtyDaysAgo, ct);

            var insightsViewed = await db.AnalyticsEvents
                .CountAsync(e => e.EventType == AnalyticsEvents.InsightViewed && e.CreatedAt >= thirtyDaysAgo, ct);

            var gamesCompleted = await db.AnalyticsEvents
                .CountAsync(e => e.EventType == AnalyticsEvents.GameCompleted && e.CreatedAt >= thirtyDaysAgo, ct);

            var activeExperiments = await db.AbExperiments
                .CountAsync(e => e.IsActive, ct);

            return Results.Ok(new
            {
                last30Days = new
                {
                    weightLearningRuns,
                    opinionsSubmitted,
                    insightsViewed,
                    gamesCompleted
                },
                activeExperiments,
                asOf = DateTimeOffset.UtcNow
            });
        }).WithName("AdminAnalyticsScoring");
    }
}
