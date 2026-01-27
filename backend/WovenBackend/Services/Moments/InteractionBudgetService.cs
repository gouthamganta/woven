using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.data.Entities.Moments;

namespace WovenBackend.Services.Moments;

public class InteractionBudgetService
{
    private readonly WovenDbContext _db;

    public InteractionBudgetService(WovenDbContext db)
    {
        _db = db;
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

        return new SpendResult(true, null, row.TotalUsed, row.PendingUsed);
    }
}
