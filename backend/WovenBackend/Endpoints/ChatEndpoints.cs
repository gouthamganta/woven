using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
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
    public record TrialDecisionRequest(string Decision, string? EndReason);
    public record VoiceMessageRequest(string AudioUrl, int DurationSecs);

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
                    lastActiveAt = u.LastActiveAt,
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
                    ? new { userId = u.userId, fullName = u.fullName, isVerified = u.isVerified, displayPronouns = u.displayPronouns, profilePhoto = u.profilePhoto, lastActiveAt = u.lastActiveAt }
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
            SparkWalletService sparks,
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

            // Trial open tracking: timer starts when BOTH users have opened the thread
            if (match.IsTrial)
            {
                var meIsUserA = match.UserAId == me;
                var changed = false;

                if (meIsUserA && match.TrialUserAOpenedAt == null)
                {
                    match.TrialUserAOpenedAt = now;
                    changed = true;
                }
                else if (!meIsUserA && match.TrialUserBOpenedAt == null)
                {
                    match.TrialUserBOpenedAt = now;
                    changed = true;
                }

                // Both have opened — start the 3-minute window
                if (changed && match.TrialEndsAt == null
                    && match.TrialUserAOpenedAt != null && match.TrialUserBOpenedAt != null)
                {
                    match.TrialEndsAt = now.AddMinutes(3);
                }

                if (changed)
                    await db.SaveChangesAsync(ct);
            }

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

                        // Ghost refund: no messages were exchanged — return 0.5 sparks to both
                        var uA = match.UserAId;
                        var uB = match.UserBId;
                        _ = Task.Run(async () =>
                        {
                            try { await sparks.GhostRefundAsync(uA); } catch { }
                            try { await sparks.GhostRefundAsync(uB); } catch { }
                        });
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

            // Load match explanation for date ideas (only if Find Love unlocked)
            string? dateIdea = null;
            List<string>? dateIdeas = null;
            if (showFindLove)
            {
                var explanation = await db.MatchExplanations.AsNoTracking()
                    .Where(e => (e.UserId == me && e.CandidateId == otherUserId) ||
                               (e.UserId == otherUserId && e.CandidateId == me))
                    .OrderByDescending(e => e.CreatedAt)
                    .FirstOrDefaultAsync(ct);

                dateIdeas = explanation?.GetDateIdeas();
                dateIdea = dateIdeas?.Count > 0 ? dateIdeas[0] : explanation?.DateIdea;
            }

            // Load ChatNotes (opening notes both users wrote when they chose each other)
            var chatNotes = await db.ChatNotes.AsNoTracking()
                .Where(n => n.MatchId == thread.MatchId)
                .OrderBy(n => n.CreatedAt)
                .Select(n => new
                {
                    fromUserId = n.FromUserId,
                    noteText = n.NoteText,
                    choice = n.Choice.ToString(),
                    createdAt = n.CreatedAt
                })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                meUserId = me,
                threadId = thread.Id,
                matchId = thread.MatchId,
                matchType = match.MatchType.ToString(),
                balloonState = match.BalloonState.ToString(),
                expiresAt = match.ExpiresAt,
                bothMessagedAt = match.BothMessagedAt,
                findLoveAt = match.FindLoveAt,
                showBalloonTimer,
                reflectionSecondsLeft,
                showFindLove,
                dateIdea,
                dateIdeas,
                chatNotes,
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
            ICacheService cache,
            IAnalyticsService analytics,
            INotificationService notify,
            IMatchSignalService signals,
            HttpContext http,
            CancellationToken ct) =>
        {
            var me = GetUserId(http.User);

            // Rate limit: 10 messages per minute per user
            var rateLimitKey = $"ratelimit:chat:message:{me}";
            var allowed = await cache.CheckRateLimitAsync(rateLimitKey, 10, TimeSpan.FromMinutes(1), ct);
            if (!allowed)
                return Results.Json(new { error = "RATE_LIMIT_EXCEEDED", retryAfter = 60 }, statusCode: 429);

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

            // Push real-time event to the other participant
            var otherUserId = match.UserAId == me ? match.UserBId : match.UserAId;

            try
            {
                await signals.RecordAsync(me, otherUserId, MatchSignalEventTypes.MessageSent, 1f, ct: ct);
                if (prevMsg != null)
                {
                    var latencyMs = (float)(now - prevMsg.CreatedAt).TotalMilliseconds;
                    await signals.RecordAsync(me, otherUserId, MatchSignalEventTypes.MessageResponseLatencyMs, latencyMs, ct: ct);
                }
                if (isFirstMessage)
                {
                    var timeToFirstMs = (float)(now - match.CreatedAt).TotalMilliseconds;
                    await signals.RecordAsync(me, otherUserId, MatchSignalEventTypes.TimeToFirstMessageMs, timeToFirstMs, ct: ct);
                }
            }
            catch { /* non-critical */ }
            _ = notify.NewChatMessageAsync(otherUserId, threadId, msg.Id, body, me, now, ct);

            // ✅ Set BothMessagedAt + FindLoveAt ONCE when both users have messaged
            if (match.BothMessagedAt == null)
            {

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
            SparkWalletService sparks,
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

            var noMessages = match.BothMessagedAt == null;

            match.BalloonState = BalloonState.CLOSED;
            match.ClosedReason = ClosedReason.UNMATCH;
            match.ClosedAt = now;
            await db.SaveChangesAsync(ct);

            await notify.MomentExpiredAsync(match.UserAId, match.UserBId, match.Id, ct);

            // Ghost refund: if nobody messaged, give both users 0.5 sparks back
            if (noMessages)
            {
                var uA = match.UserAId;
                var uB = match.UserBId;
                _ = Task.Run(async () =>
                {
                    try { await sparks.GhostRefundAsync(uA); } catch { }
                    try { await sparks.GhostRefundAsync(uB); } catch { }
                });
            }

            return Results.Ok(new { status = "CLOSED", closedAt = now });
        });

        MapNudgeAndDateEndpoints(group);

        // POST /chats/{threadId}/trial-decision
        group.MapPost("/{threadId:guid}/trial-decision", async (
            Guid threadId,
            TrialDecisionRequest req,
            WovenDbContext db,
            SparkWalletService sparks,
            IMatchSignalService signals,
            IIdempotencyService idempotency,
            HttpContext http,
            CancellationToken ct) =>
        {
            var me = GetUserId(http.User);
            var now = MomentsRules.NowUtc();

            // Check idempotency key
            var idempotencyKey = http.Request.Headers["X-Idempotency-Key"].ToString();
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                var cached = await idempotency.CheckAsync(idempotencyKey, me, $"/chats/{threadId}/trial-decision", ct);
                if (cached != null)
                {
                    return Results.Json(
                        System.Text.Json.JsonSerializer.Deserialize<object>(cached.ResponseBodyJson),
                        statusCode: cached.StatusCode);
                }
            }

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

            // Validate decision — CONTINUE, END, or BLOCK
            var decision = (req.Decision ?? "").Trim().ToUpperInvariant();
            if (decision != "CONTINUE" && decision != "END" && decision != "BLOCK")
                return Results.BadRequest(new { error = "INVALID_DECISION" });

            var isUserA = match.UserAId == me;
            var otherUserId = isUserA ? match.UserBId : match.UserAId;
            var endReason = (req.EndReason ?? "").Trim().ToLowerInvariant();

            // Store decision (BLOCK treated as END for mutual outcome)
            var storedDecision = decision == "BLOCK" ? "END" : decision;
            if (isUserA)
                match.UserADecision = storedDecision;
            else
                match.UserBDecision = storedDecision;

            // Store end reason on the match for ECHO
            if (decision != "CONTINUE" && !string.IsNullOrEmpty(endReason))
                match.TrialEndReason = endReason;

            await db.SaveChangesAsync(ct);

            // Count messages exchanged during the trial for ECHO signal
            var trialMessageCount = match.TrialStartedAt != null
                ? await db.ChatMessages.AsNoTracking()
                    .CountAsync(m => m.ThreadId == threadId && m.CreatedAt >= match.TrialStartedAt, ct)
                : 0;

            // Record trial signals
            try
            {
                var trialEventType = decision == "CONTINUE"
                    ? MatchSignalEventTypes.TrialAccepted
                    : MatchSignalEventTypes.TrialRejected;
                await signals.RecordAsync(me, otherUserId, trialEventType, decision == "CONTINUE" ? 1f : 0f, ct: ct);

                await signals.RecordAsync(me, otherUserId, MatchSignalEventTypes.TrialMessageCount, trialMessageCount, ct: ct);

                var reasonSignal = endReason switch
                {
                    "no_spark"     => MatchSignalEventTypes.TrialEndedNoSpark,
                    "wrong_timing" => MatchSignalEventTypes.TrialEndedWrongTiming,
                    "not_my_type"  => MatchSignalEventTypes.TrialEndedNotMyType,
                    _              => null
                };
                if (reasonSignal != null)
                    await signals.RecordAsync(me, otherUserId, reasonSignal, 1f, ct: ct);
            }
            catch { /* non-critical */ }

            // Handle BLOCK immediately — close + block, no waiting for other user
            if (decision == "BLOCK")
            {
                var alreadyBlocked = await db.Blocks.AnyAsync(b => b.BlockerId == me && b.BlockedId == otherUserId, ct);
                if (!alreadyBlocked)
                    db.Blocks.Add(new WovenBackend.data.Entities.Moments.Block { BlockerId = me, BlockedId = otherUserId, CreatedAt = now });

                match.BalloonState = BalloonState.CLOSED;
                match.ClosedReason = ClosedReason.BLOCK;
                match.ClosedAt = now;
                match.IsTrial = false;
                await db.SaveChangesAsync(ct);

                var blockResponse = new { status = "MATCH_BLOCKED", closedAt = match.ClosedAt };
                if (!string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    _ = idempotency.StoreAsync(idempotencyKey, me, $"/chats/{threadId}/trial-decision", 200, blockResponse, ct);
                }
                return Results.Ok(blockResponse);
            }

            // Check if both decided
            if (!string.IsNullOrEmpty(match.UserADecision) && !string.IsNullOrEmpty(match.UserBDecision))
            {
                if (match.UserADecision == "CONTINUE" && match.UserBDecision == "CONTINUE")
                {
                    match.IsTrial = false;
                    match.FindLoveAt = now;
                    await db.SaveChangesAsync(ct);

                    var continueResponse = new { status = "MATCH_CONTINUES", findLoveAt = match.FindLoveAt };
                    if (!string.IsNullOrWhiteSpace(idempotencyKey))
                    {
                        _ = idempotency.StoreAsync(idempotencyKey, me, $"/chats/{threadId}/trial-decision", 200, continueResponse, ct);
                    }
                    return Results.Ok(continueResponse);
                }
                else
                {
                    var noMessages = match.BothMessagedAt == null;

                    match.BalloonState = BalloonState.CLOSED;
                    match.ClosedReason = ClosedReason.UNMATCH;
                    match.ClosedAt = now;
                    match.IsTrial = false;
                    await db.SaveChangesAsync(ct);

                    if (noMessages)
                    {
                        var uA = match.UserAId;
                        var uB = match.UserBId;
                        _ = Task.Run(async () =>
                        {
                            try { await sparks.GhostRefundAsync(uA); } catch { }
                            try { await sparks.GhostRefundAsync(uB); } catch { }
                        });
                    }

                    var endResponse = new { status = "MATCH_ENDED", closedAt = match.ClosedAt };
                    if (!string.IsNullOrWhiteSpace(idempotencyKey))
                    {
                        _ = idempotency.StoreAsync(idempotencyKey, me, $"/chats/{threadId}/trial-decision", 200, endResponse, ct);
                    }
                    return Results.Ok(endResponse);
                }
            }

            var waitingResponse = new { status = "DECISION_RECORDED", waitingForOther = true };
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                _ = idempotency.StoreAsync(idempotencyKey, me, $"/chats/{threadId}/trial-decision", 200, waitingResponse, ct);
            }
            return Results.Ok(waitingResponse);
        });

        // POST /chats/{threadId}/voice-message
        // Client uploads audio to blob storage first, then calls this with the confirmed URL.
        group.MapPost("/{threadId:guid}/voice-message", async (
            Guid threadId,
            VoiceMessageRequest req,
            WovenDbContext db,
            ICacheService cache,
            INotificationService notify,
            IMatchSignalService signals,
            HttpContext http,
            CancellationToken ct) =>
        {
            var me = GetUserId(http.User);

            // Rate limit: 10 voice messages per minute per user (same as text)
            var rateLimitKey = $"ratelimit:chat:voice:{me}";
            var allowed = await cache.CheckRateLimitAsync(rateLimitKey, 10, TimeSpan.FromMinutes(1), ct);
            if (!allowed)
                return Results.Json(new { error = "RATE_LIMIT_EXCEEDED", retryAfter = 60 }, statusCode: 429);

            if (string.IsNullOrWhiteSpace(req.AudioUrl))
                return Results.BadRequest(new { error = "AUDIO_URL_REQUIRED" });

            if (req.DurationSecs <= 0 || req.DurationSecs > 180)
                return Results.BadRequest(new { error = "INVALID_DURATION" });

            var thread = await db.ChatThreads.FirstOrDefaultAsync(t => t.Id == threadId, ct);
            if (thread == null) return Results.NotFound(new { error = "THREAD_NOT_FOUND" });

            var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == thread.MatchId, ct);
            if (match == null) return Results.NotFound(new { error = "MATCH_NOT_FOUND" });

            var isParticipant = match.UserAId == me || match.UserBId == me;
            if (!isParticipant) return Results.Forbid();

            if (match.BalloonState != BalloonState.ACTIVE)
                return Results.BadRequest(new { error = "BALLOON_NOT_ACTIVE" });

            var now = MomentsRules.NowUtc();
            var otherUserId = match.UserAId == me ? match.UserBId : match.UserAId;

            var metaJson = JsonSerializer.Serialize(new
            {
                audioUrl    = req.AudioUrl,
                durationSecs = req.DurationSecs
            });

            var msg = new ChatMessage
            {
                ThreadId    = threadId,
                SenderUserId = me,
                Body        = "",
                MessageType = "VOICE",
                MetaJson    = metaJson,
                CreatedAt   = now
            };

            db.ChatMessages.Add(msg);
            thread.UpdatedAt = now;
            thread.LastMessageAt = now;
            thread.MessageCount++;
            await db.SaveChangesAsync(ct);

            // Record TimeToFirstVoiceNote signal (once per thread per sender)
            try
            {
                var isFirstVoice = await db.ChatMessages.AsNoTracking()
                    .CountAsync(m => m.ThreadId == threadId
                                  && m.SenderUserId == me
                                  && m.MessageType == "VOICE", ct) == 1;

                if (isFirstVoice)
                {
                    var elapsedMs = (float)(now - match.CreatedAt).TotalMilliseconds;
                    await signals.RecordAsync(me, otherUserId,
                        MatchSignalEventTypes.TimeToFirstMessageMs, elapsedMs, ct: ct);
                }
            }
            catch { /* non-critical */ }

            _ = notify.NewChatMessageAsync(otherUserId, threadId, msg.Id, "🎤 Voice note", me, now, ct);

            return Results.Ok(new
            {
                status    = "SENT",
                messageId = msg.Id,
                createdAt = msg.CreatedAt
            });
        });

        // POST /chats/{threadId}/messages/{messageId}/voice-listened
        // Called when the recipient plays a voice note to completion.
        group.MapPost("/{threadId:guid}/messages/{messageId:guid}/voice-listened", async (
            Guid threadId,
            Guid messageId,
            WovenDbContext db,
            IMatchSignalService signals,
            HttpContext http,
            CancellationToken ct) =>
        {
            var me = GetUserId(http.User);

            var thread = await db.ChatThreads.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == threadId, ct);
            if (thread == null) return Results.NotFound(new { error = "THREAD_NOT_FOUND" });

            var match = await db.Matches.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == thread.MatchId, ct);
            if (match == null) return Results.NotFound(new { error = "MATCH_NOT_FOUND" });

            var isParticipant = match.UserAId == me || match.UserBId == me;
            if (!isParticipant) return Results.Forbid();

            var message = await db.ChatMessages.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == messageId && m.ThreadId == threadId
                                       && m.MessageType == "VOICE", ct);
            if (message == null) return Results.NotFound(new { error = "MESSAGE_NOT_FOUND" });

            // Cannot credit listening to your own note
            if (message.SenderUserId == me)
                return Results.BadRequest(new { error = "CANNOT_LISTEN_OWN_NOTE" });

            var senderUserId = message.SenderUserId;

            try
            {
                // Record listen completion
                await signals.RecordAsync(me, senderUserId,
                    MatchSignalEventTypes.VoiceNoteListenComplete, 1f, ct: ct);

                // Check for mutual voice exchange — both users have sent a voice note in this thread
                var senderHasVoice = await db.ChatMessages.AsNoTracking()
                    .AnyAsync(m => m.ThreadId == threadId && m.SenderUserId == senderUserId
                                && m.MessageType == "VOICE", ct);
                var listenerHasVoice = await db.ChatMessages.AsNoTracking()
                    .AnyAsync(m => m.ThreadId == threadId && m.SenderUserId == me
                                && m.MessageType == "VOICE", ct);

                if (senderHasVoice && listenerHasVoice)
                {
                    await signals.RecordAsync(me, senderUserId,
                        MatchSignalEventTypes.MutualVoiceExchange, 1f, ct: ct);
                }
            }
            catch { /* non-critical */ }

            return Results.Ok(new { status = "RECORDED" });
        });

        // POST /chatnotes/{noteId}/love
        // Lets a match participant love-react to the other user's ChatNote.
        // One love per (user, note). Fires ChatNoteLove signal for ECHO.
        group.MapPost("/chatnotes/{noteId:guid}/love", async (
            Guid noteId,
            WovenDbContext db,
            IMatchSignalService signals,
            HttpContext http,
            CancellationToken ct) =>
        {
            var me = GetUserId(http.User);

            var note = await db.ChatNotes.AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == noteId, ct);

            if (note == null)
                return Results.NotFound(new { error = "NOTE_NOT_FOUND" });

            // Cannot love your own note
            if (note.FromUserId == me)
                return Results.BadRequest(new { error = "CANNOT_LOVE_OWN_NOTE" });

            // Must be in an active match together
            if (note.MatchId == null)
                return Results.BadRequest(new { error = "NOTE_NOT_LINKED_TO_MATCH" });

            var match = await db.Matches.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == note.MatchId && m.BalloonState == BalloonState.ACTIVE, ct);

            if (match == null)
                return Results.BadRequest(new { error = "MATCH_NOT_ACTIVE" });

            var isParticipant = match.UserAId == me || match.UserBId == me;
            if (!isParticipant) return Results.Forbid();

            // Idempotent: already loved?
            var alreadyLoved = await db.ChatNoteLoveReactions
                .AnyAsync(r => r.FromUserId == me && r.NoteId == noteId, ct);

            if (alreadyLoved)
                return Results.Ok(new { status = "ALREADY_LOVED" });

            db.ChatNoteLoveReactions.Add(new WovenBackend.data.Entities.Moments.ChatNoteLoveReaction
            {
                NoteId = noteId,
                FromUserId = me,
                NoteAuthorUserId = note.FromUserId,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);

            try { await signals.RecordAsync(me, note.FromUserId, MatchSignalEventTypes.ChatNoteLove, 1f, ct: ct); }
            catch { /* non-critical */ }

            return Results.Ok(new { status = "LOVED" });
        });

        // POST /chats/{threadId}/messages/{messageId}/love
        // Love-react to a message. One love per (user, message). Fires MessageLove signal for ECHO.
        group.MapPost("/{threadId:guid}/messages/{messageId:guid}/love", async (
            Guid threadId,
            Guid messageId,
            WovenDbContext db,
            IMatchSignalService signals,
            HttpContext http,
            CancellationToken ct) =>
        {
            var me = GetUserId(http.User);

            var thread = await db.ChatThreads.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == threadId, ct);
            if (thread == null) return Results.NotFound(new { error = "THREAD_NOT_FOUND" });

            var match = await db.Matches.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == thread.MatchId && m.BalloonState == BalloonState.ACTIVE, ct);
            if (match == null) return Results.BadRequest(new { error = "MATCH_NOT_ACTIVE" });

            var isParticipant = match.UserAId == me || match.UserBId == me;
            if (!isParticipant) return Results.Forbid();

            var message = await db.ChatMessages.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == messageId && m.ThreadId == threadId, ct);
            if (message == null) return Results.NotFound(new { error = "MESSAGE_NOT_FOUND" });

            // Cannot love your own message
            if (message.SenderUserId == me)
                return Results.BadRequest(new { error = "CANNOT_LOVE_OWN_MESSAGE" });

            // Idempotent
            var alreadyLoved = await db.MessageLoveReactions
                .AnyAsync(r => r.FromUserId == me && r.MessageId == messageId, ct);
            if (alreadyLoved)
                return Results.Ok(new { status = "ALREADY_LOVED" });

            db.MessageLoveReactions.Add(new WovenBackend.data.Entities.Moments.MessageLoveReaction
            {
                MessageId = messageId,
                ThreadId = threadId,
                FromUserId = me,
                MessageAuthorUserId = message.SenderUserId,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);

            try { await signals.RecordAsync(me, message.SenderUserId, MatchSignalEventTypes.MessageLove, 1f, ct: ct); }
            catch { /* non-critical */ }

            return Results.Ok(new { status = "LOVED" });
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
            DateInterestRequest? req,
            WovenDbContext db,
            ICacheService cache,
            INotificationService notify,
            IAnalyticsService analytics,
            IMatchSignalService matchSignal,
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

            // Log DateIdeaAccepted signal with chosen idea metadata for ECHO feedback loop
            try
            {
                var metaJson = JsonSerializer.Serialize(new
                {
                    chosenIdea = req?.IdeaText,
                    ideaIndex = req?.IdeaIndex ?? 0
                });
                await matchSignal.RecordAsync(me, otherUserId, MatchSignalEventTypes.DateIdeaAccepted, 1f, metaJson, ct);
            }
            catch { /* non-critical */ }

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

    public class DateInterestRequest
    {
        public int IdeaIndex { get; set; } = 0;
        public string? IdeaText { get; set; }
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