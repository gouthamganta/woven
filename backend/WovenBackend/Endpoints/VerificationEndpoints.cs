using System.Security.Claims;
using WovenBackend.Services;
using WovenBackend.Services.Analytics;
using WovenBackend.Services.Verification;

namespace WovenBackend.Endpoints;

public static class VerificationEndpoints
{
    private const int DailyAttemptLimit = 5;

    public record SelfieRequest(string BlobPath);

    private static int GetUserId(ClaimsPrincipal user)
    {
        var uid = user.FindFirstValue("uid");
        if (int.TryParse(uid, out var id)) return id;
        var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(sub, out id)) return id;
        throw new UnauthorizedAccessException("Missing user id claim");
    }

    public static void MapVerificationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/verification");
        group.RequireAuthorization();

        // POST /verification/selfie
        group.MapPost("/selfie", async (
            SelfieRequest req,
            IVerificationService svc,
            IAnalyticsService analytics,
            ICacheService cache,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);

            // Validate blobPath ownership: must start with "{userId}/"
            if (string.IsNullOrWhiteSpace(req.BlobPath) ||
                !req.BlobPath.StartsWith($"{userId}/", StringComparison.Ordinal))
                return Results.BadRequest(new { error = "Invalid blob path" });

            // Rate limit: 5 attempts per user per day
            var rlKey = $"rl:verify:{userId}:{DateOnly.FromDateTime(DateTime.UtcNow)}";
            var allowed = await cache.CheckRateLimitAsync(rlKey, DailyAttemptLimit, CacheTtl.UntilMidnightUtc(), ct);
            if (!allowed)
            {
                http.Response.Headers["Retry-After"] = ((int)CacheTtl.UntilMidnightUtc().TotalSeconds).ToString();
                return Results.StatusCode(429);
            }

            // Track start event (fire-and-forget)
            _ = analytics.TrackAsync(userId, null, AnalyticsEvents.VerificationStarted,
                new { type = "selfie" });

            var result = await svc.SubmitSelfieAsync(userId, req.BlobPath, ct);

            return Results.Ok(new
            {
                success = result.Success,
                verified = result.Verified,
                error = result.Error
            });
        });

        // GET /verification/status
        group.MapGet("/status", async (
            IVerificationService svc,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);
            var status = await svc.GetVerificationStatusAsync(userId, ct);

            return Results.Ok(new
            {
                isVerified = status.IsVerified,
                verifiedAt = status.VerifiedAt,
                verificationType = status.VerificationType,
                latestAttempt = status.LatestAttempt == null ? null : new
                {
                    id = status.LatestAttempt.Id,
                    type = status.LatestAttempt.Type,
                    status = status.LatestAttempt.Status,
                    submittedAt = status.LatestAttempt.SubmittedAt,
                    verifiedAt = status.LatestAttempt.VerifiedAt,
                    failureReason = status.LatestAttempt.FailureReason
                }
            });
        });
    }
}
