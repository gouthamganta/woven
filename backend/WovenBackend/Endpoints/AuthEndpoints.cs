using Microsoft.EntityFrameworkCore;
using WovenBackend.Auth;
using WovenBackend.Data;
using WovenBackend.Services;
using WovenBackend.Services.Analytics;
using WovenBackend.Services.Security;
using WovenBackend.Services.Trust;

namespace WovenBackend.Endpoints;

public static class AuthEndpoints
{
    public record GoogleAuthRequest(string IdToken, string? DeviceFingerprint = null);

    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/auth/google", async (
            GoogleAuthRequest req,
            IGoogleTokenVerifier googleVerifier,
            WovenDbContext db,
            JwtTokenService jwt,
            ITrustService trust,
            ICacheService cache,
            ILogger<Program> logger,
            IAnalyticsService analytics,
            HttpContext http,
            CancellationToken ct) =>
        {
            // Rate limit: 20 auth attempts per IP per day
            var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var ipHash = PiiSanitizer.HashForAudit(ip, "rl-auth-v1");
            var rlKey = $"rl:auth:{ipHash}:{DateOnly.FromDateTime(DateTime.UtcNow)}";
            var allowed = await cache.CheckRateLimitAsync(rlKey, 20, CacheTtl.UntilMidnightUtc(), ct);
            if (!allowed)
            {
                http.Response.Headers["Retry-After"] = ((int)CacheTtl.UntilMidnightUtc().TotalSeconds).ToString();
                return Results.StatusCode(429);
            }

            if (string.IsNullOrWhiteSpace(req.IdToken))
            {
                return Results.BadRequest(new { error = "ID token is required" });
            }

            GoogleUserInfo googleUser;
            try
            {
                googleUser = await googleVerifier.VerifyAsync(req.IdToken, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Google token verification failed");
                return Results.Unauthorized();
            }

            const string provider = "google";

            // 1) Check if this Google account already linked
            var existingIdentity = await db.AuthIdentities
                .Include(x => x.User)
                .FirstOrDefaultAsync(x =>
                    x.Provider == provider && x.ProviderSubject == googleUser.Subject, ct);

            User user;

            if (existingIdentity != null)
            {
                user = existingIdentity.User;
            }
            else
            {
                // 2) Create or reuse user by email (prevents duplicates)
                user = await db.Users.FirstOrDefaultAsync(u => u.Email == googleUser.Email, ct)
                       ?? new User
                       {
                           Email = googleUser.Email,
                           FullName = googleUser.Name,
                           ProfilePhoto = googleUser.Picture,
                           PasswordHash = null,

                           ProfileStatus = ProfileStatus.INCOMPLETE,
                           CreatedAt = DateTime.UtcNow,
                           UpdatedAt = DateTime.UtcNow
                       };

                if (user.Id == 0)
                {
                    db.Users.Add(user);
                    await db.SaveChangesAsync(ct);
                }

                var identity = new AuthIdentity
                {
                    UserId = user.Id,
                    Provider = provider,
                    ProviderSubject = googleUser.Subject,
                    Email = googleUser.Email
                };

                db.AuthIdentities.Add(identity);
                await db.SaveChangesAsync(ct);
            }

            user.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            // Phase 2B: trust signals — fire-and-forget, non-blocking
            _ = Task.Run(async () =>
            {
                try
                {
                    await trust.CheckDeviceFingerprintAsync(user.Id, req.DeviceFingerprint);
                    await trust.CheckVelocityAsync(user.Id);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[Auth] Trust check failed for user {UserId}", user.Id);
                }
            });

            var accessToken = jwt.CreateAccessToken(user.Id, user.Email);

            var isNewUser = existingIdentity == null;
            _ = analytics.TrackAsync(user.Id, null,
                isNewUser ? AnalyticsEvents.UserRegistered : AnalyticsEvents.AppOpened,
                new { provider = "google", isNewUser });

            return Results.Ok(new
            {
                accessToken,
                user = new
                {
                    user.Id,
                    user.Email,
                    user.FullName,
                    user.ProfilePhoto
                }
            });
        })

        .WithName("GoogleAuth");
    }
}
