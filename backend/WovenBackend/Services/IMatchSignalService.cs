namespace WovenBackend.Services;

public interface IMatchSignalService
{
    Task RecordAsync(
        int viewerId,
        int candidateId,
        string eventType,
        float eventValue,
        string? metadataJson = null,
        CancellationToken ct = default);
}
