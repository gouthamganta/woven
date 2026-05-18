using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Services.Security;

namespace WovenBackend.Services.Feedback;

public class FeedbackInsightService
{
    private readonly WovenDbContext _db;
    private readonly IOpenAiResilientClient _ai;
    private readonly ICacheService _cache;
    private readonly ISecurityAuditService _audit;
    private readonly ILogger<FeedbackInsightService> _logger;

    public FeedbackInsightService(
        WovenDbContext db,
        IOpenAiResilientClient ai,
        ICacheService cache,
        ISecurityAuditService audit,
        ILogger<FeedbackInsightService> logger)
    {
        _db = db;
        _ai = ai;
        _cache = cache;
        _audit = audit;
        _logger = logger;
    }

    public async Task ProcessNewFeedbackAsync(
        Guid matchId, int userId, int partnerId,
        DateFeedbackDto feedback, CancellationToken ct = default)
    {
        try
        {
            // High-quality signal: 5-star mutual date → reinforce pillar weight
            if (feedback.Stars == 5 && feedback.MeetAgain == "yes")
            {
                var existing = await _db.UserMatchingWeights
                    .FirstOrDefaultAsync(w => w.UserId == userId && w.Component == "pillar", ct);

                if (existing != null)
                {
                    existing.LearnedWeight = Math.Min(1.0f, existing.LearnedWeight + 0.05f);
                    existing.SampleCount++;
                }
                else
                {
                    _db.UserMatchingWeights.Add(new WovenBackend.Data.Entities.UserMatchingWeight
                    {
                        UserId = userId,
                        Component = "pillar",
                        LearnedWeight = 0.55f,
                        SampleCount = 1
                    });
                }
                await _db.SaveChangesAsync(ct);
            }

            // Low quality signal: 1-2 stars after in-person date → log explanation mismatch
            if (feedback.Stars <= 2 && feedback.MetInPerson)
            {
                var hasExplanation = await _db.MatchExplanations.AsNoTracking()
                    .AnyAsync(e => e.UserId == userId && e.CandidateId == partnerId, ct);

                if (hasExplanation)
                {
                    _audit.Log("suspicious_pattern", userId: userId,
                        service: "FeedbackInsightService",
                        resourceType: "explanation_quality_mismatch",
                        piiStripped: true);
                }
            }

            // Extract preference signals from free-text feedback
            var texts = new[] { feedback.FeltRightText, feedback.FeltOffText }
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();

            if (texts.Count == 0) return;

            var combined = string.Join(" ", texts);
            var sanitized = PiiSanitizer.SanitizeForAi(combined);

            var systemPrompt =
                "Extract 3 dating preference signals from this feedback. " +
                "Return JSON array of short strings only. No explanation. " +
                "Example: [\"values honesty\",\"enjoys humor\",\"wants depth\"]";

            var raw = await _ai.ExecuteAsync("feedback_insight", $"{systemPrompt}\n\n{sanitized}",
                useJsonMode: false, ct);

            if (string.IsNullOrWhiteSpace(raw)) return;

            List<string> keywords;
            try
            {
                keywords = JsonSerializer.Deserialize<List<string>>(raw) ?? new List<string>();
            }
            catch
            {
                return;
            }

            if (keywords.Count == 0) return;

            var intent = await _db.UserIntents
                .FirstOrDefaultAsync(i => i.UserId == userId, ct);

            if (intent == null) return;

            var current = intent.ReflectionSentence ?? string.Empty;
            var newKeywords = keywords
                .Where(k => !current.Contains(k, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (newKeywords.Count == 0) return;

            var appended = (current + " " + string.Join(", ", newKeywords)).Trim();
            if (appended.Length > 300)
                appended = appended[..300];

            intent.ReflectionSentence = appended;
            intent.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            await _cache.DeleteAsync(CacheKeys.PillarEmbedding(userId), ct);

            _logger.LogInformation("[FeedbackInsight] Updated reflection for user {UserId}", userId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[FeedbackInsight] Failed for user {UserId}, match {MatchId}", userId, matchId);
        }
    }
}
