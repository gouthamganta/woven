# Sparks Economy

**Status:** Production  
**Version:** 1.0  
**Last Updated:** 2026-08-17

---

## Overview

**Sparks** are Woven's daily currency that gates access to the **Drawn** tab (formerly "Liked You"). They create scarcity around the most valuable user action — responding to someone who already chose you — without paywalls.

### Design Philosophy

- **Anti-swipe mechanic** — No infinite browsing. Sparks enforce daily limits and thoughtful choices.
- **No paywalls** — Sparks cannot be purchased. Everyone plays by the same rules.
- **Ghost protection** — Users get partial refunds when matches die without conversation.
- **Background system** — Sparks are never advertised as a feature. They're invisible friction that shapes behavior.

---

## Quick Reference

| Metric | Value |
|---|---|
| Daily earn | **5 sparks** (auto-granted at UTC midnight) |
| Max wallet | **10 sparks** (overflow is lost) |
| Spend cost | **1 spark** per Drawn tab action |
| Ghost refund | **0.5 sparks** (when match ends with 0 messages) |

---

## How It Works

### Earning

- Every user earns **5 sparks per day** at 00:00 UTC
- Earning is **lazy** — sparks are credited on first balance check, not at midnight
- Max balance: **10 sparks**. If you have 8 and earn 5, you cap at 10 (2 sparks lost)
- New users start with **5 sparks** on wallet creation

### Spending

Sparks are spent **only** when:
- User taps MAGICAL (◈) or LOGICAL (◇) on a Drawn tab card
- Cost: **1 spark per action**
- If balance < 1 spark → action denied with `INSUFFICIENT_SPARKS` error

**Not spent on:**
- Today tab actions (free, gated by daily interaction cap instead)
- Passing on Drawn cards (free)
- Opening chat threads
- Sending messages

### Ghost Refund

When a match **closes with zero messages exchanged**, both users receive **0.5 sparks** back.

**Triggers:**
1. **Trial decision END** — User chooses END during trial period, no messages sent
2. **Balloon expiration** — Match expires after 14 days, no messages sent
3. **Block action** — User blocks match before any conversation

**Implementation:**
- Fire-and-forget `Task.Run` — refund failures are swallowed
- Refunds both users in the match
- Capped at max wallet (10 sparks)

---

## Why This Design

### Problem: Swipe Fatigue
Dating apps optimize engagement → endless swiping → paradox of choice → poor outcomes.

### Solution: Sparks
- **Scarcity** — Users can't browse 100 Drawn profiles. They must choose carefully.
- **Daily rhythm** — 5/day encourages daily use without binge sessions.
- **Fair refunds** — Ghost matches don't penalize users for bad behavior by others.

### Anti-Patterns Avoided
- **No pay-to-win** — Sparks can't be purchased
- **No starvation** — 5/day is enough to explore Drawn tab regularly
- **No punishment** — Ghost refunds soften the sting of dead matches

---

## User-Facing Language

| Backend Term | User-Facing Name |
|---|---|
| Sparks | **Sparks** (lowercase, no emoji) |
| Drawn tab | **Drawn** (people who liked you) |
| Daily earn | Not mentioned (happens silently) |
| Ghost refund | Not mentioned (happens silently) |

Users see:
- **"◈ 7.5 left"** on Moments page header (Drawn tab)
- **"INSUFFICIENT_SPARKS"** error toast (rare — only when out of sparks)

---

## Related Systems

- **[Daily Interaction Budget](../moments/daily-budget.md)** — Separate daily cap on Today tab (20/day)
- **[Balloon System](../matches/balloon-lifecycle.md)** — Match expiration triggers ghost refunds
- **[Trial Period](../matches/trial-period.md)** — Trial END decision triggers ghost refunds

---

## Files

| File | Purpose |
|---|---|
| [mechanics.md](./mechanics.md) | Earning, spending, and refund logic |
| [refunds.md](./refunds.md) | Ghost refund conditions and implementation |
| [frontend.md](./frontend.md) | UI display and user interactions |
| [backend.md](./backend.md) | Service implementation and database schema |

---

## Migration Notes

**Added:** 2026-05-24  
**Migration:** `20260524204334_AddSparkWallets.cs`  

**Schema:**
```sql
CREATE TABLE spark_wallets (
    user_id INTEGER PRIMARY KEY,
    balance_tenths INTEGER NOT NULL DEFAULT 50,
    last_earned_date DATE,
    updated_at TIMESTAMPTZ NOT NULL
);
```

**Pre-existing users:** No backfill. Wallets created on-demand with 5 sparks initial balance.
