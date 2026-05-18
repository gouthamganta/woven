using System.Security.Claims;
using WovenBackend.Services;

namespace WovenBackend.Endpoints;

public static class MediaEndpoints
{
    private static int GetUserId(ClaimsPrincipal user)
    {
        var uid = user.FindFirstValue("uid");
        if (int.TryParse(uid, out var id)) return id;
        var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(sub, out id)) return id;
        throw new UnauthorizedAccessException("Missing user id claim");
    }

    private static MediaContainerType? ParseContainer(string s) => s switch
    {
        "profile-photo" => MediaContainerType.ProfilePhoto,
        "tile-media"    => MediaContainerType.TileMedia,
        "voice-note"    => MediaContainerType.VoiceNote,
        _               => null
    };

    public static void MapMediaEndpoints(this WebApplication app)
    {
        // POST /media/upload-token
        // Returns a short-lived SAS token so the client can upload directly to blob storage,
        // keeping large binary traffic off the API server.
        app.MapPost("/media/upload-token", async (
            UploadTokenRequest req,
            ClaimsPrincipal user,
            IMediaService media,
            ICacheService cache,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);

            // Rate limit: 20 upload tokens per user per day
            var rlKey = $"rl:upload:{userId}:{DateOnly.FromDateTime(DateTime.UtcNow)}";
            var allowed = await cache.CheckRateLimitAsync(rlKey, 20, CacheTtl.UntilMidnightUtc(), ct);
            if (!allowed)
            {
                http.Response.Headers["Retry-After"] = ((int)CacheTtl.UntilMidnightUtc().TotalSeconds).ToString();
                return Results.StatusCode(429);
            }

            var container = ParseContainer(req.Container);
            if (container is null)
                return Results.BadRequest(new { error = "Unknown container. Use: profile-photo, tile-media, voice-note" });

            if (string.IsNullOrWhiteSpace(req.FileName))
                return Results.BadRequest(new { error = "fileName is required" });

            if (string.IsNullOrWhiteSpace(req.ContentType))
                return Results.BadRequest(new { error = "contentType is required" });

            var result = await media.GetUploadTokenAsync(userId, container.Value, req.FileName, req.ContentType, ct);

            return Results.Ok(new
            {
                sasToken  = result.SasToken,
                blobPath  = result.BlobPath,
                uploadUrl = result.UploadUrl,
                expiresAt = result.ExpiresAt
            });
        })
        .RequireAuthorization();

        // POST /media/confirm
        // Called after the client has finished uploading directly to blob storage.
        // Verifies the blob exists and (for tile-media / voice-notes) enqueues processing.
        app.MapPost("/media/confirm", async (
            ConfirmRequest req,
            ClaimsPrincipal user,
            IMediaService media,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);

            var container = ParseContainer(req.Container);
            if (container is null)
                return Results.BadRequest(new { error = "Unknown container" });

            if (string.IsNullOrWhiteSpace(req.BlobPath))
                return Results.BadRequest(new { error = "blobPath is required" });

            // Ownership check — every blob path is prefixed with the owning userId
            if (!req.BlobPath.StartsWith($"{userId}/"))
                return Results.Forbid();

            var result = await media.ConfirmUploadAsync(userId, req.BlobPath, container.Value, ct);

            return result.Success
                ? Results.Ok(new { success = true, url = result.PublicUrl })
                : Results.UnprocessableEntity(new { success = false, error = result.Error });
        })
        .RequireAuthorization();

        // DELETE /media/{container}/{**blobPath}
        // e.g. DELETE /media/profile-photo/42/a1b2c3d4.jpg
        app.MapDelete("/media/{container}/{**blobPath}", async (
            string container,
            string blobPath,
            ClaimsPrincipal user,
            IMediaService media,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);

            var containerType = ParseContainer(container);
            if (containerType is null)
                return Results.BadRequest(new { error = "Unknown container" });

            // Ownership check — callers may only delete their own blobs
            if (!blobPath.StartsWith($"{userId}/"))
                return Results.Forbid();

            await media.DeleteMediaAsync(blobPath, containerType.Value, ct);
            return Results.NoContent();
        })
        .RequireAuthorization();
    }

    private record UploadTokenRequest(string Container, string FileName, string ContentType);
    private record ConfirmRequest(string BlobPath, string Container);
}
