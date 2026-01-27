using WovenBackend.Data.Entities;

namespace WovenBackend.Services.Matchmaking;

public class DeckSelectionService : IDeckSelectionService
{
    private readonly ILogger<DeckSelectionService> _logger;

    public DeckSelectionService(ILogger<DeckSelectionService> logger)
    {
        _logger = logger;
    }

    // ✅ Backward compatible method required by the interface
    public List<(int CandidateId, MatchBucket Bucket)> SelectTop5(List<MatchScore> scores)
    {
        return SelectTop5(scores, null);
    }

    public List<(int CandidateId, MatchBucket Bucket)> SelectTop5(
        List<MatchScore> scores,
        Dictionary<int, double>? boostMap = null)
    {
        if (scores.Count == 0)
            return new List<(int CandidateId, MatchBucket Bucket)>();

        _logger.LogInformation("[DeckSelection] Selecting top 5 from {Count} scored candidates", scores.Count);

        // Use distribution-first approach to ensure bucket diversity
        var selection = SelectWithDiversity(scores, boostMap);

        _logger.LogInformation("[DeckSelection] Selected {Count} candidates: {Buckets}",
            selection.Count,
            string.Join(", ", selection.GroupBy(s => s.Bucket).Select(g => $"{g.Key}:{g.Count()}")));

        return selection.Take(5).ToList();
    }

    /// <summary>
    /// Distribution-first selection: ensures we get candidates from different buckets
    /// by using relative ranking instead of just absolute thresholds.
    /// Target: 2 CORE_FIT, 1 LIFESTYLE_FIT, 1 CONVERSATION_FIT, 1 EXPLORER
    /// </summary>
    private List<(int CandidateId, MatchBucket Bucket)> SelectWithDiversity(
        List<MatchScore> scores,
        Dictionary<int, double>? boostMap)
    {
        var selection = new List<(int CandidateId, MatchBucket Bucket)>();
        var selectedIds = new HashSet<int>();

        double GetBoostedScore(MatchScore s) =>
            s.TotalScore + (boostMap?.GetValueOrDefault(s.CandidateId) ?? 0);

        // Log score distribution for debugging
        _logger.LogDebug("[DeckSelection] Score ranges - Intent: {IntentMin:F1}-{IntentMax:F1}, " +
            "Foundational: {FoundMin:F1}-{FoundMax:F1}, Lifestyle: {LifeMin:F1}-{LifeMax:F1}, " +
            "Pulse: {PulseMin:F1}-{PulseMax:F1}",
            scores.Min(s => s.IntentScore), scores.Max(s => s.IntentScore),
            scores.Min(s => s.FoundationalScore), scores.Max(s => s.FoundationalScore),
            scores.Min(s => s.LifestyleScore), scores.Max(s => s.LifestyleScore),
            scores.Min(s => s.PulseScore), scores.Max(s => s.PulseScore));

        // 1. Pick top 2 by CORE_FIT score (Intent + Foundational combined)
        var coreFitCandidates = scores
            .Where(s => !selectedIds.Contains(s.CandidateId))
            .OrderByDescending(s => s.IntentScore + s.FoundationalScore + (boostMap?.GetValueOrDefault(s.CandidateId) ?? 0))
            .Take(2)
            .ToList();

        foreach (var candidate in coreFitCandidates)
        {
            selection.Add((candidate.CandidateId, MatchBucket.CORE_FIT));
            selectedIds.Add(candidate.CandidateId);
            _logger.LogDebug("[DeckSelection] CORE_FIT: Candidate {Id} (Intent={Intent:F1}, Found={Found:F1})",
                candidate.CandidateId, candidate.IntentScore, candidate.FoundationalScore);
        }

        // 2. Pick top 1 by LIFESTYLE_FIT score (not already selected)
        var lifestyleFitCandidate = scores
            .Where(s => !selectedIds.Contains(s.CandidateId))
            .OrderByDescending(s => s.LifestyleScore + (boostMap?.GetValueOrDefault(s.CandidateId) ?? 0) * 0.5)
            .FirstOrDefault();

        if (lifestyleFitCandidate != null)
        {
            selection.Add((lifestyleFitCandidate.CandidateId, MatchBucket.LIFESTYLE_FIT));
            selectedIds.Add(lifestyleFitCandidate.CandidateId);
            _logger.LogDebug("[DeckSelection] LIFESTYLE_FIT: Candidate {Id} (Lifestyle={Life:F1})",
                lifestyleFitCandidate.CandidateId, lifestyleFitCandidate.LifestyleScore);
        }

        // 3. Pick top 1 by CONVERSATION_FIT score (Pulse score, not already selected)
        var conversationFitCandidate = scores
            .Where(s => !selectedIds.Contains(s.CandidateId))
            .OrderByDescending(s => s.PulseScore + (boostMap?.GetValueOrDefault(s.CandidateId) ?? 0) * 0.5)
            .FirstOrDefault();

        if (conversationFitCandidate != null)
        {
            selection.Add((conversationFitCandidate.CandidateId, MatchBucket.CONVERSATION_FIT));
            selectedIds.Add(conversationFitCandidate.CandidateId);
            _logger.LogDebug("[DeckSelection] CONVERSATION_FIT: Candidate {Id} (Pulse={Pulse:F1})",
                conversationFitCandidate.CandidateId, conversationFitCandidate.PulseScore);
        }

        // 4. Pick top 1 as EXPLORER (someone different - lowest overlap with user's top traits)
        // Use inverse of foundational to find someone interestingly different
        var explorerCandidate = scores
            .Where(s => !selectedIds.Contains(s.CandidateId))
            .OrderByDescending(s => s.TotalScore - (s.FoundationalScore * 0.3) + (boostMap?.GetValueOrDefault(s.CandidateId) ?? 0) * 0.5)
            .FirstOrDefault();

        if (explorerCandidate != null)
        {
            selection.Add((explorerCandidate.CandidateId, MatchBucket.EXPLORER));
            selectedIds.Add(explorerCandidate.CandidateId);
            _logger.LogDebug("[DeckSelection] EXPLORER: Candidate {Id} (Total={Total:F1}, Found={Found:F1})",
                explorerCandidate.CandidateId, explorerCandidate.TotalScore, explorerCandidate.FoundationalScore);
        }

        // 5. Fill any remaining slots (if we have fewer than 5 candidates total)
        if (selection.Count < 5)
        {
            var remaining = scores
                .Where(s => !selectedIds.Contains(s.CandidateId))
                .OrderByDescending(s => GetBoostedScore(s))
                .Take(5 - selection.Count);

            foreach (var candidate in remaining)
            {
                var bucket = DetermineBucket(candidate);
                selection.Add((candidate.CandidateId, bucket));
                selectedIds.Add(candidate.CandidateId);
            }
        }

        return selection;
    }

    private List<(MatchScore Score, MatchBucket Bucket)> AssignBuckets(List<MatchScore> scores)
    {
        var result = new List<(MatchScore Score, MatchBucket Bucket)>();

        foreach (var score in scores)
        {
            var bucket = DetermineBucket(score);
            result.Add((score, bucket));
        }

        return result;
    }

    private MatchBucket DetermineBucket(MatchScore score)
    {
        // CORE_FIT: High intent + high foundational
        if (score.IntentScore >= 70 && score.FoundationalScore >= 65)
            return MatchBucket.CORE_FIT;

        // LIFESTYLE_FIT: High lifestyle score
        if (score.LifestyleScore >= 70)
            return MatchBucket.LIFESTYLE_FIT;

        // CONVERSATION_FIT: High pulse score
        if (score.PulseScore >= 70)
            return MatchBucket.CONVERSATION_FIT;

        // EXPLORER: Decent total but doesn't fit above
        if (score.TotalScore >= 60)
            return MatchBucket.EXPLORER;

        // WILDCARD: Lower scores but still eligible
        return MatchBucket.WILDCARD;
    }

    private List<(int CandidateId, MatchBucket Bucket)> PickFromBucket(
        List<(MatchScore Score, MatchBucket Bucket)> bucketedCandidates,
        MatchBucket bucket,
        int count,
        Dictionary<int, double>? boostMap = null)
    {
        return bucketedCandidates
            .Where(bc => bc.Bucket == bucket)
            .OrderByDescending(bc =>
                bc.Score.TotalScore + (boostMap?.GetValueOrDefault(bc.Score.CandidateId) ?? 0))
            .Take(count)
            .Select(bc => (bc.Score.CandidateId, bc.Bucket))
            .ToList();
    }
}