using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.data.Entities.Moments;

namespace WovenBackend.Services.Matchmaking;

public class DeliveryBoostService : IDeliveryBoostService
{
    private readonly WovenDbContext _db;
    private readonly ILogger<DeliveryBoostService> _logger;

    // Tunables (simple v1 defaults)
    private const int LookbackDays = 7;

    private const double ReciprocalBoost = 18; // “they found you first” — one-shot, breaks after viewer sees them
    private const double PendingBoost = 10;
    private const double PositiveChoiceBoost = 12; // MAGICAL / LOGICAL / legacy YES

    private const double FatiguePenalty_2to3 = 5;
    private const double FatiguePenalty_4plus = 12;

    private const double PopPenalty = 10;       // n
    private const double UnmatchPenalty = 18;   // n+

    // How far back do we penalize pop/unmatch
    private const int PopLookbackDays = 30;
    private const int UnmatchLookbackDays = 90;

    // Orbit: logarithmic — rewards repeated orbiting, resists gaming
    // 1 orbit ≈ +3, 5 orbits ≈ +9, 10 orbits ≈ +12, cap at +15
    private const double OrbitBoostCap = 15.0;
    private const double OrbitBoostScale = 5.0; // log(1+score) * scale

    // Dwell: candidate lingered ≥8s on viewer's tiles = passive interest signal
    // 1 dwell ≈ +3, 5 dwells ≈ +7, cap at +10
    private const int DwellThresholdMs = 8_000;
    private const int DwellLookbackDays = 30;
    private const double DwellBoostCap = 10.0;
    private const double DwellBoostScale = 4.0; // log(1+count) * scale

    // Phase E — viewer engagement into candidate's profile (ECHO deep dive build order)
    // ProfileVisitDepth: viewer viewed ≥3 of candidate's tiles = strong curiosity signal.
    //   3 tiles → +5, 6 tiles → +8, 10+ tiles → +12, cap at +12.
    // TileDwell: viewer dwelled ≥8s on candidate's tiles = additional interest signal.
    //   1 dwell → +2, 5 dwells → +6, cap at +8.
    private const int ProfileDepthMinTiles = 3;
    private const double ProfileDepthBoostCap = 12.0;
    private const double ProfileDepthBoostScale = 4.0; // log(1+tiles) * scale
    private const double ViewerDwellBoostCap = 8.0;
    private const double ViewerDwellBoostScale = 3.0;  // log(1+count) * scale
    private const int EngagementLookbackDays = 30;

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
        var dwellCutoff = now.AddDays(-DwellLookbackDays);

        // ---------------------------
        // 1) Reciprocal exposure boost ("they found you first"):
        // Candidate saw viewer in last 7 days AND viewer has NOT yet been shown candidate.
        // Once viewer sees candidate the boost vanishes — no feedback loop.
        // ---------------------------
        var sawViewerIds = await _db.CandidateExposures.AsNoTracking()
            .Where(e =>
                candidateIds.Contains(e.ViewerUserId) &&
                e.ShownUserId == viewerId &&
                e.DateUtc >= cutoffExposureDate)
            .Select(e => e.ViewerUserId)
            .Distinct()
            .ToListAsync(ct);

        // Subtract anyone the viewer has already seen in the same window
        var alreadySeenByViewer = sawViewerIds.Count > 0
            ? await _db.CandidateExposures.AsNoTracking()
                .Where(e =>
                    e.ViewerUserId == viewerId &&
                    sawViewerIds.Contains(e.ShownUserId) &&
                    e.DateUtc >= cutoffExposureDate)
                .Select(e => e.ShownUserId)
                .Distinct()
                .ToListAsync(ct)
            : [];

        var undiscoveredIds = sawViewerIds.Except(alreadySeenByViewer);
        foreach (var id in undiscoveredIds)
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
        // 3) Positive-choice boost:
        // candidate chose viewer (MAGICAL / LOGICAL / legacy YES) in recent days
        // ---------------------------
        var positiveFromCandidates = await _db.MomentResponses.AsNoTracking()
            .Where(r =>
                candidateIds.Contains(r.FromUserId) &&
                r.ToUserId == viewerId &&
                (r.Choice == MomentChoice.MAGICAL ||
                 r.Choice == MomentChoice.LOGICAL ||
                 r.Choice == MomentChoice.YES) &&
                r.DateUtc >= cutoffExposureDate)
            .Select(r => r.FromUserId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var id in positiveFromCandidates)
            boost[id] += PositiveChoiceBoost;

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

        // ---------------------------
        // 6) Orbit gravity boost:
        // Each romantic orbit increments OrbitGravity.Score by 1.0 (see OrbitService).
        // Decayed score is passed through log so repeated orbiting is rewarded but
        // single-orbit gaming yields only ~3 pts. 5 orbits ≈ 9 pts, cap 15.
        // Score is time-decayed by days since last orbit (OrbitService constant: λ=0.1).
        // ---------------------------
        var orbitGravities = await _db.OrbitGravities.AsNoTracking()
            .Where(g => candidateIds.Contains(g.UserId) && g.CandidateId == viewerId)
            .Select(g => new { g.UserId, g.Score, g.LastOrbitAt })
            .ToListAsync(ct);

        foreach (var g in orbitGravities)
        {
            if (!boost.ContainsKey(g.UserId)) continue;
            var daysSince = (now - g.LastOrbitAt).TotalDays;
            var decayedScore = g.Score * Math.Exp(-0.1 * daysSince);
            boost[g.UserId] += Math.Min(OrbitBoostCap, Math.Log(1 + decayedScore) * OrbitBoostScale);
        }

        // ---------------------------
        // 7) Tile dwell boost:
        // Candidate lingered ≥8 s on viewer's tiles in the last 30 days.
        // Passive interest signal — weaker than orbit, same logarithmic anti-gaming shape.
        // 1 dwell ≈ +3, 5 dwells ≈ +7, cap 10.
        // ---------------------------
        var dwellCounts = await _db.TileViews.AsNoTracking()
            .Where(tv =>
                candidateIds.Contains(tv.UserId) &&
                tv.DurationMs >= DwellThresholdMs &&
                tv.ViewedAt >= dwellCutoff)
            .Join(
                _db.Tiles.Where(t => t.UserId == viewerId),
                tv => tv.TileId,
                t  => t.Id,
                (tv, _) => tv.UserId)
            .GroupBy(uid => uid)
            .Select(g => new { CandidateId = g.Key, DwellCount = g.Count() })
            .ToListAsync(ct);

        foreach (var d in dwellCounts)
        {
            if (!boost.ContainsKey(d.CandidateId)) continue;
            boost[d.CandidateId] += Math.Min(DwellBoostCap, Math.Log(1 + d.DwellCount) * DwellBoostScale);
        }

        // ---------------------------
        // 8) Viewer profile-engagement boost (ECHO Phase E):
        // Viewer has recently visited a candidate's profile depth-first (≥3 tiles viewed).
        // These events are recorded in MatchSignalLogs by ProfileEndpoints/MomentsEndpoints.
        // Deep curiosity before any action is a strong leading indicator of eventual connection.
        // ProfileVisitDepth value = raw tile count viewed in that visit.
        // ---------------------------
        var engagementCutoff = now.AddDays(-EngagementLookbackDays);

        var profileDepthSignals = await _db.MatchSignalLogs.AsNoTracking()
            .Where(s => s.ViewerId == viewerId
                     && candidateIds.Contains(s.CandidateId)
                     && s.EventType == MatchSignalEventTypes.ProfileVisitDepth
                     && s.EventValue >= ProfileDepthMinTiles
                     && s.OccurredAt >= engagementCutoff)
            .GroupBy(s => s.CandidateId)
            .Select(g => new { CandidateId = g.Key, MaxTiles = g.Max(s => s.EventValue) })
            .ToListAsync(ct);

        foreach (var e in profileDepthSignals)
        {
            if (!boost.ContainsKey(e.CandidateId)) continue;
            boost[e.CandidateId] += Math.Min(ProfileDepthBoostCap, Math.Log(1 + e.MaxTiles) * ProfileDepthBoostScale);
        }

        // ---------------------------
        // 9) Viewer tile-dwell boost (ECHO Phase E):
        // Viewer dwelled ≥8s on one or more of candidate's tiles.
        // Distinct from orbit (deliberate orbit click) and profile depth (visit breadth).
        // Captures: viewer lingered on a specific piece of content from this candidate.
        // TileDwell value = dwell ms; filter ≥8000ms.
        // ---------------------------
        var viewerDwellSignals = await _db.MatchSignalLogs.AsNoTracking()
            .Where(s => s.ViewerId == viewerId
                     && candidateIds.Contains(s.CandidateId)
                     && s.EventType == MatchSignalEventTypes.TileDwell
                     && s.EventValue >= DwellThresholdMs
                     && s.OccurredAt >= engagementCutoff)
            .GroupBy(s => s.CandidateId)
            .Select(g => new { CandidateId = g.Key, DwellCount = g.Count() })
            .ToListAsync(ct);

        foreach (var d in viewerDwellSignals)
        {
            if (!boost.ContainsKey(d.CandidateId)) continue;
            boost[d.CandidateId] += Math.Min(ViewerDwellBoostCap, Math.Log(1 + d.DwellCount) * ViewerDwellBoostScale);
        }

        // ---------------------------
        // 10) Trust score multiplier:
        // Scales the boost additively based on candidate trust level.
        // TrustScore 0.5 (default) → ±0 adjustment; 1.0 → +10; 0.0 → -10.
        // This rewards trusted users and suppresses low-trust candidates without hard-blocking.
        // ---------------------------
        var trustScores = await _db.Users.AsNoTracking()
            .Where(u => candidateIds.Contains(u.Id))
            .Select(u => new { u.Id, u.TrustScore })
            .ToDictionaryAsync(u => u.Id, u => (double)u.TrustScore, ct);

        foreach (var id in boost.Keys.ToList())
        {
            if (trustScores.TryGetValue(id, out var ts))
                boost[id] += (ts - 0.5) * 20.0; // ±10 range around neutral
        }

        // ---------------------------
        // 8) GhostScore multiplier:
        // Scales the final boost by the candidate's ghosting history.
        // 1.0 = never ghosts (no reduction); 0.0 = always ghosts (zeroed out).
        // ---------------------------
        var ghostAndVerified = await _db.Users.AsNoTracking()
            .Where(u => candidateIds.Contains(u.Id))
            .Select(u => new { u.Id, u.GhostScore, u.IsVerified })
            .ToDictionaryAsync(u => u.Id, u => u, ct);

        foreach (var id in boost.Keys.ToList())
        {
            if (ghostAndVerified.TryGetValue(id, out var u))
                boost[id] *= u.GhostScore;
        }

        // ---------------------------
        // 10) Verified boost:
        // Verified users appear slightly more often in decks (+5% multiplier).
        // ---------------------------
        foreach (var id in boost.Keys.ToList())
        {
            if (ghostAndVerified.TryGetValue(id, out var u) && u.IsVerified)
                boost[id] *= 1.05;
        }

        return boost;
    }
}
