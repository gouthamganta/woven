namespace WovenBackend.Services.Embeddings;

public interface IStyleEmbeddingService
{
    /// <summary>
    /// Extracts 128 writing-style features from the user's text corpus and stores
    /// the resulting vector(128) in UserVectors.StyleEmbedding.
    /// Skips users with fewer than 200 words of content.
    /// </summary>
    Task ComputeStyleEmbeddingAsync(int userId, CancellationToken ct = default);
}
