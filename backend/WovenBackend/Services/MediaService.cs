using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using WovenBackend.Services.Moderation;
using WovenBackend.Services.Trust;

namespace WovenBackend.Services;

public class MediaService : IMediaService
{
    private readonly BlobServiceClient _blobService;
    private readonly IModerationService _moderation;
    private readonly ITrustService _trust;
    private readonly ILogger<MediaService> _logger;

    public MediaService(
        BlobServiceClient blobService,
        IModerationService moderation,
        ITrustService trust,
        ILogger<MediaService> logger)
    {
        _blobService = blobService;
        _moderation = moderation;
        _trust = trust;
        _logger = logger;
    }

    public async Task<UploadTokenResult> GetUploadTokenAsync(
        int userId,
        MediaContainerType container,
        string fileName,
        string contentType,
        CancellationToken ct = default)
    {
        var ext       = Path.GetExtension(fileName).ToLowerInvariant();
        var blobPath  = $"{userId}/{Guid.NewGuid()}{ext}";
        var containerName = ContainerName(container);

        var containerClient = _blobService.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync(cancellationToken: ct);

        var blobClient = containerClient.GetBlobClient(blobPath);
        var expiresAt  = DateTimeOffset.UtcNow.AddMinutes(15);

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = containerName,
            BlobName          = blobPath,
            Resource          = "b",
            ExpiresOn         = expiresAt
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Write | BlobSasPermissions.Create);

        var sasUri = blobClient.GenerateSasUri(sasBuilder);

        _logger.LogInformation("[Media] SAS token issued for {BlobPath} (expires {ExpiresAt})", blobPath, expiresAt);

        return new UploadTokenResult(
            SasToken:  sasUri.Query.TrimStart('?'),
            BlobPath:  blobPath,
            UploadUrl: sasUri.ToString(),
            ExpiresAt: expiresAt);
    }

    public async Task<ConfirmUploadResult> ConfirmUploadAsync(
        int userId,
        string blobPath,
        MediaContainerType container,
        CancellationToken ct = default)
    {
        try
        {
            var blobClient = _blobService
                .GetBlobContainerClient(ContainerName(container))
                .GetBlobClient(blobPath);

            var exists = await blobClient.ExistsAsync(ct);
            if (!exists.Value)
            {
                _logger.LogWarning("[Media] ConfirmUpload: blob not found at {BlobPath}", blobPath);
                return new ConfirmUploadResult(false, null, "BLOB_NOT_FOUND");
            }

            var url = blobClient.Uri.ToString();

            if (container == MediaContainerType.ProfilePhoto)
            {
                var modResult = await _moderation.ModerateImageAsync(userId, url, ct);
                if (modResult == ModerationImageResult.AUTO_REJECTED)
                {
                    await blobClient.DeleteIfExistsAsync(cancellationToken: ct);
                    _logger.LogWarning("[Media] ProfilePhoto auto-rejected for user {UserId}: {BlobPath}", userId, blobPath);
                    return new ConfirmUploadResult(false, null, "PHOTO_REJECTED");
                }
                if (modResult == ModerationImageResult.ESCALATED)
                {
                    await _trust.FlagAsync(userId, "CATFISH_SUSPECTED", 0.5f, ct);
                    _logger.LogWarning("[Media] ProfilePhoto escalated (catfish suspected) for user {UserId}: {BlobPath}", userId, blobPath);
                }
            }

            // Processing stub — full FFmpeg/resize pipeline comes in Phase 2 when Tiles exist.
            if (container == MediaContainerType.TileMedia || container == MediaContainerType.VoiceNote)
                _logger.LogInformation("[Media] PROCESSING_QUEUED:{BlobPath}", blobPath);

            _logger.LogInformation("[Media] Confirmed upload for {BlobPath}", blobPath);
            return new ConfirmUploadResult(true, url, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Media] ConfirmUpload failed for {BlobPath}", blobPath);
            return new ConfirmUploadResult(false, null, "INTERNAL_ERROR");
        }
    }

    public async Task DeleteMediaAsync(string blobPath, MediaContainerType container, CancellationToken ct = default)
    {
        try
        {
            var blobClient = _blobService
                .GetBlobContainerClient(ContainerName(container))
                .GetBlobClient(blobPath);

            await blobClient.DeleteIfExistsAsync(cancellationToken: ct);
            _logger.LogInformation("[Media] Deleted {BlobPath}", blobPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Media] DeleteMediaAsync failed for {BlobPath}", blobPath);
        }
    }

    public Task<string> GetMediaUrlAsync(string blobPath, MediaContainerType container, CancellationToken ct = default)
    {
        // Dev: direct storage / Azurite URL.
        // Prod: same for now — swap hostname for CDN when Phase 2 configures it.
        var blobClient = _blobService
            .GetBlobContainerClient(ContainerName(container))
            .GetBlobClient(blobPath);

        return Task.FromResult(blobClient.Uri.ToString());
    }

    public async Task DeleteAllForUserAsync(int userId, CancellationToken ct = default)
    {
        var containers = new[]
        {
            MediaContainerType.ProfilePhoto,
            MediaContainerType.TileMedia,
            MediaContainerType.VoiceNote
        };

        foreach (var container in containers)
        {
            try
            {
                var containerClient = _blobService.GetBlobContainerClient(ContainerName(container));
                var prefix = $"{userId}/";
                await foreach (var blob in containerClient.GetBlobsAsync(prefix: prefix, cancellationToken: ct))
                {
                    await containerClient.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: ct);
                }
                _logger.LogInformation("[Media] DeleteAllForUser: deleted {Container} blobs for user {UserId}",
                    container, userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Media] DeleteAllForUser failed for container {Container}, user {UserId}",
                    container, userId);
            }
        }
    }

    private static string ContainerName(MediaContainerType type) => type switch
    {
        MediaContainerType.ProfilePhoto => "profile-photos",
        MediaContainerType.TileMedia    => "tile-media",
        MediaContainerType.VoiceNote    => "voice-notes",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };
}
