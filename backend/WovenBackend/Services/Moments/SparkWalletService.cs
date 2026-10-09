using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;

namespace WovenBackend.Services.Moments;

public class SparkWalletService
{
    private const int DailyEarnTenths = 50;  // 5.0 sparks
    private const int MaxBalanceTenths = 100; // 10.0 sparks
    private const int SpendTenths = 10;       // 1.0 spark per LikedYou action
    private const int GhostRefundTenths = 5;  // 0.5 sparks on ghost

    private readonly WovenDbContext _db;

    public SparkWalletService(WovenDbContext db) => _db = db;

    public sealed record SpendResult(bool Allowed, string? DenyReason, decimal Balance);

    /// <summary>Returns current balance, earning daily sparks if not yet earned today.</summary>
    public async Task<decimal> GetBalanceAsync(int userId, CancellationToken ct = default)
    {
        var wallet = await EnsureWalletAsync(userId, ct);
        await EarnDailyIfDueAsync(wallet, ct);
        return wallet.BalanceTenths / 10m;
    }

    /// <summary>Attempt to spend 1 spark for a Liked You action.</summary>
    public async Task<SpendResult> TrySpendAsync(int userId, CancellationToken ct = default)
    {
        // The whole serializable transaction runs inside the execution strategy so a
        // transient failure retries the full read-earn-spend unit, not a fragment of it.
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            // Drop any tracked wallet — left over from a failed attempt or loaded earlier
            // in the request — so the read below sees committed DB values.
            DetachWallet(userId);

            await using var tx = await _db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, ct);
            try
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);

                var wallet = await _db.SparkWallets
                    .FirstOrDefaultAsync(w => w.UserId == userId, ct);

                if (wallet is null)
                {
                    wallet = new SparkWallet
                    {
                        UserId = userId,
                        BalanceTenths = DailyEarnTenths,
                        LastEarnedDate = today  // FIX: Set to today to prevent double-earn
                    };
                    _db.SparkWallets.Add(wallet);
                    await _db.SaveChangesAsync(ct);
                }

                // Earn daily inside the transaction to prevent double-earn
                if (wallet.LastEarnedDate == null || wallet.LastEarnedDate < today)
                {
                    wallet.BalanceTenths = Math.Min(MaxBalanceTenths, wallet.BalanceTenths + DailyEarnTenths);
                    wallet.LastEarnedDate = today;
                }

                if (wallet.BalanceTenths < SpendTenths)
                {
                    var balance = wallet.BalanceTenths / 10m;
                    await tx.RollbackAsync(ct);
                    DetachWallet(userId);
                    return new SpendResult(false, "INSUFFICIENT_SPARKS", balance);
                }

                wallet.BalanceTenths -= SpendTenths;
                wallet.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return new SpendResult(true, null, wallet.BalanceTenths / 10m);
            }
            catch
            {
                // Don't carry Added/Modified state from a failed attempt into the retry.
                DetachWallet(userId);
                throw;
            }
        });
    }

    private void DetachWallet(int userId)
    {
        foreach (var entry in _db.ChangeTracker.Entries<SparkWallet>()
                     .Where(e => e.Entity.UserId == userId)
                     .ToList())
            entry.State = EntityState.Detached;
    }

    /// <summary>Add 0.5 sparks — called when match ends as ghost (no messages exchanged).</summary>
    public async Task GhostRefundAsync(int userId, CancellationToken ct = default)
    {
        var wallet = await EnsureWalletAsync(userId, ct);
        wallet.BalanceTenths = Math.Min(MaxBalanceTenths, wallet.BalanceTenths + GhostRefundTenths);
        wallet.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<SparkWallet> EnsureWalletAsync(int userId, CancellationToken ct)
    {
        var wallet = await _db.SparkWallets
            .FirstOrDefaultAsync(w => w.UserId == userId, ct);

        if (wallet is null)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            wallet = new SparkWallet
            {
                UserId = userId,
                BalanceTenths = DailyEarnTenths,
                LastEarnedDate = today  // FIX: Set to today to prevent double-earn
            };
            _db.SparkWallets.Add(wallet);
            await _db.SaveChangesAsync(ct);
        }

        return wallet;
    }

    private async Task EarnDailyIfDueAsync(SparkWallet wallet, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (wallet.LastEarnedDate != null && wallet.LastEarnedDate >= today) return;

        wallet.BalanceTenths = Math.Min(MaxBalanceTenths, wallet.BalanceTenths + DailyEarnTenths);
        wallet.LastEarnedDate = today;
        wallet.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}
