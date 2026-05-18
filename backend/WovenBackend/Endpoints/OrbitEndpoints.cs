using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Services;
using WovenBackend.Services.Orbit;

namespace WovenBackend.Endpoints;

public static class OrbitEndpoints
{
    public static void MapOrbitEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/orbit").RequireAuthorization();

        // POST /orbit/{tileId}
        group.MapPost("/{tileId:guid}", async (
            Guid tileId,
            IOrbitService orbit,
            ICacheService cache,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);

            // Rate limit: 50 orbits per user per day
            var rlKey = $"rl:orbit:{userId}:{DateOnly.FromDateTime(DateTime.UtcNow)}";
            var allowed = await cache.CheckRateLimitAsync(rlKey, 50, CacheTtl.UntilMidnightUtc(), ct);
            if (!allowed)
            {
                http.Response.Headers["Retry-After"] = ((int)CacheTtl.UntilMidnightUtc().TotalSeconds).ToString();
                return Results.StatusCode(429);
            }

            try
            {
                var result = await orbit.OrbitTileAsync(userId, tileId, ct);
                return Results.Ok(new
                {
                    relationshipType = result.RelationshipType,
                    mutualDetected = result.MutualDetected
                });
            }
            catch (InvalidOperationException ex) when (
                ex.Message is "TILE_NOT_FOUND" or "CANNOT_ORBIT_OWN_TILE" or "ALREADY_ORBITED")
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // GET /orbit/bridges — list bridges I'm part of
        group.MapGet("/bridges", async (
            WovenDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);
            var bridges = await db.FriendBridges.AsNoTracking()
                .Where(b => b.UserAId == userId || b.UserBId == userId)
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => new
                {
                    b.Id,
                    b.UserAId,
                    b.UserBId,
                    b.Status,
                    b.CreatedAt,
                    b.AcceptedAt
                })
                .ToListAsync(ct);

            return Results.Ok(bridges);
        });

        // POST /orbit/bridges/{bridgeId}/accept
        group.MapPost("/bridges/{bridgeId:guid}/accept", async (
            Guid bridgeId,
            IFriendBridgeService bridgeService,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);
            try
            {
                await bridgeService.AcceptBridgeAsync(userId, bridgeId, ct);
                return Results.Ok(new { accepted = true });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // POST /orbit/bridges/{bridgeId}/decline
        group.MapPost("/bridges/{bridgeId:guid}/decline", async (
            Guid bridgeId,
            IFriendBridgeService bridgeService,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);
            try
            {
                await bridgeService.DeclineBridgeAsync(userId, bridgeId, ct);
                return Results.Ok(new { declined = true });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // GET /orbit/received — orbits placed on my tiles by others
        group.MapGet("/received", async (
            WovenDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);
            var orbits = await db.TileOrbits.AsNoTracking()
                .Where(o => o.TileOwnerId == userId)
                .OrderByDescending(o => o.OrbitedAt)
                .Take(50)
                .Select(o => new
                {
                    o.Id,
                    o.OrbiterId,
                    o.TileId,
                    o.RelationshipType,
                    o.OrbitedAt
                })
                .ToListAsync(ct);

            return Results.Ok(orbits);
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
