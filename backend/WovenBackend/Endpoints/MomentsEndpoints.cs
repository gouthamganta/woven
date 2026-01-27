using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Services.Moments;

namespace WovenBackend.Endpoints;

public static class MomentsEndpoints
{
    public record RespondRequest(int TargetUserId, string Choice, string? Source);

    public static void MapMomentsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/moments");
        group.RequireAuthorization();

        // GET /moments
        // ✅ Replaced simple candidate selection with orchestrated daily deck + explanations
        group.MapGet("", async (
            WovenDbContext db,
            WovenBackend.Services.Matchmaking.IDailyDeckOrchestrator deckOrchestrator,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);
            var today = MomentsRules.UtcToday();

            // Fetch budget row
            var budgetRow = await db.DailyInteractions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.UserId == userId && x.DateUtc == today, ct);

            var totalUsed = budgetRow?.TotalUsed ?? 0;
            var pendingUsed = budgetRow?.PendingUsed ?? 0;

            var budget = new
            {
                totalCap = MomentsRules.DailyTotalCap,
                totalUsed,
                totalRemaining = Math.Max(0, MomentsRules.DailyTotalCap - totalUsed),
                pendingCap = MomentsRules.DailyPendingCap,
                pendingUsed,
                pendingRemaining = Math.Max(0, MomentsRules.DailyPendingCap - pendingUsed)
            };

            // ✅ NEW: Get daily deck from orchestrator
            var deckResult = await deckOrchestrator.GetOrCreateDeckAsync(userId, today, ct);

            if (deckResult.Items.Count == 0)
            {
                return Results.Ok(new
                {
                    dateUtc = today.ToString("yyyy-MM-dd"),
                    theme = ThemeOfTheDay(today),
                    budget,
                    count = 0,
                    cards = Array.Empty<object>()
                });
            }

            // ✅ FIXED: Get list of users this user has already responded to TODAY
            var respondedToToday = await db.MomentResponses.AsNoTracking()
                .Where(r => r.DateUtc == today && r.FromUserId == userId)
                .Select(r => r.ToUserId)
                .ToListAsync(ct);

            var respondedSet = new HashSet<int>(respondedToToday);

            // ✅ FIXED: Filter out candidates that have already been responded to
            var filteredItems = deckResult.Items
                .Where(i => !respondedSet.Contains(i.CandidateId))
                .ToList();

            if (filteredItems.Count == 0)
            {
                return Results.Ok(new
                {
                    dateUtc = today.ToString("yyyy-MM-dd"),
                    theme = ThemeOfTheDay(today),
                    budget,
                    count = 0,
                    cards = Array.Empty<object>()
                });
            }

            // Load candidate details (only for filtered items)
            var candidateIds = filteredItems.Select(i => i.CandidateId).ToList();

            var candidates = await db.Users.AsNoTracking()
                .Where(u => candidateIds.Contains(u.Id))
                .Select(u => new
                {
                    userId = u.Id,
                    fullName = u.FullName,
                    age = db.UserProfiles.Where(p => p.UserId == u.Id).Select(p => (int?)p.Age).FirstOrDefault(),
                    gender = db.UserProfiles.Where(p => p.UserId == u.Id).Select(p => p.Gender).FirstOrDefault(),
                    location = db.UserProfiles.Where(p => p.UserId == u.Id)
                        .Select(p => new { city = p.City, state = p.State })
                        .FirstOrDefault(),
                    profilePhoto = db.UserPhotos
                        .Where(p => p.UserId == u.Id)
                        .OrderBy(p => p.SortOrder)
                        .Select(p => p.Url)
                        .FirstOrDefault()
                })
                .ToListAsync(ct);

            // Load ratings aggregation for candidates
            var ratings = await db.UserRatings
                .Where(r => candidateIds.Contains(r.RatedUserId))
                .GroupBy(r => r.RatedUserId)
                .Select(g => new
                {
                    userId = g.Key,
                    average = g.Average(r => r.RatingValue),
                    count = g.Count()
                })
                .ToListAsync(ct);

            var ratingMap = ratings.ToDictionary(r => r.userId, r => r);

            // Load explanations (only for filtered items)
            var explanationIds = filteredItems.Select(i => i.ExplanationId).ToList();
            var explanations = await db.MatchExplanations.AsNoTracking()
                .Where(e => explanationIds.Contains(e.Id))
                .ToListAsync(ct);

            var explanationMap = explanations.ToDictionary(e => e.Id, e => e);
            var candidateMap = candidates.ToDictionary(c => c.userId, c => c);

            // Build cards with explanations (using filtered items)
            var cards = filteredItems
                .Where(i => candidateMap.ContainsKey(i.CandidateId))
                .Select(i =>
                {
                    var candidate = candidateMap[i.CandidateId];
                    var explanation = explanationMap.GetValueOrDefault(i.ExplanationId);

                    var bullets = new List<string>();
                    if (explanation != null && !string.IsNullOrWhiteSpace(explanation.BulletsJson))
                    {
                        try
                        {
                            bullets = System.Text.Json.JsonSerializer.Deserialize<List<string>>(explanation.BulletsJson)
                                ?? new List<string>();
                        }
                        catch { }
                    }

                    // Include rating only if count >= 5
                    object? rating = null;
                    if (ratingMap.TryGetValue(candidate.userId, out var r) && r.count >= 5)
                    {
                        rating = new { average = (int)Math.Round(r.average), count = r.count, show = true };
                    }

                    return new
                    {
                        candidate.userId,
                        candidate.fullName,
                        candidate.age,
                        candidate.gender,
                        candidate.location,
                        candidate.profilePhoto,
                        score = i.Score,
                        bucket = i.Bucket,
                        reason = explanation == null ? null : new
                        {
                            headline = explanation.Headline,
                            bullets,
                            tone = explanation.Tone
                        },
                        rating
                    };
                })
                .ToList();

            return Results.Ok(new
            {
                dateUtc = today.ToString("yyyy-MM-dd"),
                theme = ThemeOfTheDay(today),
                budget,
                count = cards.Count,
                cards
            });
        });

        // GET /moments/pending
        group.MapGet("/pending", async (
            WovenDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var me = GetUserId(http.User);

            // Block list (both directions)
            var blockedIds = await db.Blocks
                .Where(b => b.BlockerId == me)
                .Select(b => b.BlockedId)
                .Union(db.Blocks.Where(b => b.BlockedId == me).Select(b => b.BlockerId))
                .ToListAsync(ct);

            // People already in an ACTIVE balloon with me
            var activePartnerIds = await db.Matches.AsNoTracking()
                .Where(m => m.BalloonState == BalloonState.ACTIVE && (m.UserAId == me || m.UserBId == me))
                .Select(m => m.UserAId == me ? m.UserBId : m.UserAId)
                .ToListAsync(ct);

            var pending = await db.PendingMatches.AsNoTracking()
                .Where(p => p.UserId == me)
                .Where(p => !blockedIds.Contains(p.TargetUserId))
                .Where(p => !activePartnerIds.Contains(p.TargetUserId))
                .OrderByDescending(p => p.CreatedAt)
                .Take(50)
                .ToListAsync(ct);

            if (pending.Count == 0)
                return Results.Ok(new { count = 0, cards = Array.Empty<object>() });

            var targetIds = pending.Select(p => p.TargetUserId).Distinct().ToList();

            var users = await db.Users.AsNoTracking()
                .Where(u => targetIds.Contains(u.Id))
                .Select(u => new
                {
                    userId = u.Id,
                    fullName = u.FullName,
                    age = db.UserProfiles.Where(p => p.UserId == u.Id).Select(p => (int?)p.Age).FirstOrDefault(),
                    location = db.UserProfiles.Where(p => p.UserId == u.Id)
                        .Select(p => new { city = p.City, state = p.State })
                        .FirstOrDefault(),
                    profilePhoto = db.UserPhotos
                        .Where(p => p.UserId == u.Id)
                        .OrderBy(p => p.SortOrder)
                        .Select(p => p.Url)
                        .FirstOrDefault()
                })
                .ToListAsync(ct);

            var map = users.ToDictionary(x => x.userId, x => x);

            var cards = pending
                .Select(p => map.TryGetValue(p.TargetUserId, out var u)
                    ? new { u.userId, u.fullName, u.age, u.location, u.profilePhoto, savedAt = p.CreatedAt }
                    : null)
                .Where(x => x != null);

            return Results.Ok(new { count = cards.Count(), cards });
        });

        // POST /moments/respond
        group.MapPost("/respond", async (
            RespondRequest req,
            WovenDbContext db,
            InteractionBudgetService budget,
            MomentsMatchService matchService,
            HttpContext http,
            CancellationToken ct) =>
        {
            var me = GetUserId(http.User);
            var today = MomentsRules.UtcToday();

            if (req.TargetUserId <= 0 || req.TargetUserId == me)
                return Results.BadRequest(new { error = "INVALID_TARGET" });

            var targetExists = await db.Users.AnyAsync(u => u.Id == req.TargetUserId, ct);
            if (!targetExists) return Results.BadRequest(new { error = "TARGET_NOT_FOUND" });

            var blocked = await db.Blocks.AnyAsync(b =>
                (b.BlockerId == me && b.BlockedId == req.TargetUserId) ||
                (b.BlockerId == req.TargetUserId && b.BlockedId == me), ct);

            if (blocked) return Results.BadRequest(new { error = "BLOCKED" });

            var choice = (req.Choice ?? "").Trim().ToUpperInvariant();
            var source = (req.Source ?? "").Trim().ToUpperInvariant();

            var isFromPending = source == "PENDING";
            var isPendingChoice = choice == "PENDING";

            // ✅ If already responded today, allow update when coming from Pending
            MomentResponse? existingToday = null;

            if (!isPendingChoice)
            {
                existingToday = await db.MomentResponses
                    .FirstOrDefaultAsync(r =>
                        r.DateUtc == today &&
                        r.FromUserId == me &&
                        r.ToUserId == req.TargetUserId, ct);

                if (existingToday != null && !isFromPending)
                    return Results.Conflict(new { error = "ALREADY_RESPONDED_TODAY" });
            }

            // If already in pending, don't spend again
            if (isPendingChoice)
            {
                var alreadyPending = await db.PendingMatches.AsNoTracking().AnyAsync(p =>
                    p.UserId == me && p.TargetUserId == req.TargetUserId, ct);

                if (alreadyPending)
                    return Results.Ok(new { status = "PENDING_ALREADY_SAVED" });
            }

            // ✅ Budget fix: If responding from pending, check if pending row exists
            // If it does, don't spend again (just remove the pending row)
            PendingMatch? existingPending = null;
            if (isFromPending && !isPendingChoice)
            {
                existingPending = await db.PendingMatches
                    .FirstOrDefaultAsync(p => p.UserId == me && p.TargetUserId == req.TargetUserId, ct);
            }

            // Only spend budget if NOT converting from existing pending
            InteractionBudgetService.SpendResult spend;
            if (existingPending != null)
            {
                var budgetRow = await db.DailyInteractions.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.UserId == me && x.DateUtc == today, ct);
                
                spend = new InteractionBudgetService.SpendResult(
                    Allowed: true,
                    DenyReason: null,
                    TotalUsed: budgetRow?.TotalUsed ?? 0,
                    PendingUsed: budgetRow?.PendingUsed ?? 0
                );
            }
            else
            {
                var spendType = (isPendingChoice || isFromPending)
                    ? InteractionBudgetService.SpendType.Pending
                    : InteractionBudgetService.SpendType.Moment;
                
                spend = await budget.TrySpendAsync(me, spendType, ct);
            }

            if (!spend.Allowed)
                return Results.BadRequest(new { error = spend.DenyReason, spend.TotalUsed, spend.PendingUsed });

            var choiceEnum = choice switch
            {
                "YES" => MomentChoice.YES,
                "NO" => MomentChoice.NO,
                "PENDING" => MomentChoice.PENDING,
                _ => (MomentChoice?)null
            };

            if (choiceEnum is null)
                return Results.BadRequest(new { error = "INVALID_CHOICE" });

            var (a, b) = MomentsRules.NormalizePair(me, req.TargetUserId);

            // ✅ PENDING does NOT create MomentResponse
            if (choiceEnum == MomentChoice.PENDING)
            {
                try
                {
                    db.PendingMatches.Add(new PendingMatch
                    {
                        UserId = me,
                        TargetUserId = req.TargetUserId,
                        CreatedAt = MomentsRules.NowUtc()
                    });
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    // ignore duplicate pending
                }

                return Results.Ok(new
                {
                    status = "PENDING_SAVED",
                    spend.TotalUsed,
                    spend.PendingUsed
                });
            }

            // ✅ YES/NO: record response (unique per day)
            // If from Pending and response already exists today -> update it (idempotent)
            if (existingToday == null)
            {
                db.MomentResponses.Add(new MomentResponse
                {
                    DateUtc = today,
                    FromUserId = me,
                    ToUserId = req.TargetUserId,
                    Choice = choiceEnum.Value,
                    CreatedAt = MomentsRules.NowUtc()
                });
            }
            else
            {
                existingToday.Choice = choiceEnum.Value;
            }

            await db.SaveChangesAsync(ct);

            // If YES/NO came from Pending, remove pending row (already loaded above)
            if (existingPending != null)
            {
                db.PendingMatches.Remove(existingPending);
                await db.SaveChangesAsync(ct);
            }

            // ✅ Find counterpart response
            // - Normal Moments deck: must be today
            // - Pending conversion: allow latest YES/NO across any day
            MomentResponse? other;
            if (isFromPending)
            {
                other = await db.MomentResponses.AsNoTracking()
                    .Where(r =>
                        r.FromUserId == req.TargetUserId &&
                        r.ToUserId == me &&
                        r.Choice != MomentChoice.PENDING)
                    .OrderByDescending(r => r.CreatedAt)
                    .FirstOrDefaultAsync(ct);
            }
            else
            {
                other = await db.MomentResponses.AsNoTracking()
                    .FirstOrDefaultAsync(r =>
                        r.DateUtc == today &&
                        r.FromUserId == req.TargetUserId &&
                        r.ToUserId == me, ct);
            }

            if (other is null)
            {
                return Results.Ok(new
                {
                    status = "RECORDED_WAITING",
                    spend.TotalUsed,
                    spend.PendingUsed
                });
            }

            if (other.Choice == MomentChoice.PENDING)
            {
                return Results.Ok(new
                {
                    status = "OTHER_PENDING",
                    spend.TotalUsed,
                    spend.PendingUsed
                });
            }

            // ✅ PURE if BOTH chose same (YES/YES OR NO/NO)
            if (choiceEnum.Value == other.Choice)
            {
                var created = await matchService.CreateActiveMatchAsync(
                    a, b,
                    WovenBackend.data.Entities.Moments.MatchType.PURE,
                    edgeOwnerId: null,
                    ct);

                // ✅ Cleanup pending rows both directions once a match is created via Pending
                if (isFromPending && created.Created)
                {
                    var pendings = await db.PendingMatches
                        .Where(p =>
                            (p.UserId == me && p.TargetUserId == req.TargetUserId) ||
                            (p.UserId == req.TargetUserId && p.TargetUserId == me))
                        .ToListAsync(ct);

                    if (pendings.Count > 0)
                    {
                        db.PendingMatches.RemoveRange(pendings);
                        await db.SaveChangesAsync(ct);
                    }
                }

                return Results.Ok(new
                {
                    status = created.Created ? "PURE_MATCH_CREATED" : "MATCH_NOT_CREATED",
                    reason = created.Reason,
                    matchId = created.Match?.Id,
                    spend.TotalUsed,
                    spend.PendingUsed
                });
            }

            // ✅ EDGE otherwise
            var edgeOwner = Random.Shared.Next(0, 2) == 0 ? a : b;

            var edgeCreated = await matchService.CreateActiveMatchAsync(
                a, b,
                WovenBackend.data.Entities.Moments.MatchType.EDGE,
                edgeOwnerId: edgeOwner,
                ct);

            // ✅ Cleanup pending rows both directions once a match is created via Pending
            if (isFromPending && edgeCreated.Created)
            {
                var pendings = await db.PendingMatches
                    .Where(p =>
                        (p.UserId == me && p.TargetUserId == req.TargetUserId) ||
                        (p.UserId == req.TargetUserId && p.TargetUserId == me))
                    .ToListAsync(ct);

                if (pendings.Count > 0)
                {
                    db.PendingMatches.RemoveRange(pendings);
                    await db.SaveChangesAsync(ct);
                }
            }

            return Results.Ok(new
            {
                status = edgeCreated.Created ? "EDGE_MATCH_CREATED" : "MATCH_NOT_CREATED",
                reason = edgeCreated.Reason,
                matchId = edgeCreated.Match?.Id,
                edgeOwnerId = edgeCreated.Match?.EdgeOwnerId,
                spend.TotalUsed,
                spend.PendingUsed
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

    private static object ThemeOfTheDay(DateOnly day)
    {
        // Single consistent theme: Brunch vs Dinner
        return new
        {
            id = "BRUNCH_DINNER",
            question = "If we grabbed a meal together...",
            left = new { label = "Brunch", emoji = "☕", choice = "NO" },
            mid = new { label = "Hold", emoji = "⏳", choice = "PENDING" },
            right = new { label = "Dinner", emoji = "🍽️", choice = "YES" }
        };
    }
}