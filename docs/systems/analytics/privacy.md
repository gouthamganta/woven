# Analytics Privacy Approach

How Woven's analytics system protects user privacy while enabling product insights.

## Privacy Principles

1. **Minimal data collection** — Track actions, not identities
2. **Time-bound retention** — Auto-delete PII after 12 months
3. **Hashed identifiers** — No raw user IDs in analytics DB
4. **Session anonymity** — 2-hour sliding windows, no device fingerprinting
5. **Fail-silent tracking** — Analytics failures never block user actions
6. **Audit trails** — PII access logged to security audit table

---

## What We Track

### User Behavior (Not Identity)

**We track:**
- ✅ User clicked "Magical" (action)
- ✅ User spent 45 seconds on tile (engagement)
- ✅ User completed onboarding step 3 (progress)
- ✅ User sent voice note (interaction type)

**We don't track:**
- ❌ User's real name
- ❌ User's email address
- ❌ User's phone number
- ❌ User's precise location (GPS coords)
- ❌ User's IP address
- ❌ User's device ID / IMEI
- ❌ Message content

### Event Properties vs PII

**Safe to log:**
- `{ action: "magical" }` — choice made
- `{ tileType: "voice" }` — content type
- `{ durationSecs: 45 }` — engagement metric
- `{ stepIndex: 3 }` — progress indicator

**Never logged:**
- `{ fullName: "..." }` — identifying info
- `{ email: "..." }` — contact info
- `{ messageText: "..." }` — private content
- `{ lat: ..., lng: ... }` — precise location
- `{ deviceId: "..." }` — device fingerprint

---

## Three-Tier Privacy Model

### Tier 1: UserInteractionLog (Product Analytics)

**Table:** `user_interaction_logs`  
**Privacy level:** Medium  
**User ID:** Direct FK (integer)

**Purpose:** Product analytics, feature usage, UI interactions

**Retention:** Indefinite (no auto-deletion)

**Why direct user_id:**
- Needed for user-level queries (profile visits, tile engagement)
- Used for debugging (user reports "tile not loading")
- Tied to app functionality (not pure analytics)

**Privacy controls:**
- No PII in `context` field
- Access logged to security audit
- Query access restricted (backend only)

### Tier 2: AnalyticsEvent (Privacy-Safe Analytics)

**Table:** `analytics_events`  
**Privacy level:** High  
**User ID:** SHA-256 hash

**Purpose:** Retention analysis, cohort tracking, A/B testing

**Retention:** 12 months (auto-anonymized)

**Why hashed user_id:**
- Enables cohort analysis (same hash = same user)
- Cannot reverse to real user ID
- Separates analytics from production DB

**Privacy controls:**
- Hash salt rotates on security incidents
- Auto-anonymization via `AnalyticsRetentionWorker`
- Session IDs expire after 2h

### Tier 3: MatchSignalLog (ECHO Learning)

**Table:** `match_signal_logs`  
**Privacy level:** Low (internal ML)  
**User ID:** Direct viewer/candidate IDs

**Purpose:** ECHO weight learning, preference extraction

**Retention:** Indefinite (needed for ML retraining)

**Why direct user_id:**
- ML requires stable user identifiers
- Joined with `users` table for foundational scores
- Never exposed in public APIs

**Privacy controls:**
- No message content (only signals)
- `UserFlagged` events feed trust score only
- Access restricted to backend services

---

## User ID Hashing Strategy

### How It Works

**Implementation:**

```csharp
var userIdHash = userId.HasValue
    ? PiiSanitizer.HashForAudit(userId.Value.ToString(), _hashSalt)
    : null;
```

**Hash function:** SHA-256

**Salt:** Configured via `Analytics:HashSalt` in app settings

**Example:**

```csharp
userId = 12345
hashSalt = "woven-analytics-salt-v1"
input = "12345" + "woven-analytics-salt-v1"
userIdHash = SHA256(input) = "a3f8c9d2e1b4f7a2..."
```

### Properties

**Deterministic:** Same user → same hash (within retention window)

**One-way:** Cannot reverse hash → original user ID

**Collision-resistant:** Different users → different hashes (SHA-256 strength)

**Salt-dependent:** Changing salt invalidates all hashes

### Salt Rotation

**When to rotate:**
- Security incident (salt leak)
- Major privacy policy change
- Migration to new analytics provider

**Impact:**
- All historical hashes invalidated
- Cohort analysis breaks across rotation boundary
- A/B test results preserved (pre-rotation data still valid)

**Process:**

1. Change `Analytics:HashSalt` in app settings
2. Deploy new backend
3. Historical events remain with old hashes
4. New events use new hashes
5. Mark rotation date in audit log

---

## Session Privacy

### Session Lifecycle

**Creation:**

```csharp
var sessionId = Guid.NewGuid().ToString("N");
await _cache.SetAsync($"analytics:session:{userId}", sessionId, TimeSpan.FromHours(2));
```

**Properties:**
- Random GUID (no PII)
- Cached in Redis (ephemeral)
- 2-hour sliding window
- Not tied to device or IP

**Expiry:**
- Auto-expires after 2h of inactivity
- Not persisted to disk
- Cleared on app uninstall

### What Sessions Enable

**Funnel analysis:**

```sql
-- % of users who view Deck → respond → match in single session
SELECT 
  session_id,
  MAX(CASE WHEN event_type = 'moments_deck_viewed' THEN 1 ELSE 0 END) AS viewed,
  MAX(CASE WHEN event_type = 'moment_responded' THEN 1 ELSE 0 END) AS responded,
  MAX(CASE WHEN event_type = 'match_created' THEN 1 ELSE 0 END) AS matched
FROM analytics_events
WHERE session_id IS NOT NULL
GROUP BY session_id;
```

**Session length distribution:**

```sql
SELECT 
  session_id,
  MIN(created_at) AS session_start,
  MAX(created_at) AS session_end,
  EXTRACT(EPOCH FROM (MAX(created_at) - MIN(created_at))) AS duration_secs
FROM analytics_events
WHERE session_id IS NOT NULL
GROUP BY session_id;
```

### What Sessions Don't Track

**No device fingerprinting:**
- ❌ Device ID / IMEI
- ❌ Browser fingerprint
- ❌ Screen resolution
- ❌ Installed fonts
- ❌ Canvas fingerprint

**No location tracking:**
- ❌ IP address
- ❌ GPS coordinates
- ❌ WiFi network IDs
- ❌ Bluetooth beacons

---

## 12-Month Retention Policy

### Why 12 Months?

**Balances:**
- Business need (YoY retention analysis)
- User trust (data doesn't live forever)
- Privacy best practices (GDPR-inspired)

**Alternatives considered:**
- 6 months — too short for seasonal trends
- 24 months — too long for privacy-first approach
- Indefinite — unacceptable privacy risk

### Anonymization Process

**Worker:** `AnalyticsRetentionWorker`  
**Schedule:** Monthly (1st @ 02:15 UTC)

**What happens:**

1. Find events where `created_at < NOW() - 12 months`
2. Set `user_id_hash = null`
3. Set `session_id = null`
4. Keep `event_type` and `properties`

**Result:**
- Event still exists (for aggregate metrics)
- No longer tied to user or session
- Still usable for A/B test results

**Example:**

Before (10 months old):
```json
{
  "user_id_hash": "a3f8c9...",
  "session_id": "8f3c9a...",
  "event_type": "moment_responded",
  "properties": "{\"action\":\"magical\"}"
}
```

After (13 months old):
```json
{
  "user_id_hash": null,
  "session_id": null,
  "event_type": "moment_responded",
  "properties": "{\"action\":\"magical\"}"
}
```

See: [`workers.md#analyticsretentionworker`](./workers.md#analyticsretentionworker)

---

## What Users Can Request

### Data Export (GDPR-Style)

**Scope:** All analytics events tied to user ID

**Process:**

1. User requests export via settings
2. Backend queries `user_interaction_logs` by `user_id`
3. Backend queries `analytics_events` by SHA-256(user_id + salt)
4. Generate JSON export
5. Download link emailed to user

**Includes:**
- Event types
- Event timestamps
- Event properties (no PII)
- Session IDs

**Excludes:**
- Other users' events
- Aggregated metrics
- A/B test assignments (anonymized)

### Data Deletion

**Scope:** All analytics events tied to user ID

**Process:**

1. User requests deletion via settings
2. Backend deletes from `user_interaction_logs`
3. Backend anonymizes from `analytics_events` (set hash to null)
4. Deletion logged to security audit

**Impact:**
- User's individual events removed
- Aggregated metrics unaffected
- A/B test results unaffected (already anonymized)

**Confirmation:**

```
Your analytics data has been deleted.
- User interaction logs: deleted
- Analytics events: anonymized
- Match signal logs: retained (ECHO learning)
```

**Note:** `match_signal_logs` retained for ML training (not considered PII under Woven's privacy policy)

---

## Audit Trail

### Security Audit Logging

Every PII access logged:

```csharp
audit.Log("pii_access",
    service: "AnalyticsRetentionWorker",
    resourceType: "analytics_events",
    piiStripped: true);
```

**Logged events:**
- PII accessed (user ID hashing)
- PII stripped (anonymization runs)
- Analytics export requests
- Analytics deletion requests
- Session creation (high-volume, sampled)

**Table:** `security_audit_logs`

**Retention:** 2 years (security compliance)

### Query Access Logging

All direct analytics queries logged:

```csharp
_logger.LogInformation(
    "[Analytics] Query executed | Type={QueryType} UserId={UserId}",
    "cohort_retention", userId);
```

**Use case:** Detect unauthorized analytics access

---

## Fail-Silent Strategy

Analytics failures never block user actions:

```csharp
public Task TrackAsync(int? userId, string? sessionId, string eventType, ...)
{
    _ = Task.Run(async () =>  // Fire-and-forget
    {
        try
        {
            // Log event
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Analytics] TrackAsync failed");
            // User action continues
        }
    });

    return Task.CompletedTask; // Immediate return
}
```

**Benefits:**
- User experience never degraded by analytics
- Analytics DB downtime doesn't break app
- Temporary tracking failures acceptable

**Trade-off:**
- Some events may be lost (acceptable for analytics)
- No guarantees on event delivery
- Use background Task.Run (not awaited)

---

## Compliance Status

### GDPR (EU)

**Status:** ✅ GDPR-compatible (though not required for India market)

**Compliance:**
- ✅ Right to access (data export)
- ✅ Right to deletion (anonymization)
- ✅ Data minimization (hashed IDs, 12mo retention)
- ✅ Purpose limitation (analytics only)
- ✅ Storage limitation (auto-deletion)

### India Data Protection (Planned)

**Status:** 🟡 Awaiting Digital Personal Data Protection Act enforcement

**Preparedness:**
- ✅ Data localization (Azure India Central)
- ✅ User consent (onboarding opt-in)
- ✅ Data retention limits (12 months)
- 🔴 Not yet: explicit consent UI for analytics

---

## Privacy-First Analytics Checklist

### ✅ DO

- Hash user IDs before storing in analytics DB
- Set auto-deletion policies (12 months)
- Use session IDs for funnels (not device IDs)
- Log PII access to audit trail
- Fire-and-forget tracking (fail-silent)
- Keep event properties <1KB (avoid bloat)

### ❌ DON'T

- Store raw user IDs in `analytics_events`
- Log message content or private conversations
- Track precise GPS coordinates
- Fingerprint devices
- Block user actions on analytics failures
- Keep analytics data indefinitely

---

## Related Docs

- [Analytics System Overview](./README.md)
- [Event Catalog](./events.md)
- [Retention Tracking](./retention.md)
- [Workers](./workers.md)
- [Implementation Guide](./implementation.md)
