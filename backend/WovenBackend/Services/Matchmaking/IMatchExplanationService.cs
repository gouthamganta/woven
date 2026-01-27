using WovenBackend.Data.Entities;

namespace WovenBackend.Services.Matchmaking;

public interface IMatchExplanationService
{
    Task<int> GenerateAndSaveExplanationAsync(
        int userId,
        int candidateId,
        MatchScore score,
        MatchBucket bucket,
        DateOnly dateUtc,
        CancellationToken ct = default);
}