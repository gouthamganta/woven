using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;

namespace WovenBackend.Services.Orbit;

public class FriendBridgeService : IFriendBridgeService
{
    private readonly WovenDbContext _db;
    private readonly INotificationService _notify;
    private readonly ILogger<FriendBridgeService> _logger;

    public FriendBridgeService(WovenDbContext db, INotificationService notify, ILogger<FriendBridgeService> logger)
    {
        _db = db;
        _notify = notify;
        _logger = logger;
    }

    public async Task AcceptBridgeAsync(int userId, Guid bridgeId, CancellationToken ct = default)
    {
        var bridge = await _db.FriendBridges
            .FirstOrDefaultAsync(b => b.Id == bridgeId, ct)
            ?? throw new InvalidOperationException("BRIDGE_NOT_FOUND");

        if (bridge.UserAId != userId && bridge.UserBId != userId)
            throw new InvalidOperationException("NOT_PARTICIPANT");

        if (bridge.Status == "active")
            throw new InvalidOperationException("ALREADY_ACTIVE");

        if (bridge.Status == "declined")
            throw new InvalidOperationException("BRIDGE_DECLINED");

        var isUserA = bridge.UserAId == userId;

        bridge.Status = bridge.Status switch
        {
            "pending_both" => isUserA ? "a_accepted" : "b_accepted",
            "a_accepted" when !isUserA => "active",
            "b_accepted" when isUserA => "active",
            _ => bridge.Status
        };

        if (bridge.Status == "active")
        {
            bridge.AcceptedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            _ = Task.Run(async () =>
            {
                try { await _notify.SendFriendBridgeActivatedAsync(bridge.UserAId, bridge.UserBId, bridgeId); }
                catch (Exception ex) { _logger.LogWarning(ex, "[Bridge] FriendBridgeActivated notification failed"); }
            });
        }
        else
        {
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task DeclineBridgeAsync(int userId, Guid bridgeId, CancellationToken ct = default)
    {
        var bridge = await _db.FriendBridges
            .FirstOrDefaultAsync(b => b.Id == bridgeId, ct)
            ?? throw new InvalidOperationException("BRIDGE_NOT_FOUND");

        if (bridge.UserAId != userId && bridge.UserBId != userId)
            throw new InvalidOperationException("NOT_PARTICIPANT");

        if (bridge.Status == "active")
            throw new InvalidOperationException("ALREADY_ACTIVE");

        bridge.Status = "declined";
        await _db.SaveChangesAsync(ct);
    }
}
