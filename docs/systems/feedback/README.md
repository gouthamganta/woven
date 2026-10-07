# Feedback System

**Purpose:** Collect post-date feedback from users to improve ECHO matching quality and detect behavioral patterns.

**Status:** ✅ PRODUCTION (2026-05-25)

---

## Overview

The Feedback system requests and processes structured feedback after users meet in person or have deep chats. This data feeds back into ECHO's learning pipeline and helps detect trust/safety issues.

### Core Entities

| Entity | Table | Purpose |
|---|---|---|
| `DateFeedbackPrompt` | `date_feedback_prompts` | Scheduled feedback requests |
| `DateFeedback` | `date_feedback` | User-submitted feedback |

### Workflow

```
Match closes (BalloonState.CLOSED)
         ↓
FeedbackTriggerWorker runs daily @ 08:00 UTC
         ↓
Triggers queued → DateFeedbackPrompts created
         ↓
Push notifications sent to users
         ↓
User submits via POST /matches/{matchId}/feedback
         ↓
DateFeedback record created
         ↓
FeedbackInsightService processes insights
         ↓
ECHO weights updated + TrustService alerts triggered
```

---

## Key Features

### 1. Multi-Stage Triggers

Three trigger types based on match behavior:
- **`interested_both`** — Both users expressed date interest 5+ days ago
- **`deep_chat`** — 25+ messages, fast responses, 10+ days post-unlock
- **`silent_thread`** — Active thread went silent for 5+ days

See [triggers.md](./triggers.md) for full logic.

### 2. Reschedule Strategy

- Prompts sent → if no response in 3 days → reschedule +5 days
- Max 2 reschedules, then prompt expires
- Prevents notification spam while maximizing response rate

### 3. ECHO Signal Integration

Feedback generates `MatchSignalEventTypes.ExplicitFeedback`:
- 1-5 star rating normalized to 0.0-1.0
- Fed into `WeightLearningService` for pillar weight adjustment
- High-quality signal: 5 stars + "yes" to meet again → boost pillar weight by 0.05

### 4. Trust/Safety Detection

- **Low rating pattern**: 3+ users rate someone ≤2 stars → `TrustService.FlagAsync("LOW_ENGAGEMENT", 0.6)`
- **Explanation mismatch**: Low stars after promising match explanation → audit log for review

---

## Architecture

### Services

| Service | Responsibility |
|---|---|
| `DateFeedbackService` | Queue/send/submit feedback logic |
| `FeedbackInsightService` | Extract signals from free-text, update weights |
| `FeedbackTriggerWorker` | Daily batch job (08:00 UTC) |

### Dependencies

```
DateFeedbackService
  ├─ WovenDbContext
  ├─ INotificationService (push notifications)
  ├─ ITrustService (safety flags)
  ├─ ISecurityAuditService (PII access logs)
  ├─ IMatchSignalService (ECHO signals)
  └─ IAnalyticsService (tracking)

FeedbackInsightService
  ├─ WovenDbContext
  ├─ IOpenAiResilientClient (keyword extraction)
  ├─ ICacheService (invalidate embeddings)
  └─ ISecurityAuditService
```

---

## Data Flow

### Submission Flow

```
User opens feedback form
         ↓
GET /me/feedback-prompt
   → Returns pending prompt + partner name
         ↓
User fills out form:
  - Met in person? (bool)
  - Stars (1-5, required if met)
  - What felt right? (text, 300 char max)
  - What felt off? (text, 300 char max)
  - Meet again? (yes/no/maybe)
         ↓
POST /matches/{matchId}/feedback
         ↓
DateFeedback record created
         ↓
DateFeedbackPrompt.RespondedAt marked
         ↓
Background processing:
  1. FeedbackInsightService.ProcessNewFeedbackAsync()
  2. WeightLearningService.LearnWeightsAsync()
  3. InsightService.ComputeInsightsAsync()
  4. CheckBadRatingPatternAsync()
```

---

## Configuration

### Worker Schedule
```csharp
// FeedbackTriggerWorker.cs
private static readonly TimeSpan DailyTarget = new(8, 0, 0); // 08:00 UTC
```

### Trigger Thresholds
```csharp
// Primary: both interested in date
var primaryCutoff = DateTimeOffset.UtcNow.AddDays(-5);

// Secondary: deep chat
var deepChatCutoff = DateTimeOffset.UtcNow.AddDays(-10);
MessageCount > 25 && AvgResponseTimeMs < 7200000 (2 hours)

// Tertiary: silent thread
var silentCutoff = DateTimeOffset.UtcNow.AddDays(-5);
BalloonState == ACTIVE && LastMessageAt < cutoff
```

### Reschedule Limits
```csharp
RescheduleCount <= 2  // Max 2 retries
SentAt < cutoff       // 3 days since last send
ScheduledFor = now.AddDays(5)  // Next attempt in 5 days
```

---

## Pending Tasks Integration

The feedback system surfaces in the Assistant's pending tasks list:

```typescript
// InteractionEndpoints.cs - GET /me/pending-tasks
{
  type: "date_feedback",
  priority: 1,  // Highest priority
  data: {
    matchId: "<guid>",
    partnerName: "Sarah"
  }
}
```

The Assistant displays this as a clickable prompt to open the feedback form.

---

## Analytics Events

```csharp
AnalyticsEvents.DateFeedbackSubmitted
  → { metInPerson, stars, meetAgain }
```

---

## Files

| File | Purpose |
|---|---|
| [date-feedback.md](./date-feedback.md) | Feedback form structure |
| [triggers.md](./triggers.md) | Trigger logic details |
| [insights.md](./insights.md) | FeedbackInsightService |
| [workers.md](./workers.md) | FeedbackTriggerWorker |
| [api.md](./api.md) | HTTP endpoints |

---

## Future Enhancements

- [ ] Venue suggestion follow-up (if users went to recommended place)
- [ ] Photo upload prompt after good dates
- [ ] Aggregate "date quality" metric per user
- [ ] Gender-specific feedback prompts (women's safety signals)
- [ ] Partner feedback matching (compare both sides)

---

**Last Updated:** 2026-10-07
**Owned By:** ECHO Learning Pipeline
