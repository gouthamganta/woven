# Blocking System

**Feature:** User blocking and safety  
**Related:** [Moderation](../../systems/moderation/block-system.md) | [Trial Period](../matches/trial-period.md)

---

## Overview

Users can block others during trial decision or from match/chat contexts. Blocking prevents all future interactions.

---

## Block Actions

**Where blocking happens:**
- Trial decision: `BLOCK` option (closes match + creates block)
- Match profile: Block button
- Chat context: Report → Block

**Effect:**
- Immediate match closure
- Both users hidden from each other's candidate pools
- No future deck appearances
- Cannot message each other

---

## Database

**Table:** `blocks`

**Columns:**
- `blocker_user_id` — who blocked
- `blocked_user_id` — who was blocked
- `reason` — optional block reason
- `created_at` — timestamp

**Evidence:** [Block.cs](../../../backend/WovenBackend/data/Entities/Moments/Block.cs)

---

## Candidate Filtering

Block relationships are checked in `CandidatePoolService` — SQL WHERE clause excludes blocked/blocker users.

**Evidence:** [CandidatePoolService.cs](../../../backend/WovenBackend/Services/Matchmaking/CandidatePoolService.cs)

---

## Related

- [Block System Implementation](../../systems/moderation/block-system.md)
- [Trial Decision](../chats/trial-decision.md)
- [Match Close Paths](../chats/close-paths.md)
