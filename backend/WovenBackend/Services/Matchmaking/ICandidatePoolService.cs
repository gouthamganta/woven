namespace WovenBackend.Services.Matchmaking;

public interface ICandidatePoolService
{
    Task<List<int>> GetEligibleCandidatesAsync(int userId, CancellationToken ct = default);
}