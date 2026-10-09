# Ghost Refunds

**Last Updated:** 2026-08-17

---

## Overview

When a match **closes with zero messages exchanged**, both users receive **0.5 sparks** back. This softens the penalty of "ghosting" — matches that die before any conversation.

---

## Refund Amount

```csharp
private const int GhostRefundTenths = 5;  // 0.5 sparks
```

**Amount:** 0.5 sparks (5 tenths)  
**Cap:** Refund still respects max wallet (10 sparks)  
**Recipients:** **Both** users in the match (UserA and UserB)

---

## Trigger Conditions

Ghost refunds fire in **3 scenarios**, all sharing the same condition:  
**Match closes + `BothMessagedAt == null`** (no messages were ever exchanged)

### 1. Trial Decision: END

**Flow:**
1. Match enters trial period (both users opened chat)
2. User chooses **END** decision (`POST /chats/{threadId}/trial-decision`)
3. If other user already chose END → match closes immediately
4. Backend checks: were any messages sent?
5. If `BothMessagedAt == null` → refund both users

**Code:** `ChatEndpoints.cs` (trial-decision handler)

```csharp
if (match.BothMessagedAt == null)
{
    match.IsTrial = false;
    await db.SaveChangesAsync(ct);

    // Ghost refund: no messages were exchanged — return 0.5 sparks to both
    var uA = match.UserAId;
    var uB = match.UserBId;
    _ = Task.Run(async () =>
    {
        try { await sparks.GhostRefundAsync(uA); } catch { }
        try { await sparks.GhostRefundAsync(uB); } catch { }
    });
}
```

### 2. Balloon Expiration

**Flow:**
1. Balloon expires after 14 days (`ExpiresAt < now`)
2. System auto-closes balloon → `BalloonState = CLOSED`
3. Backend checks: were any messages sent?
4. If `BothMessagedAt == null` → refund both users

**Code:** `ChatEndpoints.cs` (expiration handler)

```csharp
if (noMessages)
{
    var uA = match.UserAId;
    var uB = match.UserBId;
    _ = Task.Run(async () =>
    {
        try { await sparks.GhostRefundAsync(uA); } catch { }
        try { await sparks.GhostRefundAsync(uB); } catch { }
    });
}
```

### 3. Block Action

**Flow:**
1. User blocks match before conversation starts
2. Match immediately closes
3. Backend checks: were any messages sent?
4. If `BothMessagedAt == null` → refund both users

**Code:** `ChatEndpoints.cs` (block handler — same pattern as above)

---

## Implementation

### Service Method

**Service:** `SparkWalletService.GhostRefundAsync()`

```csharp
/// <summary>Add 0.5 sparks — called when match ends as ghost (no messages exchanged).</summary>
public async Task GhostRefundAsync(int userId, CancellationToken ct = default)
{
    var wallet = await EnsureWalletAsync(userId, ct);
    wallet.BalanceTenths = Math.Min(MaxBalanceTenths, wallet.BalanceTenths + GhostRefundTenths);
    wallet.UpdatedAt = DateTimeOffset.UtcNow;
    await _db.SaveChangesAsync(ct);
}
```

**Key details:**
- **No transaction** — refunds are idempotent (additive only)
- **Capped at max** — `Math.Min(100, balance + 5)` prevents overflow
- **Creates wallet if missing** — `EnsureWalletAsync()` handles new users

### Fire-and-Forget Pattern

**All refund calls use:**
```csharp
_ = Task.Run(async () =>
{
    try { await sparks.GhostRefundAsync(uA); } catch { }
    try { await sparks.GhostRefundAsync(uB); } catch { }
});
```

**Why `Task.Run`?**
- Refunds are **non-critical** — match closure succeeds even if refund fails
- Avoid blocking main request with DB writes
- Separate try-catch per user → if one refund fails, other still runs

**Why swallow exceptions?**
- Match is already closed — user got their primary result
- Refund failure shouldn't fail the entire request
- Errors logged internally via EF Core (not shown here)

---

## User-Facing Behavior

### Silent Operation

Users **never see** refund notifications. Balance quietly updates.

**Example:**
1. User has 3.5 sparks
2. Likes someone on Drawn tab → spends 1 spark → balance now 2.5 sparks
3. Match dies with no messages
4. User opens app next day → sees 3.0 sparks (2.5 + 0.5 refund)
5. **No toast, no notification** — just updated balance

### When Refund Is Visible

**Scenario:** User opens app immediately after ghost match closure  
**Result:** Spark count increases without user action

**Before:** ◈ 2.5 left  
**After:** ◈ 3.0 left (refund applied)

Most users won't notice — they'll assume they earned daily sparks.

---

## Edge Cases

### Max Balance During Refund

**Scenario:** User has 9.8 sparks when refund fires

```
Current: 9.8 sparks (98 tenths)
Refund: 0.5 sparks (5 tenths)
Result: min(100, 98 + 5) = 100 tenths = 10.0 sparks
Actual gain: 0.2 sparks (cap hit)
```

User loses 0.3 sparks to cap — **working as intended**.

### Multiple Ghost Matches Same Day

**Scenario:** User gets 3 ghost refunds in one day

```
Start: 4.0 sparks
Refund 1: +0.5 → 4.5 sparks
Refund 2: +0.5 → 5.0 sparks
Refund 3: +0.5 → 5.5 sparks
Daily earn (next day): +5.0 → 10.0 sparks (capped)
```

All refunds apply independently. No daily refund cap.

### Refund Before First Spend

**Scenario:** New user gets matched, match dies before they spend any sparks

```
Initial wallet: 5.0 sparks
Ghost refund: +0.5 → 5.5 sparks
```

Valid — refunds aren't contingent on prior spending.

### Trial CONTINUE → Later Ghost

**Scenario:**
1. Match enters trial
2. Both choose CONTINUE → match stays open
3. 14 days pass, no messages sent
4. Balloon expires → ghost refund fires

**Result:** Refund still applies (checked at expiration time, not trial decision time).

---

## Why Not Refund Full Spark?

### Design Rationale

**Partial refund (0.5 instead of 1.0) because:**
1. **Opportunity cost** — User spent decision budget on this person (lost other potential matches)
2. **Behavioral signal** — Partial loss incentivizes better candidate selection
3. **Anti-gaming** — Full refund could enable "swipe and ghost" farming

**Analogy:** Restaurant reservation fee — you lose some even if the other party cancels.

---

## Related Systems

- **[Trial Period](../matches/trial-period.md)** — Trial END decision triggers refund check
- **[Balloon Lifecycle](../matches/balloon-lifecycle.md)** — Expiration triggers refund check
- **[Block Flow](../safety/blocking.md)** — Block action triggers refund check
- **[Spark Mechanics](./mechanics.md)** — How earning/spending interacts with refunds

---

## Monitoring

### Key Metrics

1. **Refund rate** — % of closed matches that trigger ghost refunds
2. **Average refunds per user per week** — Proxy for match quality
3. **Refund-to-spend ratio** — Are users net-positive or net-negative on sparks?

**Healthy baseline:**
- Refund rate: **< 30%** (most matches should have some conversation)
- Avg refunds/user/week: **< 2** (users shouldn't be burning through dead matches)

**Red flags:**
- Refund rate > 50% → matching quality problem
- Avg refunds > 5/week → user frustration, potential churn risk

### Query Example

```sql
-- Count ghost refunds in last 7 days
SELECT 
    COUNT(*) AS ghost_matches,
    COUNT(*) * 100.0 / NULLIF((SELECT COUNT(*) FROM matches WHERE closed_at > NOW() - INTERVAL '7 days'), 0) AS refund_rate_pct
FROM matches
WHERE closed_at > NOW() - INTERVAL '7 days'
  AND both_messaged_at IS NULL;
```

---

## Related Files

- **Backend:** `backend/WovenBackend/Services/Moments/SparkWalletService.cs`
- **Endpoints:** `backend/WovenBackend/Endpoints/ChatEndpoints.cs` (all 3 triggers)
- **Service:** `backend/WovenBackend/Services/AntiGhosting/GhostDetectionService.cs` (separate ghost detection system)
