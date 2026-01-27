namespace WovenBackend.Services.Matchmaking;

public interface IUserVectorBuilder
{
    Task<int> BuildAndSaveV1Async(int userId, CancellationToken ct = default);
    Task UpdatePulseAsync(int userId, Dictionary<string, string> pulseAnswers, CancellationToken ct = default);
}