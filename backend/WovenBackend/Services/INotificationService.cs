namespace WovenBackend.Services;

public interface INotificationService
{
    Task DeckReadyAsync(int userId, DateOnly date, CancellationToken ct = default);
    Task MomentReceivedAsync(int recipientUserId, Guid matchId, int fromUserId, CancellationToken ct = default);
    Task MomentExpiredAsync(int userAId, int userBId, Guid matchId, CancellationToken ct = default);
    Task GameInviteReceivedAsync(int recipientUserId, Guid sessionId, Guid matchId, string gameType, DateTimeOffset expiresAt, CancellationToken ct = default);
    Task GameStartedAsync(int userAId, int userBId, Guid sessionId, Guid matchId, string gameType, CancellationToken ct = default);
    Task GameCompletedAsync(int userAId, int userBId, Guid sessionId, Guid matchId, string gameType, int? winnerUserId, CancellationToken ct = default);
    Task SendFriendBridgeProposalAsync(int userAId, int userBId, Guid bridgeId, CancellationToken ct = default);
    Task SendFriendBridgeActivatedAsync(int userAId, int userBId, Guid bridgeId, CancellationToken ct = default);
    Task SeasonResponseSubmittedAsync(int userId, CancellationToken ct = default);
    Task NewSeasonStartedAsync(int userId, int seasonNumber, string promptText, CancellationToken ct = default);
    // General-purpose in-app push (nudges, ghost refunds, digest messages)
    Task SendPushAsync(int userId, string message, CancellationToken ct = default);
}
