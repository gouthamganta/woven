namespace WovenBackend.Services.Matchmaking;

public interface IMatchScoringService
{
    Task<List<MatchScore>> ScoreCandidatesAsync(int userId, List<int> candidateIds, CancellationToken ct = default);
}