using System.Threading.Channels;
using WovenBackend.Services.Tiles;

namespace WovenBackend.Services.Queue;

// Dev fallback: unbounded in-process channel. Tasks survive request scope but not pod restarts.
public sealed class InMemoryEmbeddingQueue : IEmbeddingQueue
{
    internal readonly Channel<Guid> Channel = System.Threading.Channels.Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(2000)
        {
            FullMode    = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

    public ValueTask EnqueueAsync(Guid tileId, CancellationToken ct = default)
        => Channel.Writer.WriteAsync(tileId, ct);
}

// Paired consumer — inject InMemoryEmbeddingQueue directly (not IEmbeddingQueue) to access the channel.
public sealed class InMemoryEmbeddingWorker : BackgroundService
{
    private readonly InMemoryEmbeddingQueue _queue;
    private readonly TileEmbeddingService  _embeddings;
    private readonly ILogger<InMemoryEmbeddingWorker> _logger;

    public InMemoryEmbeddingWorker(
        InMemoryEmbeddingQueue queue,
        TileEmbeddingService embeddings,
        ILogger<InMemoryEmbeddingWorker> logger)
    {
        _queue      = queue;
        _embeddings = embeddings;
        _logger     = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var tileId in _queue.Channel.Reader.ReadAllAsync(ct))
        {
            try
            {
                await _embeddings.EmbedTileAsync(tileId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EmbeddingQueue] Failed to embed tile {TileId}", tileId);
            }
        }
    }
}
