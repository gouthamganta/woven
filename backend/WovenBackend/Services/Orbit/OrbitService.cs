using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.Services.Analytics;

namespace WovenBackend.Services.Orbit;

public class OrbitService : IOrbitService
{
    private const double GravityDecayRate = 0.1;  // e^(-0.1 × days)
    private const double GravityIncrement = 1.0;

    private readonly WovenDbContext _db;
    private readonly INotificationService _notify;
    private readonly IAnalyticsService _analytics;
    private readonly ILogger<OrbitService> _logger;

    public OrbitService(WovenDbContext db, INotificationService notify, IAnalyticsService analytics, ILogger<OrbitService> logger)
    {
        _db = db;
        _notify = notify;
        _analytics = analytics;
        _logger = logger;
    }

    public async Task<OrbitResult> OrbitTileAsync(int orbiterId, Guid tileId, CancellationToken ct = default)
    {
        // 1. Load tile — validate exists and not own tile
        var tile = await _db.Tiles.AsNoTracking()
            .Where(t => t.Id == tileId && !t.IsExpired)
            .Select(t => new { t.Id, t.UserId })
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("TILE_NOT_FOUND");

        if (tile.UserId == orbiterId)
            throw new InvalidOperationException("CANNOT_ORBIT_OWN_TILE");

        // 2. Check already orbited (UNIQUE constraint guard)
        var alreadyOrbited = await _db.TileOrbits.AsNoTracking()
            .AnyAsync(o => o.OrbiterId == orbiterId && o.TileId == tileId, ct);

        if (alreadyOrbited)
            throw new InvalidOperationException("ALREADY_ORBITED");

        // 3. Determine relationship_type via preference check
        var orbiterPref = await _db.UserPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == orbiterId, ct);

        var orbiterProfile = await _db.UserProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == orbiterId, ct);

        var ownerProfile = await _db.UserProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == tile.UserId, ct);

        var relationshipType = DetermineRelationshipType(
            orbiterPref, orbiterProfile, ownerProfile);

        // 4. Insert orbit row
        var orbit = new TileOrbit
        {
            OrbiterId = orbiterId,
            TileId = tileId,
            TileOwnerId = tile.UserId,
            RelationshipType = relationshipType,
            OrbitedAt = DateTimeOffset.UtcNow
        };
        _db.TileOrbits.Add(orbit);
        await _db.SaveChangesAsync(ct);

        var mutualDetected = false;

        if (relationshipType == "romantic")
        {
            // 5a. Upsert orbit_gravity with recency decay
            await UpsertOrbitGravityAsync(orbiterId, tile.UserId, ct);

            // Check mutual: does tile_owner have any orbit on any of orbiter's tiles?
            var ownerOrbitedOrbiterTile = await _db.TileOrbits.AsNoTracking()
                .AnyAsync(o =>
                    o.OrbiterId == tile.UserId &&
                    o.TileOwnerId == orbiterId &&
                    o.RelationshipType == "romantic", ct);

            if (ownerOrbitedOrbiterTile)
            {
                mutualDetected = true;

                // Write CandidateSignal for both users (boosted YES signal)
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                _db.CandidateSignals.AddRange(
                    new CandidateSignal
                    {
                        FromUserId = orbiterId,
                        ToUserId = tile.UserId,
                        Type = "YES",
                        MetaJson = """{"source":"orbit_mutual"}""",
                        ExpiresAt = DateTime.UtcNow.AddDays(7),
                        DateUtc = today,
                        CreatedAt = DateTime.UtcNow
                    },
                    new CandidateSignal
                    {
                        FromUserId = tile.UserId,
                        ToUserId = orbiterId,
                        Type = "YES",
                        MetaJson = """{"source":"orbit_mutual"}""",
                        ExpiresAt = DateTime.UtcNow.AddDays(7),
                        DateUtc = today,
                        CreatedAt = DateTime.UtcNow
                    }
                );
                await _db.SaveChangesAsync(ct);
                // Anonymous — no notification (spec: "they don't know")
            }
        }
        else // social
        {
            // 5b. Check for existing friend bridge (either direction)
            var bridgeExists = await _db.FriendBridges.AsNoTracking()
                .AnyAsync(b =>
                    (b.UserAId == orbiterId && b.UserBId == tile.UserId) ||
                    (b.UserAId == tile.UserId && b.UserBId == orbiterId), ct);

            if (!bridgeExists)
            {
                // Check mutual out-of-preference orbit
                var mutualSocialOrbit = await _db.TileOrbits.AsNoTracking()
                    .AnyAsync(o =>
                        o.OrbiterId == tile.UserId &&
                        o.TileOwnerId == orbiterId &&
                        o.RelationshipType == "social", ct);

                if (mutualSocialOrbit)
                {
                    mutualDetected = true;

                    var bridge = new FriendBridge
                    {
                        UserAId = orbiterId,
                        UserBId = tile.UserId,
                        Status = "pending_both",
                        CreatedAt = DateTimeOffset.UtcNow
                    };
                    _db.FriendBridges.Add(bridge);
                    await _db.SaveChangesAsync(ct);

                    _ = Task.Run(async () =>
                    {
                        try { await _notify.SendFriendBridgeProposalAsync(orbiterId, tile.UserId, bridge.Id); }
                        catch (Exception ex) { _logger.LogWarning(ex, "[Orbit] FriendBridge notification failed"); }
                    });
                }
            }
        }

        _ = _analytics.TrackAsync(orbiterId, null, AnalyticsEvents.TileOrbited,
            new { relationshipType, mutualDetected });

        return new OrbitResult(relationshipType, mutualDetected);
    }

    // -------------------------------------------------------
    private async Task UpsertOrbitGravityAsync(int userId, int candidateId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = await _db.OrbitGravities
            .FirstOrDefaultAsync(g => g.UserId == userId && g.CandidateId == candidateId, ct);

        if (existing == null)
        {
            _db.OrbitGravities.Add(new OrbitGravity
            {
                UserId = userId,
                CandidateId = candidateId,
                Score = GravityIncrement,
                LastOrbitAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            var daysSinceLast = (now - existing.LastOrbitAt).TotalDays;
            var decayed = existing.Score * Math.Exp(-GravityDecayRate * daysSinceLast);
            existing.Score = decayed + GravityIncrement;
            existing.LastOrbitAt = now;
            existing.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct);
    }

    private static string DetermineRelationshipType(
        WovenBackend.Data.UserPreference? orbiterPref,
        WovenBackend.Data.UserProfile? orbiterProfile,
        WovenBackend.Data.UserProfile? ownerProfile)
    {
        if (orbiterPref == null || orbiterProfile == null || ownerProfile == null)
            return "social";

        // Parse InterestedIn
        HashSet<string> interestedIn;
        try
        {
            var parsed = JsonSerializer.Deserialize<string[]>(orbiterPref.InterestedInJson);
            interestedIn = parsed != null
                ? new HashSet<string>(parsed, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>();
        }
        catch { interestedIn = new HashSet<string>(); }

        if (interestedIn.Count == 0) return "social";

        // Gender match
        if (!interestedIn.Contains(ownerProfile.Gender)) return "social";

        // Age match
        if (ownerProfile.Age < orbiterPref.AgeMin || ownerProfile.Age > orbiterPref.AgeMax)
            return "social";

        // Distance match (only if both have location)
        if (orbiterProfile.Lat.HasValue && orbiterProfile.Lng.HasValue &&
            ownerProfile.Lat.HasValue && ownerProfile.Lng.HasValue)
        {
            var dist = Haversine(
                orbiterProfile.Lat.Value, orbiterProfile.Lng.Value,
                ownerProfile.Lat.Value, ownerProfile.Lng.Value);

            if (dist > orbiterPref.DistanceMiles) return "social";
        }

        return "romantic";
    }

    private static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 3959;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
