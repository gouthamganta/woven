namespace WovenBackend.Services.Embeddings;

public interface IIntentEmbeddingService
{
    /// <summary>
    /// Generates a 1536-dim semantic embedding from the user's relationship intent metadata
    /// (seriousness, commitment readiness, flexibility, and intent tags) and stores it in
    /// UserVectors.IntentEmbedding. Skips users without complete intent metadata.
    /// </summary>
    Task ComputeIntentEmbeddingAsync(int userId, CancellationToken ct = default);
}
