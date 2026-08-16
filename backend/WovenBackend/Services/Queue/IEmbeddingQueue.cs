namespace WovenBackend.Services.Queue;

public interface IEmbeddingQueue
{
    ValueTask EnqueueAsync(Guid tileId, CancellationToken ct = default);
}
