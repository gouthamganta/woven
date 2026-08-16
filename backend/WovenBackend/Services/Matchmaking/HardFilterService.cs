using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;

namespace WovenBackend.Services.Matchmaking;

public class HardFilterService : IHardFilterService
{
    private readonly WovenDbContext _db;
    private readonly ILogger<HardFilterService> _logger;

    public HardFilterService(WovenDbContext db, ILogger<HardFilterService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<int>> ApplyAsync(int userId, List<int> candidateIds, CancellationToken ct = default)
    {
        if (candidateIds.Count == 0) return candidateIds;

        var userProfile = await _db.UserProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var userPref = await _db.UserPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (userProfile == null || userPref == null)
            return new List<int>();

        var candidates = await _db.UserProfiles.AsNoTracking()
            .Where(p => candidateIds.Contains(p.UserId))
            .Join(_db.UserPreferences.AsNoTracking(),
                  p => p.UserId, pref => pref.UserId,
                  (p, pref) => new { Profile = p, Pref = pref })
            .ToListAsync(ct);

        int filteredAge = 0, filteredDist = 0;
        var passed = new List<int>(candidates.Count);

        foreach (var c in candidates)
        {
            // Hard filter 1: reciprocal age — user's age must fit inside candidate's stated window
            if (userProfile.Age < c.Pref.AgeMin || userProfile.Age > c.Pref.AgeMax)
            {
                filteredAge++;
                continue;
            }

            // Hard filter 2: distance — only applied when both sides have location data
            if (userProfile.Lat.HasValue && userProfile.Lng.HasValue &&
                c.Profile.Lat.HasValue && c.Profile.Lng.HasValue)
            {
                var dist = Haversine(
                    userProfile.Lat.Value, userProfile.Lng.Value,
                    c.Profile.Lat.Value,  c.Profile.Lng.Value);

                if (dist > userPref.DistanceMiles || dist > c.Pref.DistanceMiles)
                {
                    filteredDist++;
                    continue;
                }
            }

            passed.Add(c.Profile.UserId);
        }

        _logger.LogInformation(
            "[HardFilter] user={UserId} in={In} out={Out} (age={Age} dist={Dist})",
            userId, candidateIds.Count, passed.Count, filteredAge, filteredDist);

        return passed;
    }

    private static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 3959; // miles
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2))
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRad(double deg) => deg * Math.PI / 180;
}
