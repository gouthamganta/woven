using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WovenBackend.Data;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Services.Moments;
using MatchType = WovenBackend.data.Entities.Moments.MatchType;

namespace WovenBackend.Endpoints;

public static class MatchesEndpoints
{
    public record UnmatchRequest(int? Rating);

    public static void MapMatchesEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/matches");
        group.RequireAuthorization();

        // GET /matches -> active balloons for current user
        group.MapGet("", async (WovenDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var me = GetUserId(http.User);
            var now = MomentsRules.NowUtc();

            var matches = await db.Matches.AsNoTracking()
                .Where(m => m.BalloonState == BalloonState.ACTIVE && (m.UserAId == me || m.UserBId == me))
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => new
                {
                    matchId = m.Id,
                    matchType = m.MatchType.ToString(),
                    edgeOwnerId = m.EdgeOwnerId,
                    balloonState = m.BalloonState.ToString(),
                    createdAt = m.CreatedAt,
                    expiresAt = m.ExpiresAt,
                    bothMessagedAt = m.BothMessagedAt,
                    findLoveAt = m.FindLoveAt,
                    showFindLove = m.FindLoveAt != null && m.FindLoveAt <= now,
                    showBalloonTimer = m.BothMessagedAt != null && m.FindLoveAt != null && m.FindLoveAt > now,
                    reflectionSecondsLeft = (m.FindLoveAt != null && m.FindLoveAt > now)
                        ? (int)Math.Ceiling((m.FindLoveAt.Value - now).TotalSeconds)
                        : 0,
                    otherUserId = (m.UserAId == me ? m.UserBId : m.UserAId)
                })
                .ToListAsync(ct);

            var otherIds = matches.Select(x => x.otherUserId).Distinct().ToList();

            // Use first uploaded photo
            var others = await db.Users.AsNoTracking()
                .Where(u => otherIds.Contains(u.Id))
                .Select(u => new
                {
                    userId = u.Id,
                    fullName = u.FullName,
                    profilePhoto = db.UserPhotos
                        .Where(p => p.UserId == u.Id)
                        .OrderBy(p => p.SortOrder)
                        .Select(p => p.Url)
                        .FirstOrDefault()
                })
                .ToListAsync(ct);

            var map = others.ToDictionary(x => x.userId, x => x);

            var shaped = matches.Select(x => new
            {
                x.matchId,
                x.matchType,
                x.edgeOwnerId,
                x.balloonState,
                x.createdAt,
                x.expiresAt,
                x.bothMessagedAt,
                x.findLoveAt,
                x.showFindLove,
                x.showBalloonTimer,
                x.reflectionSecondsLeft,
                other = map.TryGetValue(x.otherUserId, out var u)
                    ? new { userId = u.userId, fullName = u.fullName, profilePhoto = u.profilePhoto }
                    : null
            });

            return Results.Ok(new { count = shaped.Count(), matches = shaped });
        })
        .WithName("MatchesList");

        // GET /matches/{matchId}/profile-access
        group.MapGet("/{matchId:guid}/profile-access", async (
            Guid matchId,
            WovenDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var me = GetUserId(http.User);
            var now = MomentsRules.NowUtc();

            var match = await db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == matchId, ct);
            if (match == null) return Results.NotFound(new { error = "MATCH_NOT_FOUND" });
            if (!(match.UserAId == me || match.UserBId == me)) return Results.Forbid();

            // PURE => FULL immediately
            if (match.MatchType == MatchType.PURE)
            {
                return Results.Ok(new
                {
                    matchId,
                    accessLevel = "FULL",
                    reason = "PURE_MATCH",
                    showBalloonTimer = match.BothMessagedAt != null && match.FindLoveAt != null && match.FindLoveAt > now,
                    reflectionSecondsLeft = (match.FindLoveAt != null && match.FindLoveAt > now)
                        ? (int)Math.Ceiling((match.FindLoveAt.Value - now).TotalSeconds)
                        : 0,
                    showFindLove = match.FindLoveAt != null && match.FindLoveAt <= now
                });
            }

            // EDGE: edge owner sees full immediately
            if (match.EdgeOwnerId == me)
            {
                return Results.Ok(new
                {
                    matchId,
                    accessLevel = "FULL",
                    reason = "EDGE_OWNER",
                    showBalloonTimer = match.BothMessagedAt != null && match.FindLoveAt != null && match.FindLoveAt > now,
                    reflectionSecondsLeft = (match.FindLoveAt != null && match.FindLoveAt > now)
                        ? (int)Math.Ceiling((match.FindLoveAt.Value - now).TotalSeconds)
                        : 0,
                    showFindLove = match.FindLoveAt != null && match.FindLoveAt <= now
                });
            }

            // ✅ EDGE non-owner unlocks only after 2-way communication
            var unlocked = match.BothMessagedAt != null;

            return Results.Ok(new
            {
                matchId,
                accessLevel = unlocked ? "FULL" : "LIMITED",
                reason = unlocked ? "TWO_WAY_MESSAGE" : "WAIT_TWO_WAY_MESSAGE",
                showBalloonTimer = match.BothMessagedAt != null && match.FindLoveAt != null && match.FindLoveAt > now,
                reflectionSecondsLeft = (match.FindLoveAt != null && match.FindLoveAt > now)
                    ? (int)Math.Ceiling((match.FindLoveAt.Value - now).TotalSeconds)
                    : 0,
                showFindLove = match.FindLoveAt != null && match.FindLoveAt <= now
            });
        })
        .WithName("MatchProfileAccess");

        // GET /matches/{matchId}/profile
        group.MapGet("/{matchId:guid}/profile", async (
            Guid matchId,
            WovenDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var me = GetUserId(http.User);
            var now = MomentsRules.NowUtc();

            var match = await db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == matchId, ct);
            if (match == null) return Results.NotFound(new { error = "MATCH_NOT_FOUND" });
            if (!(match.UserAId == me || match.UserBId == me)) return Results.Forbid();

            var otherUserId = (match.UserAId == me ? match.UserBId : match.UserAId);

            // --- Access rules ---
            string accessLevel;
            string reason;

            if (match.MatchType == MatchType.PURE)
            {
                accessLevel = "FULL";
                reason = "PURE_MATCH";
            }
            else if (match.EdgeOwnerId == me)
            {
                accessLevel = "FULL";
                reason = "EDGE_OWNER";
            }
            else
            {
                var unlocked = match.BothMessagedAt != null;
                accessLevel = unlocked ? "FULL" : "LIMITED";
                reason = unlocked ? "TWO_WAY_MESSAGE" : "WAIT_TWO_WAY_MESSAGE";
            }

            // --- Load other user core/profile/intent ---
            var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == otherUserId, ct);
            if (u == null) return Results.NotFound(new { error = "USER_NOT_FOUND" });

            var profile = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == otherUserId, ct);
            var intent = await db.UserIntents.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == otherUserId, ct);

            // openness parsing (same as review)
            string[] openness = Array.Empty<string>();
            if (intent != null && !string.IsNullOrWhiteSpace(intent.OpennessJson))
            {
                try { openness = JsonSerializer.Deserialize<string[]>(intent.OpennessJson) ?? Array.Empty<string>(); }
                catch { openness = Array.Empty<string>(); }
            }

            // optional fields
            var optional = await db.UserOptionalFields.AsNoTracking()
                .Where(x => x.UserId == otherUserId)
                .ToListAsync(ct);

            // ✅ Bio: show only when FULL (prevents leak in LIMITED)
            var bio = "";
            if (accessLevel == "FULL")
            {
                bio = optional.FirstOrDefault(x => x.Key == "bio")?.Value ?? "";
            }

            var detailOptional = optional
                .Where(x => x.Key != "bio" && !x.Key.StartsWith("pref_", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // ✅ OptionalPublic: FULL shows public fields, LIMITED shows none (fixed type mismatch)
            var optionalPublic = accessLevel == "FULL"
                ? detailOptional
                    .Where(x => x.Visibility == VisibilityLevel.Public)
                    .Select(x => (object)new { key = x.Key, value = x.Value })
                    .ToList()
                : new List<object>();

            // photos (LIMITED => 1 photo, FULL => all)
            var photosQuery = db.UserPhotos.AsNoTracking()
                .Where(x => x.UserId == otherUserId)
                .OrderBy(x => x.SortOrder)
                .Select(p => new { url = p.Url, caption = p.Caption, sortOrder = p.SortOrder });

            var photos = accessLevel == "FULL"
                ? await photosQuery.ToListAsync(ct)
                : await photosQuery.Take(1).ToListAsync(ct);

            // ✅ Clean location formatting
            string? loc = null;
            var city = (profile?.City ?? "").Trim();
            var state = (profile?.State ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(state)) loc = $"{city}, {state}";
            else if (!string.IsNullOrWhiteSpace(city)) loc = city;
            else if (!string.IsNullOrWhiteSpace(state)) loc = state;

            var publicPreview = new
            {
                name = u.FullName,
                age = profile?.Age,
                gender = profile?.Gender,
                location = loc,
                bio,

                intent = intent == null
                    ? null
                    : new { primaryIntent = intent.PrimaryIntent, openness },

                photos = photos.OrderBy(p => p.sortOrder).ToList(),

                optionalPublic
            };

            return Results.Ok(new
            {
                matchId,
                accessLevel,
                reason,

                showBalloonTimer = match.BothMessagedAt != null && match.FindLoveAt != null && match.FindLoveAt > now,
                reflectionSecondsLeft = (match.FindLoveAt != null && match.FindLoveAt > now)
                    ? (int)Math.Ceiling((match.FindLoveAt.Value - now).TotalSeconds)
                    : 0,
                showFindLove = match.FindLoveAt != null && match.FindLoveAt <= now,

                publicPreview
            });
        })
        .WithName("MatchProfileView");

        // POST /matches/{matchId}/pop - starts trial period instead of closing
        group.MapPost("/{matchId:guid}/pop", async (Guid matchId, WovenDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var me = GetUserId(http.User);

            var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == matchId, ct);
            if (match == null) return Results.NotFound(new { error = "MATCH_NOT_FOUND" });
            if (!IsParticipant(match, me)) return Results.Forbid();

            if (match.BalloonState != BalloonState.ACTIVE)
                return Results.BadRequest(new { error = "BALLOON_NOT_ACTIVE" });

            // Don't allow pop if already in trial or Find Love has started
            if (match.IsTrial || match.FindLoveAt != null)
                return Results.BadRequest(new { error = "CANNOT_POP_NOW" });

            var now = MomentsRules.NowUtc();

            // Start trial period (1 minute)
            match.IsTrial = true;
            match.TrialStartedAt = now;
            match.TrialEndsAt = now.AddMinutes(1);

            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                status = "TRIAL_STARTED",
                matchId = match.Id,
                trialEndsAt = match.TrialEndsAt
            });
        })
        .WithName("MatchPop");

        // POST /matches/{matchId}/unmatch - accepts optional rating
        group.MapPost("/{matchId:guid}/unmatch", async (Guid matchId, UnmatchRequest? req, WovenDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var me = GetUserId(http.User);

            var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == matchId, ct);
            if (match == null) return Results.NotFound(new { error = "MATCH_NOT_FOUND" });
            if (!IsParticipant(match, me)) return Results.Forbid();

            if (match.BalloonState != BalloonState.ACTIVE)
                return Results.BadRequest(new { error = "BALLOON_NOT_ACTIVE" });

            var now = MomentsRules.NowUtc();
            var candidateId = match.UserAId == me ? match.UserBId : match.UserAId;

            // Save rating if provided and valid
            if (req?.Rating != null && req.Rating >= -100 && req.Rating <= 100)
            {
                db.UserRatings.Add(new UserRating
                {
                    RatedUserId = candidateId,
                    RaterUserId = me,
                    MatchId = matchId,
                    RatingValue = req.Rating.Value,
                    CreatedAt = now
                });
            }

            match.BalloonState = BalloonState.CLOSED;
            match.ClosedReason = ClosedReason.UNMATCH;
            match.ClosedAt = now;

            await db.SaveChangesAsync(ct);

            // ✅ OUTCOME TRACKING: Record unmatch (non-blocking)
            try
            {
                var outcomeService = http.RequestServices
                    .GetRequiredService<WovenBackend.Services.Matchmaking.IMatchOutcomeService>();

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await outcomeService.RecordUnmatchAsync(match.Id, me, candidateId, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        var logger = http.RequestServices.GetRequiredService<ILogger<Program>>();
                        logger.LogError(ex, "[Match] Failed to record unmatch for match {MatchId}", match.Id);
                    }
                });
            }
            catch
            {
                // Silent fail - outcome tracking is non-critical
            }

            return Results.Ok(new { status = "UNMATCHED", matchId = match.Id, closedAt = match.ClosedAt });
        })
        .WithName("MatchUnmatch");

        // POST /matches/{matchId}/block
        group.MapPost("/{matchId:guid}/block", async (Guid matchId, WovenDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var me = GetUserId(http.User);

            var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == matchId, ct);
            if (match == null) return Results.NotFound(new { error = "MATCH_NOT_FOUND" });
            if (!IsParticipant(match, me)) return Results.Forbid();

            var otherId = match.UserAId == me ? match.UserBId : match.UserAId;
            var now = MomentsRules.NowUtc();

            var alreadyBlocked = await db.Blocks.AnyAsync(b => b.BlockerId == me && b.BlockedId == otherId, ct);
            if (!alreadyBlocked)
            {
                db.Blocks.Add(new Block
                {
                    BlockerId = me,
                    BlockedId = otherId,
                    CreatedAt = now
                });
            }

            if (match.BalloonState == BalloonState.ACTIVE)
            {
                match.BalloonState = BalloonState.CLOSED;
                match.ClosedReason = ClosedReason.BLOCK;
                match.ClosedAt = now;
            }

            await db.SaveChangesAsync(ct);

            // ✅ OUTCOME TRACKING: Record block (non-blocking)
            try
            {
                var outcomeService = http.RequestServices
                    .GetRequiredService<WovenBackend.Services.Matchmaking.IMatchOutcomeService>();

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await outcomeService.RecordBlockAsync(me, otherId, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        var logger = http.RequestServices.GetRequiredService<ILogger<Program>>();
                        logger.LogError(ex, "[Match] Failed to record block for user {UserId}", me);
                    }
                });
            }
            catch
            {
                // Silent fail - outcome tracking is non-critical
            }

            return Results.Ok(new
            {
                status = "BLOCKED",
                matchId = match.Id,
                blockedUserId = otherId,
                closedAt = match.ClosedAt
            });
        })
        .WithName("MatchBlock");
    }

    private static bool IsParticipant(Match m, int userId) => m.UserAId == userId || m.UserBId == userId;

    private static int GetUserId(ClaimsPrincipal user)
    {
        var uid = user.FindFirstValue("uid");
        if (int.TryParse(uid, out var id)) return id;

        var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(sub, out id)) return id;

        throw new UnauthorizedAccessException("Missing user id claim");
    }
}