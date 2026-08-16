using Azure.Messaging.ServiceBus;
using WovenBackend.Services.Tiles;

namespace WovenBackend.Services.Queue;

public sealed class ServiceBusEmbeddingQueue : IEmbeddingQueue, IAsyncDisposable
{
    internal const string QueueName = "tile-embedding";

    private readonly ServiceBusSender _sender;

    public ServiceBusEmbeddingQueue(ServiceBusClient client)
        => _sender = client.CreateSender(QueueName);

    public async ValueTask EnqueueAsync(Guid tileId, CancellationToken ct = default)
    {
        var msg = new ServiceBusMessage(tileId.ToString())
        {
            MessageId             = tileId.ToString(),
            TimeToLive            = TimeSpan.FromDays(2),
        };
        await _sender.SendMessageAsync(msg, ct);
    }

    public ValueTask DisposeAsync() => _sender.DisposeAsync();
}

// Receives from Azure Service Bus and calls EmbedTileAsync.
public sealed class ServiceBusEmbeddingWorker : BackgroundService
{
    private readonly ServiceBusProcessor _processor;
    private readonly TileEmbeddingService _embeddings;
    private readonly ILogger<ServiceBusEmbeddingWorker> _logger;

    public ServiceBusEmbeddingWorker(
        ServiceBusClient client,
        TileEmbeddingService embeddings,
        ILogger<ServiceBusEmbeddingWorker> logger)
    {
        _embeddings = embeddings;
        _logger     = logger;
        _processor  = client.CreateProcessor(ServiceBusEmbeddingQueue.QueueName, new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls   = 4,
            AutoCompleteMessages = false
        });

        _processor.ProcessMessageAsync += OnMessageAsync;
        _processor.ProcessErrorAsync   += OnErrorAsync;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await _processor.StartProcessingAsync(ct);
        // Block until cancellation — processor runs on its own threads.
        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { }
        await _processor.StopProcessingAsync();
        await _processor.DisposeAsync();
    }

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        var body = args.Message.Body.ToString();
        if (!Guid.TryParse(body, out var tileId))
        {
            _logger.LogWarning("[ServiceBusEmbeddingWorker] Invalid tileId in message: {Body}", body);
            await args.DeadLetterMessageAsync(args.Message, "invalid-tile-id");
            return;
        }

        try
        {
            await _embeddings.EmbedTileAsync(tileId, args.CancellationToken);
            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ServiceBusEmbeddingWorker] Failed to embed tile {TileId}", tileId);
            // Abandon → Service Bus retries with backoff; DLQ after max delivery count.
            await args.AbandonMessageAsync(args.Message);
        }
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "[ServiceBusEmbeddingWorker] Processor error source={Source}", args.ErrorSource);
        return Task.CompletedTask;
    }
}
