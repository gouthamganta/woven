using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Services.Moments;

namespace WovenBackend.Services.Matchmaking;

public class CandidatePoolService : ICandidatePoolService
{
    private const float TrustGate = 0.25f;

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

        // ── Build all filters in SQL — no in-memory loops ─────────────────────
        // Gender reciprocity: candidate's InterestedInJson contains the viewer's gender.
        // EF Core can't JSON-query arrays in PostgreSQL, so we use a raw SQL contains check.
        // This is safe — userProfile.Gender comes from our own DB, never from user input.
        var viewerGender = userProfile.Gender;

        var eligible = await _db.UserProfiles.AsNoTracking()
            .Join(_db.UserPreferences,
                p    => p.UserId,
                pref => pref.UserId,
                (p, pref) => new { Profile = p, Pref = pref })
            .Join(_db.Users.AsNoTracking(),
                pp   => pp.Profile.UserId,
                u    => u.Id,
                (pp, u) => new { pp.Profile, pp.Pref, User = u })
            .Where(x => x.Profile.UserId != userId)
            .Where(x => !blockedIds.Contains(x.Profile.UserId))
            .Where(x => !activeBalloonPartners.Contains(x.Profile.UserId))
            .Where(x => !shownToday.Contains(x.Profile.UserId))
            // Viewer's gender preference filter
            .Where(x => !userInterestedIn.Any() || userInterestedIn.Contains(x.Profile.Gender))
            // Viewer's age preference filter
            .Where(x => x.Profile.Age >= userPref.AgeMin && x.Profile.Age <= userPref.AgeMax)
            // Trust gate in SQL — never load low-trust users into application memory
            .Where(x => x.User.TrustScore >= TrustGate)
            // Reciprocal gender filter — candidate must be interested in viewer's gender
            // JSON contains check: InterestedInJson like '%"<gender>"%'
            .Where(x => string.IsNullOrEmpty(viewerGender) ||
                        x.Pref.InterestedInJson.Contains("\"" + viewerGender + "\""))
            .Select(x => x.Profile.UserId)
            .ToListAsync(ct);

        _logger.LogInformation(
            "[CandidatePool] Found {Count} eligible candidates for user {UserId} (all filters in SQL)",
            eligible.Count, userId);

        return eligible;
    }

}