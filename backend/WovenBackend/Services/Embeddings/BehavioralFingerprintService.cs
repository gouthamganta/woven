using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WovenBackend.Data;
using WovenBackend.Data.Entities;

namespace WovenBackend.Services.Embeddings;

/// <summary>
/// Computes the 16-dim behavioural fingerprint from MatchSignalLog data.
///
/// Dimensions (all normalised to [0, 1]; missing data → 0.5 neutral):
///  [0]  response_speed       — fast replies score high; 30-min half-life
///  [1]  message_volume       — avg MessageSent per candidate, cap 20
///  [2]  self_disclosure_mean — mean SelfDisclosureRatio across threads
///  [3]  disclosure_balance   — how close to 0.5 the ratio is (1=balanced)
///  [4]  trial_affinity       — P(TrialAccepted | any trial decision)
///  [5]  love_expressiveness  — (ChatNoteLove + MessageLove) per candidate
///  [6]  feedback_positivity  — mean ExplicitFeedback value
///  [7]  tile_curiosity       — mean TileDwell / 8 000 ms threshold
///  [8]  voice_curiosity      — mean VoiceDwell / 8 000 ms threshold
///  [9]  first_msg_speed      — quick first-message score; 24-h half-life
/// [10]  date_progression     — DateIdeaAccepted / (accepted + rejected + 1)
/// [11]  profile_exploration  — mean ProfileVisitDepth / 5 tiles
/// [12]  game_engagement      — count(GameCompleted) / 10
/// [13]  balloon_eagerness    — count(BalloonPop) / 20
/// [14]  engagement_breadth   — distinct candidates with signals / 50
/// [15]  signal_density       — total signal count / 500
/// </summary>
public class BehavioralFingerprintService : IBehavioralFingerprintService
{
    private static readonly TimeSpan SignalWindow = TimeSpan.FromDays(180);

    private readonly WovenDbContext _db;
    private readonly ILogger<BehavioralFingerprintService> _logger;

    public BehavioralFingerprintService(WovenDbContext db, ILogger<BehavioralFingerprintService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ComputeAsync(int userId, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - SignalWindow;

        var signals = await _db.MatchSignalLogs.AsNoTracking()
            .Where(s => s.ViewerId == userId && s.OccurredAt >= cutoff)
            .Select(s => new { s.EventType, s.EventValue, s.CandidateId })
            .ToListAsync(ct);

        var fp = Compute(signals.Select(s => (s.EventType, s.EventValue, s.CandidateId)).ToList());

        var existing = await _db.UserBehavioralFingerprints
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);

        var json = JsonSerializer.Serialize(fp);
        var now  = DateTime.UtcNow;

        if (existing != null)
        {
            existing.VectorJson  = json;
            existing.ComputedAt  = now;
        }
        else
        {
            _db.UserBehavioralFingerprints.Add(new UserBehavioralFingerprint
            {
                UserId     = userId,
                VectorJson = json,
                ComputedAt = now
            });
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogDebug("[BehavioralFingerprint] Computed for user {UserId} from {N} signals", userId, signals.Count);
    }

    public async Task<float[]?> GetAsync(int userId, CancellationToken ct = default)
    {
        var row = await _db.UserBehavioralFingerprints.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (row == null) return null;

        try { return JsonSerializer.Deserialize<float[]>(row.VectorJson); }
        catch { return null; }
    }

    // -----------------------------------------------------------------------
    // Pure computation — no I/O, fully testable
    // -----------------------------------------------------------------------

    internal static float[] Compute(List<(string EventType, float EventValue, int CandidateId)> signals)
    {
        // Bucket by type for fast lookup
        var byType = signals
            .GroupBy(s => s.EventType)
            .ToDictionary(g => g.Key, g => g.ToList());

        var allCandidates = signals.Select(s => s.CandidateId).Distinct().Count();
        var totalSignals  = signals.Count;

        return new[]
        {
            // [0] response_speed
            ComputeSpeed(byType, MatchSignalEventTypes.MessageResponseLatencyMs, halfLifeMs: 1_800_000),

            // [1] message_volume — total MessageSent / (candidates × 10 expected messages)
            ComputeNormCount(byType, MatchSignalEventTypes.MessageSent,
                cap: Math.Max(allCandidates * 10, 1)),

            // [2] self_disclosure_mean
            ComputeMeanOrNeutral(byType, MatchSignalEventTypes.SelfDisclosureRatio),

            // [3] disclosure_balance — how close mean is to 0.5
            DisclosureBalance(byType),

            // [4] trial_affinity
            TrialAffinity(byType),

            // [5] love_expressiveness — total loves / (candidates × 2 expected per candidate)
            ComputeNormCount(byType, MatchSignalEventTypes.ChatNoteLove, MatchSignalEventTypes.MessageLove,
                cap: Math.Max(allCandidates * 2, 1)),

            // [6] feedback_positivity
            ComputeMeanOrNeutral(byType, MatchSignalEventTypes.ExplicitFeedback),

            // [7] tile_curiosity
            ComputeNormMean(byType, MatchSignalEventTypes.TileDwell, scale: 8_000f),

            // [8] voice_curiosity
            ComputeNormMean(byType, MatchSignalEventTypes.VoiceDwell, scale: 8_000f),

            // [9] first_msg_speed
            ComputeSpeed(byType, MatchSignalEventTypes.TimeToFirstMessageMs, halfLifeMs: 86_400_000),

            // [10] date_progression
            DateProgression(byType),

            // [11] profile_exploration
            ComputeNormMean(byType, MatchSignalEventTypes.ProfileVisitDepth, scale: 5f),

            // [12] game_engagement
            ComputeNormCount(byType, MatchSignalEventTypes.GameCompleted, cap: 10),

            // [13] balloon_eagerness
            ComputeNormCount(byType, MatchSignalEventTypes.BalloonPop, cap: 20),

            // [14] engagement_breadth
            Math.Min(1f, allCandidates / 50f),

            // [15] signal_density
            Math.Min(1f, totalSignals / 500f)
        };
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static float ComputeSpeed(
        Dictionary<string, List<(string, float, int)>> byType,
        string eventType,
        double halfLifeMs)
    {
        if (!byType.TryGetValue(eventType, out var rows) || rows.Count == 0) return 0.5f;
        var mean = rows.Average(r => r.Item2);
        return (float)(1.0 / (1.0 + mean / halfLifeMs));
    }

    private static float ComputeMeanOrNeutral(
        Dictionary<string, List<(string, float, int)>> byType,
        string eventType)
    {
        if (!byType.TryGetValue(eventType, out var rows) || rows.Count == 0) return 0.5f;
        return Math.Clamp((float)rows.Average(r => r.Item2), 0f, 1f);
    }

    private static float ComputeNormMean(
        Dictionary<string, List<(string, float, int)>> byType,
        string eventType,
        float scale)
    {
        if (!byType.TryGetValue(eventType, out var rows) || rows.Count == 0) return 0.5f;
        return Math.Min(1f, (float)rows.Average(r => r.Item2) / scale);
    }

    private static float ComputeNormCount(
        Dictionary<string, List<(string, float, int)>> byType,
        string eventType,
        float cap)
    {
        var count = byType.TryGetValue(eventType, out var rows) ? rows.Count : 0;
        return Math.Min(1f, count / cap);
    }

    private static float ComputeNormCount(
        Dictionary<string, List<(string, float, int)>> byType,
        string typeA,
        string typeB,
        float cap)
    {
        var countA = byType.TryGetValue(typeA, out var ra) ? ra.Count : 0;
        var countB = byType.TryGetValue(typeB, out var rb) ? rb.Count : 0;
        return Math.Min(1f, (countA + countB) / cap);
    }

    private static float DisclosureBalance(
        Dictionary<string, List<(string, float, int)>> byType)
    {
        if (!byType.TryGetValue(MatchSignalEventTypes.SelfDisclosureRatio, out var rows) || rows.Count == 0)
            return 0.5f;
        var mean = (float)rows.Average(r => r.Item2);
        return 1f - 2f * Math.Abs(mean - 0.5f);
    }

    private static float TrialAffinity(
        Dictionary<string, List<(string, float, int)>> byType)
    {
        var accepted = byType.TryGetValue(MatchSignalEventTypes.TrialAccepted, out var ra) ? ra.Count : 0;
        var rejected = byType.TryGetValue(MatchSignalEventTypes.TrialRejected, out var rr) ? rr.Count : 0;
        var total    = accepted + rejected;
        return total == 0 ? 0.5f : (float)accepted / total;
    }

    private static float DateProgression(
        Dictionary<string, List<(string, float, int)>> byType)
    {
        var accepted = byType.TryGetValue(MatchSignalEventTypes.DateIdeaAccepted, out var ra) ? ra.Count : 0;
        var rejected = byType.TryGetValue(MatchSignalEventTypes.DateIdeaRejected, out var rr) ? rr.Count : 0;
        return (float)accepted / (accepted + rejected + 1f);
    }
}
