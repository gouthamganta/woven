namespace WovenBackend.Services.Trust;

public interface ITrustService
{
    Task CheckDeviceFingerprintAsync(int userId, string? fingerprint, CancellationToken ct = default);
    Task CheckVelocityAsync(int userId, CancellationToken ct = default);
    Task RunBotDetectionAsync(int userId, CancellationToken ct = default);
    Task FlagAsync(int userId, string reason, float penaltyScore, CancellationToken ct = default);
    Task<float> GetTrustScoreAsync(int userId, CancellationToken ct = default);
    Task<bool> IsTrustedEnoughAsync(int userId, CancellationToken ct = default);
}
