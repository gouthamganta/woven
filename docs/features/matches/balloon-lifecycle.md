# Balloon Lifecycle

**Feature:** Balloon (match connection window)  
**Related:** [Trial Period](./trial-period.md) | [Match Backend](./backend.md)

---

## Overview

The balloon is the connection window between matched users. It has two states: **ACTIVE** (can chat) and **CLOSED** (immutable, no reopening).

---

## Lifecycle States

```
MATCH CREATED → ACTIVE → (user pops or expires) → CLOSED
```

**ACTIVE:**
- Users can send messages
- Trial period hasn't started yet (starts when second user opens chat)
- Lifetime: 36 hours from match creation

**CLOSED:**
- Immutable state — cannot reopen
- Close triggers: POP (user action), EXPIRE (timeout), BLOCK

---

## Rules

**Balloon Lifetime:** 36 hours from `Match.CreatedAt`

**Evidence:** [MomentsRules.cs:7](../../../backend/WovenBackend/Services/Moments/MomentsRules.cs#L7)

```csharp
public static readonly TimeSpan BalloonLifetime = TimeSpan.FromHours(36);
```

**Expiration Worker:** `BalloonExpiryWorker` runs every 60s, closes balloons past `BalloonExpiresAt`

---

## Related

- [Trial Period](./trial-period.md) — starts AFTER balloon is popped
- [Match State Machine](../../business/state-machines.md#match-lifecycle)
- [Sparks Refunds](../sparks/refunds.md) — ghost refund logic
