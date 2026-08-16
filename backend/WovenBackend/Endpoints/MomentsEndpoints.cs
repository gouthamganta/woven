using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Data.Entities;
using WovenBackend.Services;
using WovenBackend.Services.Analytics;
using WovenBackend.Services.Moments;

namespace WovenBackend.Endpoints;

public static class MomentsEndpoints
{
    public record RespondRequest(int TargetUserId, string Choice, string? Source, int? TimeOnCardMs);
    public record ChooseRequest(int TargetUserId, string Choice, string NoteText, string? Source, int? TimeOnCardMs);

    // Positive choices that can create a match
    private static bool IsPositive(MomentChoice c) =>
        c is MomentChoice.MAGICAL or MomentChoice.LOGICAL or MomentChoice.YES;

    // Pure = both same positive type; Edge = different positive types
    private static bool IsPure(MomentChoice a, MomentChoice b)
    {
        var aMag = a is MomentChoice.MAGICAL or MomentChoice.YES;
        var bMag = b is MomentChoice.MAGICAL or MomentChoice.YES;
        var aLog = a is MomentChoice.LOGICAL or MomentChoice.NO;
        var bLog = b is MomentChoice.LOGICAL or MomentChoice.NO;
        return (aMag && bMag) || (aLog && bLog);
    }

    public static void MapMomentsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/moments");
        group.RequireAuthorization();

        // ── GET /moments ─────────────────────────────────────────────────────────
        group.MapGet("", async (
            WovenDbContext db,
            WovenBackend.Services.Matchmaking.IDailyDeckOrchestrator deckOrchestrator,
            SparkWalletService sparks,
            WovenBackend.Services.Embeddings.IVisualPreferenceService visualPreference,
            IAnalyticsService analytics,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);
            var today = MomentsRules.UtcToday();

            var budgetRow = await db.DailyInteractions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.UserId == userId && x.DateUtc == today, ct);

            var budget = new
            {
                totalCap = MomentsRules.DailyTotalCap,
                totalUsed = budgetRow?.TotalUsed ?? 0,
                totalRemaining = Math.Max(0, MomentsRules.DailyTotalCap - (budgetRow?.TotalUsed ?? 0)),
            };

            var sparkBalance = await sparks.GetBalanceAsync(userId, ct);

            var deckResult = await deckOrchestrator.GetOrCreateDeckAsync(userId, today, ct);

            if (deckResult.Items.Count == 0)
                return Results.Ok(new { dateUtc = today.ToString("yyyy-MM-dd"), budget, sparkBalance, count = 0, cards = Array.Empty<object>() });

            // Filter out candidates already responded to today
            var respondedToday = await db.MomentResponses.AsNoTracking()
                .Where(r => r.DateUtc == today && r.FromUserId == userId)
                .Select(r => r.ToUserId)
                .ToListAsync(ct);

            var respondedSet = new HashSet<int>(respondedToday);
            var filteredItems = deckResult.Items.Where(i => !respondedSet.Contains(i.CandidateId)).ToList();

            if (filteredItems.Count == 0)
                return Results.Ok(new { dateUtc = today.ToString("yyyy-MM-dd"), budget, sparkBalance, count = 0, cards = Array.Empty<object>() });

            var candidateIds = filteredItems.Select(i => i.CandidateId).ToList();

            // All photos per candidate (for gallery + per-viewer best photo selection)
            var allPhotosTask = db.UserPhotos.AsNoTracking()
                .Where(p => candidateIds.Contains(p.UserId))
                .OrderBy(p => p.UserId).ThenBy(p => p.SortOrder)
                .Select(p => new { p.UserId, p.Url })
                .ToListAsync(ct);

            // Active tiles (live in Commons, not expired, moderated/visible)
            var activeTilesTask = db.Tiles.AsNoTracking()
                .Where(t => candidateIds.Contains(t.UserId) && !t.IsExpired && t.IsModerated)
                .OrderBy(t => t.UserId).ThenByDescending(t => t.CreatedAt)
                .Select(t => new
                {
                    t.UserId,
                    tileId      = t.Id,
                    contentType = t.ContentType,
                    contentText = t.ContentText,
                    mediaUrl    = t.MediaUrl,
                    isActive    = true
                })
                .ToListAsync(ct);

            // Highlighted tiles per candidate (slot order) — fallback when active < 3
            var highlightedTilesTask = db.Highlights.AsNoTracking()
                .Where(h => candidateIds.Contains(h.UserId))
                .OrderBy(h => h.UserId).ThenBy(h => h.SlotNumber)
                .Select(h => new
                {
                    h.UserId,
                    tileId      = h.TileId,
                    contentType = h.Tile.ContentType,
                    contentText = h.Tile.ContentText,
                    mediaUrl    = h.Tile.MediaUrl,
                    isActive    = false
                })
                .ToListAsync(ct);

            // Check which candidates have already chosen the viewer
            var theyChoseMe = await db.MomentResponses.AsNoTracking()
                .Where(r => candidateIds.Contains(r.FromUserId) && r.ToUserId == userId && IsPositiveExpression(r.Choice))
                .Select(r => r.FromUserId)
                .ToListAsync(ct);
            var theyChoseMeSet = new HashSet<int>(theyChoseMe);

            var candidates = await db.Users.AsNoTracking()
                .Where(u => candidateIds.Contains(u.Id))
                .Select(u => new
                {
                    userId = u.Id,
                    fullName = u.FullName,
                    isVerified = u.IsVerified,
                    gender = db.UserProfiles.Where(p => p.UserId == u.Id).Select(p => p.Gender).FirstOrDefault(),
                    displayPronouns = db.UserProfiles.Where(p => p.UserId == u.Id).Select(p => p.DisplayPronouns).FirstOrDefault(),
                    location = db.UserProfiles.Where(p => p.UserId == u.Id)
                        .Select(p => new { city = p.City, state = p.State }).FirstOrDefault(),
                    profilePhoto = db.UserPhotos
                        .Where(p => p.UserId == u.Id)
                        .OrderBy(p => p.SortOrder)
                        .Select(p => p.Url)
                        .FirstOrDefault()
                })
                .ToListAsync(ct);

            var ratings = await db.UserRatings
                .Where(r => candidateIds.Contains(r.RatedUserId))
                .GroupBy(r => r.RatedUserId)
                .Select(g => new { userId = g.Key, average = g.Average(r => r.RatingValue), count = g.Count() })
                .ToListAsync(ct);
            var ratingMap = ratings.ToDictionary(r => r.userId, r => r);

            var explanationIds = filteredItems.Select(i => i.ExplanationId).ToList();
            var explanations = await db.MatchExplanations.AsNoTracking()
                .Where(e => explanationIds.Contains(e.Id)).ToListAsync(ct);
            var explanationMap = explanations.ToDictionary(e => e.Id, e => e);
            var candidateMap = candidates.ToDictionary(c => c.userId, c => c);

            // Photos + tiles (all tasks started earlier)
            await Task.WhenAll(allPhotosTask, activeTilesTask, highlightedTilesTask);
            var allPhotosByUser = (await allPhotosTask)
                .GroupBy(p => p.UserId)
                .ToDictionary(g => g.Key, g => g.Select(p => p.Url).ToList());

            // Merge: active tiles first (newest first), fill remaining slots from highlights
            var activeByUser = (await activeTilesTask)
                .GroupBy(t => t.UserId)
                .ToDictionary(g => g.Key, g => g.ToList());
            var highlightByUser = (await highlightedTilesTask)
                .GroupBy(h => h.UserId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var tilesByUser = candidateIds.ToDictionary(
                cid => cid,
                cid =>
                {
                    var active    = activeByUser.GetValueOrDefault(cid) ?? new();
                    var highlight = highlightByUser.GetValueOrDefault(cid) ?? new();
                    var merged    = active.Take(3).ToList();
                    if (merged.Count < 3)
                        merged.AddRange(highlight.Take(3 - merged.Count));
                    return merged.Select(t => new
                    {
                        id          = t.tileId,
                        contentType = t.contentType,
                        contentText = t.contentText,
                        mediaUrl    = t.mediaUrl,
                        isActive    = t.isActive
                    }).ToList<object>();
                });

            // Per-viewer photo selection — best CLIP match, fallback to sortOrder 0
            var bestPhotoTasks = candidateIds.Select(async cid =>
                (cid, url: await visualPreference.GetBestPhotoUrlAsync(userId, cid, ct)));
            var bestPhotoResults = await Task.WhenAll(bestPhotoTasks);
            var bestPhotoMap = bestPhotoResults.ToDictionary(x => x.cid, x => x.url);

            var cards = filteredItems
                .Where(i => candidateMap.ContainsKey(i.CandidateId))
                .Select(i =>
                {
                    var candidate = candidateMap[i.CandidateId];
                    var explanation = explanationMap.GetValueOrDefault(i.ExplanationId);
                    var bullets = new List<string>();
                    if (explanation != null && !string.IsNullOrWhiteSpace(explanation.BulletsJson))
                    {
                        try { bullets = System.Text.Json.JsonSerializer.Deserialize<List<string>>(explanation.BulletsJson) ?? new List<string>(); }
                        catch { }
                    }

                    object? rating = null;
                    if (ratingMap.TryGetValue(candidate.userId, out var r) && r.count >= 5)
                        rating = new { average = (int)Math.Round(r.average), count = r.count, show = true };

                    return new
                    {
                        candidate.userId,
                        candidate.fullName,
                        candidate.isVerified,
                        candidate.displayPronouns,
                        candidate.gender,
                        candidate.location,
                        profilePhoto = bestPhotoMap.GetValueOrDefault(candidate.userId) ?? candidate.profilePhoto,
                        score = i.Score,
                        bucket = i.Bucket,
                        alreadyChoseYou = theyChoseMeSet.Contains(candidate.userId),
                        // All photos for gallery (long-press)
                        photos = allPhotosByUser.GetValueOrDefault(candidate.userId),
                        // Tiles strip — active first, highlights fill remaining slots
                        highlightedTiles = tilesByUser.GetValueOrDefault(candidate.userId),
                        // Cinematic intro (null until Build N+1 populates them)
                        kenBurnsPhotoUrls = i.KenBurnsPhotoUrls,
                        curatedQuote = i.CuratedQuote,
                        narrationUrl = i.NarrationUrl,
                        narrationExposed = i.NarrationExposed,
                        reason = explanation == null ? null : new
                        {
                            headline = explanation.Headline,
                            bullets,
                            tone = explanation.Tone,
                            bridgeQuestion = explanation.BridgeQuestion
                        },
                        rating
                    };
                })
                .ToList();

            _ = analytics.TrackAsync(userId, null, AnalyticsEvents.MomentsDeckViewed,
                new { deckSize = cards.Count, hasStrongMatch = filteredItems.Any(i => i.Score >= 80) });

            return Results.Ok(new { dateUtc = today.ToString("yyyy-MM-dd"), budget, sparkBalance, moodLine = deckResult.MoodLine, count = cards.Count, cards });
        });

        // ── GET /moments/liked-you ────────────────────────────────────────────────
        group.MapGet("/liked-you", async (
            WovenDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var me = GetUserId(http.User);
            var today = MomentsRules.UtcToday();
            var cutoff = DateTimeOffset.UtcNow.AddDays(-7);

            // Block list (both directions)
            var blockedIds = await db.Blocks
                .Where(b => b.BlockerId == me || b.BlockedId == me)
                .Select(b => b.BlockerId == me ? b.BlockedId : b.BlockerId)
                .ToListAsync(ct);
            var blockedSet = new HashSet<int>(blockedIds);

            // Already matched with me (active)
            var matchedIds = await db.Matches.AsNoTracking()
                .Where(m => m.BalloonState == BalloonState.ACTIVE && (m.UserAId == me || m.UserBId == me))
                .Select(m => m.UserAId == me ? m.UserBId : m.UserAId)
                .ToListAsync(ct);
            var matchedSet = new HashSet<int>(matchedIds);

            // Today's deck IDs (exclude from liked-you so no overlap)
            var todayDeckIds = await db.MomentResponses.AsNoTracking()
                .Where(r => r.DateUtc == today && r.FromUserId == me)
                .Select(r => r.ToUserId)
                .ToListAsync(ct);
            // Also get unresponded deck candidates
            var deckCandidates = await db.CandidateExposures.AsNoTracking()
                .Where(e => e.ViewerUserId == me && e.DateUtc == today)
                .Select(e => e.ShownUserId)
                .ToListAsync(ct);
            var todayDeckSet = new HashSet<int>(todayDeckIds.Concat(deckCandidates));

            // People who chose me positively in last 7 days, not already matched, not blocked, not in today deck
            var whoLikedMe = await db.MomentResponses.AsNoTracking()
                .Where(r =>
                    r.ToUserId == me &&
                    IsPositiveExpression(r.Choice) &&
                    r.CreatedAt >= cutoff &&
                    !blockedSet.Contains(r.FromUserId) &&
                    !matchedSet.Contains(r.FromUserId) &&
                    !todayDeckSet.Contains(r.FromUserId))
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new { r.FromUserId, r.CreatedAt })
                .ToListAsync(ct);

            // Deduplicate — keep most recent per user
            var unique = whoLikedMe
                .GroupBy(r => r.FromUserId)
                .Select(g => g.First())
                .ToList();

            if (unique.Count == 0)
                return Results.Ok(new { count = 0, cards = Array.Empty<object>() });

            var userIds = unique.Select(r => r.FromUserId).ToList();

            var users = await db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new
                {
                    userId = u.Id,
                    fullName = u.FullName,
                    isVerified = u.IsVerified,
                    location = db.UserProfiles.Where(p => p.UserId == u.Id)
                        .Select(p => new { city = p.City, state = p.State }).FirstOrDefault(),
                    profilePhoto = db.UserPhotos
                        .Where(p => p.UserId == u.Id)
                        .OrderBy(p => p.SortOrder)
                        .Select(p => p.Url)
                        .FirstOrDefault()
                })
                .ToListAsync(ct);

            // All photos for gallery
            var lyPhotos = await db.UserPhotos.AsNoTracking()
                .Where(p => userIds.Contains(p.UserId))
                .OrderBy(p => p.UserId).ThenBy(p => p.SortOrder)
                .Select(p => new { p.UserId, p.Url })
                .ToListAsync(ct);
            var lyPhotosByUser = lyPhotos
                .GroupBy(p => p.UserId)
                .ToDictionary(g => g.Key, g => g.Select(p => p.Url).ToList());

            // Active tiles (live, newest first)
            var lyActiveTiles = await db.Tiles.AsNoTracking()
                .Where(t => userIds.Contains(t.UserId) && !t.IsExpired && t.IsModerated)
                .OrderBy(t => t.UserId).ThenByDescending(t => t.CreatedAt)
                .Select(t => new
                {
                    t.UserId,
                    tileId      = t.Id,
                    contentType = t.ContentType,
                    contentText = t.ContentText,
                    mediaUrl    = t.MediaUrl,
                    isActive    = true
                })
                .ToListAsync(ct);
            var lyActiveByUser = lyActiveTiles.GroupBy(t => t.UserId).ToDictionary(g => g.Key, g => g.ToList());

            // Highlighted tiles (slot order) — fallback
            var lyHighlights = await db.Highlights.AsNoTracking()
                .Where(h => userIds.Contains(h.UserId))
                .OrderBy(h => h.UserId).ThenBy(h => h.SlotNumber)
                .Select(h => new
                {
                    h.UserId,
                    tileId      = h.TileId,
                    contentType = h.Tile.ContentType,
                    contentText = h.Tile.ContentText,
                    mediaUrl    = h.Tile.MediaUrl,
                    isActive    = false
                })
                .ToListAsync(ct);
            var lyHighlightByUser = lyHighlights.GroupBy(h => h.UserId).ToDictionary(g => g.Key, g => g.ToList());

            var lyTilesByUser = userIds.ToDictionary(
                uid => uid,
                uid =>
                {
                    var active    = lyActiveByUser.GetValueOrDefault(uid) ?? new();
                    var highlight = lyHighlightByUser.GetValueOrDefault(uid) ?? new();
                    var merged    = active.Take(3).ToList();
                    if (merged.Count < 3)
                        merged.AddRange(highlight.Take(3 - merged.Count));
                    return merged.Select(t => new
                    {
                        id          = t.tileId,
                        contentType = t.contentType,
                        contentText = t.contentText,
                        mediaUrl    = t.mediaUrl,
                        isActive    = t.isActive
                    }).ToList<object>();
                });

            // Ratings
            var lyRatings = await db.UserRatings
                .Where(r => userIds.Contains(r.RatedUserId))
                .GroupBy(r => r.RatedUserId)
                .Select(g => new { userId = g.Key, average = g.Average(r => r.RatingValue), count = g.Count() })
                .ToListAsync(ct);
            var lyRatingMap = lyRatings.ToDictionary(r => r.userId, r => r);

            var userMap = users.ToDictionary(u => u.userId, u => u);

            var likedCutoff = DateTimeOffset.UtcNow;
            var cards = unique
                .Where(r => userMap.ContainsKey(r.FromUserId))
                .Select(r =>
                {
                    var u = userMap[r.FromUserId];
                    var expiresAt = r.CreatedAt.AddDays(7);
                    var hoursLeft = (int)Math.Max(0, (expiresAt - likedCutoff).TotalHours);

                    object? rating = null;
                    if (lyRatingMap.TryGetValue(u.userId, out var rat) && rat.count >= 5)
                        rating = new { average = (int)Math.Round(rat.average), count = rat.count, show = true };

                    return new
                    {
                        u.userId,
                        u.fullName,
                        u.isVerified,
                        u.location,
                        u.profilePhoto,
                        photos          = lyPhotosByUser.GetValueOrDefault(u.userId),
                        highlightedTiles = lyTilesByUser.GetValueOrDefault(u.userId),
                        rating,
                        likedAt         = r.CreatedAt,
                        expiresInHours  = hoursLeft
                    };
                })
                .ToList();

            return Results.Ok(new { count = cards.Count, cards });
        });

        // ── POST /moments/respond ─────────────────────────────────────────────────
        group.MapPost("/respond", async (
            RespondRequest req,
            WovenDbContext db,
            InteractionBudgetService budget,
            SparkWalletService sparks,
            MomentsMatchService matchService,
            IAnalyticsService analytics,
            IMatchSignalService signals,
            IIdempotencyService idempotency,
            IServiceScopeFactory scopeFactory,
            HttpContext http,
            CancellationToken ct) =>
        {
            var me = GetUserId(http.User);
            var today = MomentsRules.UtcToday();

            var choiceStr = (req.Choice ?? "").Trim().ToUpperInvariant();
            var source = (req.Source ?? "").Trim().ToUpperInvariant();

            // Check idempotency key for spark spend (LIKED_YOU source only)
            var isFromLikedYou = source == "LIKED_YOU";
            var idempotencyKey = http.Request.Headers["X-Idempotency-Key"].ToString();

            if (isFromLikedYou && !string.IsNullOrWhiteSpace(idempotencyKey))
            {
                var cached = await idempotency.CheckAsync(idempotencyKey, me, $"/moments/respond/{req.TargetUserId}", ct);
                if (cached != null)
                {
                    return Results.Json(
                        System.Text.Json.JsonSerializer.Deserialize<object>(cached.ResponseBodyJson),
                        statusCode: cached.StatusCode);
                }
            }

            if (req.TargetUserId <= 0 || req.TargetUserId == me)
                return Results.BadRequest(new { error = "INVALID_TARGET" });

            var targetExists = await db.Users.AnyAsync(u => u.Id == req.TargetUserId, ct);
            if (!targetExists) return Results.BadRequest(new { error = "TARGET_NOT_FOUND" });

            var blocked = await db.Blocks.AnyAsync(b =>
                (b.BlockerId == me && b.BlockedId == req.TargetUserId) ||
                (b.BlockerId == req.TargetUserId && b.BlockedId == me), ct);
            if (blocked) return Results.BadRequest(new { error = "BLOCKED" });

            var choiceEnum = choiceStr switch
            {
                "MAGICAL" => (MomentChoice?)MomentChoice.MAGICAL,
                "LOGICAL" => (MomentChoice?)MomentChoice.LOGICAL,
                "PASS"    => (MomentChoice?)MomentChoice.PASS,
                // Legacy support
                "YES"     => (MomentChoice?)MomentChoice.MAGICAL,
                "NO"      => (MomentChoice?)MomentChoice.LOGICAL,
                _ => null
            };

            if (choiceEnum is null)
                return Results.BadRequest(new { error = "INVALID_CHOICE" });

            // PASS — record signal, no budget spend, no match check
            if (choiceEnum == MomentChoice.PASS)
            {
                var alreadyPassed = await db.MomentResponses.AnyAsync(r =>
                    r.DateUtc == today && r.FromUserId == me && r.ToUserId == req.TargetUserId, ct);

                if (!alreadyPassed)
                {
                    db.MomentResponses.Add(new MomentResponse
                    {
                        DateUtc = today,
                        FromUserId = me,
                        ToUserId = req.TargetUserId,
                        Choice = MomentChoice.PASS,
                        TimeOnCardMs = req.TimeOnCardMs,
                        Source = source,
                        CreatedAt = MomentsRules.NowUtc()
                    });
                    await db.SaveChangesAsync(ct);
                }

                _ = analytics.TrackAsync(me, null, AnalyticsEvents.MomentResponded,
                    new { choice = "PASS", timeOnCardMs = req.TimeOnCardMs, source });

                return Results.Ok(new { status = "RECORDED_PASS" });
            }

            // MAGICAL / LOGICAL — check for duplicate response today
            var existingToday = await db.MomentResponses
                .FirstOrDefaultAsync(r =>
                    r.DateUtc == today && r.FromUserId == me && r.ToUserId == req.TargetUserId, ct);

            // Only block if they already made a positive choice (not a pass they're upgrading from)
            if (existingToday != null && existingToday.Choice != MomentChoice.PASS && !isFromLikedYou)
                return Results.Conflict(new { error = "ALREADY_RESPONDED_TODAY" });

            // LikedYou actions cost 1 spark from wallet; Today deck actions cost 1 of the daily cap.
            decimal? newSparkBalance = null;
            if (isFromLikedYou)
            {
                var sparkSpend = await sparks.TrySpendAsync(me, ct);
                if (!sparkSpend.Allowed)
                    return Results.BadRequest(new { error = sparkSpend.DenyReason, sparkBalance = sparkSpend.Balance });
                newSparkBalance = sparkSpend.Balance;
            }
            else
            {
                var spend = await budget.TrySpendAsync(me, InteractionBudgetService.SpendType.Moment, ct);
                if (!spend.Allowed)
                    return Results.BadRequest(new { error = spend.DenyReason, spend.TotalUsed });
            }

            // Record response (upsert — may be upgrading from PASS)
            if (existingToday == null)
            {
                db.MomentResponses.Add(new MomentResponse
                {
                    DateUtc = today,
                    FromUserId = me,
                    ToUserId = req.TargetUserId,
                    Choice = choiceEnum.Value,
                    TimeOnCardMs = req.TimeOnCardMs,
                    Source = source,
                    CreatedAt = MomentsRules.NowUtc()
                });
            }
            else
            {
                existingToday.Choice = choiceEnum.Value;
                existingToday.TimeOnCardMs = req.TimeOnCardMs;
                existingToday.Source = source;
            }

            await db.SaveChangesAsync(ct);

            _ = analytics.TrackAsync(me, null, AnalyticsEvents.MomentResponded,
                new { choice = choiceEnum.Value.ToString(), source });

            try { await signals.RecordAsync(me, req.TargetUserId, MatchSignalEventTypes.BalloonPop, 1f, ct: ct); }
            catch { /* non-critical */ }

            // Fire-and-forget: record visual decision for preference learning
            var capturedMe = me;
            var capturedTarget = req.TargetUserId;
            var capturedChoiceStr = choiceEnum.Value.ToString();
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var innerDb = scope.ServiceProvider.GetRequiredService<WovenDbContext>();
                    var primaryPhoto = await innerDb.PhotoEmbeddings.AsNoTracking()
                        .Where(p => p.UserId == capturedTarget)
                        .OrderBy(p => p.EmbeddedAt)
                        .Select(p => (int?)p.Id)
                        .FirstOrDefaultAsync();

                    innerDb.UserVisualDecisions.Add(new WovenBackend.Data.Entities.UserVisualDecision
                    {
                        ViewerUserId = capturedMe,
                        TargetUserId = capturedTarget,
                        PhotoEmbeddingId = primaryPhoto,
                        Choice = capturedChoiceStr,
                        DecidedAt = DateTimeOffset.UtcNow
                    });
                    await innerDb.SaveChangesAsync();
                }
                catch { /* best-effort */ }
            });

            // Look for counterpart response — only positive choices trigger match
            MomentResponse? other;
            if (isFromLikedYou)
            {
                other = await db.MomentResponses.AsNoTracking()
                    .Where(r => r.FromUserId == req.TargetUserId && r.ToUserId == me && IsPositiveExpression(r.Choice))
                    .OrderByDescending(r => r.CreatedAt)
                    .FirstOrDefaultAsync(ct);
            }
            else
            {
                other = await db.MomentResponses.AsNoTracking()
                    .FirstOrDefaultAsync(r =>
                        r.DateUtc == today &&
                        r.FromUserId == req.TargetUserId &&
                        r.ToUserId == me &&
                        IsPositiveExpression(r.Choice), ct);
            }

            if (other is null)
            {
                var waitingResponse = new { status = "RECORDED_WAITING", sparkBalance = newSparkBalance };
                if (isFromLikedYou && !string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    _ = idempotency.StoreAsync(idempotencyKey, me, $"/moments/respond/{req.TargetUserId}", 200, waitingResponse, ct);
                }
                return Results.Ok(waitingResponse);
            }

            // Both positive → create match
            var (a, b) = MomentsRules.NormalizePair(me, req.TargetUserId);
            var matchType = IsPure(choiceEnum.Value, other.Choice)
                ? WovenBackend.data.Entities.Moments.MatchType.PURE
                : WovenBackend.data.Entities.Moments.MatchType.EDGE;

            var edgeOwner = matchType == WovenBackend.data.Entities.Moments.MatchType.EDGE
                ? (int?)(Random.Shared.Next(0, 2) == 0 ? a : b)
                : null;

            var created = await matchService.CreateActiveMatchAsync(a, b, matchType, edgeOwnerId: edgeOwner, ct);

            var statusStr = created.Created
                ? (matchType == WovenBackend.data.Entities.Moments.MatchType.PURE ? "PURE_MATCH_CREATED" : "EDGE_MATCH_CREATED")
                : "MATCH_NOT_CREATED";

            var matchResponse = new
            {
                status = statusStr,
                reason = created.Reason,
                matchId = created.Match?.Id,
                matchType = matchType.ToString(),
                edgeOwnerId = created.Match?.EdgeOwnerId,
                sparkBalance = newSparkBalance,
            };

            if (isFromLikedYou && !string.IsNullOrWhiteSpace(idempotencyKey))
            {
                _ = idempotency.StoreAsync(idempotencyKey, me, $"/moments/respond/{req.TargetUserId}", 200, matchResponse, ct);
            }

            return Results.Ok(matchResponse);
        });

        // ── POST /moments/choose ──────────────────────────────────────────────────
        // MAGICAL or LOGICAL with a required note. Balloon only created when both users
        // have submitted a choice + note. This is the atomic commitment point.
        group.MapPost("/choose", async (
            ChooseRequest req,
            WovenDbContext db,
            InteractionBudgetService budget,
            SparkWalletService sparks,
            MomentsMatchService matchService,
            IAnalyticsService analytics,
            IMatchSignalService signals,
            IServiceScopeFactory scopeFactory,
            HttpContext http,
            CancellationToken ct) =>
        {
            var me = GetUserId(http.User);
            var today = MomentsRules.UtcToday();

            if (req.TargetUserId <= 0 || req.TargetUserId == me)
                return Results.BadRequest(new { error = "INVALID_TARGET" });

            var noteText = (req.NoteText ?? "").Trim();
            if (noteText.Length < 20 || noteText.Length > 150)
                return Results.BadRequest(new { error = "NOTE_LENGTH_INVALID", min = 20, max = 150 });

            var choiceStr = (req.Choice ?? "").Trim().ToUpperInvariant();
            var choiceEnum = choiceStr switch
            {
                "MAGICAL" => (MomentChoice?)MomentChoice.MAGICAL,
                "LOGICAL" => (MomentChoice?)MomentChoice.LOGICAL,
                _ => null
            };
            if (choiceEnum is null)
                return Results.BadRequest(new { error = "INVALID_CHOICE" });

            var targetExists = await db.Users.AnyAsync(u => u.Id == req.TargetUserId, ct);
            if (!targetExists) return Results.BadRequest(new { error = "TARGET_NOT_FOUND" });

            var blocked = await db.Blocks.AnyAsync(b =>
                (b.BlockerId == me && b.BlockedId == req.TargetUserId) ||
                (b.BlockerId == req.TargetUserId && b.BlockedId == me), ct);
            if (blocked) return Results.BadRequest(new { error = "BLOCKED" });

            var source = (req.Source ?? "").Trim().ToUpperInvariant();
            var isFromLikedYou = source == "LIKED_YOU";

            // Block double-submission (unless upgrading from a pass)
            var existingResponse = await db.MomentResponses
                .FirstOrDefaultAsync(r =>
                    r.DateUtc == today && r.FromUserId == me && r.ToUserId == req.TargetUserId, ct);

            if (existingResponse != null && existingResponse.Choice != MomentChoice.PASS && !isFromLikedYou)
                return Results.Conflict(new { error = "ALREADY_RESPONDED_TODAY" });

            // Already has a ChatNote for this target?
            var existingNote = await db.ChatNotes
                .FirstOrDefaultAsync(n => n.FromUserId == me && n.ToUserId == req.TargetUserId, ct);
            if (existingNote != null)
                return Results.Conflict(new { error = "NOTE_ALREADY_SUBMITTED" });

            // LikedYou costs 1 spark from wallet; Today deck costs 1 daily cap slot.
            if (isFromLikedYou)
            {
                var sparkSpend = await sparks.TrySpendAsync(me, ct);
                if (!sparkSpend.Allowed)
                    return Results.BadRequest(new { error = sparkSpend.DenyReason, sparkBalance = sparkSpend.Balance });
            }
            else
            {
                var spend = await budget.TrySpendAsync(me, InteractionBudgetService.SpendType.Moment, ct);
                if (!spend.Allowed)
                    return Results.BadRequest(new { error = spend.DenyReason, spend.TotalUsed });
            }

            // Record MomentResponse (upsert from pass)
            if (existingResponse == null)
            {
                db.MomentResponses.Add(new MomentResponse
                {
                    DateUtc = today,
                    FromUserId = me,
                    ToUserId = req.TargetUserId,
                    Choice = choiceEnum.Value,
                    TimeOnCardMs = req.TimeOnCardMs,
                    Source = source,
                    CreatedAt = MomentsRules.NowUtc()
                });
            }
            else
            {
                existingResponse.Choice = choiceEnum.Value;
                existingResponse.TimeOnCardMs = req.TimeOnCardMs;
                existingResponse.Source = source;
            }

            // Record ChatNote
            var note = new WovenBackend.data.Entities.Moments.ChatNote
            {
                FromUserId = me,
                ToUserId = req.TargetUserId,
                Choice = choiceEnum.Value,
                NoteText = noteText,
                Source = source,
                CreatedAt = MomentsRules.NowUtc()
            };
            db.ChatNotes.Add(note);
            await db.SaveChangesAsync(ct);

            _ = analytics.TrackAsync(me, null, AnalyticsEvents.MomentResponded,
                new { choice = choiceEnum.Value.ToString(), hasNote = true, source });

            try { await signals.RecordAsync(me, req.TargetUserId, MatchSignalEventTypes.BalloonPop, 1f, ct: ct); }
            catch { /* non-critical */ }

            // Fire-and-forget: visual decision
            var capturedMe = me;
            var capturedTarget = req.TargetUserId;
            var capturedChoiceStr = choiceEnum.Value.ToString();
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var innerDb = scope.ServiceProvider.GetRequiredService<WovenDbContext>();
                    var primaryPhoto = await innerDb.PhotoEmbeddings.AsNoTracking()
                        .Where(p => p.UserId == capturedTarget)
                        .OrderBy(p => p.EmbeddedAt)
                        .Select(p => (int?)p.Id)
                        .FirstOrDefaultAsync();
                    innerDb.UserVisualDecisions.Add(new WovenBackend.Data.Entities.UserVisualDecision
                    {
                        ViewerUserId = capturedMe,
                        TargetUserId = capturedTarget,
                        PhotoEmbeddingId = primaryPhoto,
                        Choice = capturedChoiceStr,
                        DecidedAt = DateTimeOffset.UtcNow
                    });
                    await innerDb.SaveChangesAsync();
                }
                catch { /* best-effort */ }
            });

            // Check counterpart — need BOTH a positive response AND a ChatNote
            MomentResponse? otherResponse;
            if (isFromLikedYou)
            {
                otherResponse = await db.MomentResponses.AsNoTracking()
                    .Where(r => r.FromUserId == req.TargetUserId && r.ToUserId == me && IsPositiveExpression(r.Choice))
                    .OrderByDescending(r => r.CreatedAt)
                    .FirstOrDefaultAsync(ct);
            }
            else
            {
                otherResponse = await db.MomentResponses.AsNoTracking()
                    .FirstOrDefaultAsync(r =>
                        r.DateUtc == today &&
                        r.FromUserId == req.TargetUserId &&
                        r.ToUserId == me &&
                        IsPositiveExpression(r.Choice), ct);
            }

            // No counterpart response yet — wait
            if (otherResponse is null)
                return Results.Ok(new { status = "RECORDED_WAITING" });

            // Counterpart has a response — do they also have a note?
            var otherNote = await db.ChatNotes.AsNoTracking()
                .FirstOrDefaultAsync(n => n.FromUserId == req.TargetUserId && n.ToUserId == me, ct);

            // No note from other side yet — waiting for their note
            if (otherNote is null)
                return Results.Ok(new { status = "WAITING_FOR_OTHER_NOTE" });

            // Both have response + note → create balloon
            var (a, b) = MomentsRules.NormalizePair(me, req.TargetUserId);
            var matchType = IsPure(choiceEnum.Value, otherResponse.Choice)
                ? WovenBackend.data.Entities.Moments.MatchType.PURE
                : WovenBackend.data.Entities.Moments.MatchType.EDGE;

            var edgeOwner = matchType == WovenBackend.data.Entities.Moments.MatchType.EDGE
                ? (int?)(Random.Shared.Next(0, 2) == 0 ? a : b)
                : null;

            var created = await matchService.CreateActiveMatchAsync(a, b, matchType, edgeOwner, ct);

            if (created.Created && created.Match != null)
            {
                // Link both ChatNotes to the match
                note.MatchId = created.Match.Id;
                otherNote = await db.ChatNotes
                    .FirstOrDefaultAsync(n => n.FromUserId == req.TargetUserId && n.ToUserId == me, ct);
                if (otherNote != null) otherNote.MatchId = created.Match.Id;
                await db.SaveChangesAsync(ct);
            }

            var statusStr = created.Created
                ? (matchType == WovenBackend.data.Entities.Moments.MatchType.PURE ? "PURE_MATCH_CREATED" : "EDGE_MATCH_CREATED")
                : "MATCH_NOT_CREATED";

            return Results.Ok(new
            {
                status = statusStr,
                reason = created.Reason,
                matchId = created.Match?.Id,
                matchType = matchType.ToString(),
                edgeOwnerId = created.Match?.EdgeOwnerId
            });
        });
    }

    // EF can't translate instance methods in LINQ — use a static expression helper
    private static bool IsPositiveExpression(MomentChoice c) =>
        c is MomentChoice.MAGICAL or MomentChoice.LOGICAL or MomentChoice.YES;

    private static int GetUserId(ClaimsPrincipal user)
    {
        var uid = user.FindFirstValue("uid");
        if (int.TryParse(uid, out var id)) return id;
        var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(sub, out id)) return id;
        throw new UnauthorizedAccessException("Missing user id claim");
    }
}
