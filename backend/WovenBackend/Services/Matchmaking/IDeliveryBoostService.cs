namespace WovenBackend.Services.Matchmaking;

public interface IDeliveryBoostService
{
    Task<Dictionary<int, double>> GetBoostMapAsync(
        int viewerId,
        List<int> candidateIds,
        DateOnly dateUtc,
        CancellationToken ct = default);
}
