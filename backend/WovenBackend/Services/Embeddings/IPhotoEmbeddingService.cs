namespace WovenBackend.Services.Embeddings;

public interface IPhotoEmbeddingService
{
    /// <summary>
    /// Fetches the photo at <paramref name="photoUrl"/>, strips EXIF metadata,
    /// calls Replicate CLIP to produce a 512-dim embedding, and persists it in photo_embeddings.
    /// </summary>
    Task<int?> EmbedPhotoAsync(int userId, string photoUrl, CancellationToken ct = default);
}
