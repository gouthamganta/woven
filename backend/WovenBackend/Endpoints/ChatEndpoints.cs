using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WovenBackend.Data;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Services;
using WovenBackend.Services.Analytics;
using WovenBackend.Services.Moments;
using WovenBackend.Services.Nudges;
using WovenBackend.Services.Venues;
using System.Text.Json;

namespace WovenBackend.Endpoints;

public static class ChatEndpoints
{
    public record StartChatRequest(Guid MatchId);
    public record SendMessageRequest(string Body);
    public record TrialDecisionRequest(string Decision, int? Rating);

    private static readonly TimeSpan ReflectionWindow = TimeSpan.FromMinutes(5);

    public static void MapChatEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/chats");
        group.RequireAuthorization();

        // GET /chats -> list active chat threads (active balloons only)
        group.MapGet("", async (
            WovenDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var me = GetUserId(http.User);
            var now = MomentsRules.NowUtc();

            var threads = await (
                from t in db.ChatThreads.AsNoTracking()
                join m in db.Matches.AsNoTracking() on t.MatchId equals m.Id
                where m.BalloonState == BalloonState.ACTIVE
                      && (m.UserAId == me || m.UserBId == me)
                orderby t.UpdatedAt descending
                select new
                {
                    threadId = t.Id,
                    matchId = m.Id,
                    matchType = m.MatchType.ToString(),
                    edgeOwnerId = m.EdgeOwnerId,
                    expiresAt = m.ExpiresAt,
                    bothMessagedAt = m.BothMessagedAt,
                    findLoveAt = m.FindLoveAt,
                    showFindLove = m.FindLoveAt != null && m.FindLoveAt <= now,
                    showBalloonTimer = m.BothMessagedAt != null && m.FindLoveAt != null && m.FindLoveAt > now,
                    reflectionSecondsLeft = (m.FindLoveAt != null && m.FindLoveAt > now)
                        ? (int)Math.Ceiling((m.FindLoveAt.Value - now).TotalSeconds)
                        : 0,
                    otherUserId = (m.UserAId == me ? m.UserBId : m.UserAId),
                    updatedAt = t.UpdatedAt,
                    // Trial fields
                    isTrial = m.IsTrial,
                    trialEndsAt = m.TrialEndsAt,
                    trialSecondsLeft = (m.IsTrial && m.TrialEndsAt != null && m.TrialEndsAt > now)
                        ? (int)Math.Ceiling((m.TrialEndsAt.Value - now).TotalSeconds)
                        : 0
                }
            ).ToListAsync(ct);

            if (threads.Count == 0)
                return Results.Ok(new { meUserId = me, count = 0, chats = Array.Empty<object>() });

            var otherIds = threads.Select(x => x.otherUserId).Distinct().ToList();

            // ✅ First uploaded photo for chat list consistency
            var others = await db.Users.AsNoTracking()
                .Where(u => otherIds.Contains(u.Id))
                .Select(u => new
                {
                    userId = u.Id,
                    fullName = u.FullName,
                    isVerified = u.IsVerified,
                    displayPronouns = db.UserProfiles
                        .Where(p => p.UserId == u.Id)
                        .Select(p => p.DisplayPronouns)
                        .FirstOrDefault(),
                    profilePhoto = db.UserPhotos
                        .Where(p => p.UserId == u.Id)
                        .OrderBy(p => p.SortOrder)
                        .Select(p => p.Url)
                        .FirstOrDefault()
                })
                .ToListAsync(ct);

            var userMap = others.ToDictionary(x => x.userId, x => x);

            var threadIds = threads.Select(x => x.threadId).ToList();

            var lastMessages = await db.ChatMessages.AsNoTracking()
                .Where(m => threadIds.Contains(m.ThreadId))
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => new
                {
                    threadId = m.ThreadId,
                    body = m.Body,
                    createdAt = m.CreatedAt,
                    senderUserId = m.SenderUserId,
                    messageType = m.MessageType,  // ✅ ADDED
                    metaJson = m.MetaJson          // ✅ ADDED
                })
                .ToListAsync(ct);

            var lastMap = lastMessages
                .GroupBy(x => x.threadId)
                .Select(g => g.First())
                .ToDictionary(x => x.threadId, x => x);

            var result = threads.Select(x => new
            {
                x.threadId,
                x.matchId,
                x.matchType,
                x.edgeOwnerId,
                x.expiresAt,
                x.bothMessagedAt,
                x.findLoveAt,
                x.showFindLove,
                x.showBalloonTimer,
                x.reflectionSecondsLeft,
                title = userMap.TryGetValue(x.otherUserId, out var u1) ? $"A moment with {u1.fullName}" : "A moment",
                other = userMap.TryGetValue(x.otherUserId, out var u)
                    ? new { userId = u.userId, fullName = u.fullName, isVerified = u.isVerified, displayPronouns = u.displayPronouns, profilePhoto = u.profilePhoto }
                    : null,
                lastMessage = lastMap.TryGetValue(x.threadId, out var lm)
                    ? new { lm.body, lm.createdAt, lm.senderUserId, lm.messageType, lm.metaJson }
                    : null,
                // Trial fields
                x.isTrial,
                x.trialEndsAt,
                x.trialSecondsLeft
            });

            return Results.Ok(new { meUserId = me, count = result.Count(), chats = result });
        });

        // POST /chats/start -> returns threadId for a match (creates if missing)
        group.MapPost("/start", async (
            StartChatRequest req,
            WovenDbContext db,
            IAnalyticsService analytics,
            HttpContext http,
            CancellationToken ct) =>
        {
            var me = GetUserId(http.User);

            var match = await db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == req.MatchId, ct);
            if (match == null) return Results.NotFound(new { error = "MATCH_NOT_FOUND" });

            var isParticipant = match.UserAId == me || match.UserBId == me;
            if (!isParticipant) return Results.Forbid();

            if (match.BalloonState != BalloonState.ACTIVE)
                return Results.BadRequest(new { error = "BALLOON_NOT_ACTIVE" });

            var existing = await db.ChatThreads.FirstOrDefaultAsync(t => t.MatchId == req.MatchId, ct);
            if (existing != null)
                return Results.Ok(new { threadId = existing.Id, matchId = req.MatchId });

            var now = MomentsRules.NowUtc();

            var thread = new ChatThread
            {
                MatchId = req.MatchId,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.ChatThreads.Add(thread);
            await db.SaveChangesAsync(ct);

            _ = analytics.TrackAsync(me, null, AnalyticsEvents.ChatStarted, new { matchId = req.MatchId });

            return Results.Ok(new { threadId = thread.Id, matchId = req.MatchId });
        });

        // GET /chats/{threadId} -> loads messages + other user + date idea (if Find Love unlocked)
        group.MapGet("/{threadId:guid}", async (
            Guid threadId,
            WovenDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var me = GetUserId(http.User);
            var now = MomentsRules.NowUtc();

            var thread = await db.ChatThreads.AsNoTracking().FirstOrDefaultAsync(t => t.Id == threadId, ct);
            if (thread == null) return Results.NotFound(new { error = "THREAD_NOT_FOUND" });

            // Load match with tracking for potential auto-reject update
            var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == thread.MatchId, ct);
            if (match == null) return Results.NotFound(new { error = "MATCH_NOT_FOUND" });

            var isParticipant = match.UserAId == me || match.UserBId == me;
            if (!isParticipant) return Results.Forbid();

            var otherUserId = match.UserAId == me ? match.UserBId : match.UserAId;

            // Auto-reject logic: if trial ended and not both decided, check for messages
            if (match.IsTrial && match.TrialEndsAt != null && match.TrialEndsAt <= now)
            {
                var bothDecided = !string.IsNullOrEmpty(match.UserADecision) && !string.IsNullOrEmpty(match.UserBDecision);
                if (!bothDecided)
                {
                    // Check for messages during trial
                    var hasTrialMessages = await db.ChatMessages.AsNoTracking()
                        .AnyAsync(m => m.ThreadId == threadId && m.CreatedAt >= match.TrialStartedAt, ct);

                    if (!hasTrialMessages)
                    {
                        // Auto-close as UNMATCH
                        match.BalloonState = BalloonState.CLOSED;
                        match.ClosedReason = ClosedReason.UNMATCH;
                        match.ClosedAt = now;
                        match.IsTrial = false;
                        await db.SaveChangesAsync(ct);
                    }
                }
            }

            var other = await db.Users.AsNoTracking()
                .Where(u => u.Id == otherUserId)
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
                .FirstOrDefaultAsync(ct);

            // Load messages
            var rawMessages = await db.ChatMessages.AsNoTracking()
                .Where(m => m.ThreadId == threadId)
                .OrderByDescending(m => m.CreatedAt)
                .Take(50)
                .OrderBy(m => m.CreatedAt)
                .Select(m => new
                {
                    messageId = m.Id,
                    senderUserId = m.SenderUserId,
                    body = m.Body,
                    messageType = m.MessageType,
                    metaJson = m.MetaJson,
                    createdAt = m.CreatedAt
                })
                .ToListAsync(ct);

            // Parse JSON in memory
            var messages = rawMessages.Select(m => new
            {
                m.messageId,
                m.senderUserId,
                m.body,
                m.messageType,
                meta = string.IsNullOrEmpty(m.metaJson) || m.metaJson == "{}"
                    ? (object?)null
                    : JsonDocument.Parse(m.metaJson).RootElement,
                m.createdAt
            }).ToList();

            var showBalloonTimer = match.BothMessagedAt != null && match.FindLoveAt != null && match.FindLoveAt > now;
            var reflectionSecondsLeft = (match.FindLoveAt != null && match.FindLoveAt > now)
                ? (int)Math.Ceiling((match.FindLoveAt.Value - now).TotalSeconds)
                : 0;
            var showFindLove = match.FindLoveAt != null && match.FindLoveAt <= now;

            // Trial fields
            var isTrial = match.IsTrial;
            var trialEndsAt = match.TrialEndsAt;
            var trialSecondsLeft = (isTrial && trialEndsAt != null && trialEndsAt > now)
                ? (int)Math.Ceiling((trialEndsAt.Value - now).TotalSeconds)
                : 0;
            var canMakeDecision = isTrial && trialEndsAt != null && trialEndsAt <= now;
            var isUserA = match.UserAId == me;

            // Load match explanation for date idea (only if Find Love unlocked)
            string? dateIdea = null;
            if (showFindLove)
            {
                var explanation = await db.MatchExplanations.AsNoTracking()
                    .Where(e => (e.UserId == me && e.CandidateId == otherUserId) ||
                               (e.UserId == otherUserId && e.CandidateId == me))
                    .OrderByDescending(e => e.CreatedAt)
                    .FirstOrDefaultAsync(ct);

                dateIdea = explanation?.DateIdea;
            }

            return Results.Ok(new
            {
                meUserId = me,
                threadId = thread.Id,
                matchId = thread.MatchId,
                balloonState = match.BalloonState.ToString(),
                expiresAt = match.ExpiresAt,
                bothMessagedAt = match.BothMessagedAt,
                findLoveAt = match.FindLoveAt,
                showBalloonTimer,
                reflectionSecondsLeft,
                showFindLove,
                dateIdea,
                other,
                messages,
                // Trial fields
                isTrial,
                trialEndsAt,
                trialSecondsLeft,
                canMakeDecision,
                isUserA,
                userADecision = match.UserADecision,
                userBDecision = match.UserBDecision
            });
        });

        // POST /chats/{threadId}/messages
        // - saves message
        // - updates thread updatedAt
        // - sets BothMessagedAt + FindLoveAt ONCE when both users have messaged
        group.MapPost("/{threadId:guid}/messages", async (
            Guid threadId,
            SendMessageRequest req,
            WovenDbContext db,
            IAnalyticsService analytics,
            HttpContext http,
            CancellationToken ct) =>
        {
            var me = GetUserId(http.User);

            if (string.IsNullOrWhiteSpace(req.Body))
                return Results.BadRequest(new { error = "EMPTY_MESSAGE" });

            var body = req.Body.Trim();
            if (body.Length > 1000)
                return Results.BadRequest(new { error = "MESSAGE_TOO_LONG" });

            var thread = await db.ChatThreads.FirstOrDefaultAsync(t => t.Id == threadId, ct);
            if (thread == null) return Results.NotFound(new { error = "THREAD_NOT_FOUND" });

            var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == thread.MatchId, ct);
            if (match == null) return Results.NotFound(new { error = "MATCH_NOT_FOUND" });

            var isParticipant = match.UserAId == me || match.UserBId == me;
            if (!isParticipant) return Results.Forbid();

            if (match.BalloonState != BalloonState.ACTIVE)
                return Results.BadRequest(new { error = "BALLOON_NOT_ACTIVE" });

            var now = MomentsRules.NowUtc();

            // ✅ Check if this is the first message from this user (for outcome tracking)
            var isFirstMessage = !await db.ChatMessages.AsNoTracking()
                .AnyAsync(m => m.ThreadId == threadId && m.SenderUserId == me, ct);

            var msg = new ChatMessage
            {
                ThreadId = threadId,
                SenderUserId = me,
                Body = body,
                CreatedAt = now
            };

            db.ChatMessages.Add(msg);
            thread.UpdatedAt = now;
            thread.LastMessageAt = now;

            // Compute AvgResponseTimeMs: find last message from the OTHER user
            var otherUserIdForResp = match.UserAId == me ? match.UserBId : match.UserAId;
            var prevMsg = await db.ChatMessages.AsNoTracking()
                .Where(m => m.ThreadId == threadId && m.SenderUserId == otherUserIdForResp)
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (prevMsg != null)
            {
                var responseMs = (long)(now - prevMsg.CreatedAt).TotalMilliseconds;
                thread.MessageCount++;
                thread.AvgResponseTimeMs = thread.AvgResponseTimeMs == null
                    ? responseMs
                    : (thread.AvgResponseTimeMs * (thread.MessageCount - 1) + responseMs) / thread.MessageCount;
            }
            else
            {
                thread.MessageCount++;
            }

            await db.SaveChangesAsync(ct);

            _ = analytics.TrackAsync(me, null, AnalyticsEvents.MessageSent,
                new { messageNumber = thread.MessageCount });

            // ✅ Set BothMessagedAt + FindLoveAt ONCE when both users have messaged
            if (match.BothMessagedAt == null)
            {
                var otherUserId = match.UserAId == me ? match.UserBId : match.UserAId;

                var otherHasMessaged = await db.ChatMessages.AsNoTracking()
                    .AnyAsync(m => m.ThreadId == threadId && m.SenderUserId == otherUserId, ct);

                if (otherHasMessaged)
                {
                    match.BothMessagedAt = now;

                    // Reflection unlock after 5 minutes
                    if (match.FindLoveAt == null)
                        match.FindLoveAt = now.Add(ReflectionWindow);

                    await db.SaveChangesAsync(ct);

                    var otherUserIdForUnlock = match.UserAId == me ? match.UserBId : match.UserAId;
                    _ = analytics.TrackAsync(me, null, AnalyticsEvents.FindLoveUnlocked, null);
                    _ = analytics.TrackAsync(otherUserIdForUnlock, null, AnalyticsEvents.FindLoveUnlocked, null);
                }
            }

            // ✅ OUTCOME TRACKING: Record chat started (non-blocking)
            if (isFirstMessage)
            {
                try
                {
                    var outcomeService = http.RequestServices
                        .GetRequiredService<WovenBackend.Services.Matchmaking.IMatchOutcomeService>();

                    var candidateId = match.UserAId == me ? match.UserBId : match.UserAId;

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await outcomeService.RecordChatStartedAsync(match.Id, me, candidateId, CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            var logger = http.RequestServices.GetRequiredService<ILogger<Program>>();
                            logger.LogError(ex, "[Chat] Failed to record chat started for match {MatchId}", match.Id);
                        }
                    });
                }
                catch
                {
                    // Silent fail - outcome tracking is non-critical
                }
            }

            return Results.Ok(new
            {
                status = "SENT",
                messageId = msg.Id,
                createdAt = msg.CreatedAt
            });
        });

        // POST /chats/{threadId}/close-gracefully — mutual walk-away without ghosting penalty
        group.MapPost("/{threadId:guid}/close-gracefully", async (
            Guid threadId,
            WovenDbContext db,
            INotificationService notify,
            HttpContext http,
            CancellationToken ct) =>
        {
            var me = GetUserId(http.User);
            var now = MomentsRules.NowUtc();

            var thread = await db.ChatThreads.AsNoTracking().FirstOrDefaultAsync(t => t.Id == threadId, ct);
            if (thread == null) return Results.NotFound(new { error = "THREAD_NOT_FOUND" });

            var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == thread.MatchId, ct);
            if (match == null) return Results.NotFound(new { error = "MATCH_NOT_FOUND" });

            var isParticipant = match.UserAId == me || match.UserBId == me;
            if (!isParticipant) return Results.Forbid();

            if (match.BalloonState != BalloonState.ACTIVE)
                return Results.BadRequest(new { error = "BALLOON_NOT_ACTIVE" });

            match.BalloonState = BalloonState.CLOSED;
            match.ClosedReason = ClosedReason.UNMATCH;
            match.ClosedAt = now;
            await db.SaveChangesAsync(ct);

            await notify.MomentExpiredAsync(match.UserAId, match.UserBId, match.Id, ct);

            return Results.Ok(new { status = "CLOSED", closedAt = now });
        });

        MapNudgeAndDateEndpoints(group);

        // POST /chats/{threadId}/trial-decision
        group.MapPost("/{threadId:guid}/trial-decision", async (
            Guid threadId,
            TrialDecisionRequest req,
            WovenDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var me = GetUserId(http.User);
            var now = MomentsRules.NowUtc();

            var thread = await db.ChatThreads.AsNoTracking().FirstOrDefaultAsync(t => t.Id == threadId, ct);
            if (thread == null) return Results.NotFound(new { error = "THREAD_NOT_FOUND" });

            var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == thread.MatchId, ct);
            if (match == null) return Results.NotFound(new { error = "MATCH_NOT_FOUND" });

            var isParticipant = match.UserAId == me || match.UserBId == me;
            if (!isParticipant) return Results.Forbid();

            // Must be in trial
            if (!match.IsTrial)
                return Results.BadRequest(new { error = "NOT_IN_TRIAL" });

            // Trial must have ended
            if (match.TrialEndsAt == null || match.TrialEndsAt > now)
                return Results.BadRequest(new { error = "TRIAL_NOT_ENDED" });

            // Validate decision
            var decision = (req.Decision ?? "").Trim().ToUpperInvariant();
            if (decision != "CONTINUE" && decision != "END")
                return Results.BadRequest(new { error = "INVALID_DECISION" });

            var isUserA = match.UserAId == me;
            var otherUserId = isUserA ? match.UserBId : match.UserAId;

            // User A (who popped) must provide rating
            if (isUserA)
            {
                if (req.Rating == null)
                    return Results.BadRequest(new { error = "RATING_REQUIRED_FOR_USER_A" });

                if (req.Rating < -100 || req.Rating > 100)
                    return Results.BadRequest(new { error = "RATING_OUT_OF_RANGE" });

                // Store or update rating
                var existingRating = await db.UserRatings
                    .FirstOrDefaultAsync(r => r.MatchId == match.Id && r.RaterUserId == me, ct);

                if (existingRating != null)
                {
                    existingRating.RatingValue = req.Rating.Value;
                }
                else
                {
                    db.UserRatings.Add(new UserRating
                    {
                        RatedUserId = otherUserId,
                        RaterUserId = me,
                        MatchId = match.Id,
                        RatingValue = req.Rating.Value,
                        CreatedAt = now
                    });
                }

                match.UserADecision = decision;
            }
            else
            {
                // User B cannot rate
                match.UserBDecision = decision;
            }

            await db.SaveChangesAsync(ct);

            // Check if both decided
            if (!string.IsNullOrEmpty(match.UserADecision) && !string.IsNullOrEmpty(match.UserBDecision))
            {
                if (match.UserADecision == "CONTINUE" && match.UserBDecision == "CONTINUE")
                {
                    // Both continue - unlock Find Love immediately
                    match.IsTrial = false;
                    match.FindLoveAt = now;
                    await db.SaveChangesAsync(ct);

                    return Results.Ok(new
                    {
                        status = "MATCH_CONTINUES",
                        findLoveAt = match.FindLoveAt
                    });
                }
                else
                {
                    // At least one chose END - close match
                    match.BalloonState = BalloonState.CLOSED;
                    match.ClosedReason = ClosedReason.UNMATCH;
                    match.ClosedAt = now;
                    match.IsTrial = false;
                    await db.SaveChangesAsync(ct);

                    return Results.Ok(new
                    {
                        status = "MATCH_ENDED",
                        closedAt = match.ClosedAt
                    });
                }
            }

            return Results.Ok(new
            {
                status = "DECISION_RECORDED",
                waitingForOther = true
            });
        });
    }

    // GET /chats/{threadId}/nudge
    // POST /chats/{threadId}/nudge/dismiss
    // POST /chats/{threadId}/date-interest
    private static void MapNudgeAndDateEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/{threadId:guid}/nudge", async (
            Guid threadId,
            INudgeService nudges,
            HttpContext http,
            CancellationToken ct) =>
        {
            var me = GetUserId(http.User);
            var nudge = await nudges.GetConversationNudgeAsync(me, threadId, ct);
            return Results.Ok(new { nudge });
        });

        group.MapPost("/{threadId:guid}/nudge/dismiss", async (
            Guid threadId,
            ICacheService cache,
            IAnalyticsService analytics,
            HttpContext http,
            CancellationToken ct) =>
        {
            var me = GetUserId(http.User);
            var key = $"nudge:dismissed:{threadId}:{me}";
            await cache.SetAsync(key, "1", TimeSpan.FromHours(48), ct);
            _ = analytics.TrackAsync(me, null, AnalyticsEvents.NudgeDismissed, new { threadId });
            return Results.NoContent();
        });

        group.MapPost("/{threadId:guid}/date-interest", async (
            Guid threadId,
            WovenDbContext db,
            ICacheService cache,
            INotificationService notify,
            IAnalyticsService analytics,
            HttpContext http,
            CancellationToken ct) =>
        {
            var me = GetUserId(http.User);

            var thread = await db.ChatThreads.AsNoTracking().FirstOrDefaultAsync(t => t.Id == threadId, ct);
            if (thread == null) return Results.NotFound(new { error = "THREAD_NOT_FOUND" });

            var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == thread.MatchId, ct);
            if (match == null) return Results.NotFound(new { error = "MATCH_NOT_FOUND" });

            var isParticipant = match.UserAId == me || match.UserBId == me;
            if (!isParticipant) return Results.Forbid();

            if (match.BalloonState != BalloonState.ACTIVE)
                return Results.BadRequest(new { error = "BALLOON_NOT_ACTIVE" });

            var isUserA = match.UserAId == me;
            var otherUserId = isUserA ? match.UserBId : match.UserAId;

            if (isUserA)
                match.DateIdeaInterestedA = true;
            else
                match.DateIdeaInterestedB = true;

            var mutualInterest = match.DateIdeaInterestedA && match.DateIdeaInterestedB;
            if (mutualInterest)
                match.DateIdeaInterestedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);

            _ = analytics.TrackAsync(me, null, AnalyticsEvents.DateInterestExpressed, new { threadId });

            if (mutualInterest)
            {
                _ = analytics.TrackAsync(match.UserAId, null, AnalyticsEvents.DateInterestMutual, new { threadId });
                _ = analytics.TrackAsync(match.UserBId, null, AnalyticsEvents.DateInterestMutual, new { threadId });

                var msg = "You both want to meet up! Check out some nearby spots 🗺️";
                await Task.WhenAll(
                    notify.SendPushAsync(match.UserAId, msg, ct),
                    notify.SendPushAsync(match.UserBId, msg, ct));

                return Results.Ok(new { mutualInterest = true });
            }

            // Notify the other user once (dedup via Redis)
            var notifyKey = $"dateinterest:notified:{match.Id}";
            var alreadyNotified = await cache.GetAsync<string>(notifyKey, ct);
            if (alreadyNotified == null)
            {
                var senderName = await db.Users.AsNoTracking()
                    .Where(u => u.Id == me)
                    .Select(u => u.FullName)
                    .FirstOrDefaultAsync(ct);

                var firstName = senderName?.Split(' ')[0] ?? "Someone";
                await notify.SendPushAsync(otherUserId,
                    $"{firstName} is interested in the date idea 👀", ct);

                await cache.SetAsync(notifyKey, "1", TimeSpan.FromDays(7), ct);
            }

            return Results.Ok(new { mutualInterest = false });
        });

        // GET /chats/{threadId}/venue-suggestions
        group.MapGet("/{threadId:guid}/venue-suggestions", async (
            Guid threadId,
            WovenDbContext db,
            IVenueService venues,
            IAnalyticsService analytics,
            HttpContext http,
            CancellationToken ct) =>
        {
            var me = GetUserId(http.User);

            var thread = await db.ChatThreads.AsNoTracking().FirstOrDefaultAsync(t => t.Id == threadId, ct);
            if (thread == null) return Results.NotFound(new { error = "THREAD_NOT_FOUND" });

            var match = await db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == thread.MatchId, ct);
            if (match == null) return Results.NotFound(new { error = "MATCH_NOT_FOUND" });

            var isParticipant = match.UserAId == me || match.UserBId == me;
            if (!isParticipant) return Results.Forbid();

            if (!match.DateIdeaInterestedA || !match.DateIdeaInterestedB)
                return Results.Json(new { error = "MUTUAL_INTEREST_REQUIRED" }, statusCode: 403);

            var partnerId = match.UserAId == me ? match.UserBId : match.UserAId;
            var suggestions = await venues.GetVenueSuggestionsAsync(me, partnerId, ct);
            _ = analytics.TrackAsync(me, null, AnalyticsEvents.VenueSuggestionsViewed,
                new { threadId, venueCount = suggestions.Count });
            return Results.Ok(new { venues = suggestions });
        });

        // POST /chats/{threadId}/availability
        group.MapPost("/{threadId:guid}/availability", async (
            Guid threadId,
            AvailabilitySignalRequest req,
            WovenDbContext db,
            INotificationService notify,
            HttpContext http,
            CancellationToken ct) =>
        {
            var me = GetUserId(http.User);

            if (string.IsNullOrWhiteSpace(req.SignalText))
                return Results.BadRequest(new { error = "SIGNAL_TEXT_REQUIRED" });
            if (req.SignalText.Length > 200)
                return Results.BadRequest(new { error = "SIGNAL_TEXT_TOO_LONG" });

            var thread = await db.ChatThreads.AsNoTracking().FirstOrDefaultAsync(t => t.Id == threadId, ct);
            if (thread == null) return Results.NotFound(new { error = "THREAD_NOT_FOUND" });

            var match = await db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == thread.MatchId, ct);
            if (match == null) return Results.NotFound(new { error = "MATCH_NOT_FOUND" });

            var isParticipant = match.UserAId == me || match.UserBId == me;
            if (!isParticipant) return Results.Forbid();

            if (match.BalloonState != BalloonState.ACTIVE)
                return Results.BadRequest(new { error = "BALLOON_NOT_ACTIVE" });

            db.ChatAvailabilitySignals.Add(new ChatAvailabilitySignal
            {
                ThreadId = threadId,
                UserId = me,
                SignalText = req.SignalText.Trim(),
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(ct);

            var partnerId = match.UserAId == me ? match.UserBId : match.UserAId;
            var senderName = await db.Users.AsNoTracking()
                .Where(u => u.Id == me)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync(ct);
            var firstName = senderName?.Split(' ')[0] ?? "Someone";

            await notify.SendPushAsync(partnerId, $"{firstName} is free: {req.SignalText.Trim()}", ct);

            return Results.NoContent();
        });
    }

    private record AvailabilitySignalRequest(string SignalText);

    private static int GetUserId(ClaimsPrincipal user)
    {
        var uid = user.FindFirstValue("uid");
        if (int.TryParse(uid, out var id)) return id;

        var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(sub, out id)) return id;

        throw new UnauthorizedAccessException("Missing user id claim");
    }
}