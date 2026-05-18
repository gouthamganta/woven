namespace WovenBackend.Services.Trust;

public interface ICatfishDetectionService
{
    Task CheckPhotoAsync(int userId, int photoEmbeddingId, CancellationToken ct = default);
}
