using System.Threading.Channels;

namespace WovenBackend.Services.Embeddings;

public interface IPhotoEmbeddingQueue
{
    ValueTask QueuePhotoAsync(int userId, string photoUrl);
}

public class PhotoEmbeddingWorker : BackgroundService, IPhotoEmbeddingQueue
{
    private readonly Channel<(int UserId, string PhotoUrl)> _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PhotoEmbeddingWorker> _logger;

    public PhotoEmbeddingWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<PhotoEmbeddingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        // Unbounded channel for simplicity — production might want bounded with backpressure
        _queue = Channel.CreateUnbounded<(int, string)>(new UnboundedChannelOptions
        {
            SingleReader = true
        });
    }

    public ValueTask QueuePhotoAsync(int userId, string photoUrl)
    {
        _logger.LogInformation("[PhotoEmbeddingWorker] Queued photo for user {UserId}: {Url}",
            userId, photoUrl);
        return _queue.Writer.WriteAsync((userId, photoUrl));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[PhotoEmbeddingWorker] Started");

        await foreach (var (userId, photoUrl) in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                _logger.LogInformation("[PhotoEmbeddingWorker] Processing photo for user {UserId}", userId);

                using var scope = _scopeFactory.CreateScope();
                var photoEmbedding = scope.ServiceProvider.GetRequiredService<IPhotoEmbeddingService>();

                var embeddingId = await photoEmbedding.EmbedPhotoAsync(userId, photoUrl, stoppingToken);

                if (embeddingId.HasValue)
                {
                    _logger.LogInformation(
                        "[PhotoEmbeddingWorker] Successfully embedded photo | UserId={UserId} EmbeddingId={EmbeddingId}",
                        userId, embeddingId.Value);
                }
                else
                {
                    _logger.LogWarning(
                        "[PhotoEmbeddingWorker] Failed to embed photo for user {UserId}: {Url}",
                        userId, photoUrl);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[PhotoEmbeddingWorker] Error processing photo for user {UserId}: {Url}",
                    userId, photoUrl);
                // Don't rethrow — continue processing other photos
            }
        }

        _logger.LogInformation("[PhotoEmbeddingWorker] Stopped");
    }
}
