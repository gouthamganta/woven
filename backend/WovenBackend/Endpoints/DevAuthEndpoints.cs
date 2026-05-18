using Microsoft.EntityFrameworkCore;
using WovenBackend.Auth;
using WovenBackend.Data;

namespace WovenBackend.Endpoints;

public static class DevAuthEndpoints
{
    public static void MapDevAuthEndpoints(this WebApplication app)
    {
        // Only enable in Development
        if (!app.Environment.IsDevelopment()) return;

        app.MapPost("/dev/login/{userId:int}", async (
            int userId,
            WovenDbContext db,
            JwtTokenService jwt,
            CancellationToken ct) =>
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct);
            if (user == null) return Results.NotFound(new { error = "USER_NOT_FOUND" });

            var accessToken = jwt.CreateAccessToken(user.Id, user.Email);

            return Results.Ok(new
            {
                accessToken,
                user = new { user.Id, user.Email, user.FullName }
            });
        })
        .WithName("DevLogin");

        // POST /debug/admin-token — issues a JWT with role=admin for local admin endpoint testing
        app.MapPost("/debug/admin-token", async (
            int userId,
            WovenDbContext db,
            JwtTokenService jwt,
            CancellationToken ct) =>
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct);
            if (user == null) return Results.NotFound(new { error = "USER_NOT_FOUND" });

            var accessToken = jwt.CreateAdminToken(user.Id, user.Email);

            return Results.Ok(new
            {
                accessToken,
                role = "admin",
                user = new { user.Id, user.Email, user.FullName }
            });
        })
        .WithName("DevAdminToken");
    }
}
