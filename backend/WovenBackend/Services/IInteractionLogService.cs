using System.Text.Json;
using WovenBackend.Data;

namespace WovenBackend.Services;

public interface IInteractionLogService
{
    Task LogAsync(int userId, string eventType, object? context = null, CancellationToken ct = default);
}

public class InteractionLogService : IInteractionLogService
{
    private readonly WovenDbContext _db;
    private readonly ILogger<InteractionLogService> _logger;

    public InteractionLogService(WovenDbContext db, ILogger<InteractionLogService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task LogAsync(int userId, string eventType, object? context = null, CancellationToken ct = default)
    {
        try
        {
            var contextJson = context != null
                ? JsonSerializer.Serialize(context, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
                : "{}";

            var log = new data.Entities.UserInteractionLog
            {
                UserId = userId,
                EventType = eventType,
                OccurredAt = DateTimeOffset.UtcNow,
                ContextJson = contextJson,
                CreatedAt = DateTimeOffset.UtcNow
            };

            _db.UserInteractionLogs.Add(log);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[InteractionLog] {EventType} | UserId={UserId}",
                eventType, userId);
        }
        catch (Exception ex)
        {
            // Non-critical — log but don't throw
            _logger.LogError(ex,
                "[InteractionLog] Failed to log event | EventType={EventType} UserId={UserId}",
                eventType, userId);
        }
    }
}
