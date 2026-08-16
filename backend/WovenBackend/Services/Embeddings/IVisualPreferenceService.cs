namespace WovenBackend.Services.Embeddings;

public interface IVisualPreferenceService
{
    /// <summary>
    /// Aggregates YES and NO photo embedding decisions into preference and aversion
    /// embeddings via element-wise mean, then upserts user_visual_preference.
    /// Requires at least 10 YES decisions and 10 NO decisions to compute each vector.
    /// </summary>
    Task UpdateVisualPreferenceAsync(int userId, CancellationToken ct = default);

    /// <summary>
    /// Returns the URL of the candidate photo that best matches the viewer's
    /// visual preference embedding. Falls back to null if there is insufficient
    /// preference data (caller should then use sortOrder 0).
    /// </summary>
    Task<string?> GetBestPhotoUrlAsync(int viewerUserId, int candidateUserId, CancellationToken ct = default);
}
