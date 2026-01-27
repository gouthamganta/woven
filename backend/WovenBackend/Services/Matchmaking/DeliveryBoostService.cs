using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.data.Entities.Moments;

namespace WovenBackend.Services.Matchmaking;

public class DeliveryBoostService : IDeliveryBoostService
{
    private readonly WovenDbContext _db;
    private readonly ILogger<DeliveryBoostService> _logger;

    // Tunables (simple v1 defaults)
    private const int LookbackDays = 7;

    private const double ReciprocalBoost = 25; // BIG so “within 7 days” becomes very likely
    private const double PendingBoost = 10;
    private const double YesBoost = 12;

    private const double FatiguePenalty_2to3 = 5;
    private const double FatiguePenalty_4plus = 12;

    private const double PopPenalty = 10;       // n
    private const double UnmatchPenalty = 18;   // n+

    // How far back do we penalize pop/unmatch
    private const int PopLookbackDays = 30;
    private const int UnmatchLookbackDays = 90;

    public DeliveryBoostService(WovenDbContext db, ILogger<DeliveryBoostService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Dictionary<int, double>> GetBoostMapAsync(
        int viewerId,
        List<int> candidateIds,
        DateOnly dateUtc,
        CancellationToken ct = default)
    {
        var boost = candidateIds.Distinct().ToDictionary(id => id, _ => 0.0);

        if (boost.Count == 0) return boost;

        // date windows
        var cutoffExposureDate = dateUtc.AddDays(-LookbackDays);
        var now = DateTimeOffset.UtcNow;

        var popCutoff = now.AddDays(-PopLookbackDays);
        var unmatchCutoff = now.AddDays(-UnmatchLookbackDays);

        // ---------------------------
        // 1) Reciprocal exposure boost:
        // If candidate saw viewer in last 7 days -> boost candidate for viewer
        // ---------------------------
        var reciprocalIds = await _db.CandidateExposures.AsNoTracking()
            .Where(e =>
                candidateIds.Contains(e.ViewerUserId) &&
                e.ShownUserId == viewerId &&
                e.DateUtc >= cutoffExposureDate)
            .Select(e => e.ViewerUserId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var id in reciprocalIds)
            boost[id] += ReciprocalBoost;

        // ---------------------------
        // 2) Pending boost:
        // candidate saved viewer (pending) recently
        // ---------------------------
        var pendingFromCandidates = await _db.PendingMatches.AsNoTracking()
            .Where(p =>
                candidateIds.Contains(p.UserId) &&
                p.TargetUserId == viewerId &&
                p.CreatedAt >= now.AddDays(-LookbackDays))
            .Select(p => p.UserId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var id in pendingFromCandidates)
            boost[id] += PendingBoost;

        // ---------------------------
        // 3) YES boost:
        // candidate YES’d viewer in recent days
        // (MomentResponse is 1/day; use DateUtc window)
        // ---------------------------
        var yesFromCandidates = await _db.MomentResponses.AsNoTracking()
            .Where(r =>
                candidateIds.Contains(r.FromUserId) &&
                r.ToUserId == viewerId &&
                r.Choice == MomentChoice.YES &&
                r.DateUtc >= cutoffExposureDate)
            .Select(r => r.FromUserId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var id in yesFromCandidates)
            boost[id] += YesBoost;

        // ---------------------------
        // 4) Fatigue penalties (viewer saw candidate too often in last 7 days)
        // ---------------------------
        var fatigueCounts = await _db.CandidateExposures.AsNoTracking()
            .Where(e =>
                e.ViewerUserId == viewerId &&
                candidateIds.Contains(e.ShownUserId) &&
                e.DateUtc >= cutoffExposureDate)
            .GroupBy(e => e.ShownUserId)
            .Select(g => new { CandidateId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        foreach (var row in fatigueCounts)
        {
            if (!boost.ContainsKey(row.CandidateId)) continue;

            if (row.Count >= 4) boost[row.CandidateId] -= FatiguePenalty_4plus;
            else if (row.Count >= 2) boost[row.CandidateId] -= FatiguePenalty_2to3;
        }

        // ---------------------------
        // 5) POP / UNMATCH penalties (recent history)
        // ---------------------------
        // We only penalize if there was a closed match between viewer and candidate.
        var closedMatches = await _db.Matches.AsNoTracking()
            .Where(m =>
                m.BalloonState == BalloonState.CLOSED &&
                (
                    (m.UserAId == viewerId && candidateIds.Contains(m.UserBId)) ||
                    (m.UserBId == viewerId && candidateIds.Contains(m.UserAId))
                ) &&
                m.ClosedAt != null)
            .Select(m => new
            {
                OtherId = (m.UserAId == viewerId) ? m.UserBId : m.UserAId,
                Reason = m.ClosedReason,
                ClosedAt = m.ClosedAt
            })
            .ToListAsync(ct);

        foreach (var m in closedMatches)
        {
            if (!boost.ContainsKey(m.OtherId)) continue;
            if (m.Reason == null || m.ClosedAt == null) continue;

            // ClosedAt is DateTimeOffset? (in your Match entity)
            var closedAt = m.ClosedAt.Value;

            if (m.Reason == ClosedReason.POP && closedAt >= popCutoff)
                boost[m.OtherId] -= PopPenalty;

            if (m.Reason == ClosedReason.UNMATCH && closedAt >= unmatchCutoff)
                boost[m.OtherId] -= UnmatchPenalty;
        }

        return boost;
    }
}
