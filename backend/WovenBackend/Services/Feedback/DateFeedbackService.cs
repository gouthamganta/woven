using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Services.Analytics;
using WovenBackend.Services.Insights;
using WovenBackend.Services.Matchmaking;
using WovenBackend.Services.Security;
using WovenBackend.Services.Trust;
using WovenBackend.Services;

namespace WovenBackend.Services.Feedback;

public class DateFeedbackService : IDateFeedbackService
{
    private readonly WovenDbContext _db;
    private readonly INotificationService _notify;
    private readonly ITrustService _trust;
    private readonly ISecurityAuditService _audit;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAnalyticsService _analytics;
    private readonly ILogger<DateFeedbackService> _logger;

    public DateFeedbackService(
        WovenDbContext db,
        INotificationService notify,
        ITrustService trust,
        ISecurityAuditService audit,
        IServiceScopeFactory scopeFactory,
        IAnalyticsService analytics,
        ILogger<DateFeedbackService> logger)
    {
        _db = db;
        _notify = notify;
        _trust = trust;
        _audit = audit;
        _scopeFactory = scopeFactory;
        _analytics = analytics;
        _logger = logger;
    }

    public async Task QueueFeedbackPromptsAsync(CancellationToken ct = default)
    {
        var existingMatchIds = await _db.DateFeedbackPrompts.AsNoTracking()
            .Select(p => p.MatchId)
            .Distinct()
            .ToListAsync(ct);

        // Primary: mutual date interest, 5+ days ago
        var primaryCutoff = DateTimeOffset.UtcNow.AddDays(-5);
        var primaryMatches = await _db.Matches.AsNoTracking()
            .Where(m => m.DateIdeaInterestedA == true
                     && m.DateIdeaInterestedB == true
                     && m.DateIdeaInterestedAt != null
                     && m.DateIdeaInterestedAt < primaryCutoff
                     && m.ClosedReason != WovenBackend.data.Entities.Moments.ClosedReason.BLOCK
                     && !existingMatchIds.Contains(m.Id))
            .Select(m => new { m.Id, m.UserAId, m.UserBId })
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var match in primaryMatches)
        {
            _db.DateFeedbackPrompts.Add(new DateFeedbackPrompt
            {
                MatchId = match.Id, UserId = match.UserAId,
                TriggerType = "interested_both", ScheduledFor = now
            });
            _db.DateFeedbackPrompts.Add(new DateFeedbackPrompt
            {
                MatchId = match.Id, UserId = match.UserBId,
                TriggerType = "interested_both", ScheduledFor = now
            });
        }

        // Secondary: deep chat (25+ messages, fast response, find_love_at 10+ days ago)
        var deepChatCutoff = DateTimeOffset.UtcNow.AddDays(-10);
        var secondaryMatches = await (
            from m in _db.Matches.AsNoTracking()
            join t in _db.ChatThreads.AsNoTracking() on m.Id equals t.MatchId
            where t.MessageCount > 25
               && t.AvgResponseTimeMs < 7200000
               && m.FindLoveAt != null
               && m.FindLoveAt < deepChatCutoff
               && m.ClosedReason != WovenBackend.data.Entities.Moments.ClosedReason.BLOCK
               && !(m.ClosedReason == WovenBackend.data.Entities.Moments.ClosedReason.EXPIRE && t.MessageCount < 5)
               && !existingMatchIds.Contains(m.Id)
            select new { m.Id, m.UserAId, m.UserBId }
        ).ToListAsync(ct);

        foreach (var match in secondaryMatches)
        {
            _db.DateFeedbackPrompts.Add(new DateFeedbackPrompt
            {
                MatchId = match.Id, UserId = match.UserAId,
                TriggerType = "deep_chat", ScheduledFor = now
            });
            _db.DateFeedbackPrompts.Add(new DateFeedbackPrompt
            {
                MatchId = match.Id, UserId = match.UserBId,
                TriggerType = "deep_chat", ScheduledFor = now
            });
        }

        if (primaryMatches.Count > 0 || secondaryMatches.Count > 0)
            await _db.SaveChangesAsync(ct);

        _logger.LogInformation("[FeedbackQueue] Queued {P} primary + {S} secondary match prompts",
            primaryMatches.Count, secondaryMatches.Count);
    }

    public async Task SendDuePromptsAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var due = await _db.DateFeedbackPrompts
            .Where(p => p.ScheduledFor <= now && p.SentAt == null && p.RescheduleCount <= 2)
            .ToListAsync(ct);

        foreach (var prompt in due)
        {
            try
            {
                var match = await _db.Matches.AsNoTracking()
                    .FirstOrDefaultAsync(m => m.Id == prompt.MatchId, ct);
                if (match == null) continue;

                var partnerId = match.UserAId == prompt.UserId ? match.UserBId : match.UserAId;
                var partnerName = await _db.Users.AsNoTracking()
                    .Where(u => u.Id == partnerId)
                    .Select(u => u.FullName)
                    .FirstOrDefaultAsync(ct) ?? "your match";
                var firstName = partnerName.Split(' ')[0];

                await _notify.SendPushAsync(prompt.UserId,
                    $"How did it go with {firstName}? We'd love to know 💙", ct);

                prompt.SentAt = now;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[FeedbackSend] Failed for prompt {PromptId}", prompt.Id);
            }
        }

        if (due.Count > 0)
            await _db.SaveChangesAsync(ct);
    }

    public async Task ReschedulePendingAsync(CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-3);
        var now = DateTimeOffset.UtcNow;

        // Reschedule those with < 2 reschedules
        var toReschedule = await _db.DateFeedbackPrompts
            .Where(p => p.SentAt != null
                     && p.RespondedAt == null
                     && p.SentAt < cutoff
                     && p.RescheduleCount < 2)
            .ToListAsync(ct);

        foreach (var p in toReschedule)
        {
            p.RescheduleCount++;
            p.ScheduledFor = now.AddDays(5);
            p.SentAt = null;
        }

        // Give up on those exhausted (mark sent again so they expire)
        var toExpire = await _db.DateFeedbackPrompts
            .Where(p => p.SentAt != null
                     && p.RespondedAt == null
                     && p.RescheduleCount >= 2
                     && p.SentAt < cutoff)
            .ToListAsync(ct);

        foreach (var p in toExpire)
            p.SentAt = now;

        if (toReschedule.Count + toExpire.Count > 0)
            await _db.SaveChangesAsync(ct);
    }

    public async Task SubmitFeedbackAsync(int userId, Guid matchId, DateFeedbackDto dto, CancellationToken ct = default)
    {
        var prompt = await _db.DateFeedbackPrompts
            .FirstOrDefaultAsync(p => p.UserId == userId && p.MatchId == matchId, ct);
        if (prompt == null)
            throw new InvalidOperationException("NO_PROMPT_FOUND");

        var stars = dto.MetInPerson ? dto.Stars : null;

        var existing = await _db.DateFeedbacks
            .FirstOrDefaultAsync(f => f.UserId == userId && f.MatchId == matchId, ct);

        if (existing == null)
        {
            _db.DateFeedbacks.Add(new DateFeedback
            {
                MatchId = matchId,
                UserId = userId,
                MetInPerson = dto.MetInPerson,
                Stars = stars,
                FeltRightText = dto.FeltRightText,
                FeltOffText = dto.FeltOffText,
                MeetAgain = dto.MeetAgain,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.MetInPerson = dto.MetInPerson;
            existing.Stars = stars;
            existing.FeltRightText = dto.FeltRightText;
            existing.FeltOffText = dto.FeltOffText;
            existing.MeetAgain = dto.MeetAgain;
        }

        prompt.RespondedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        _audit.Log("pii_access", userId: userId, service: "DateFeedbackService",
            resourceType: "feedback_text", piiStripped: true);

        _ = _analytics.TrackAsync(userId, null, AnalyticsEvents.DateFeedbackSubmitted,
            new { metInPerson = dto.MetInPerson, stars = stars, meetAgain = dto.MeetAgain });

        var match2 = await _db.Matches.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == matchId, ct);
        var partnerId2 = match2 != null
            ? (match2.UserAId == userId ? match2.UserBId : match2.UserAId)
            : (int?)null;

        var capturedUserId = userId;
        var capturedPartnerId = partnerId2;
        var capturedStars = stars;
        var capturedDto = dto;
        var capturedMatchId = matchId;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var insightSvc = scope.ServiceProvider.GetRequiredService<IInsightService>();
                var feedbackInsight = scope.ServiceProvider.GetRequiredService<FeedbackInsightService>();

                await feedbackInsight.ProcessNewFeedbackAsync(
                    capturedMatchId, capturedUserId,
                    capturedPartnerId ?? capturedUserId, capturedDto);

                if (capturedDto.MetInPerson && capturedStars.HasValue)
                {
                    var weightSvc = scope.ServiceProvider.GetRequiredService<IWeightLearningService>();
                    await weightSvc.LearnWeightsAsync(capturedUserId);
                    await insightSvc.ComputeInsightsAsync(capturedUserId);
                    await insightSvc.DeliverInsightAtMomentAsync(capturedUserId, "date_feedback_submitted");

                    if (capturedPartnerId.HasValue)
                        await CheckBadRatingPatternAsync(capturedPartnerId.Value, capturedStars.Value, scope);
                }
            }
            catch { /* non-critical */ }
        });
    }

    private async Task CheckBadRatingPatternAsync(int ratedUserId, int stars, IServiceScope scope)
    {
        if (stars > 2) return;

        var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();
        var trust = scope.ServiceProvider.GetRequiredService<ITrustService>();
        var audit = scope.ServiceProvider.GetRequiredService<ISecurityAuditService>();

        var lowRaterCount = await (
            from f in db.DateFeedbacks.AsNoTracking()
            join m in db.Matches.AsNoTracking() on f.MatchId equals m.Id
            where (m.UserAId == ratedUserId || m.UserBId == ratedUserId)
               && f.UserId != ratedUserId
               && f.Stars <= 2
            select f.UserId
        ).Distinct().CountAsync();

        if (lowRaterCount >= 3)
        {
            await trust.FlagAsync(ratedUserId, "LOW_ENGAGEMENT", 0.6f);
            audit.Log("suspicious_pattern", userId: ratedUserId,
                service: "DateFeedbackService",
                resourceType: "repeated_low_ratings",
                piiStripped: true);
        }
    }
}
