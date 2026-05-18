using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Services.Analytics;
using WovenBackend.Services.Security;

namespace WovenBackend.Services.Insights;

public class InsightService : IInsightService
{
    private readonly WovenDbContext _db;
    private readonly IOpenAiResilientClient _ai;
    private readonly ICacheService _cache;
    private readonly ISecurityAuditService _audit;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAnalyticsService _analytics;
    private readonly ILogger<InsightService> _logger;

    private const int MaxInsights = 5;
    private const float ClusterThreshold = 0.80f;
    private const int MinClusterSize = 3;

    public InsightService(
        WovenDbContext db,
        IOpenAiResilientClient ai,
        ICacheService cache,
        ISecurityAuditService audit,
        IServiceScopeFactory scopeFactory,
        IAnalyticsService analytics,
        ILogger<InsightService> logger)
    {
        _db = db;
        _ai = ai;
        _cache = cache;
        _audit = audit;
        _scopeFactory = scopeFactory;
        _analytics = analytics;
        _logger = logger;
    }

    // ── ComputeInsightsAsync ─────────────────────────────────────────────────

    public async Task ComputeInsightsAsync(int userId, CancellationToken ct = default)
    {
        // Last 20 matches for this user
        var recentMatches = await _db.Matches.AsNoTracking()
            .Where(m => (m.UserAId == userId || m.UserBId == userId)
                        && m.BalloonState == BalloonState.CLOSED)
            .OrderByDescending(m => m.ClosedAt)
            .Take(20)
            .Select(m => new { m.Id, OtherUserId = m.UserAId == userId ? m.UserBId : m.UserAId })
            .ToListAsync(ct);

        if (recentMatches.Count == 0) return;

        var matchIds = recentMatches.Select(m => m.Id).ToList();
        var otherUserIds = recentMatches.Select(m => m.OtherUserId).ToList();

        // Message counts per match (via threads)
        var msgCounts = await (
            from t in _db.ChatThreads.AsNoTracking()
            where matchIds.Contains(t.MatchId)
            join msg in _db.ChatMessages.AsNoTracking() on t.Id equals msg.ThreadId into msgs
            select new { t.MatchId, Count = msgs.Count() }
        ).ToDictionaryAsync(x => x.MatchId, x => x.Count, ct);

        // Ratings given (rater = userId, match in the set)
        var ratings = await _db.UserRatings.AsNoTracking()
            .Where(r => r.RaterUserId == userId && r.MatchId != null && matchIds.Contains(r.MatchId!.Value))
            .ToDictionaryAsync(r => r.MatchId!.Value, r => r.RatingValue, ct);

        // Positive matches: message_count > 15 OR rating > 30
        var positiveMatches = recentMatches
            .Where(m =>
                (msgCounts.TryGetValue(m.Id, out var mc) && mc > 15) ||
                (ratings.TryGetValue(m.Id, out var rv) && rv > 30))
            .ToList();

        if (positiveMatches.Count < MinClusterSize) return;

        // Load PillarEmbeddings for other users in positive matches
        var positiveOtherIds = positiveMatches.Select(m => m.OtherUserId).Distinct().ToList();
        var embeddings = await _db.UserVectors.AsNoTracking()
            .Where(v => positiveOtherIds.Contains(v.UserId) && v.PillarEmbedding != null)
            .GroupBy(v => v.UserId)
            .Select(g => g.OrderByDescending(v => v.Version).First())
            .Select(v => new { v.UserId, v.PillarEmbedding, v.PillarScoresJson })
            .ToListAsync(ct);

        if (embeddings.Count < MinClusterSize) return;

        // Find cluster: greedy — pick first, count others with cosine_sim > 0.80
        var vecs = embeddings
            .Where(e => e.PillarEmbedding != null)
            .Select(e => new { e.UserId, Vec = e.PillarEmbedding!.ToArray(), e.PillarScoresJson })
            .ToList();

        if (vecs.Count < MinClusterSize) return;

        var anchor = vecs[0].Vec;
        var cluster = vecs.Where(v => CosineSim(anchor, v.Vec) >= ClusterThreshold).ToList();

        if (cluster.Count < MinClusterSize) return;

        // Build anonymized cluster description from average pillar scores
        var avgPillars = new Dictionary<string, double>();
        foreach (var v in cluster)
        {
            try
            {
                var scores = JsonSerializer.Deserialize<Dictionary<string, double>>(v.PillarScoresJson);
                if (scores == null) continue;
                foreach (var kv in scores)
                {
                    avgPillars.TryGetValue(kv.Key, out var cur);
                    avgPillars[kv.Key] = cur + kv.Value / cluster.Count;
                }
            }
            catch { /* skip malformed */ }
        }

        var topPillars = avgPillars
            .OrderByDescending(kv => kv.Value)
            .Take(3)
            .Select(kv => $"{kv.Key} ({kv.Value:F2})")
            .ToList();

        var description = PiiSanitizer.SanitizeForAi(
            $"This person has had {cluster.Count} strong connections with people scoring high in: {string.Join(", ", topPillars)}. " +
            $"These connections all showed deep resonance across shared values.");

        var prompt =
            "Act as a compassionate dating coach. Generate a short personal insight " +
            "(max 150 chars) about this person's connection patterns. Be warm, specific, " +
            "encouraging. Never mention specific people.\n\n" +
            $"Data: {description}";

        var insight = await _ai.ExecuteAsync("insight_generation", prompt, useJsonMode: false, ct);
        if (string.IsNullOrWhiteSpace(insight)) return;

        insight = insight.Trim();
        if (insight.Length > 150) insight = insight[..150];

        // Upsert user_insights: append, keep last 5
        var row = await _db.UserInsights.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (row == null)
        {
            row = new UserInsight { UserId = userId };
            _db.UserInsights.Add(row);
        }

        var existing = JsonSerializer.Deserialize<List<string>>(row.InsightsJson) ?? new List<string>();
        existing.Add(insight);
        if (existing.Count > MaxInsights)
            existing = existing.TakeLast(MaxInsights).ToList();

        row.InsightsJson = JsonSerializer.Serialize(existing);
        row.ComputedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("[Insight] Computed insight for user {UserId}: {Insight}", userId, insight);
    }

    // ── ShouldAskOpinionAsync ────────────────────────────────────────────────

    public async Task<(bool ShouldAsk, string? Trigger, string? Prompt)> ShouldAskOpinionAsync(
        int userId, CancellationToken ct = default)
    {
        var insight = await _db.UserInsights.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);

        // 30-day cooldown on opinions
        if (insight?.OpinionSubmittedAt != null &&
            insight.OpinionSubmittedAt > DateTimeOffset.UtcNow.AddDays(-30))
            return (false, null, null);

        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.CreatedAt })
            .FirstOrDefaultAsync(ct);

        if (user == null) return (false, null, null);

        var now = DateTimeOffset.UtcNow;

        // TRIGGER 1: no_dates_yet
        if (user.CreatedAt < DateTime.UtcNow.AddDays(-30))
        {
            var hasDates = await _db.Matches.AsNoTracking()
                .AnyAsync(m => (m.UserAId == userId || m.UserBId == userId)
                               && m.DateAgreedAt != null, ct);
            if (!hasDates)
                return (true, "no_dates_yet",
                    "You've been on Woven for a month. How has it been feeling?");
        }

        // TRIGGER 2: pattern_shift
        var last10Matches = await _db.Matches.AsNoTracking()
            .Where(m => m.UserAId == userId || m.UserBId == userId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(10)
            .Select(m => new { OtherUserId = m.UserAId == userId ? m.UserBId : m.UserAId })
            .ToListAsync(ct);

        if (last10Matches.Count == 10)
        {
            var otherIds = last10Matches.Select(m => m.OtherUserId).ToList();
            var pillarVecs = await _db.UserVectors.AsNoTracking()
                .Where(v => otherIds.Contains(v.UserId) && v.PillarEmbedding != null)
                .GroupBy(v => v.UserId)
                .Select(g => g.OrderByDescending(v => v.Version).First())
                .Select(v => new { v.UserId, v.PillarEmbedding })
                .ToListAsync(ct);

            var orderedVecs = last10Matches
                .Select(m => pillarVecs.FirstOrDefault(v => v.UserId == m.OtherUserId))
                .Where(v => v?.PillarEmbedding != null)
                .Select(v => v!.PillarEmbedding!.ToArray())
                .ToList();

            if (orderedVecs.Count >= 8)
            {
                var first5 = orderedVecs.TakeLast(5).ToList();
                var last5 = orderedVecs.Take(5).ToList();
                var avgDist = AverageCosineDistance(first5, last5);
                if (avgDist > 0.30)
                    return (true, "pattern_shift",
                        "We noticed your connections have had a different energy lately. What's changed for you?");
            }
        }

        // TRIGGER 3: high_rejection
        var weekAgo = now.AddDays(-7);
        var rejectionCount = await _db.MomentResponses.AsNoTracking()
            .CountAsync(r => r.FromUserId == userId
                          && r.Choice == MomentChoice.NO
                          && r.CreatedAt >= weekAgo, ct);

        var recentlyActive = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.LastActiveAt >= weekAgo)
            .AnyAsync(ct);

        if (recentlyActive && rejectionCount >= 5)
            return (true, "high_rejection",
                "You seem to be looking for something specific. Want to tell us more about what feels right?");

        // TRIGGER 4: low_depth
        var monthAgo = now.AddDays(-30);
        var matchIds30d = await _db.Matches.AsNoTracking()
            .Where(m => (m.UserAId == userId || m.UserBId == userId)
                        && m.CreatedAt >= monthAgo)
            .Select(m => m.Id)
            .ToListAsync(ct);

        if (matchIds30d.Count > 0)
        {
            var lowDepthCount = 0;
            foreach (var matchId in matchIds30d)
            {
                var thread = await _db.ChatThreads.AsNoTracking()
                    .FirstOrDefaultAsync(t => t.MatchId == matchId, ct);
                if (thread == null) { lowDepthCount++; continue; }

                var msgCount = await _db.ChatMessages.AsNoTracking()
                    .CountAsync(m => m.ThreadId == thread.Id, ct);
                if (msgCount < 10) lowDepthCount++;
            }
            if (lowDepthCount >= 3)
                return (true, "low_depth",
                    "Connections have been starting but not quite clicking. What's been missing for you?");
        }

        return (false, null, null);
    }

    // ── SubmitOpinionAsync ───────────────────────────────────────────────────

    public async Task SubmitOpinionAsync(int userId, string text, string trigger, CancellationToken ct = default)
    {
        var sanitized = PiiSanitizer.SanitizeForAi(text);

        var row = await _db.UserInsights.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (row == null)
        {
            row = new UserInsight { UserId = userId };
            _db.UserInsights.Add(row);
        }

        row.OpinionText = sanitized[..Math.Min(sanitized.Length, 300)];
        row.OpinionTrigger = trigger[..Math.Min(trigger.Length, 50)];
        row.OpinionSubmittedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        _audit.Log("pii_access", userId: userId, service: "InsightService",
            resourceType: "opinion_text", piiStripped: true);

        _ = _analytics.TrackAsync(userId, null, AnalyticsEvents.OpinionSubmitted,
            new { trigger });

        // Fire-and-forget: extract keywords → blend into UserIntent
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();
                var ai = scope.ServiceProvider.GetRequiredService<IOpenAiResilientClient>();
                var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();

                var extractPrompt =
                    "Extract 3-5 preference keywords from this dating app user feedback. " +
                    "Return JSON array of strings only. Example: [\"values depth\",\"creative\"]\n\n" +
                    $"Feedback: {sanitized}";

                var raw = await ai.ExecuteAsync("opinion_keyword_extraction", extractPrompt,
                    useJsonMode: false, CancellationToken.None);

                if (string.IsNullOrWhiteSpace(raw)) return;

                // Parse JSON array (may be wrapped in json object from json_mode=false)
                raw = raw.Trim();
                int start = raw.IndexOf('[');
                int end = raw.LastIndexOf(']');
                if (start < 0 || end < 0) return;

                var keywords = JsonSerializer.Deserialize<List<string>>(raw[start..(end + 1)]);
                if (keywords == null || keywords.Count == 0) return;

                var intent = await db.UserIntents.FirstOrDefaultAsync(x => x.UserId == userId);
                if (intent == null) return;

                var existing = intent.ReflectionSentence ?? "";
                foreach (var kw in keywords.Take(2))
                {
                    if (!existing.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    {
                        var candidate = existing.Length == 0 ? kw : $"{existing}, {kw}";
                        if (candidate.Length <= 300)
                            existing = candidate;
                    }
                }

                intent.ReflectionSentence = existing;
                intent.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();

                // Invalidate pillar embedding cache
                await cache.DeleteAsync(CacheKeys.PillarEmbedding(userId));

                _logger.LogInformation("[Insight] Opinion keywords blended for user {UserId}", userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Insight] Opinion keyword blending failed for user {UserId}", userId);
            }
        });
    }

    // ── DeliverInsightAtMomentAsync ──────────────────────────────────────────

    public async Task<InsightDeliveryDto?> DeliverInsightAtMomentAsync(
        int userId, string moment, CancellationToken ct = default)
    {
        var row = await _db.UserInsights.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);

        var insights = JsonSerializer.Deserialize<List<string>>(row?.InsightsJson ?? "[]") ?? new List<string>();
        if (insights.Count == 0) return null;

        var latestInsight = insights.Last();
        var (shouldAsk, _, prompt) = await ShouldAskOpinionAsync(userId, ct);

        return new InsightDeliveryDto(latestInsight, shouldAsk, prompt);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static float CosineSim(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0f;
        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
            normA += (double)a[i] * a[i];
            normB += (double)b[i] * b[i];
        }
        var denom = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denom < 1e-10 ? 0f : (float)(dot / denom);
    }

    private static double AverageCosineDistance(List<float[]> groupA, List<float[]> groupB)
    {
        if (groupA.Count == 0 || groupB.Count == 0) return 0;
        double total = 0;
        int count = 0;
        foreach (var a in groupA)
        {
            foreach (var b in groupB)
            {
                total += 1.0 - CosineSim(a, b);
                count++;
            }
        }
        return count > 0 ? total / count : 0;
    }
}
