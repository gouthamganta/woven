using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Services.Matchmaking;

namespace WovenBackend.Endpoints;

public static class DevMatchmakingSmokeEndpoints
{
    public static IEndpointRouteBuilder MapDevMatchmakingSmokeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/dev/smoke")
            .WithTags("Dev Smoke");

        // POST /dev/smoke/matchmaking?count=5&seedPulse=true
        group.MapPost("/matchmaking", async (
            int? count,
            bool? seedPulse,
            WovenDbContext db,
            IUserVectorBuilder vectorBuilder,
            IDailyDeckOrchestrator deckOrchestrator,
            ICandidatePoolService pool,
            IMatchScoringService scoring,
            IDeliveryBoostService boost,
            IDeckSelectionService selection,
            CancellationToken ct) =>
        {
            // --- choose test users (must have profile + preferences)
            var take = Math.Clamp(count ?? 5, 2, 25);
            var users = await db.UserProfiles.AsNoTracking()
                .Join(db.UserPreferences.AsNoTracking(),
                    p => p.UserId,
                    pref => pref.UserId,
                    (p, pref) => new { p.UserId })
                .Select(x => x.UserId)
                .Distinct()
                .OrderBy(x => x)
                .Take(take)
                .ToListAsync(ct);

            if (users.Count < 2)
                return Results.BadRequest(new
                {
                    error = "NOT_ENOUGH_USERS",
                    message = "Need at least 2 users with UserProfile + UserPreference to run smoke test."
                });

            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var report = new
            {
                dateUtc = today,
                usersTested = users.Count,
                results = new List<object>()
            };

            var resultsList = new List<object>();

            foreach (var userId in users)
            {
                // --- ensure v1 exists (build if missing)
                var hasV1 = await db.UserVectors.AsNoTracking()
                    .AnyAsync(v => v.UserId == userId && v.Version == 1, ct);

                int? v1Id = null;
                if (!hasV1)
                {
                    v1Id = await vectorBuilder.BuildAndSaveV1Async(userId, ct);
                }

                // --- optionally seed pulse so PulseScore is not neutral
                if (seedPulse is true)
                {
                    // These are your canonical raw answers; UpdatePulseAsync maps to numeric pulse features.
                    await vectorBuilder.UpdatePulseAsync(userId, new Dictionary<string, string>
                    {
                        ["d1_battery"] = "high",
                        ["d2_tone"] = "calm",
                        ["d3_role"] = "copilot"
                    }, ct);
                }

                // --- run the same pipeline pieces you use in production
                var candidateIds = await pool.GetEligibleCandidatesAsync(userId, ct);

                List<object> topCandidatesDebug = new();
                int deckCount = 0;
                int exposuresToday = 0;

                if (candidateIds.Count > 0)
                {
                    var scores = await scoring.ScoreCandidatesAsync(userId, candidateIds, ct);
                    var boostMap = await boost.GetBoostMapAsync(userId, candidateIds, today, ct);

                    // Debug: show a few top rows AFTER boost
                    var top = scores
                        .OrderByDescending(s => s.TotalScore + (boostMap.TryGetValue(s.CandidateId, out var b) ? b : 0))
                        .Take(8)
                        .Select(s => new
                        {
                            candidateId = s.CandidateId,
                            total = s.TotalScore,
                            boost = boostMap.TryGetValue(s.CandidateId, out var b) ? b : 0,
                            adjusted = s.TotalScore + (boostMap.TryGetValue(s.CandidateId, out var b2) ? b2 : 0),
                            intent = s.IntentScore,
                            foundational = s.FoundationalScore,
                            lifestyle = s.LifestyleScore,
                            pulse = s.PulseScore
                        })
                        .ToList<object>();

                    topCandidatesDebug = top;

                    // This calls your DailyDeckOrchestrator, which also logs CandidateExposures (you already added that)
                    var deck = await deckOrchestrator.GetOrCreateDeckAsync(userId, today, ct);
                    deckCount = deck.Items.Count;

                    exposuresToday = await db.CandidateExposures.AsNoTracking()
                        .CountAsync(e => e.ViewerUserId == userId && e.DateUtc == today, ct);
                }

                resultsList.Add(new
                {
                    userId,
                    builtV1 = !hasV1,
                    v1Id,
                    seededPulse = seedPulse is true,
                    candidatePoolCount = candidateIds.Count,
                    deckCount,
                    exposuresToday,
                    topCandidates = topCandidatesDebug
                });
            }

            return Results.Ok(new
            {
                dateUtc = today,
                usersTested = users.Count,
                results = resultsList
            });
        });

        return app;
    }
}