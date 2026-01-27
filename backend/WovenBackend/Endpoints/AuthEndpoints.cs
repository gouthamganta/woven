using Microsoft.EntityFrameworkCore;
using WovenBackend.Auth;
using WovenBackend.Data;

namespace WovenBackend.Endpoints;

public static class AuthEndpoints
{
    public record GoogleAuthRequest(string IdToken);

    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/auth/google", async (
            GoogleAuthRequest req,
            IGoogleTokenVerifier googleVerifier,
            WovenDbContext db,
            JwtTokenService jwt,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
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

            var accessToken = jwt.CreateAccessToken(user.Id, user.Email);

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
