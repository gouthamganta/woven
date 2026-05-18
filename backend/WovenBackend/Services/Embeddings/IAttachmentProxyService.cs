namespace WovenBackend.Services.Embeddings;

public interface IAttachmentProxyService
{
    /// <summary>
    /// Derives a 4-dim attachment proxy embedding from chat thread behaviour.
    /// Requires at least 5 threads with >=10 messages each.
    /// This embedding is NEVER exposed via any API endpoint.
    /// </summary>
    Task ComputeAttachmentProxyAsync(int userId, CancellationToken ct = default);
}
