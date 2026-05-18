using System.Security.Claims;
using WovenBackend.Services.Tiles;

namespace WovenBackend.Endpoints;

public static class TileEndpoints
{
    private record HighlightRequest(int SlotNumber);

    public static void MapTileEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/tiles").RequireAuthorization();

        // POST /tiles
        group.MapPost("", async (
            CreateTileRequest req,
            ITileService tiles,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);
            var result = await tiles.CreateAsync(userId, req, ct);

            return result.Success
                ? Results.Created($"/tiles/{result.TileId}", new { tileId = result.TileId })
                : Results.BadRequest(new { error = result.Error });
        });

        // GET /tiles/mine
        group.MapGet("/mine", async (
            ITileService tiles,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId  = GetUserId(http.User);
            var myTiles = await tiles.GetMineAsync(userId, ct);
            return Results.Ok(new { count = myTiles.Count, tiles = myTiles });
        });

        // POST /tiles/{tileId}/highlight
        group.MapPost("/{tileId:guid}/highlight", async (
            Guid tileId,
            HighlightRequest req,
            ITileService tiles,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);
            var result = await tiles.HighlightAsync(userId, tileId, req.SlotNumber, ct);

            return result.Success
                ? Results.Ok(new { slot = req.SlotNumber })
                : Results.BadRequest(new { error = result.Error });
        });

        // DELETE /tiles/{tileId}/highlight
        group.MapDelete("/{tileId:guid}/highlight", async (
            Guid tileId,
            ITileService tiles,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);
            var ok = await tiles.UnhighlightAsync(userId, tileId, ct);
            return ok
                ? Results.NoContent()
                : Results.NotFound(new { error = "HIGHLIGHT_NOT_FOUND" });
        });

        // DELETE /tiles/{tileId}
        group.MapDelete("/{tileId:guid}", async (
            Guid tileId,
            ITileService tiles,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);
            var ok = await tiles.DeleteAsync(userId, tileId, ct);
            return ok
                ? Results.NoContent()
                : Results.BadRequest(new { error = "TILE_NOT_FOUND_OR_NOT_DELETABLE" });
        });
    }

    private static int GetUserId(ClaimsPrincipal user)
    {
        var uid = user.FindFirstValue("uid");
        if (int.TryParse(uid, out var id)) return id;
        var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(sub, out id)) return id;
        throw new UnauthorizedAccessException("Missing user id claim");
    }
}
