namespace WovenBackend.Services.Embeddings;

public interface IVisualPreferenceService
{
    /// <summary>
    /// Aggregates YES and NO photo embedding decisions into preference and aversion
    /// embeddings via element-wise mean, then upserts user_visual_preference.
    /// Requires at least 10 YES decisions and 10 NO decisions to compute each vector.
    /// </summary>
    Task UpdateVisualPreferenceAsync(int userId, CancellationToken ct = default);
}
