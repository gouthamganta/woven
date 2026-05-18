namespace WovenBackend.Services;

public interface IMediaService
{
    Task<UploadTokenResult> GetUploadTokenAsync(int userId, MediaContainerType container, string fileName, string contentType, CancellationToken ct = default);
    Task<ConfirmUploadResult> ConfirmUploadAsync(int userId, string blobPath, MediaContainerType container, CancellationToken ct = default);
    Task DeleteMediaAsync(string blobPath, MediaContainerType container, CancellationToken ct = default);
    Task<string> GetMediaUrlAsync(string blobPath, MediaContainerType container, CancellationToken ct = default);
    Task DeleteAllForUserAsync(int userId, CancellationToken ct = default);
}

public enum MediaContainerType { ProfilePhoto, TileMedia, VoiceNote }

public record UploadTokenResult(
    string SasToken,
    string BlobPath,
    string UploadUrl,
    DateTimeOffset ExpiresAt);

public record ConfirmUploadResult(
    bool Success,
    string? PublicUrl,
    string? Error);
