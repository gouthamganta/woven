# Analytics System

**Path:** `backend/WovenBackend/Services/Analytics/`  
**Status:** Production  
**Owner:** Platform team

## Overview

Woven's analytics system tracks user behavior, retention, and A/B experiments while maintaining strict privacy guarantees. The system operates on three parallel tracks:

1. **User Interaction Logs** — General product events (page views, actions, lifecycle)
2. **Match Signal Logs** — Behavioral signals between matched users for ECHO learning
3. **Analytics Events** — Privacy-first event tracking with PII hashing for retention analysis

## Design Principles

### Privacy-First Architecture

- **12-month retention limit** — Events older than 12 months are automatically anonymized
- **User ID hashing** — Raw user IDs never stored in analytics events
- **Minimal PII** — No names, emails, or identifying details in event payloads
- **Audit logging** — All PII access logged via `ISecurityAuditService`

### Fail-Silent Strategy

Analytics failures never block user actions. All tracking is fire-and-forget with error swallowing.

```csharp
// Analytics failures are logged but never thrown
catch (Exception ex)
{
    _logger.LogWarning(ex, "[Analytics] TrackAsync failed");
    // User action continues
}
```

### Session Management

Sessions are 2-hour sliding windows cached in Redis:

- First event creates session ID (GUID)
- Session renewed on each event within 2-hour window
- Enables funnel analysis without PII

## Three Analytics Tracks

### 1. User Interaction Logs

**Table:** `user_interaction_logs`  
**Service:** `IInteractionLogService`  
**Purpose:** Product analytics, feature usage

```csharp
await _interactionLog.LogAsync(
    userId: userId,
    eventType: "moments_deck_viewed",
    context: new { deckId, itemCount }
);
```

Fields:
- `user_id` (int) — Direct user FK for join queries
- `event_type` (string) — Constant from usage patterns
- `context` (jsonb) — Flexible payload
- `occurred_at` (timestamptz)

**Use when:** Tracking general product usage, feature adoption, UI interactions

### 2. Match Signal Logs

**Table:** `match_signal_logs`  
**Service:** `IMatchSignalService`  
**Purpose:** ECHO learning, preference extraction

```csharp
await _matchSignal.RecordAsync(
    viewerId: userId,
    candidateId: matchId,
    eventType: MatchSignalEventTypes.TrialAccepted,
    eventValue: 1.0f
);
```

Fields:
- `viewer_id` / `candidate_id` — Directional signal (A→B)
- `event_type` — Constant from `MatchSignalEventTypes`
- `event_value` — Numeric payload (interpretation varies by type)
- `metadata_json` — Optional extra context

**Use when:** Recording behavioral signals for matchmaking, ECHO weight learning

See: [`docs/systems/analytics/events.md`](./events.md)

### 3. Analytics Events

**Table:** `analytics_events`  
**Service:** `IAnalyticsService`  
**Purpose:** Privacy-safe retention, cohort analysis, A/B testing

```csharp
await _analytics.TrackAsync(
    userId: userId,
    sessionId: null, // auto-generated
    eventType: AnalyticsEvents.MomentResponded,
    properties: new { action = "magical", sparkCost = 1 }
);
```

Fields:
- `user_id_hash` (string, SHA-256) — Hashed user ID for privacy
- `session_id` (string, GUID) — 2-hour window
- `event_type` — Constant from `AnalyticsEvents`
- `properties` (jsonb) — Structured payload
- `created_at` (timestamptz)

**Use when:** Cross-session funnels, retention cohorts, A/B experiment analysis

## A/B Testing

Built-in experiment framework with deterministic assignment:

```csharp
// Assign user to variant (idempotent)
var variant = await _analytics.GetOrAssignVariantAsync(userId, "new_deck_algorithm");
// Returns: "control" | "treatment"

// Track conversion
await _analytics.TrackAbConversionAsync(userId, "new_deck_algorithm", "first_match");
```

Tables:
- `ab_experiments` — Experiment definitions
- `ab_assignments` — User→variant mappings (permanent)
- `ab_conversions` — Conversion events per experiment

See: [`docs/systems/analytics/implementation.md`](./implementation.md#ab-testing)

## Workers

### AnalyticsRetentionWorker

**Schedule:** Monthly (1st of month, 02:15 UTC)  
**Job:** Anonymize `analytics_events` older than 12 months

```csharp
// Strips PII from old events
await db.AnalyticsEvents
    .Where(e => e.CreatedAt < cutoff && e.UserIdHash != null)
    .ExecuteUpdateAsync(s => s
        .SetProperty(e => e.UserIdHash, (string?)null)
        .SetProperty(e => e.SessionId, (string?)null));
```

See: [`docs/systems/analytics/workers.md`](./workers.md)

## Privacy Guarantees

1. **No raw user IDs in `analytics_events`** — Only SHA-256 hashes
2. **Automatic PII expiry** — 12-month retention enforced by worker
3. **Session anonymity** — Sessions auto-expire after 2 hours
4. **Audit trail** — PII access logged to `security_audit_logs`

See: [`docs/systems/analytics/privacy.md`](./privacy.md)

## Event Catalog

75+ tracked events across:
- User lifecycle (registration, onboarding, profile updates)
- Discovery (Moments, Commons, tiles)
- Matching (creation, expiry, Find Love)
- Conversations (chat, games, nudges)
- Dating (date interest, venue suggestions, feedback)
- Trust & Safety (verification, reports, blocks)

See: [`docs/systems/analytics/events.md`](./events.md)

## Integration Points

```
Program.cs
  ├─ IInteractionLogService → scoped (direct DB writes)
  ├─ IMatchSignalService → scoped (ECHO data)
  └─ IAnalyticsService → scoped (privacy-safe events)

AnalyticsRetentionWorker
  └─ Hosted service (monthly anonymization)
```

## Files

| Path | Purpose |
|------|---------|
| `IAnalyticsService.cs` | Privacy-first tracking + A/B testing interface |
| `AnalyticsService.cs` | Implementation with user ID hashing |
| `NullAnalyticsService.cs` | No-op implementation for testing |
| `AnalyticsEvents.cs` | Catalog of 75+ event type constants |
| `AnalyticsRetentionWorker.cs` | Monthly PII anonymization worker |
| `IInteractionLogService.cs` | Simple user interaction logging |
| `MatchSignalService.cs` | ECHO behavioral signal tracking |

## Related Systems

- **ECHO** — Consumes `match_signal_logs` for weight learning
- **Trust Score** — Reads signals but never writes to compatibility
- **Coaching** — Triggers based on interaction patterns
- **Notifications** — Event-driven push via `NotificationService`

## Next Steps

- [ ] Retention dashboard (Grafana + `analytics_events`)
- [ ] Funnel analysis tooling (session-based cohorts)
- [ ] A/B experiment UI (experiment setup + results)
- [ ] Real-time event streaming (Redis pub/sub)
