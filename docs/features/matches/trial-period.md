# Trial Period

**Feature:** Trial (3-minute test chat before commitment)  
**Related:** [Balloon Lifecycle](./balloon-lifecycle.md) | [Match Backend](./backend.md)

---

## Overview

After popping a balloon, users enter a **3-minute trial period** to test compatibility before deciding to continue the match.

---

## Timeline

```
BALLOON POP → Second user opens chat → TRIAL START (3 min) → DECISION
```

**Trial starts when:**
- Both `TrialUserAOpenedAt` AND `TrialUserBOpenedAt` are non-null
- `TrialEndsAt = now + 3 minutes`

**Evidence:** [Match.cs](../../../backend/WovenBackend/data/Entities/Moments/Match.cs)

---

## Decision Options

At trial end, each user chooses:

| Decision | Action | Outcome |
|----------|--------|---------|
| **CONTINUE** | Match stays active | Chat continues, no spark refund |
| **END** | Close match gracefully | Match closed, ghost refund if 0 messages |
| **BLOCK** | Close + block user | Match closed + `Block` record created |

**End Reasons:**
- `no_spark` — didn't feel connection
- `wrong_timing` — not ready now
- `not_my_type` — incompatible

**Evidence:** [TrialDecisionEndpoints.cs](../../../backend/WovenBackend/Endpoints/TrialDecisionEndpoints.cs)

---

## Spark Mechanics

**Ghost Refund:** 0.5 sparks if match ends with 0 messages exchanged

**Evidence:** [SparkWalletService.cs:GhostRefundAsync](../../../backend/WovenBackend/Services/Moments/SparkWalletService.cs#L67-L73)

---

## Related

- [Trial Decision Flow](../../features/chats/trial-decision.md)
- [Sparks Refunds](../sparks/refunds.md)
- [Match State Machine](../../business/state-machines.md#match-lifecycle)
