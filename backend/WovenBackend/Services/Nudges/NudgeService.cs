using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Data.Entities.Games;
using WovenBackend.Services.Analytics;

namespace WovenBackend.Services.Nudges;

public class NudgeService : INudgeService
{
    private readonly WovenDbContext _db;
    private readonly ICacheService _cache;
    private readonly IAnalyticsService _analytics;
    private readonly ILogger<NudgeService> _logger;

    public NudgeService(WovenDbContext db, ICacheService cache, IAnalyticsService analytics, ILogger<NudgeService> logger)
    {
        _db = db;
        _cache = cache;
        _analytics = analytics;
        _logger = logger;
    }

    public async Task<NudgeDto?> GetConversationNudgeAsync(int userId, Guid threadId, CancellationToken ct = default)
    {
        // Load thread + match in one join
        var threadMatch = await (
            from t in _db.ChatThreads.AsNoTracking()
            join m in _db.Matches.AsNoTracking() on t.MatchId equals m.Id
            where t.Id == threadId
            select new
            {
                t.Id,
                MatchId = m.Id,
                m.UserAId,
                m.UserBId,
                m.BalloonState,
                m.FindLoveAt,
                m.DateIdeaInterestedA,
                m.DateIdeaInterestedB
            }
        ).FirstOrDefaultAsync(ct);

        if (threadMatch == null) return null;
        if (threadMatch.BalloonState != BalloonState.ACTIVE) return null;

        var isParticipant = threadMatch.UserAId == userId || threadMatch.UserBId == userId;
        if (!isParticipant) return null;

        var otherUserId = threadMatch.UserAId == userId ? threadMatch.UserBId : threadMatch.UserAId;
        var now = DateTimeOffset.UtcNow;

        // Load message stats
        var msgStats = await _db.ChatMessages.AsNoTracking()
            .Where(m => m.ThreadId == threadId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                MessageCount = g.Count(),
                LastMessageAt = g.Max(m => m.CreatedAt)
            })
            .FirstOrDefaultAsync(ct);

        var messageCount = msgStats?.MessageCount ?? 0;
        var lastMessageAt = msgStats?.LastMessageAt;

        // Load both users' LastActiveAt
        var userActivity = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId || u.Id == otherUserId)
            .Select(u => new { u.Id, u.LastActiveAt })
            .ToDictionaryAsync(u => u.Id, u => u.LastActiveAt, ct);

        userActivity.TryGetValue(userId, out var myLastActive);
        userActivity.TryGetValue(otherUserId, out var otherLastActive);

        var bothActive = myLastActive != null && myLastActive > now.AddHours(-24)
                      && otherLastActive != null && otherLastActive > now.AddHours(-24);

        // Check game sessions for this match started by this user
        var gamesInitiated = await _db.GameSessions.AsNoTracking()
            .CountAsync(s => s.MatchId == threadMatch.MatchId && s.InitiatorUserId == userId, ct);

        // -- NUDGE 1: Game suggestion --
        var nudge1 = messageCount >= 3
            && messageCount < 15
            && gamesInitiated == 0
            && bothActive
            && lastMessageAt != null && lastMessageAt < now.AddHours(-6);

        // -- NUDGE 2: Date idea follow-up --
        var nudge2 = threadMatch.FindLoveAt != null
            && threadMatch.FindLoveAt < now.AddHours(-48)
            && !threadMatch.DateIdeaInterestedA
            && !threadMatch.DateIdeaInterestedB;

        if (!nudge1 && !nudge2) return null;

        // Check if user dismissed nudges for this thread
        var dismissedKey = $"nudge:dismissed:{threadId}:{userId}";
        var dismissed = await _cache.GetAsync<string>(dismissedKey, ct);
        if (dismissed != null) return null;

        // Dedup: only one nudge per thread per 24h
        var sentKey = $"nudge:sent:{threadId}";
        var alreadySent = await _cache.GetAsync<string>(sentKey, ct);
        if (alreadySent != null) return null;

        await _cache.SetAsync(sentKey, "1", TimeSpan.FromHours(24), ct);

        if (nudge1)
        {
            _logger.LogDebug("[Nudge] Game suggestion for thread {ThreadId} user {UserId}", threadId, userId);
            _ = _analytics.TrackAsync(userId, null, AnalyticsEvents.NudgeShown,
                new { nudgeType = "game_suggestion", threadId });
            return new NudgeDto(
                "game_suggestion",
                "You're both around — want to break the ice with a quick game? 🎮",
                "start_game");
        }

        _logger.LogDebug("[Nudge] Date follow-up for thread {ThreadId} user {UserId}", threadId, userId);
        _ = _analytics.TrackAsync(userId, null, AnalyticsEvents.NudgeShown,
            new { nudgeType = "date_followup", threadId });
        return new NudgeDto(
            "date_followup",
            "You've had a date idea for 2 days — has anything been planned? 😊",
            "show_interested_button");
    }
}
