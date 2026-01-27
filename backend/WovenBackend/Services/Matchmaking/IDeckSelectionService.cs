using WovenBackend.Data.Entities;

namespace WovenBackend.Services.Matchmaking;

public interface IDeckSelectionService
{
    // existing signature (keep for compatibility)
    List<(int CandidateId, MatchBucket Bucket)> SelectTop5(List<MatchScore> scores);

    // ✅ new signature (used by DailyDeckOrchestrator)
    List<(int CandidateId, MatchBucket Bucket)> SelectTop5(
        List<MatchScore> scores,
        Dictionary<int, double>? boostMap);
}