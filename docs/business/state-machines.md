# State Machines

**Last Updated:** 2026-10-07

This document visualizes Woven's key state machines — how entities transition through their lifecycle.

---

## Match Lifecycle

```
┌─────────────────────────────────────────────────────────────────┐
│                      MATCH LIFECYCLE                            │
└─────────────────────────────────────────────────────────────────┘

     [Both users swipe MAGICAL or LOGICAL]
                    │
                    ▼
            ┌───────────────┐
            │  Match Created │
            │  BalloonState: │
            │    ACTIVE      │
            └───────┬───────┘
                    │
       ┌────────────┴────────────┐
       │                         │
       ▼                         ▼
[Either user pops]        [72h expires]
    balloon                  (timeout)
       │                         │
       │                         │
       └────────────┬────────────┘
                    ▼
            ┌───────────────┐
            │  Balloon CLOSED│
            │  (immutable)   │
            └───────┬───────┘
                    │
         ┌──────────┴──────────┐
         │                     │
         ▼                     ▼
  [User A opens chat]   [Both users open chat]
         │                     │
         ▼                     ▼
  TrialUserAOpenedAt    TrialEndsAt = now + 3min
       set                    │
         │                     │
         │                     ▼
         │            ┌─────────────────┐
         │            │  Trial Active   │
         │            │  (3 min window) │
         │            └────────┬────────┘
         │                     │
         │          ┌──────────┴──────────┐
         │          │                     │
         ▼          ▼                     ▼
  [Waiting for B] [Both decide CONTINUE] [Either decides END/BLOCK]
         │          │                     │
         │          ▼                     ▼
         │    ┌──────────────┐     ┌──────────────┐
         │    │  Full Chat   │     │ Match Ended  │
         │    │  (unlocked)  │     │  EndedAt set │
         │    └──────────────┘     └──────────────┘
         │
         └──────> [Loop: wait for B to open]

```

**Terminal states:**
- **Full Chat** (unlocked) — both users chose CONTINUE
- **Match Ended** — one or both chose END/BLOCK, or trial expired

**Immutable transitions:**
- `ACTIVE` → `CLOSED` (balloon pop/expiry) — cannot revert
- `EndedAt != null` — match closed, cannot reopen

---

## Balloon State Machine

```
┌─────────────────────────────────────────────────────────────────┐
│                    BALLOON STATE MACHINE                        │
└─────────────────────────────────────────────────────────────────┘

             ┌─────────────┐
     START → │   ACTIVE    │
             │ (72h window)│
             └──────┬──────┘
                    │
       ┌────────────┴────────────┐
       │                         │
       │ [Either user pops]      │ [BalloonExpiresAt reached]
       │                         │
       ▼                         ▼
    ┌──────────────┐      ┌──────────────┐
    │   CLOSED     │      │   CLOSED     │
    │ (user action)│      │  (expired)   │
    └──────────────┘      └──────────────┘
           │                      │
           └──────────┬───────────┘
                      │
                      ▼
                  [TERMINAL]
                (cannot reopen)

```

**Rules:**
- `ACTIVE` → `CLOSED` (one-way)
- `CLOSED` is immutable (never transitions back to `ACTIVE`)
- Balloon can be popped by EITHER user (first to pop wins)

**Database field:** `BalloonState` enum (`ACTIVE` | `CLOSED`)

**Evidence:** [Match.cs](../../backend/WovenBackend/data/Entities/Moments/Match.cs)

---

## Trial Period Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                    TRIAL PERIOD FLOW                            │
└─────────────────────────────────────────────────────────────────┘

    [Balloon popped]
           │
           ▼
    ┌──────────────┐
    │  User A      │
    │  opens chat  │
    └──────┬───────┘
           │
           │ TrialUserAOpenedAt = now
           │ TrialEndsAt = null (waiting for B)
           │
           ▼
    ┌──────────────┐
    │  Waiting for │
    │  User B      │
    └──────┬───────┘
           │
           │ [User B opens chat]
           │
           ▼
    ┌──────────────┐
    │  Trial Starts│
    │  TrialEndsAt │
    │  = now + 3min│
    └──────┬───────┘
           │
           │ [3 minutes pass]
           │
           ▼
    ┌──────────────┐
    │  Trial Expired│
    │  (UI modal   │
    │   shown)     │
    └──────┬───────┘
           │
    ┌──────┴───────┬────────────┬───────────┐
    │              │            │           │
    │ [CONTINUE]   │ [END]      │ [BLOCK]   │
    │              │            │           │
    ▼              ▼            ▼           │
┌─────────┐  ┌─────────┐  ┌─────────┐     │
│ Full    │  │ Match   │  │ Match   │     │
│ Chat    │  │ Ended   │  │ Ended + │     │
│ Unlocked│  │ Soft    │  │ Block   │     │
└─────────┘  └─────────┘  └─────────┘     │
                                           │
                                           │
                  [Either user blocks anytime]
                                           │
                                           ▼
                                    ┌──────────────┐
                                    │ Block Created│
                                    │ Match Closed │
                                    └──────────────┘

```

**Key timestamps:**
- `TrialUserAOpenedAt` — when User A first opens chat
- `TrialUserBOpenedAt` — when User B first opens chat
- `TrialEndsAt` — when trial expires (both opened + 3min)

**Trial decisions:**
- `CONTINUE` — unlock full chat (no expiry)
- `END` — close match (with reason: `no_spark`, `wrong_timing`, `not_my_type`)
- `BLOCK` — close match + create `Block` record

**Evidence:** [ChatEndpoints.cs:TrialDecision](../../backend/WovenBackend/Endpoints/ChatEndpoints.cs)

---

## Profile Status Progression

```
┌─────────────────────────────────────────────────────────────────┐
│                  PROFILE STATUS PROGRESSION                     │
└─────────────────────────────────────────────────────────────────┘

    [User signs up via Google OAuth]
                │
                ▼
         ┌──────────────┐
         │  INCOMPLETE  │
         │  (new user)  │
         └──────┬───────┘
                │
                │ [Upload 3+ photos]
                │ [Answer foundational questions]
                │ [Complete onboarding form]
                │
                ▼
         ┌──────────────┐
         │   COMPLETE   │
         │ (can use app)│
         └──────┬───────┘
                │
       ┌────────┴────────┐
       │                 │
       │ [Normal use]    │ [Moderation action]
       │                 │
       ▼                 ▼
┌──────────────┐  ┌──────────────┐
│   COMPLETE   │  │  SUSPENDED   │
│  (active)    │  │  (banned)    │
└──────────────┘  └──────┬───────┘
                         │
                         │ [Appeal approved]
                         │
                         ▼
                  ┌──────────────┐
                  │  COMPLETE    │
                  │ (reinstated) │
                  └──────────────┘

```

**Statuses:**
- `INCOMPLETE` — user hasn't finished onboarding (blocked from main app)
- `COMPLETE` — user completed onboarding (full app access)
- `SUSPENDED` — account suspended (moderation action)

**Enforcement:**  
- Frontend `AuthGuard` redirects `INCOMPLETE` users to `/onboarding`
- Backend endpoints reject `INCOMPLETE` users (401 Unauthorized)

**Database field:** `User.profile_status` enum

**Evidence:** [User.cs](../../backend/WovenBackend/data/Entities/User.cs)

---

## Moderation Queue Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                  MODERATION QUEUE FLOW                          │
└─────────────────────────────────────────────────────────────────┘

    [User creates tile]
           │
           ▼
    ┌──────────────┐
    │   PENDING    │
    │  (in queue)  │
    └──────┬───────┘
           │
           │ [ModerationQueueWorker processes]
           │
    ┌──────┴───────┐
    │              │
    │ [Approved]   │ [Rejected]
    │              │
    ▼              ▼
┌─────────┐   ┌─────────┐
│APPROVED │   │REJECTED │
│(visible)│   │(hidden) │
└─────────┘   └────┬────┘
                   │
                   │ [Author notified]
                   │
                   ▼
            ┌──────────────┐
            │  Tile soft-  │
            │  deleted     │
            └──────────────┘

```

**Statuses:**
- `PENDING` — awaiting moderation
- `APPROVED` — passed moderation (visible to users)
- `REJECTED` — failed moderation (hidden, author notified)

**Auto-flagging:**  
If tile receives 5+ reports → priority review (escalated to human moderator)

**Evidence:** [ModerationQueue.cs](../../backend/WovenBackend/data/Entities/ModerationQueue.cs)

---

## Spark Wallet Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                    SPARK WALLET FLOW                            │
└─────────────────────────────────────────────────────────────────┘

       [User creates account]
                │
                ▼
         ┌──────────────┐
         │  Wallet = 5  │
         │ (initial)    │
         └──────┬───────┘
                │
    ┌───────────┴───────────┐
    │                       │
    │ [Daily refill]        │ [Action costs spark]
    │ (00:00 UTC)           │
    │                       │
    ▼                       ▼
┌────────────┐       ┌────────────┐
│ Wallet+5   │       │ Wallet-1   │
│ (max 10)   │       │ (pop/msg)  │
└────┬───────┘       └──────┬─────┘
     │                      │
     │                      │
     │ [Wallet < 1]         │
     │                      │
     ▼                      ▼
┌────────────┐       ┌────────────┐
│ Cannot     │       │ Ghost      │
│ perform    │       │ refund     │
│ actions    │       │ (+0.5)     │
└────────────┘       └────────────┘

```

**Rules:**
- **Daily refill:** +5 sparks at midnight UTC (max 10)
- **Pop balloon:** -1 spark (from Drawn tab)
- **Ghost refund:** +0.5 sparks if match ends with 0 messages
- **Insufficient sparks:** Actions blocked (UI shows "Out of sparks")

**Database field:** `SparkWallet.balance` (integer, range 0–10)

**Evidence:** [SparkWallet.cs](../../backend/WovenBackend/data/Entities/SparkWallet.cs)

---

## Game Session Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                   GAME SESSION FLOW                             │
└─────────────────────────────────────────────────────────────────┘

    [User sends game invite]
           │
           ▼
    ┌──────────────┐
    │  PENDING     │
    │ (waiting for │
    │  acceptance) │
    └──────┬───────┘
           │
    ┌──────┴───────┐
    │              │
    │ [Accepted]   │ [Declined / Expired]
    │              │
    ▼              ▼
┌─────────┐   ┌─────────┐
│  ACTIVE │   │CANCELLED│
│(in play)│   │(closed) │
└────┬────┘   └─────────┘
     │
     │ [Users submit answers]
     │
     ▼
┌─────────┐
│COMPLETED│
│(results │
│ shown)  │
└─────────┘

```

**Statuses:**
- `PENDING` — invite sent, waiting for acceptance
- `ACTIVE` — game in progress
- `COMPLETED` — game finished (results available)
- `CANCELLED` — invite declined or expired

**Timeout:** Game invites expire after 24 hours if not accepted.

**Evidence:** [GameSession.cs](../../backend/WovenBackend/data/Entities/Games/GameEntities.cs)

---

## References

- **Glossary:** [glossary.md](glossary.md)
- **Business rules:** [rules.md](rules.md)
- **Product philosophy:** [product-philosophy.md](product-philosophy.md)
- **Code evidence:** [backend/WovenBackend/data/Entities/](../../backend/WovenBackend/data/Entities/)

---

**Last Updated:** 2026-10-07  
**Evidence:** Database entity definitions, endpoint logic, CLAUDE.md state machine notes
