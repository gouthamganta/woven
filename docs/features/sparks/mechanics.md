# Spark Mechanics

**Last Updated:** 2026-08-17

---

## Daily Earning

### Constants

```csharp
private const int DailyEarnTenths = 50;  // 5.0 sparks
private const int MaxBalanceTenths = 100; // 10.0 sparks
```

### Earning Logic

**When:** First balance check after UTC midnight  
**Amount:** 5.0 sparks (50 tenths)  
**Cap:** Balance cannot exceed 10.0 sparks (100 tenths)

**Lazy evaluation:**  
Sparks are **not** granted at midnight via batch job. Instead:
1. User opens `/moments` endpoint → `GetBalanceAsync()` called
2. Service checks `wallet.LastEarnedDate < today`
3. If true → add 5 sparks (capped at 10), update `LastEarnedDate`

**Overflow behavior:**
```
Current: 8.5 sparks (85 tenths)
Earn: 5.0 sparks (50 tenths)
Result: 10.0 sparks (100 tenths) — 3.5 sparks lost
```

### Code Path

**Service:** `SparkWalletService.GetBalanceAsync()`

```csharp
private async Task EarnDailyIfDueAsync(SparkWallet wallet, CancellationToken ct)
{
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    if (wallet.LastEarnedDate != null && wallet.LastEarnedDate >= today) return;

    wallet.BalanceTenths = Math.Min(MaxBalanceTenths, wallet.BalanceTenths + DailyEarnTenths);
    wallet.LastEarnedDate = today;
    wallet.UpdatedAt = DateTimeOffset.UtcNow;
    await _db.SaveChangesAsync(ct);
}
```

**Idempotency:** Multiple calls to `GetBalanceAsync()` on same day → earns only once (guarded by `LastEarnedDate`).

---

## Spending

### Constants

```csharp
private const int SpendTenths = 10; // 1.0 spark per action
```

### Spend Conditions

**Trigger:** User chooses MAGICAL (◈) or LOGICAL (◇) on Drawn tab card  
**Cost:** 1.0 spark (10 tenths)  
**Gate:** Must have `balance >= 1.0 spark` before action allowed

**Not spent on:**
- Today tab actions (free, gated by `DailyInteraction.TotalUsed`)
- PASS actions on Drawn tab (free)
- Opening matches, sending messages, etc.

### Transaction Isolation

**Level:** `IsolationLevel.Serializable`  
**Why:** Prevent double-spend if user rapid-taps two Drawn actions concurrently.

**Flow:**
1. Begin serializable transaction
2. Load wallet (or create with 5 sparks if new user)
3. Earn daily sparks **inside transaction** (prevents race condition)
4. Check `balance >= 1 spark`
5. If insufficient → rollback + return `INSUFFICIENT_SPARKS`
6. Deduct 1 spark
7. Commit transaction
8. Return new balance

### Code Path

**Service:** `SparkWalletService.TrySpendAsync()`

```csharp
public async Task<SpendResult> TrySpendAsync(int userId, CancellationToken ct = default)
{
    await using var tx = await _db.Database.BeginTransactionAsync(
        System.Data.IsolationLevel.Serializable, ct);

    var wallet = await _db.SparkWallets
        .FirstOrDefaultAsync(w => w.UserId == userId, ct);

    if (wallet is null)
    {
        wallet = new SparkWallet { UserId = userId, BalanceTenths = DailyEarnTenths };
        _db.SparkWallets.Add(wallet);
        await _db.SaveChangesAsync(ct);
    }

    // Earn daily inside transaction to prevent double-earn
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    if (wallet.LastEarnedDate == null || wallet.LastEarnedDate < today)
    {
        wallet.BalanceTenths = Math.Min(MaxBalanceTenths, wallet.BalanceTenths + DailyEarnTenths);
        wallet.LastEarnedDate = today;
    }

    if (wallet.BalanceTenths < SpendTenths)
    {
        await tx.RollbackAsync(ct);
        return new SpendResult(false, "INSUFFICIENT_SPARKS", wallet.BalanceTenths / 10m);
    }

    wallet.BalanceTenths -= SpendTenths;
    wallet.UpdatedAt = DateTimeOffset.UtcNow;
    await _db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);

    return new SpendResult(true, null, wallet.BalanceTenths / 10m);
}
```

**Endpoint:** `POST /moments/choose` (Drawn tab actions)

```csharp
// Inside MomentsEndpoints.cs MapMomentsEndpoints()
var sparkSpend = await sparks.TrySpendAsync(me, ct);
if (!sparkSpend.Allowed)
    return Results.BadRequest(new { error = sparkSpend.DenyReason, sparkBalance = sparkSpend.Balance });
```

---

## Storage Format

### Why Tenths?

**Problem:** Floating point drift — `0.5 + 0.5 + 0.5 = 1.5000000000000002`

**Solution:** Store as integers (tenths)
- `50 tenths = 5.0 sparks`
- `5 tenths = 0.5 sparks`
- `100 tenths = 10.0 sparks (max)`

**Display conversion:**
```csharp
public async Task<decimal> GetBalanceAsync(int userId, CancellationToken ct = default)
{
    var wallet = await EnsureWalletAsync(userId, ct);
    await EarnDailyIfDueAsync(wallet, ct);
    return wallet.BalanceTenths / 10m;
}
```

### Database Schema

```sql
CREATE TABLE spark_wallets (
    user_id INTEGER PRIMARY KEY,
    balance_tenths INTEGER NOT NULL DEFAULT 50,    -- 5.0 sparks initial
    last_earned_date DATE,                          -- UTC date of last earn
    updated_at TIMESTAMPTZ NOT NULL
);
```

**Indexes:** Primary key on `user_id` (no additional indexes needed)

---

## Edge Cases

### New User
- First `GetBalanceAsync()` or `TrySpendAsync()` → creates wallet with 5 sparks
- No separate onboarding flow needed

### Midnight Boundary
- User at 11:59 PM UTC with 2 sparks
- Opens app at 12:01 AM UTC → earns 5 sparks → balance now 7 sparks
- Opens app again at 12:05 AM UTC → no additional earn (already earned today)

### Max Balance Enforcement
- User has 9.5 sparks at 11:59 PM
- Midnight passes → opens app → earns 5 sparks
- Actual result: `min(100, 95 + 50) = 100 tenths = 10.0 sparks`
- Lost: 4.5 sparks (overflow)

### Concurrent Spend
- User taps two Drawn cards rapidly (< 200ms apart)
- Both requests enter `TrySpendAsync()` concurrently
- Serializable isolation → one succeeds, one waits
- Second request sees updated balance from first commit
- If first spend left balance at 0 → second fails with `INSUFFICIENT_SPARKS`

### Wallet Corruption (Negative Balance)
**Impossible:** Serializable transaction + explicit checks prevent this.  
**Safeguard:** `wallet.BalanceTenths < SpendTenths` check before deduction.

---

## Related Files

- **Backend:** `backend/WovenBackend/Services/Moments/SparkWalletService.cs`
- **Endpoint:** `backend/WovenBackend/Endpoints/SparkEndpoints.cs`
- **Entity:** `backend/WovenBackend/data/Entities/SparkWallet.cs`
- **Migration:** `backend/WovenBackend/Migrations/20260524204334_AddSparkWallets.cs`
