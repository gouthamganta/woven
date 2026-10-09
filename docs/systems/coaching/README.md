# Coaching System

**Last Updated:** 2026-10-07  
**Status:** ACTIVE  
**Worker:** `CoachingSummaryWorker`  
**Schedule:** Wednesday 18:00 UTC

---

## Overview

The Coaching system delivers weekly, personalized AI summaries to active users. These aren't metric dashboards — they're warm, perceptive reflections from ECHO about what they noticed in the user's week.

**Key principles:**
- **Private** — summaries are never shared or compared
- **Behavioral** — grounded in permitted signals only (no chat content, no photos)
- **Warm** — high temperature GPT output for natural, encouraging tone
- **Suppressible** — if there's nothing meaningful to say, no summary is delivered

---

## Architecture

```
CoachingSummaryWorker (Wednesday 18:00 UTC)
  ↓
Eligibility Check (account age, activity, opt-out status)
  ↓
Signal Aggregation (DailyInteractions + MatchSignalLogs)
  ↓
C# Narrative Builder (deterministic, no raw numbers)
  ↓
GPT-4.1-mini Call (temp=0.8, max_tokens=200)
  ↓
Suppression Logic (SUPPRESS keyword, <50 chars)
  ↓
CoachingSummaries table (90-day TTL)
  ↓
Frontend: GET /coaching/current-summary
```

---

## Eligibility

A user receives a coaching summary if **all** of these conditions are met:

1. **Account ≥14 days old** — `User.CreatedAt ≤ now - 14 days`
2. **≥3 deck interactions in prior 7 days** — `SUM(DailyInteractions.TotalUsed) ≥ 3`
3. **Not opted out** — `User.CoachingOptedOut == false`
4. **No existing summary for this week** — no `CoachingSummary` row for `WeekStartDate`

**Typical eligible users per week:** ~60-70% of MAU (varies by season)

---

## Data Sources

### Permitted Signals Only

Coaching **never** accesses:
- ❌ Private chat messages
- ❌ ChatNotes (AI-generated profile critiques)
- ❌ Match scores or compatibility data
- ❌ Profile photos or voice note audio
- ❌ Demographic data (beyond age for eligibility)

Coaching **does** use:
- ✅ `DailyInteractions.TotalUsed` — deck engagement count
- ✅ `MatchSignalLogs` (5 permitted event types only):
  - `TimeToFirstMessageMs` — user sent first message
  - `TrialMessageCount` — conversation depth
  - `DateIdeaAccepted` — showed intent to meet
  - `MutualVoiceExchange` — vulnerability signal
  - `TrialAccepted` — chose to continue match

**Privacy guarantee:** GPT-4.1-mini never sees PII, chat content, or identifiable data.

---

## Two-Stage Generation

### Stage 1: C# Narrative Builder

A deterministic C# function interprets behavioral signals into prose **before** calling GPT.

**Why:**
- Prevents GPT from leaking raw metrics ("you sent 47 messages")
- Ensures consistent quality (not dependent on GPT hallucinating from sparse data)
- Protects privacy (GPT never sees raw signal IDs or user data)

**Example narrative:**
```
This person has been active on the app this week. They showed up consistently, 
reviewing a solid set of potential connections. When they felt a connection, they 
were willing to make the first move. Their conversations have gone deep — there's 
real exchange happening, not just surface pleasantries.
```

### Stage 2: GPT-4.1-mini

The C# narrative is passed to GPT with this system prompt:

```
You are a warm, perceptive friend who happens to understand how dating apps work. 
You know this person has been putting themselves out there this week. 

Your job is to reflect back what you noticed — genuine, specific, encouraging 
without being hollow. 

Never mention numbers directly. Never compare them to others. Never imply they 
should change who they are. 

If there is nothing genuinely positive or actionable to say, return the string "SUPPRESS".
```

**Model:** `gpt-4.1-mini`  
**Temperature:** 0.8 (higher than other agents — encourages warmth)  
**Max Tokens:** 200  
**Expected output:** 3-5 sentences, plain text, no headers

---

## Suppression Logic

A summary is **not saved** if:
1. GPT returns `"SUPPRESS"` (case-insensitive)
2. Output is `< 50 characters`
3. Output is empty/whitespace

**Typical suppression rate:** 15-20% of eligible users (expected — not everyone has signal-rich weeks)

---

## Storage

**Table:** `CoachingSummaries`

| Column | Type | Description |
|---|---|---|
| `Id` | bigint | Primary key |
| `UserId` | int | FK to Users |
| `WeekStartDate` | DateOnly | Monday of this week |
| `SummaryText` | text | GPT output (delivered to user) |
| `InterpretedNarrative` | text | C# narrative (debugging only) |
| `DeliveredAt` | timestamptz | When created (not when user reads it) |
| `DismissedAt` | timestamptz | When user dismissed card |
| `OptedOutAt` | timestamptz | If user opted out via this summary |
| `CreatedAt` | timestamptz | Row creation timestamp |

**TTL:** 90 days (cleanup runs in worker preamble)

```sql
DELETE FROM CoachingSummaries WHERE CreatedAt < NOW() - INTERVAL '90 days';
```

---

## User Flow

1. **Wednesday 18:00 UTC** — CoachingSummaryWorker runs, writes qualified summaries to DB
2. **User opens /you tab** — frontend calls `GET /coaching/current-summary`
3. **Summary card displayed** — if one exists and `DismissedAt == null`
4. **User dismisses** — frontend calls `POST /coaching/{id}/dismiss`
5. **Card disappears** — next summary arrives following Wednesday

**No push notifications** — summaries are discovered organically in the You tab.

---

## Opt-Out

Users can opt out via:
- **Settings toggle:** `POST /coaching/opt-out`
- **Effect:** 
  - Sets `User.CoachingOptedOut = true`
  - Dismisses all unread summaries
  - Future weekly runs skip this user

Users can opt back in via:
- **Settings toggle:** `POST /coaching/opt-in`
- **Effect:**
  - Sets `User.CoachingOptedOut = false`
  - Next Wednesday they're eligible again

---

## Monitoring

**Key metrics:**
- Eligible users per week
- Suppression rate (target: 15-20%)
- Avg summary length (chars)
- OpenAI token usage per summary
- Batch duration (target: <30min for 1000 users)

**Alerts:**
- Suppression rate >40% → GPT prompt degraded
- Batch duration >1h → performance regression
- OpenAI API failure rate >10% → upstream issue

---

## Files

| File | Purpose |
|---|---|
| [summary-generation.md](./summary-generation.md) | How summaries are generated (C# narrative + GPT call) |
| [delivery.md](./delivery.md) | When summaries are delivered (DeliveredAt, DismissedAt) |
| [workers.md](./workers.md) | CoachingSummaryWorker schedule + batch logic |
| [api.md](./api.md) | Coaching endpoints (GET/POST) |

---

## See Also

- [docs/systems/echo/coach.md](../echo/coach.md) — ECHO's coaching persona
- [docs/systems/echo/signals.md](../echo/signals.md) — All signal types
- [docs/systems/echo/workers.md](../echo/workers.md) — All ECHO batch workers
