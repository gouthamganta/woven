namespace WovenBackend.Services.Embeddings;

public interface IHumorEmbeddingService
{
    /// <summary>
    /// Builds a 64-dim humor embedding from game rounds, chat emoji, and RedGreenFlag choices.
    /// Skips users with fewer than 2 completed games.
    /// </summary>
    Task ComputeHumorEmbeddingAsync(int userId, CancellationToken ct = default);
}
