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
    /// visual preference embedding. Falls back to the first photo (sortOrder 0)
    /// if there is insufficient preference data.
    /// </summary>
    Task<string?> GetBestPhotoUrlAsync(int viewerUserId, int candidateUserId, CancellationToken ct = default);

    /// <summary>
    /// Returns a ranked list of photo URLs personalized for the viewer.
    /// Scoring: visual similarity (70%) + photo quality (30%).
    /// Photos without embeddings are included with base quality score.
    /// Falls back to sortOrder when viewer has no preference data.
    /// Includes ALL user photos (even if embeddings are pending).
    /// </summary>
    Task<string[]> GetPersonalizedPhotosAsync(
        int viewerUserId,
        int candidateUserId,
        int count = 3,
        CancellationToken ct = default);
}
