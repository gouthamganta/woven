using WovenBackend.Data;
using WovenBackend.Data.Entities;

namespace WovenBackend.Services;

public class MatchSignalService : IMatchSignalService
{
    private readonly WovenDbContext _db;
    private readonly ILogger<MatchSignalService> _logger;

    public MatchSignalService(WovenDbContext db, ILogger<MatchSignalService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task RecordAsync(
        int viewerId,
        int candidateId,
        string eventType,
        float eventValue,
        string? metadataJson = null,
        CancellationToken ct = default)
    {
        if (viewerId == candidateId) return;

        _db.MatchSignalLogs.Add(new MatchSignalLog
        {
            ViewerId      = viewerId,
            CandidateId   = candidateId,
            EventType     = eventType,
            EventValue    = eventValue,
            MetadataJson  = metadataJson,
            OccurredAt    = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);

        _logger.LogDebug("[MatchSignal] {EventType} viewer={ViewerId} candidate={CandidateId} value={Value}",
            eventType, viewerId, candidateId, eventValue);
    }
}
