# Retention Tracking

How Woven measures and tracks user retention while respecting privacy.

## Overview

Woven's retention system balances three goals:
1. **Measure engagement** — Track cohort retention over time
2. **Respect privacy** — Auto-anonymize old data
3. **Enable analysis** — Session-based funnels without raw user IDs

## Retention Windows

### User-Level Retention

**Data source:** `analytics_events` table  
**Key field:** `user_id_hash` (SHA-256)

Cohort definitions:
- **Registration cohort** — Users who signed up in same week
- **Onboarding cohort** — Users who completed onboarding
- **First match cohort** — Users who got first match
- **Trial cohort** — Users who reached trial stage

Retention buckets:
- **D1** — Day 1 (next day after cohort join)
- **D3** — Day 3
- **D7** — Day 7 (weekly active)
- **D14** — Day 14
- **D30** — Day 30 (monthly active)
- **D60** — Day 60
- **D90** — Day 90 (quarterly active)

### Session-Level Retention

**Data source:** `analytics_events.session_id`  
**Window:** 2 hours (sliding)

Session lifecycle:
1. User opens app → `GetOrCreateSessionIdAsync()` called
2. Session ID cached in Redis (2h TTL)
3. All events within 2h share session ID
4. Session expires → new ID on next open

**Use case:** Funnel analysis without PII

Example: "% of users who view Deck → respond → match in single session"

## Retention Metrics

### Product Health

| Metric | Definition | Calculation |
|--------|------------|-------------|
| DAU | Daily Active Users | Distinct `user_id_hash` in last 24h with `app_opened` |
| WAU | Weekly Active Users | Distinct `user_id_hash` in last 7d with `app_opened` |
| MAU | Monthly Active Users | Distinct `user_id_hash` in last 30d with `app_opened` |
| Stickiness | DAU/MAU ratio | Higher = more engaged |

### Cohort Retention

**Example query:**

```sql
-- D7 retention for users who registered in week of 2026-10-01
WITH cohort AS (
  SELECT user_id_hash, DATE_TRUNC('day', created_at) AS reg_date
  FROM analytics_events
  WHERE event_type = 'user_registered'
    AND created_at >= '2026-10-01'
    AND created_at < '2026-10-08'
),
d7_active AS (
  SELECT DISTINCT ae.user_id_hash
  FROM analytics_events ae
  JOIN cohort c ON ae.user_id_hash = c.user_id_hash
  WHERE ae.event_type = 'app_opened'
    AND ae.created_at >= c.reg_date + INTERVAL '7 days'
    AND ae.created_at < c.reg_date + INTERVAL '8 days'
)
SELECT 
  COUNT(DISTINCT cohort.user_id_hash) AS cohort_size,
  COUNT(DISTINCT d7_active.user_id_hash) AS d7_retained,
  ROUND(100.0 * COUNT(DISTINCT d7_active.user_id_hash) / COUNT(DISTINCT cohort.user_id_hash), 2) AS d7_retention_pct
FROM cohort
LEFT JOIN d7_active ON cohort.user_id_hash = d7_active.user_id_hash;
```

### Behavioral Cohorts

Segment retention by behavior:

```sql
-- Retention by first Moment response type
WITH first_response AS (
  SELECT 
    user_id_hash,
    properties->>'action' AS action,
    ROW_NUMBER() OVER (PARTITION BY user_id_hash ORDER BY created_at) AS rn
  FROM analytics_events
  WHERE event_type = 'moment_responded'
),
cohort AS (
  SELECT user_id_hash, action
  FROM first_response
  WHERE rn = 1
)
-- Then measure D7/D14/D30 by `action` (MAGICAL vs LOGICAL)
```

## Privacy-First Retention

### Hashed User IDs

Raw user IDs **never** stored in `analytics_events`:

```csharp
var userIdHash = userId.HasValue
    ? PiiSanitizer.HashForAudit(userId.Value.ToString(), _hashSalt)
    : null;

db.AnalyticsEvents.Add(new AnalyticsEvent
{
    UserIdHash = userIdHash, // SHA-256(userId + salt)
    EventType = eventType,
    // ...
});
```

**Salt:** Configured via `Analytics:HashSalt` in app settings

**Properties:**
- Same user → same hash (within retention window)
- Cannot reverse hash to get raw user ID
- Hash changes if salt rotates

### 12-Month Auto-Anonymization

**Worker:** `AnalyticsRetentionWorker`  
**Schedule:** Monthly (1st of month, 02:15 UTC)

```csharp
var cutoff = DateTimeOffset.UtcNow.AddMonths(-12);

await db.AnalyticsEvents
    .Where(e => e.CreatedAt < cutoff && e.UserIdHash != null)
    .ExecuteUpdateAsync(s => s
        .SetProperty(e => e.UserIdHash, (string?)null)
        .SetProperty(e => e.SessionId, (string?)null));
```

After anonymization:
- Event still exists (for aggregate metrics)
- `user_id_hash` = null (no cohort tracking)
- `session_id` = null (no session analysis)
- `properties` preserved (for A/B test results)

See: [`workers.md`](./workers.md#analyticsretentionworker)

### Session Anonymity

Sessions expire after 2 hours:

```csharp
private async Task<string?> GetOrCreateSessionIdAsync(int userId, CancellationToken ct)
{
    var key = $"analytics:session:{userId}";
    var existing = await _cache.GetAsync<string>(key, ct);
    if (existing != null) return existing;

    var newSessionId = Guid.NewGuid().ToString("N");
    await _cache.SetAsync(key, newSessionId, TimeSpan.FromHours(2), ct);
    return newSessionId;
}
```

**Properties:**
- Session ID is random GUID (no PII)
- Cached in Redis (ephemeral)
- Expires 2h after last activity
- Not tied to device ID or IP

## Funnel Analysis

### Example: Onboarding Funnel

Track % of users who complete each step:

```sql
WITH cohort AS (
  SELECT DISTINCT user_id_hash
  FROM analytics_events
  WHERE event_type = 'user_registered'
    AND created_at >= NOW() - INTERVAL '7 days'
),
steps AS (
  SELECT 
    cohort.user_id_hash,
    MAX(CASE WHEN ae.event_type = 'onboarding_step_completed' 
             AND ae.properties->>'step' = 'personality' THEN 1 ELSE 0 END) AS completed_personality,
    MAX(CASE WHEN ae.event_type = 'onboarding_step_completed' 
             AND ae.properties->>'step' = 'photos' THEN 1 ELSE 0 END) AS completed_photos,
    MAX(CASE WHEN ae.event_type = 'onboarding_step_completed' 
             AND ae.properties->>'step' = 'foundational' THEN 1 ELSE 0 END) AS completed_foundational
  FROM cohort
  LEFT JOIN analytics_events ae ON cohort.user_id_hash = ae.user_id_hash
  GROUP BY cohort.user_id_hash
)
SELECT 
  COUNT(*) AS total,
  SUM(completed_personality) AS personality_ct,
  SUM(completed_photos) AS photos_ct,
  SUM(completed_foundational) AS foundational_ct,
  ROUND(100.0 * SUM(completed_personality) / COUNT(*), 2) AS personality_pct,
  ROUND(100.0 * SUM(completed_photos) / COUNT(*), 2) AS photos_pct,
  ROUND(100.0 * SUM(completed_foundational) / COUNT(*), 2) AS foundational_pct
FROM steps;
```

### Example: Moment → Match Funnel

```sql
WITH session_events AS (
  SELECT 
    session_id,
    MAX(CASE WHEN event_type = 'moments_deck_viewed' THEN 1 ELSE 0 END) AS viewed_deck,
    MAX(CASE WHEN event_type = 'moment_responded' THEN 1 ELSE 0 END) AS responded,
    MAX(CASE WHEN event_type = 'match_created' THEN 1 ELSE 0 END) AS matched
  FROM analytics_events
  WHERE created_at >= NOW() - INTERVAL '7 days'
    AND session_id IS NOT NULL
  GROUP BY session_id
)
SELECT 
  COUNT(*) AS sessions,
  SUM(viewed_deck) AS viewed_deck_ct,
  SUM(responded) AS responded_ct,
  SUM(matched) AS matched_ct,
  ROUND(100.0 * SUM(responded) / NULLIF(SUM(viewed_deck), 0), 2) AS deck_to_response_pct,
  ROUND(100.0 * SUM(matched) / NULLIF(SUM(responded), 0), 2) AS response_to_match_pct
FROM session_events;
```

## Retention Strategy

### What We Track

**High priority:**
- Daily active users (DAU/WAU/MAU)
- Cohort retention (D1/D7/D30)
- Onboarding completion rates
- First match time (time to value)
- Trial conversion rates
- Find Love unlock rates

**Medium priority:**
- Session length distribution
- Feature adoption (games, voice notes)
- A/B experiment retention impact
- Notification engagement

**Low priority:**
- Page view counts
- Button click rates
- Hover events
- Non-critical UI interactions

### What We Don't Track

- ❌ Raw user IDs in analytics DB
- ❌ Events older than 12 months (with PII)
- ❌ Device fingerprints
- ❌ IP addresses
- ❌ Precise location data
- ❌ Message content
- ❌ Photo metadata

## Retention Goals (Target)

| Metric | Target | Status |
|--------|--------|--------|
| D1 retention | 50% | 🔴 Not measured yet |
| D7 retention | 30% | 🔴 Not measured yet |
| D30 retention | 15% | 🔴 Not measured yet |
| Onboarding completion | 80% | 🔴 Not measured yet |
| Time to first match | <48h (median) | 🔴 Not measured yet |
| Trial conversion | 40% | 🔴 Not measured yet |

## Retention Improvements

### High-Impact Levers

1. **Faster time to first match** — D1 retention correlates with match speed
2. **Quality over quantity** — Better matches → higher D7 retention
3. **Trial stage conversion** — Users who continue trials stay longer
4. **Find Love unlock** — Strongest retention signal

### A/B Test Ideas

- Deck refresh timing (daily vs 12h vs 6h)
- Trial duration (3min vs 5min vs 10min)
- Push notification frequency (daily vs 3x/week)
- Coaching delivery timing (morning vs evening)

See: [`implementation.md#ab-testing`](./implementation.md#ab-testing)

## Analytics Dashboard (Planned)

**Stack:** Grafana + PostgreSQL  
**Metrics:**
- Real-time DAU/WAU/MAU
- Cohort retention heatmap
- Funnel conversion charts
- A/B experiment results

**Privacy:**
- Dashboard queries hashed IDs only
- No individual user drill-down
- Aggregates only (min 10 users per cohort)

## Related Docs

- [Analytics System Overview](./README.md)
- [Event Catalog](./events.md)
- [Workers](./workers.md)
- [Privacy Approach](./privacy.md)
- [Implementation Guide](./implementation.md)
