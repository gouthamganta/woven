# Backend: Spark Implementation

**Last Updated:** 2026-08-17

---

## Service Architecture

### SparkWalletService

**File:** `backend/WovenBackend/Services/Moments/SparkWalletService.cs`  
**Namespace:** `WovenBackend.Services.Moments`  
**Registration:** `Program.cs` → `builder.Services.AddScoped<SparkWalletService>()`

**Responsibilities:**
- Track spark balance per user
- Earn daily sparks (lazy evaluation)
- Spend sparks for Drawn tab actions
- Issue ghost refunds

**Not responsible for:**
- Daily interaction caps (handled by `InteractionBudgetService`)
- Match state changes (handled by `ChatEndpoints`)
- Notifications (handled by `INotificationService`)

---

## Public API

### GetBalanceAsync

```csharp
/// <summary>Returns current balance, earning daily sparks if not yet earned today.</summary>
public async Task<decimal> GetBalanceAsync(int userId, CancellationToken ct = default)
```

**Behavior:**
1. Load wallet (create if missing)
2. Check if daily earn is due → add 5 sparks (capped at 10)
3. Return balance as decimal (e.g., `7.5m`)

**Used by:**
- `GET /moments` — include balance in response
- `GET /sparks/balance` — dedicated balance endpoint (rarely used)

**Example:**
```csharp
var sparkBalance = await sparks.GetBalanceAsync(userId, ct);
// Returns: 7.5m
```

### TrySpendAsync

```csharp
/// <summary>Attempt to spend 1 spark for a Liked You action.</summary>
public async Task<SpendResult> TrySpendAsync(int userId, CancellationToken ct = default)
```

**Returns:** `SpendResult(bool Allowed, string? DenyReason, decimal Balance)`

**Behavior:**
1. Begin **serializable** transaction
2. Load wallet (create if missing)
3. Earn daily sparks **inside transaction** (prevents race condition)
4. Check `balance >= 1.0 spark`
5. If insufficient → rollback, return `(false, "INSUFFICIENT_SPARKS", currentBalance)`
6. Deduct 1 spark, commit, return `(true, null, newBalance)`

**Used by:**
- `POST /moments/choose` (Drawn tab actions only)

**Example:**
```csharp
var result = await sparks.TrySpendAsync(userId, ct);
if (!result.Allowed)
    return Results.BadRequest(new { error = result.DenyReason, sparkBalance = result.Balance });

// Continue with match logic...
```

### GhostRefundAsync

```csharp
/// <summary>Add 0.5 sparks — called when match ends as ghost (no messages exchanged).</summary>
public async Task GhostRefundAsync(int userId, CancellationToken ct = default)
```

**Behavior:**
1. Load wallet (create if missing)
2. Add 0.5 sparks (capped at 10.0 max)
3. Save changes

**Used by:**
- `POST /chats/{threadId}/trial-decision` (trial END with no messages)
- Balloon expiration handler (no messages)
- Block handler (no messages)

**Example:**
```csharp
if (noMessages)
{
    _ = Task.Run(async () =>
    {
        try { await sparks.GhostRefundAsync(userAId); } catch { }
        try { await sparks.GhostRefundAsync(userBId); } catch { }
    });
}
```

---

## Database Schema

### Table: `spark_wallets`

**Migration:** `20260524204334_AddSparkWallets.cs`

```sql
CREATE TABLE spark_wallets (
    user_id INTEGER PRIMARY KEY,
    balance_tenths INTEGER NOT NULL DEFAULT 50,
    last_earned_date DATE,
    updated_at TIMESTAMPTZ NOT NULL
);
```

**Columns:**

| Column | Type | Nullable | Description |
|---|---|---|---|
| `user_id` | `INTEGER` | NOT NULL | PK, FK to `users.id` |
| `balance_tenths` | `INTEGER` | NOT NULL | Balance * 10 (e.g., 75 = 7.5 sparks) |
| `last_earned_date` | `DATE` | NULL | UTC date of last daily earn |
| `updated_at` | `TIMESTAMPTZ` | NOT NULL | Last balance change timestamp |

**Indexes:**
- **Primary key:** `user_id` (implicit unique index)
- **No additional indexes** (table accessed via PK only)

**Constraints:**
- `balance_tenths >= 0` (enforced in code, not DB constraint)
- `balance_tenths <= 100` (10.0 sparks max, enforced in code)

---

## Entity Model

**File:** `backend/WovenBackend/data/Entities/SparkWallet.cs`

```csharp
[Table("spark_wallets")]
public class SparkWallet
{
    [Column("user_id")]
    public int UserId { get; set; }

    // Stored as tenths to support 0.5 increments without floating point drift.
    // balance_tenths = 50 → 5.0 sparks. Max 100 → 10.0 sparks.
    [Column("balance_tenths")]
    public int BalanceTenths { get; set; } = 50;

    [Column("last_earned_date")]
    public DateOnly? LastEarnedDate { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

**Why `balance_tenths`?**  
Avoids floating-point drift (e.g., `0.5 + 0.5 + 0.5 = 1.5000000000000002`).

**Conversion:**
- Display: `balance_tenths / 10m` → `7.5m`
- Storage: `7.5m * 10` → `75`

---

## DbContext Integration

**File:** `backend/WovenBackend/data/WovenDbContext.cs`

```csharp
public class WovenDbContext : DbContext
{
    public DbSet<SparkWallet> SparkWallets { get; set; } = null!;
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SparkWallet>()
            .HasKey(w => w.UserId);
        
        modelBuilder.Entity<SparkWallet>()
            .Property(w => w.BalanceTenths)
            .HasDefaultValue(50);  // 5.0 sparks for new users
    }
}
```

---

## Endpoints

### GET /sparks/balance

**File:** `backend/WovenBackend/Endpoints/SparkEndpoints.cs`

```csharp
group.MapGet("/balance", async (
    SparkWalletService wallet,
    HttpContext http,
    CancellationToken ct) =>
{
    var userId = GetUserId(http.User);
    var balance = await wallet.GetBalanceAsync(userId, ct);
    return Results.Ok(new { balance, max = 10.0m });
});
```

**Response:**
```json
{
  "balance": 7.5,
  "max": 10.0
}
```

**Rarely used** — frontend prefers balance included in `/moments` response.

### GET /moments

**File:** `backend/WovenBackend/Endpoints/MomentsEndpoints.cs`

```csharp
var sparkBalance = await sparks.GetBalanceAsync(userId, ct);

return Results.Ok(new {
    dateUtc = today.ToString("yyyy-MM-dd"),
    budget,
    sparkBalance,  // ✅ Included in response
    count = filteredItems.Count,
    cards = ...
});
```

**Response:**
```json
{
  "dateUtc": "2026-08-17",
  "budget": { "totalCap": 20, "totalUsed": 3, "totalRemaining": 17 },
  "sparkBalance": 7.5,
  "cards": [...]
}
```

### POST /moments/choose

**File:** `backend/WovenBackend/Endpoints/MomentsEndpoints.cs`

```csharp
// Only Drawn tab actions (source = "LIKED_YOU") spend sparks
if (req.Source == "LIKED_YOU")
{
    var sparkSpend = await sparks.TrySpendAsync(me, ct);
    if (!sparkSpend.Allowed)
        return Results.BadRequest(new { 
            error = sparkSpend.DenyReason, 
            sparkBalance = sparkSpend.Balance 
        });
}

// ... match creation logic ...

return Results.Ok(new {
    status = "MATCH_CREATED",
    matchId = match.Id,
    matchType = match.MatchType.ToString(),
    edgeOwnerId = match.EdgeOwnerId,
    sparkBalance = sparkSpend?.Balance  // ✅ Return updated balance
});
```

**Request:**
```json
{
  "targetUserId": 42,
  "choice": "MAGICAL",
  "noteText": "Love your vibe!",
  "source": "LIKED_YOU",  // ✅ Triggers spark spend
  "timeOnCardMs": 8450
}
```

**Response (success):**
```json
{
  "status": "MATCH_CREATED",
  "matchId": "...",
  "matchType": "EDGE",
  "edgeOwnerId": 42,
  "sparkBalance": 6.5  // ✅ Updated after spend
}
```

**Response (insufficient):**
```json
{
  "error": "INSUFFICIENT_SPARKS",
  "sparkBalance": 0
}
```

---

## Constants

```csharp
private const int DailyEarnTenths = 50;   // 5.0 sparks
private const int MaxBalanceTenths = 100; // 10.0 sparks
private const int SpendTenths = 10;       // 1.0 spark per action
private const int GhostRefundTenths = 5;  // 0.5 sparks on ghost
```

**Change impact:**

| Constant | Current | If Changed To | Impact |
|---|---|---|---|
| `DailyEarnTenths` | 50 (5.0) | 30 (3.0) | Users earn 3 sparks/day instead of 5 |
| `MaxBalanceTenths` | 100 (10.0) | 150 (15.0) | Max wallet increases to 15 sparks |
| `SpendTenths` | 10 (1.0) | 20 (2.0) | Drawn actions cost 2 sparks instead of 1 |
| `GhostRefundTenths` | 5 (0.5) | 10 (1.0) | Ghost refunds return full spark |

**All changes require code recompile** (no config-driven).

---

## Transaction Isolation

### Why Serializable?

**Problem:** Two concurrent `TrySpendAsync()` calls for same user  
**Without isolation:** Both could read `balance = 1`, both deduct 1 → final balance = 0 (should fail one request)  
**With serializable:** Second request waits for first commit, sees updated balance, fails correctly

### Performance Impact

**Serializable = most strict isolation** → higher lock contention  
**Acceptable because:**
- Spark spends are rare (< 10/day per user)
- Transaction is fast (single row update)
- No cross-user locking (isolated by `user_id`)

**Alternative (not chosen):** Optimistic concurrency via `RowVersion` column

---

## Error Handling

### Insufficient Sparks

**Trigger:** `TrySpendAsync()` when `balance < 1.0`

**Response:**
```csharp
return new SpendResult(false, "INSUFFICIENT_SPARKS", currentBalance);
```

**Endpoint behavior:**
```csharp
if (!result.Allowed)
    return Results.BadRequest(new { error = result.DenyReason, sparkBalance = result.Balance });
```

**HTTP 400:**
```json
{
  "error": "INSUFFICIENT_SPARKS",
  "sparkBalance": 0
}
```

### Wallet Creation Failure

**Scenario:** Database unavailable during `EnsureWalletAsync()`  
**Result:** Exception propagates → HTTP 500

**No special handling** — wallet creation is non-optional.

### Ghost Refund Failure

**Scenario:** Database unavailable during `GhostRefundAsync()`  
**Result:** Exception swallowed (fire-and-forget `Task.Run`)

**Code:**
```csharp
_ = Task.Run(async () =>
{
    try { await sparks.GhostRefundAsync(uA); } catch { }
    try { await sparks.GhostRefundAsync(uB); } catch { }
});
```

**Why swallow?**  
Match closure succeeds even if refund fails. Refunds are **non-critical**.

---

## Logging

### Current State

**No dedicated logging in `SparkWalletService`.**

**Why:** Sparks are transactional — DB state is source of truth.

### Recommended Additions

```csharp
_logger.LogInformation(
    "[Sparks] Earned daily | UserId={UserId} Balance={Balance}",
    userId, balance);

_logger.LogInformation(
    "[Sparks] Spent | UserId={UserId} NewBalance={Balance}",
    userId, newBalance);

_logger.LogInformation(
    "[Sparks] GhostRefund | UserId={UserId} NewBalance={Balance}",
    userId, newBalance);
```

**Benefits:**
- Track spark economy health
- Debug user complaints ("I should have more sparks")
- Audit trail for balance changes

---

## Testing Strategies

### Unit Tests (Not Implemented)

**Recommended:**
```csharp
[Fact]
public async Task TrySpend_InsufficientBalance_ReturnsFalse()
{
    // Arrange: user with 0.5 sparks
    // Act: TrySpendAsync()
    // Assert: result.Allowed == false, DenyReason == "INSUFFICIENT_SPARKS"
}

[Fact]
public async Task GetBalance_EarnsDailyOnce()
{
    // Arrange: user with LastEarnedDate = yesterday
    // Act: GetBalanceAsync() twice
    // Assert: balance increased by 5 only once
}

[Fact]
public async Task GhostRefund_RespectsMaxBalance()
{
    // Arrange: user with 9.8 sparks
    // Act: GhostRefundAsync()
    // Assert: balance = 10.0 (not 10.3)
}
```

### Integration Tests

**Manual test flows:**
1. New user → check initial balance (should be 5.0)
2. Spend on Drawn action → verify deduction
3. Ghost match → verify refund
4. Cross midnight boundary → verify daily earn

---

## Migration Path

### Adding Sparks to Existing Users

**Problem:** Table created 2026-05-24, but users existed before then.

**Solution:** Lazy wallet creation  
- `EnsureWalletAsync()` creates wallet on first access
- Default balance: 5.0 sparks (same as new users)

**No backfill migration needed.**

---

## Related Files

| File | Purpose |
|---|---|
| `Services/Moments/SparkWalletService.cs` | Core service implementation |
| `Endpoints/SparkEndpoints.cs` | Dedicated `/sparks/balance` endpoint |
| `Endpoints/MomentsEndpoints.cs` | Spark spending in Drawn tab actions |
| `Endpoints/ChatEndpoints.cs` | Ghost refunds on match closure |
| `data/Entities/SparkWallet.cs` | Entity model |
| `data/WovenDbContext.cs` | DbContext registration |
| `Migrations/20260524204334_AddSparkWallets.cs` | Initial migration |
| `Program.cs` | Service registration |
