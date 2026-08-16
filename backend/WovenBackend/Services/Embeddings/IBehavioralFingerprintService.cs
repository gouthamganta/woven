namespace WovenBackend.Services.Embeddings;

public interface IBehavioralFingerprintService
{
    /// <summary>
    /// Computes and upserts the 16-dim behavioural fingerprint for <paramref name="userId"/>
    /// from the last 180 days of MatchSignalLog events.
    /// The fingerprint is stored in UserBehavioralFingerprints and consumed by the
    /// AttachmentProxyService (Step 12) and LinUCB bandit context (Phase 7).
    /// </summary>
    Task ComputeAsync(int userId, CancellationToken ct = default);

    Task<float[]?> GetAsync(int userId, CancellationToken ct = default);
}
