using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.Services.Embeddings;

namespace WovenBackend.Services.Matchmaking;

/// <summary>
/// Disjoint LinUCB bandit — 24-dim context (8 pillar + 16 behavioural fingerprint).
///
/// UCB score for candidate c given user model (A_inv, b):
///   θ = A_inv · b
///   p = θ · x_c  +  α × sqrt(x_c · A_inv · x_c)
///
/// Returned as a boost on top of TotalScore (0–100 scale).
/// Boost is capped at 10 so the bandit cannot override a very strong/weak match.
/// </summary>
public class LinUcbService : ILinUcbService
{
    private const int Dim = 24;
    private const double MaxBoost = 10.0;
    // Model trusted once we have this many observations (below → heavier exploration)
    private const int TrustThreshold = 10;

    private readonly WovenDbContext _db;
    private readonly IBehavioralFingerprintService _fingerprints;
    private readonly ILogger<LinUcbService> _logger;

    public LinUcbService(
        WovenDbContext db,
        IBehavioralFingerprintService fingerprints,
        ILogger<LinUcbService> logger)
    {
        _db = db;
        _fingerprints = fingerprints;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    // Scoring
    // ------------------------------------------------------------------

    public async Task<Dictionary<int, double>> GetBoostMapAsync(
        int userId,
        List<int> candidateIds,
        float alpha = 1.0f,
        CancellationToken ct = default)
    {
        var result = new Dictionary<int, double>(candidateIds.Count);
        if (candidateIds.Count == 0) return result;

        // Load user model (may be null for new users)
        var model = await _db.LinUcbUserModels.AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId, ct);

        var (aInv, b) = LoadOrInitModel(model);
        var theta     = MatVec(aInv, b);

        // Load candidate pillar embeddings and fingerprints in batch
        var pillarVecs = await _db.UserVectors.AsNoTracking()
            .Where(v => candidateIds.Contains(v.UserId) && v.PillarEmbedding != null)
            .GroupBy(v => v.UserId)
            .Select(g => g.OrderByDescending(x => x.Version).First())
            .Select(v => new { v.UserId, v.PillarEmbedding })
            .ToListAsync(ct);

        var fpRows = await _db.UserBehavioralFingerprints.AsNoTracking()
            .Where(f => candidateIds.Contains(f.UserId))
            .Select(f => new { f.UserId, f.VectorJson })
            .ToListAsync(ct);

        var pillarMap = pillarVecs.ToDictionary(v => v.UserId, v => v.PillarEmbedding!.ToArray());
        var fpMap = fpRows.ToDictionary(
            f => f.UserId,
            f => DeserializeFloats(f.VectorJson));

        // Compute UCB for each candidate
        double maxUcb = 0;
        var rawUcb = new Dictionary<int, double>(candidateIds.Count);

        foreach (var cid in candidateIds)
        {
            var x = BuildContext(
                pillarMap.TryGetValue(cid, out var p) ? p : null,
                fpMap.TryGetValue(cid, out var fp) ? fp : null);

            var exploit   = Dot(theta, x);
            var xAInvX    = Dot(x, MatVec(aInv, x));
            var explore   = alpha * Math.Sqrt(Math.Max(0, xAInvX));
            var ucb       = exploit + explore;

            rawUcb[cid] = ucb;
            if (ucb > maxUcb) maxUcb = ucb;
        }

        // Normalise to [0, MaxBoost] — preserve relative ordering
        var scale = maxUcb > 0 ? MaxBoost / maxUcb : 0;
        foreach (var (cid, ucb) in rawUcb)
            result[cid] = Math.Clamp(ucb * scale, 0, MaxBoost);

        // For users with few observations, inflate exploration uniformly
        if ((model?.ObservationCount ?? 0) < TrustThreshold)
        {
            foreach (var cid in candidateIds)
                if (!result.ContainsKey(cid))
                    result[cid] = MaxBoost * 0.5; // uniform exploration bonus
        }

        return result;
    }

    // ------------------------------------------------------------------
    // Model update (Sherman-Morrison)
    // ------------------------------------------------------------------

    public async Task UpdateAsync(
        int userId,
        IReadOnlyList<(float[] Context, float Reward)> observations,
        CancellationToken ct = default)
    {
        if (observations.Count == 0) return;

        var model = await _db.LinUcbUserModels
            .FirstOrDefaultAsync(m => m.UserId == userId, ct);

        var (aInv, b) = LoadOrInitModel(model);
        int obsCount  = model?.ObservationCount ?? 0;

        foreach (var (x, r) in observations)
        {
            if (x.Length != Dim) continue;

            // Sherman-Morrison: A_inv_new = A_inv - (A_inv·x)(x^T·A_inv) / (1 + x^T·A_inv·x)
            var aInvX  = MatVec(aInv, x);
            var denom  = 1.0 + Dot(x, aInvX);
            for (int row = 0; row < Dim; row++)
                for (int col = 0; col < Dim; col++)
                    aInv[row * Dim + col] -= (float)(aInvX[row] * aInvX[col] / denom);

            // b += r · x
            for (int j = 0; j < Dim; j++)
                b[j] += r * x[j];

            obsCount++;
        }

        var now = DateTime.UtcNow;
        if (model == null)
        {
            _db.LinUcbUserModels.Add(new LinUcbUserModel
            {
                UserId           = userId,
                Dim              = Dim,
                AInvJson         = JsonSerializer.Serialize(aInv),
                BJson            = JsonSerializer.Serialize(b),
                ObservationCount = obsCount,
                UpdatedAt        = now
            });
        }
        else
        {
            model.AInvJson         = JsonSerializer.Serialize(aInv);
            model.BJson            = JsonSerializer.Serialize(b);
            model.ObservationCount = obsCount;
            model.UpdatedAt        = now;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogDebug("[LinUCB] Updated model for user {UserId} — {N} new obs, total={T}",
            userId, observations.Count, obsCount);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds a 24-dim context: [pillar[0..7] / 100, fingerprint[0..15]].
    /// Missing dims default to 0.5 (neutral / unknown).
    /// </summary>
    internal static float[] BuildContext(float[]? pillar, float[]? fingerprint)
    {
        var ctx = new float[Dim];
        for (int i = 0; i < 8; i++)
            ctx[i] = (pillar != null && pillar.Length > i)
                ? Math.Clamp(pillar[i] / 100f, 0f, 1f)
                : 0.5f;
        for (int i = 0; i < 16; i++)
            ctx[8 + i] = (fingerprint != null && fingerprint.Length > i)
                ? Math.Clamp(fingerprint[i], 0f, 1f)
                : 0.5f;
        return ctx;
    }

    private static (float[] AInv, float[] B) LoadOrInitModel(LinUcbUserModel? model)
    {
        float[] aInv, b;

        if (model == null || model.AInvJson.Length <= 2)
        {
            // Initialise A_inv to identity
            aInv = new float[Dim * Dim];
            for (int i = 0; i < Dim; i++)
                aInv[i * Dim + i] = 1f;
            b = new float[Dim];
        }
        else
        {
            aInv = DeserializeFloats(model.AInvJson) ?? IdentityMatrix(Dim);
            b    = DeserializeFloats(model.BJson)    ?? new float[Dim];
        }

        return (aInv, b);
    }

    private static float[] IdentityMatrix(int d)
    {
        var m = new float[d * d];
        for (int i = 0; i < d; i++) m[i * d + i] = 1f;
        return m;
    }

    // A (d×d row-major) · v (d) → result (d)
    private static float[] MatVec(float[] a, float[] v)
    {
        var res = new float[Dim];
        for (int row = 0; row < Dim; row++)
        {
            float s = 0;
            for (int col = 0; col < Dim; col++)
                s += a[row * Dim + col] * v[col];
            res[row] = s;
        }
        return res;
    }

    private static double Dot(float[] a, float[] b)
    {
        double s = 0;
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            s += a[i] * b[i];
        return s;
    }

    private static float[]? DeserializeFloats(string json)
    {
        try { return JsonSerializer.Deserialize<float[]>(json); }
        catch { return null; }
    }
}
