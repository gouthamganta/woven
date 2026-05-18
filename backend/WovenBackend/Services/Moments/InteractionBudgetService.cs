using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.data.Entities.Moments;

namespace WovenBackend.Services.Moments;

public class InteractionBudgetService
{
    private readonly WovenDbContext _db;
    private readonly WovenBackend.Services.ICacheService _cache;

    public InteractionBudgetService(WovenDbContext db, WovenBackend.Services.ICacheService cache)
    {
        _db = db;
        _cache = cache;
    }

    public enum SpendType
    {
        Moment = 1,   // consumes total_used
        Pending = 2   // consumes total_used + pending_used
    }

    public sealed record SpendResult(
        bool Allowed,
        string? DenyReason,
        int TotalUsed,
        int PendingUsed
    );

    public async Task<SpendResult> TrySpendAsync(int userId, SpendType type, CancellationToken ct = default)
    {
        var today = MomentsRules.UtcToday();

        // Phase 1B: Redis fast-gate — reject immediately if the counter already shows cap reached.
        // Returns -1 when Redis is unavailable; in that case we skip the gate and fall through to DB.
        var totalKey   = WovenBackend.Services.CacheKeys.SparkCounter(userId, today);
        var pendingKey = WovenBackend.Services.CacheKeys.PendingCounter(userId, today);

        var cachedTotal = await _cache.GetCounterAsync(totalKey, ct);
        if (cachedTotal >= 0 && cachedTotal >= MomentsRules.DailyTotalCap)
            return new SpendResult(false, "DAILY_TOTAL_CAP_REACHED", (int)cachedTotal, 0);

        if (type == SpendType.Pending)
        {
            var cachedPending = await _cache.GetCounterAsync(pendingKey, ct);
            if (cachedPending >= 0 && cachedPending >= MomentsRules.DailyPendingCap)
                return new SpendResult(false, "DAILY_PENDING_CAP_REACHED", (int)cachedTotal, (int)cachedPending);
        }

        // Use a serializable transaction so two concurrent spends can't both succeed.
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);

        var row = await _db.DailyInteractions
            .SingleOrDefaultAsync(x => x.UserId == userId && x.DateUtc == today, ct);

        if (row is null)
        {
            row = new DailyInteraction
            {
                UserId = userId,
                DateUtc = today,
                TotalUsed = 0,
                PendingUsed = 0,
                UpdatedAt = MomentsRules.NowUtc()
            };
            _db.DailyInteractions.Add(row);
            await _db.SaveChangesAsync(ct);
        }

        var total = row.TotalUsed;
        var pending = row.PendingUsed;

        // Check caps
        if (total >= MomentsRules.DailyTotalCap)
        {
            await tx.RollbackAsync(ct);
            return new SpendResult(false, "DAILY_TOTAL_CAP_REACHED", total, pending);
        }

        if (type == SpendType.Pending && pending >= MomentsRules.DailyPendingCap)
        {
            await tx.RollbackAsync(ct);
            return new SpendResult(false, "DAILY_PENDING_CAP_REACHED", total, pending);
        }

        // Spend
        row.TotalUsed = (short)(row.TotalUsed + 1);
        if (type == SpendType.Pending)
            row.PendingUsed = (short)(row.PendingUsed + 1);

        row.UpdatedAt = MomentsRules.NowUtc();

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // Phase 1B: sync Redis counters after a successful DB commit (best-effort)
        var midnight = WovenBackend.Services.CacheTtl.UntilMidnightUtc();
        await _cache.IncrementAsync(totalKey, midnight, ct);
        if (type == SpendType.Pending)
            await _cache.IncrementAsync(pendingKey, midnight, ct);

        return new SpendResult(true, null, row.TotalUsed, row.PendingUsed);
    }

    public async Task RefundSparkAsync(int userId, CancellationToken ct = default)
    {
        var today = MomentsRules.UtcToday();
        var totalKey = WovenBackend.Services.CacheKeys.SparkCounter(userId, today);

        // Decrement Redis counter (floor 0) — read-then-set is acceptable for refund ops
        var current = await _cache.GetCounterAsync(totalKey, ct);
        if (current > 0)
            await _cache.SetAsync(totalKey, (current - 1).ToString(), WovenBackend.Services.CacheTtl.UntilMidnightUtc(), ct);

        // Decrement DB (floor 0)
        var row = await _db.DailyInteractions
            .FirstOrDefaultAsync(x => x.UserId == userId && x.DateUtc == today, ct);

        if (row != null && row.TotalUsed > 0)
        {
            row.TotalUsed = (short)(row.TotalUsed - 1);
            row.UpdatedAt = MomentsRules.NowUtc();
            await _db.SaveChangesAsync(ct);
        }
    }
}
