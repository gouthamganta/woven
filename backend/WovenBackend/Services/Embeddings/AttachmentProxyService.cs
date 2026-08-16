using Microsoft.EntityFrameworkCore;
using Pgvector;
using System.Text.Json;
using WovenBackend.Data;

namespace WovenBackend.Services.Embeddings;

/// <summary>
/// ECHO Phase 5 (Step 12) — derives a 4-dim attachment proxy from the
/// behavioral fingerprint rather than raw chat message statistics.
///
/// Attachment dimensions (each [0, 1], independent — do NOT sum to 1):
///
///   [0] Secure       — balanced disclosure, willing to deepen, positive feedback
///   [1] Anxious      — fast responses, high activity density, over-sharing skew
///   [2] Avoidant     — slow responses, low expressiveness, avoids trials/dates
///   [3] Disorganised — simultaneous approach-avoidance (min of anxious, avoidant)
///
/// Fingerprint index reference (computed by BehavioralFingerprintService):
///   [0]  response_speed        [6]  feedback_positivity
///   [1]  message_volume        [7]  tile_curiosity
///   [2]  self_disclosure_mean  [8]  voice_curiosity
///   [3]  disclosure_balance    [9]  first_msg_speed
///   [4]  trial_affinity        [10] date_progression
///   [5]  love_expressiveness   [15] signal_density
///
/// Requires BehavioralFingerprintService to run first (guaranteed by EmbeddingBatchWorker order).
/// </summary>
public class AttachmentProxyService : IAttachmentProxyService
{
    private readonly WovenDbContext _db;
    private readonly IBehavioralFingerprintService _fingerprints;
    private readonly ILogger<AttachmentProxyService> _logger;

    public AttachmentProxyService(
        WovenDbContext db,
        IBehavioralFingerprintService fingerprints,
        ILogger<AttachmentProxyService> logger)
    {
        _db           = db;
        _fingerprints = fingerprints;
        _logger       = logger;
    }

    public async Task ComputeAttachmentProxyAsync(int userId, CancellationToken ct = default)
    {
        var fp = await _fingerprints.GetAsync(userId, ct);
        if (fp == null || fp.Length < 16)
        {
            _logger.LogInformation("[AttachmentProxy] No fingerprint for user {UserId} — skipping", userId);
            return;
        }

        var attachment = Derive(fp);

        var vector = await _db.UserVectors
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);

        if (vector == null) return;

        vector.AttachmentProxyEmbedding = new Vector(attachment);
        vector.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogDebug(
            "[AttachmentProxy] user={UserId} secure={S:F2} anxious={An:F2} avoidant={Av:F2} disorg={D:F2}",
            userId, attachment[0], attachment[1], attachment[2], attachment[3]);
    }

    // -----------------------------------------------------------------------
    // Pure mapping — no I/O, testable
    // -----------------------------------------------------------------------

    internal static float[] Derive(float[] fp)
    {
        // Secure: balanced disclosure + willing to deepen + positive outcomes
        var secure = 0.30f * fp[3]   // disclosure_balance
                   + 0.25f * fp[4]   // trial_affinity
                   + 0.25f * fp[6]   // feedback_positivity
                   + 0.20f * fp[10]; // date_progression

        // Anxious: hypervigilant responsiveness + activity flooding + over-sharing skew
        var anxious = 0.30f * fp[0]            // response_speed (fast = high)
                    + 0.20f * fp[9]            // first_msg_speed (eager initiator)
                    + 0.25f * fp[15]           // signal_density (high activity)
                    + 0.25f * (1f - fp[3]);    // 1 - disclosure_balance (over-sharing)

        // Avoidant: slow to respond, unexpressive, rejects deepening
        var avoidant = 0.25f * (1f - fp[0])   // 1 - response_speed
                     + 0.25f * (1f - fp[5])   // 1 - love_expressiveness
                     + 0.25f * (1f - fp[4])   // 1 - trial_affinity
                     + 0.25f * (1f - fp[10]); // 1 - date_progression

        // Disorganised: approach-avoidance coexist — captured by the minimum of both
        var disorganised = Math.Min(anxious, avoidant);

        return new[]
        {
            Math.Clamp(secure,       0f, 1f),
            Math.Clamp(anxious,      0f, 1f),
            Math.Clamp(avoidant,     0f, 1f),
            Math.Clamp(disorganised, 0f, 1f)
        };
    }
}
