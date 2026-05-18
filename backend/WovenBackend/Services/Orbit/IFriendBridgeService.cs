namespace WovenBackend.Services.Orbit;

public interface IFriendBridgeService
{
    Task AcceptBridgeAsync(int userId, Guid bridgeId, CancellationToken ct = default);
    Task DeclineBridgeAsync(int userId, Guid bridgeId, CancellationToken ct = default);
}
