namespace WovenBackend.Services.AntiGhosting;

public interface IGhostDetectionService
{
    Task ProcessSilentThreadsAsync(CancellationToken ct = default);
    Task ProcessExpiringBalloonsAsync(CancellationToken ct = default);
    Task UpdateGhostScoresAsync(CancellationToken ct = default);
}
