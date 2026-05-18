namespace WovenBackend.Services.Embeddings;

public interface ILifestyleEmbeddingService
{
    /// <summary>
    /// Builds a 128-dim lifestyle embedding combining self-reported optional fields
    /// and behavioral signals. Skips users with fewer than 14 days of app history.
    /// </summary>
    Task ComputeLifestyleEmbeddingAsync(int userId, CancellationToken ct = default);
}
