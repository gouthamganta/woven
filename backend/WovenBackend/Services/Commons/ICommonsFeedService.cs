namespace WovenBackend.Services.Commons;

public record CommonsFeedTile(
    Guid TileId,
    int OwnerId,
    string ContentType,
    string? ContentText,
    string? MediaUrl,
    DateTimeOffset CreatedAt,
    double Similarity
);

public record CommonsFeedResult(List<CommonsFeedTile> Tiles, bool EnergyDepleted);

public interface ICommonsFeedService
{
    Task<CommonsFeedResult> GetFeedAsync(int userId, int page, Guid sessionId, CancellationToken ct = default);
    Task RecordViewAsync(int userId, Guid tileId, int? durationMs, CancellationToken ct = default);
    Task RefreshFeedAsync(int userId, CancellationToken ct = default);
}
