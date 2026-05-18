using WovenBackend.Data.Entities;

namespace WovenBackend.Services.Moderation;

public enum ModerationImageResult { APPROVED, ESCALATED, AUTO_REJECTED }

public record ModerationQueueDto(
    Guid Id,
    Guid TileId,
    int UserId,
    string ContentType,
    string? ContentText,
    string? MediaUrl,
    DateTimeOffset QueuedAt);

public record TileReportDto(
    Guid Id,
    Guid TileId,
    int ReporterId,
    string Reason,
    DateTimeOffset ReportedAt);

public interface IModerationService
{
    Task<ModerationImageResult> ModerateImageAsync(int userId, string imageUrl, CancellationToken ct = default);
    Task EnqueueAsync(Guid tileId, int userId, CancellationToken ct = default);
    Task ProcessPendingAsync(CancellationToken ct = default);
    Task<bool> ApproveAsync(Guid queueItemId, int reviewerId, CancellationToken ct = default);
    Task<bool> RejectAsync(Guid queueItemId, int reviewerId, string reason, CancellationToken ct = default);
    Task<List<ModerationQueueDto>> GetPendingAsync(int limit = 50, CancellationToken ct = default);
    Task<List<TileReportDto>> GetReportsAsync(Guid tileId, CancellationToken ct = default);
}
