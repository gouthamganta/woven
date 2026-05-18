using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using WovenBackend.Hubs;
using WovenBackend.Services.Security;

namespace WovenBackend.Services;

public class NotificationService : INotificationService
{
    private readonly IHubContext<WovenHub> _hub;
    private readonly ISecurityAuditService _audit;
    private readonly ILogger<NotificationService> _logger;
    private readonly byte[] _signingKey;

    public NotificationService(
        IHubContext<WovenHub> hub,
        IEncryptionService enc,
        ISecurityAuditService audit,
        ILogger<NotificationService> logger)
    {
        _hub = hub;
        _audit = audit;
        _logger = logger;
        _signingKey = Convert.FromBase64String(enc.DeriveKey("signing-v1"));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private object Sign(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var sig = ComputeHmac(json, _signingKey);
        return new { payload, signature = sig };
    }

    internal static string ComputeHmac(string data, byte[] key)
    {
        var bytes = Encoding.UTF8.GetBytes(data);
        var hash = HMACSHA256.HashData(key, bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task Send(IClientProxy client, string method, object payload, CancellationToken ct)
    {
        await client.SendAsync(method, Sign(payload), ct);
    }

    // ── INotificationService implementation ──────────────────────────────────

    public async Task DeckReadyAsync(int userId, DateOnly date, CancellationToken ct = default)
    {
        try
        {
            await Send(_hub.Clients.Group(WovenHub.UserGroup(userId)), "DeckReady",
                new { date = date.ToString("yyyy-MM-dd") }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Notify] DeckReady failed for user {UserId}", userId);
        }
    }

    public async Task MomentReceivedAsync(int recipientUserId, Guid matchId, int fromUserId, CancellationToken ct = default)
    {
        try
        {
            await Send(_hub.Clients.Group(WovenHub.UserGroup(recipientUserId)), "MomentReceived",
                new { matchId, fromUserId }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Notify] MomentReceived failed for user {UserId}", recipientUserId);
        }
    }

    public async Task MomentExpiredAsync(int userAId, int userBId, Guid matchId, CancellationToken ct = default)
    {
        try
        {
            var envelope = Sign(new { matchId });
            await Task.WhenAll(
                _hub.Clients.Group(WovenHub.UserGroup(userAId)).SendAsync("MomentExpired", envelope, ct),
                _hub.Clients.Group(WovenHub.UserGroup(userBId)).SendAsync("MomentExpired", envelope, ct));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Notify] MomentExpired failed for match {MatchId}", matchId);
        }
    }

    public async Task GameInviteReceivedAsync(int recipientUserId, Guid sessionId, Guid matchId,
        string gameType, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        try
        {
            await Send(_hub.Clients.Group(WovenHub.UserGroup(recipientUserId)), "GameInviteReceived",
                new { sessionId, matchId, gameType, expiresAt }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Notify] GameInviteReceived failed for user {UserId}", recipientUserId);
        }
    }

    public async Task GameStartedAsync(int userAId, int userBId, Guid sessionId, Guid matchId,
        string gameType, CancellationToken ct = default)
    {
        try
        {
            var envelope = Sign(new { sessionId, matchId, gameType });
            await Task.WhenAll(
                _hub.Clients.Group(WovenHub.UserGroup(userAId)).SendAsync("GameStarted", envelope, ct),
                _hub.Clients.Group(WovenHub.UserGroup(userBId)).SendAsync("GameStarted", envelope, ct));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Notify] GameStarted failed for session {SessionId}", sessionId);
        }
    }

    public async Task GameCompletedAsync(int userAId, int userBId, Guid sessionId, Guid matchId,
        string gameType, int? winnerUserId, CancellationToken ct = default)
    {
        try
        {
            var envelope = Sign(new { sessionId, matchId, gameType, winnerUserId });
            await Task.WhenAll(
                _hub.Clients.Group(WovenHub.UserGroup(userAId)).SendAsync("GameCompleted", envelope, ct),
                _hub.Clients.Group(WovenHub.UserGroup(userBId)).SendAsync("GameCompleted", envelope, ct));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Notify] GameCompleted failed for session {SessionId}", sessionId);
        }
    }

    public async Task SendFriendBridgeProposalAsync(int userAId, int userBId, Guid bridgeId, CancellationToken ct = default)
    {
        try
        {
            var envelope = Sign(new { bridgeId });
            await Task.WhenAll(
                _hub.Clients.Group(WovenHub.UserGroup(userAId)).SendAsync("FriendBridgeProposal", envelope, ct),
                _hub.Clients.Group(WovenHub.UserGroup(userBId)).SendAsync("FriendBridgeProposal", envelope, ct));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Notify] FriendBridgeProposal failed for bridge {BridgeId}", bridgeId);
        }
    }

    public async Task SendFriendBridgeActivatedAsync(int userAId, int userBId, Guid bridgeId, CancellationToken ct = default)
    {
        try
        {
            var envelope = Sign(new { bridgeId });
            await Task.WhenAll(
                _hub.Clients.Group(WovenHub.UserGroup(userAId)).SendAsync("FriendBridgeActivated", envelope, ct),
                _hub.Clients.Group(WovenHub.UserGroup(userBId)).SendAsync("FriendBridgeActivated", envelope, ct));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Notify] FriendBridgeActivated failed for bridge {BridgeId}", bridgeId);
        }
    }

    public async Task SeasonResponseSubmittedAsync(int userId, CancellationToken ct = default)
    {
        try
        {
            await Send(_hub.Clients.Group(WovenHub.UserGroup(userId)), "SeasonResponseSubmitted",
                new { submitted = true }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Notify] SeasonResponseSubmitted failed for user {UserId}", userId);
        }
    }

    public async Task NewSeasonStartedAsync(int userId, int seasonNumber, string promptText, CancellationToken ct = default)
    {
        try
        {
            await Send(_hub.Clients.Group(WovenHub.UserGroup(userId)), "NewSeasonStarted",
                new { seasonNumber, promptText }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Notify] NewSeasonStarted failed for user {UserId}", userId);
        }
    }

    public async Task SendPushAsync(int userId, string message, CancellationToken ct = default)
    {
        try
        {
            await Send(_hub.Clients.Group(WovenHub.UserGroup(userId)), "Push",
                new { message }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Notify] SendPush failed for user {UserId}", userId);
        }
    }
}
