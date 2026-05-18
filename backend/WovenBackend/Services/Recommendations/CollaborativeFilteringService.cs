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

        // 1. Load all romantic orbits — build interaction matrix
        var allOrbits = await _db.TileOrbits.AsNoTracking()
            .Where(o => o.RelationshipType == "romantic")
            .Select(o => new { o.OrbiterId, o.TileOwnerId, o.OrbitedAt })
            .ToListAsync(ct);

        if (allOrbits.Count == 0)
        {
            _logger.LogInformation("[CF] No romantic orbits found — skipping");
            return;
        }

        // orbiter → set of tile_owner_ids they orbited
        var orbitSets = allOrbits
            .GroupBy(o => o.OrbiterId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(o => o.TileOwnerId).ToHashSet());

        // orbiter → most recent orbit time (for recency weight)
        var lastOrbitByUser = allOrbits
            .GroupBy(o => o.OrbiterId)
            .ToDictionary(g => g.Key, g => g.Max(o => o.OrbitedAt));

        // Load trust scores for all orbiters
        var userIds = orbitSets.Keys.ToList();
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
            var userSet = orbitSets[userId];

            for (int j = 0; j < userIds.Count; j++)
            {
                if (i == j) continue;
                var candidateId = userIds[j];
                var candidateSet = orbitSets[candidateId];

                // Jaccard — skip if no intersection
                var intersectionCount = userSet.Count(x => candidateSet.Contains(x));
                if (intersectionCount == 0) continue;

                var unionCount = userSet.Count + candidateSet.Count - intersectionCount;
                var jaccard = (double)intersectionCount / unionCount;
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
