using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using WovenBackend.Services;
using WovenBackend.Services.Security;

namespace WovenBackend.Hubs;

[Authorize]
public class WovenHub : Hub
{
    private readonly ILogger<WovenHub> _logger;
    private readonly ISecurityAuditService _audit;
    private readonly byte[] _signingKey;

    public WovenHub(ILogger<WovenHub> logger, IEncryptionService enc, ISecurityAuditService audit)
    {
        _logger = logger;
        _audit = audit;
        _signingKey = Convert.FromBase64String(enc.DeriveKey("signing-v1"));
    }

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
        _logger.LogInformation("[Hub] User {UserId} connected ({ConnectionId})", userId, Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();
        _logger.LogInformation("[Hub] User {UserId} disconnected ({ConnectionId}) reason={Reason}",
            userId, Context.ConnectionId, exception?.Message ?? "clean");
        await base.OnDisconnectedAsync(exception);
    }

    // Clients call this to let the server verify a signed message they received from another client.
    public async Task VerifyMessageSignature(string payloadJson, string signature)
    {
        var expected = NotificationService.ComputeHmac(payloadJson, _signingKey);
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected),
                Convert.FromHexString(signature)))
        {
            var userId = GetUserId();
            _logger.LogWarning("[Hub] Invalid signature from user {UserId} on connection {ConnectionId}",
                userId, Context.ConnectionId);
            _audit.Log("suspicious_pattern", userId: userId,
                resourceType: "SignalR", resourceId: Context.ConnectionId);
            throw new HubException("Invalid message signature.");
        }
    }

    // Used by NotificationService to address a specific user across all their connections.
    public static string UserGroup(int userId) => $"user:{userId}";

    private int GetUserId()
    {
        // "uid" is an explicit custom claim in JwtTokenService.
        // Fall back to ClaimTypes.NameIdentifier which JWT bearer maps "sub" to by default.
        var user = Context.User ?? throw new InvalidOperationException("No authenticated user on hub connection");
        var raw  = user.FindFirstValue("uid")
                ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("No user ID claim on hub connection");
        return int.Parse(raw);
    }
}
