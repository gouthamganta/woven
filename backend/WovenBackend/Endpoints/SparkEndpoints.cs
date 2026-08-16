using System.Security.Claims;
using WovenBackend.Services.Moments;

namespace WovenBackend.Endpoints;

public static class SparkEndpoints
{
    public static void MapSparkEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/sparks").RequireAuthorization();

        // GET /sparks/balance
        group.MapGet("/balance", async (
            SparkWalletService wallet,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);
            var balance = await wallet.GetBalanceAsync(userId, ct);
            return Results.Ok(new { balance, max = 10.0m });
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
