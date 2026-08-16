using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;

namespace WovenBackend.Services.Recommendations;

public class CollaborativeFilteringService : ICollaborativeFilteringService
{
    private const double JaccardThreshold = 0.1;
    private const double RecencyHalfLifeDays = 30.0;

    private readonly WovenDbContext _db;
    private readonly ILogger<CollaborativeFilteringService> _logger;

    public CollaborativeFilteringService(WovenDbContext db, ILogger<CollaborativeFilteringService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        _logger.LogInformation("[CF] Starting collaborative filtering batch");

        // 1a. Explicit signal: romantic orbits
        var allOrbits = await _db.TileOrbits.AsNoTracking()
            .Where(o => o.RelationshipType == "romantic")
            .Select(o => new { o.OrbiterId, o.TileOwnerId, o.OrbitedAt })
            .ToListAsync(ct);

        // 1b. Implicit signal: qualifying tile dwells (≥8s) — treat dwell as implicit preference
        var dwellCutoff = DateTimeOffset.UtcNow.AddDays(-30); // last 30 days only
        var allDwells = await _db.TileViews.AsNoTracking()
            .Where(v => v.DurationMs >= 8_000 && v.ViewedAt >= dwellCutoff)
            .Join(_db.Tiles.AsNoTracking(),
                v => v.TileId,
                t => t.Id,
                (v, t) => new { ViewerUserId = v.UserId, TileOwnerId = t.UserId, v.ViewedAt })
            .Where(x => x.ViewerUserId != x.TileOwnerId)
            .ToListAsync(ct);

        if (allOrbits.Count == 0 && allDwells.Count == 0)
        {
            _logger.LogInformation("[CF] No interaction data found — skipping");
            return;
        }

        // orbiter → set of tile_owner_ids they orbited (explicit signal)
        var orbitSets = allOrbits
            .GroupBy(o => o.OrbiterId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(o => o.TileOwnerId).ToHashSet());

        // viewer → set of tile_owner_ids they dwelled on (implicit signal)
        var dwellSets = allDwells
            .GroupBy(d => d.ViewerUserId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(d => d.TileOwnerId).ToHashSet());

        // orbiter → most recent orbit time (for recency weight)
        var lastOrbitByUser = allOrbits
            .GroupBy(o => o.OrbiterId)
            .ToDictionary(g => g.Key, g => g.Max(o => o.OrbitedAt));

        // All users in either interaction set
        var userIds = orbitSets.Keys.Union(dwellSets.Keys).Distinct().ToList();
        var trustScores = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.TrustScore })
            .ToDictionaryAsync(u => u.Id, u => (double)u.TrustScore, ct);

        // 2. Compute Jaccard similarities between all user pairs
        var now = DateTimeOffset.UtcNow;
        var upsertRows = new List<CfScore>();

        for (int i = 0; i < userIds.Count; i++)
        {
            var userId = userIds[i];

            for (int j = 0; j < userIds.Count; j++)
            {
                if (i == j) continue;
                var candidateId = userIds[j];

                // Orbit Jaccard (explicit signal)
                orbitSets.TryGetValue(userId, out var userOrbitSet);
                orbitSets.TryGetValue(candidateId, out var candidateOrbitSet);
                var orbitJaccard = 0.0;
                if (userOrbitSet != null && candidateOrbitSet != null)
                {
                    var orbitIntersect = userOrbitSet.Count(x => candidateOrbitSet.Contains(x));
                    if (orbitIntersect > 0)
                    {
                        var orbitUnion = userOrbitSet.Count + candidateOrbitSet.Count - orbitIntersect;
                        orbitJaccard = (double)orbitIntersect / orbitUnion;
                    }
                }

                // Dwell Jaccard (implicit signal — weighted less than explicit orbit)
                dwellSets.TryGetValue(userId, out var userDwellSet);
                dwellSets.TryGetValue(candidateId, out var candidateDwellSet);
                var dwellJaccard = 0.0;
                if (userDwellSet != null && candidateDwellSet != null)
                {
                    var dwellIntersect = userDwellSet.Count(x => candidateDwellSet.Contains(x));
                    if (dwellIntersect > 0)
                    {
                        var dwellUnion = userDwellSet.Count + candidateDwellSet.Count - dwellIntersect;
                        dwellJaccard = (double)dwellIntersect / dwellUnion;
                    }
                }

                // Blend: explicit orbit is 2× more valuable than implicit dwell
                var jaccard = orbitJaccard * 0.70 + dwellJaccard * 0.30;
                if (jaccard <= JaccardThreshold) continue;

                // Recency weight based on candidate's most recent orbit
                var daysSince = lastOrbitByUser.TryGetValue(candidateId, out var lastOrbit)
                    ? (now - lastOrbit).TotalDays
                    : 90.0;
                var recencyWeight = Math.Exp(-daysSince / RecencyHalfLifeDays);

                // Trust multiplier — default 0.5 → 1.0 at full trust
                var trust = trustScores.TryGetValue(candidateId, out var ts) ? ts : 0.5;

                // cf_score ∈ [0, 1]
                var score = jaccard * recencyWeight * trust;

                upsertRows.Add(new CfScore
                {
                    UserId = userId,
                    CandidateId = candidateId,
                    Score = score,
                    ComputedAt = now
                });
            }
        }

        _logger.LogInformation("[CF] Computed {Count} scores above threshold", upsertRows.Count);

        // 3. Upsert cf_scores — load existing, merge, save
        if (upsertRows.Count > 0)
        {
            var affectedUserIds = upsertRows.Select(r => r.UserId).Distinct().ToList();
            var existingMap = await _db.CfScores
                .Where(c => affectedUserIds.Contains(c.UserId))
                .ToDictionaryAsync(c => (c.UserId, c.CandidateId), ct);

            foreach (var row in upsertRows)
            {
                if (existingMap.TryGetValue((row.UserId, row.CandidateId), out var existing))
                {
                    existing.Score = row.Score;
                    existing.ComputedAt = row.ComputedAt;
                }
                else
                {
                    _db.CfScores.Add(row);
                }
            }

            await _db.SaveChangesAsync(ct);
        }

        // 4. Delete stale rows older than 7 days
        var staleCutoff = now.AddDays(-7);
        var staleCount = await _db.CfScores
            .Where(c => c.ComputedAt < staleCutoff)
            .ExecuteDeleteAsync(ct);

        var duration = DateTime.UtcNow - startTime;
        _logger.LogInformation(
            "[CF] Batch complete — users: {Users}, scores written: {Written}, stale deleted: {Stale}, duration: {Duration}ms",
            userIds.Count, upsertRows.Count, staleCount, (int)duration.TotalMilliseconds);
    }
}
