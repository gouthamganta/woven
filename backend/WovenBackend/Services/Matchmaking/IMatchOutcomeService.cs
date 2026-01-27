namespace WovenBackend.Services.Matchmaking;

public interface IMatchOutcomeService
{
    Task RecordChatStartedAsync(Guid matchId, int userId, int candidateId, CancellationToken ct = default);
    Task RecordUnmatchAsync(Guid matchId, int userId, int candidateId, CancellationToken ct = default);
    Task RecordBlockAsync(int userId, int blockedId, CancellationToken ct = default);
    Task RecordExpiredAsync(Guid matchId, int userId, int candidateId, CancellationToken ct = default);
}