namespace WovenBackend.Services.Tiles;

public interface ITileService
{
    Task<CreateTileResult> CreateAsync(int userId, CreateTileRequest req, CancellationToken ct = default);
    Task<List<TileDto>> GetMineAsync(int userId, CancellationToken ct = default);
    Task<HighlightResult> HighlightAsync(int userId, Guid tileId, int slot, CancellationToken ct = default);
    Task<bool> UnhighlightAsync(int userId, Guid tileId, CancellationToken ct = default);
    Task<bool> DeleteAsync(int userId, Guid tileId, CancellationToken ct = default);
}

public record CreateTileRequest(
    string ContentType,
    string? ContentText,
    string? MediaUrl);

public record CreateTileResult(bool Success, Guid? TileId, string? Error);

public record TileDto(
    Guid Id,
    string ContentType,
    string? ContentText,
    string? MediaUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    bool IsExpired,
    bool IsHighlighted,
    bool IsModerated,
    int? HighlightSlot);

public record HighlightResult(bool Success, string? Error);
