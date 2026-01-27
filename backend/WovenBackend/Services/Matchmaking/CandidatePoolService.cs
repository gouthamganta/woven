using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Services.Moments;

namespace WovenBackend.Services.Matchmaking;

public class CandidatePoolService : ICandidatePoolService
{
    private readonly WovenDbContext _db;
    private readonly ILogger<CandidatePoolService> _logger;

    public CandidatePoolService(WovenDbContext db, ILogger<CandidatePoolService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<int>> GetEligibleCandidatesAsync(int userId, CancellationToken ct = default)
    {
        _logger.LogInformation("[CandidatePool] Finding candidates for user {UserId}", userId);

        var today = MomentsRules.UtcToday();

        // Load user's profile and preferences
        var userProfile = await _db.UserProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        var userPref = await _db.UserPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (userProfile == null || userPref == null)
        {
            _logger.LogWarning("[CandidatePool] User {UserId} missing profile or preferences", userId);
            return new List<int>();
        }

        // Parse user's interested in
        var userInterestedIn = new HashSet<string>();
        try
        {
            var parsed = JsonSerializer.Deserialize<string[]>(userPref.InterestedInJson);
            if (parsed != null && parsed.Length > 0)
            {
                userInterestedIn = new HashSet<string>(parsed, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[CandidatePool] Failed to parse InterestedInJson for user {UserId}", userId);
        }

        // Get blocked users (both directions)
        var blockedIds = await _db.Blocks
            .Where(b => b.BlockerId == userId)
            .Select(b => b.BlockedId)
            .Union(_db.Blocks.Where(b => b.BlockedId == userId).Select(b => b.BlockerId))
            .ToListAsync(ct);

        // Get users with active balloons
        var activeBalloonPartners = await _db.Matches
            .Where(m => m.BalloonState == BalloonState.ACTIVE &&
                       (m.UserAId == userId || m.UserBId == userId))
            .Select(m => m.UserAId == userId ? m.UserBId : m.UserAId)
            .ToListAsync(ct);

        // ✅ Get users already shown today (delivery memory)
        // This includes DECK / MOMENTS / PENDING so user sees variety, not repeats.
        var shownToday = await _db.CandidateExposures.AsNoTracking()
            .Where(e => e.DateUtc == today && e.ViewerUserId == userId)
            .Select(e => e.ShownUserId)
            .Distinct()
            .ToListAsync(ct);

        // Build candidate query
        var candidateQuery = _db.UserProfiles.AsNoTracking()
            .Where(p => p.UserId != userId) // Not self
            .Where(p => !blockedIds.Contains(p.UserId)) // Not blocked
            .Where(p => !activeBalloonPartners.Contains(p.UserId)) // Not active balloon
            .Where(p => !shownToday.Contains(p.UserId)); // Not shown today

        // Gender/orientation reciprocity
        if (userInterestedIn.Count > 0)
        {
            candidateQuery = candidateQuery.Where(p => userInterestedIn.Contains(p.Gender));
        }

        // Age range (user's preferences)
        candidateQuery = candidateQuery.Where(p =>
            p.Age >= userPref.AgeMin && p.Age <= userPref.AgeMax);

        // Get candidate profiles with their preferences
        var candidates = await candidateQuery
            .Join(_db.UserPreferences,
                p => p.UserId,
                pref => pref.UserId,
                (p, pref) => new { Profile = p, Pref = pref })
            .ToListAsync(ct);

        _logger.LogInformation("[CandidatePool] After basic filters: {Count} candidates for user {UserId}",
            candidates.Count, userId);

        var eligible = new List<int>();
        var filtered = new Dictionary<string, int>
        {
            ["age_reciprocal"] = 0,
            ["gender_reciprocal"] = 0,
            ["distance"] = 0,
            ["relationship_structure"] = 0
        };

        foreach (var candidate in candidates)
        {
            // Reciprocal age check
            if (userProfile.Age < candidate.Pref.AgeMin || userProfile.Age > candidate.Pref.AgeMax)
            {
                filtered["age_reciprocal"]++;
                continue;
            }

            // Reciprocal gender check
            var candidateInterestedIn = new HashSet<string>();
            try
            {
                var parsed = JsonSerializer.Deserialize<string[]>(candidate.Pref.InterestedInJson);
                if (parsed != null && parsed.Length > 0)
                {
                    candidateInterestedIn = new HashSet<string>(parsed, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[CandidatePool] Failed to parse InterestedInJson for candidate {CandidateId}",
                    candidate.Profile.UserId);
            }

            if (candidateInterestedIn.Count > 0 && !candidateInterestedIn.Contains(userProfile.Gender))
            {
                filtered["gender_reciprocal"]++;
                continue;
            }

            // ✅ FIXED: Distance check (both ways) - only apply if BOTH users have location data
            if (userProfile.Lat.HasValue && userProfile.Lng.HasValue &&
                candidate.Profile.Lat.HasValue && candidate.Profile.Lng.HasValue)
            {
                var distance = CalculateDistance(
                    userProfile.Lat.Value, userProfile.Lng.Value,
                    candidate.Profile.Lat.Value, candidate.Profile.Lng.Value);

                // Check if user's preference is met
                if (distance > userPref.DistanceMiles)
                {
                    filtered["distance"]++;
                    continue;
                }

                // Check if candidate's preference is met
                if (distance > candidate.Pref.DistanceMiles)
                {
                    filtered["distance"]++;
                    continue;
                }
            }
            // ✅ If either user has no location, allow the match (don't filter on distance)

            // ✅ Relationship structure compatibility
            if (!IsRelationshipStructureCompatible(userPref.RelationshipStructure, candidate.Pref.RelationshipStructure))
            {
                filtered["relationship_structure"]++;
                continue;
            }

            eligible.Add(candidate.Profile.UserId);
        }

        _logger.LogInformation(
            "[CandidatePool] Found {Count} eligible candidates for user {UserId}. " +
            "Filtered: age_reciprocal={AgeFiltered}, gender_reciprocal={GenderFiltered}, " +
            "distance={DistanceFiltered}, relationship_structure={RelationshipFiltered}",
            eligible.Count, userId,
            filtered["age_reciprocal"],
            filtered["gender_reciprocal"],
            filtered["distance"],
            filtered["relationship_structure"]);

        return eligible;
    }

    private bool IsRelationshipStructureCompatible(
        RelationshipStructure userStructure,
        RelationshipStructure candidateStructure)
    {
        // MONO_ONLY ⟂ NONMONO_ONLY (incompatible)
        if (userStructure == RelationshipStructure.MONO_ONLY &&
            candidateStructure == RelationshipStructure.NONMONO_ONLY)
            return false;

        if (userStructure == RelationshipStructure.NONMONO_ONLY &&
            candidateStructure == RelationshipStructure.MONO_ONLY)
            return false;

        // OPEN is compatible with everything
        // MONO_ONLY is compatible with MONO_ONLY and OPEN
        // NONMONO_ONLY is compatible with NONMONO_ONLY and OPEN
        return true;
    }

    private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        // Haversine formula
        const double R = 3959; // Earth radius in miles

        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return R * c;
    }

    private double DegreesToRadians(double degrees) => degrees * Math.PI / 180;
}