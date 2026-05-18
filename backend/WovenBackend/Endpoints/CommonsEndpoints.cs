using System.Security.Claims;
using WovenBackend.Services.Commons;

namespace WovenBackend.Endpoints;

public static class CommonsEndpoints
{
    private record ViewRequest(int? DurationMs);

    public static void MapCommonsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/commons").RequireAuthorization();

        // GET /commons?page=1&sessionId={uuid}
        group.MapGet("", async (
            int? page,
            Guid? sessionId,
            ICommonsFeedService feed,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);
            var pageNum = Math.Max(1, page ?? 1);
            var session = sessionId ?? Guid.NewGuid();

            var result = await feed.GetFeedAsync(userId, pageNum, session, ct);

            if (result.EnergyDepleted)
                return Results.Json(
                    new { error = "ENERGY_DEPLETED", message = "Daily browse limit reached. Resets at midnight UTC." },
                    statusCode: 429);

            return Results.Ok(new
            {
                page = pageNum,
                sessionId = session,
                count = result.Tiles.Count,
                tiles = result.Tiles
            });
        });

        // POST /commons/refresh
        group.MapPost("/refresh", async (
            ICommonsFeedService feed,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);
            await feed.RefreshFeedAsync(userId, ct);
            return Results.Ok(new { refreshed = true });
        });

        // POST /commons/{tileId}/view  body: { durationMs?: int }
        group.MapPost("/{tileId:guid}/view", async (
            Guid tileId,
            ViewRequest? req,
            ICommonsFeedService feed,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);
            await feed.RecordViewAsync(userId, tileId, req?.DurationMs, ct);
            return Results.Ok(new { recorded = true });
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
