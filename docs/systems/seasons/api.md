# Seasons API

**Endpoints:** `SeasonEndpoints.cs`  
**Base Path:** `/seasons`  
**Authentication:** Required (JWT Bearer)

---

## Endpoints

### GET /seasons/current

**Purpose:** Fetch current active season + user's response status

#### Request

**Headers:**
```
Authorization: Bearer <jwt>
```

**Query Parameters:** None

#### Response (200 OK)

**When active season exists:**
```json
{
  "season": {
    "id": 42,
    "seasonNumber": 5,
    "startDate": "2026-10-01",
    "endDate": "2026-10-22",
    "promptText": "What kind of energy do you want more of in your life this month?"
  },
  "userStatus": "unanswered",        // or "answered"
  "responseCount": 0,                 // 0-8 (how many pillars answered)
  "signaturePrompt": "What kind of energy do you want more of in your life this month?"
}
```

**When no active season:**
```json
{
  "season": null,
  "userStatus": "not_started",
  "responseCount": 0,
  "signaturePrompt": null
}
```

#### Response Codes

| Code | Meaning |
|---|---|
| `200 OK` | Success (season may be null) |
| `401 Unauthorized` | Missing or invalid JWT |

#### userStatus Values

| Status | Meaning |
|---|---|
| `"not_started"` | No active season exists (season is null) |
| `"unanswered"` | Season active, user hasn't submitted any responses |
| `"answered"` | User submitted at least one response (responseCount > 0) |

**Note:** `"answered"` doesn't mean all 8 pillars are complete. Check `responseCount` to determine coverage.

---

### PUT /seasons/current/responses

**Purpose:** Submit or update user's season responses

#### Request

**Headers:**
```
Authorization: Bearer <jwt>
Content-Type: application/json
```

**Body:**
```json
[
  {
    "pillarId": "lifestyle",
    "questionId": "fq_lifestyle_1",
    "response": "I've been prioritizing outdoor activities and early mornings more than before."
  },
  {
    "pillarId": "energy",
    "questionId": "fq_energy_2",
    "response": "I need quiet time to recharge, but I'm more open to social plans on weekends now."
  }
  // ... up to 8 items (one per pillar)
]
```

**Field Constraints:**

| Field | Type | Required | Max Length | Notes |
|---|---|---|---|---|
| `pillarId` | string | ✅ | 50 | Must match one of the 8 pillars |
| `questionId` | string | ✅ | 100 | Which foundational question was answered |
| `response` | string | ✅ | unlimited | Free-text answer (no max enforced at API level) |

**Valid `pillarId` Values:**
- `"lifestyle"`
- `"energy"`
- `"communication"`
- `"affection"`
- `"stability"`
- `"values"`
- `"curiosity"`
- `"ambition"`

**Upsert Semantics:**
- If user already answered a pillar this season → UPDATE response
- Else → INSERT new response
- Allows partial submissions (e.g., 3 pillars now, 5 more later)

#### Response (200 OK)

**Success:**
```json
{
  "submitted": true,
  "nextSeasonDate": "2026-10-23"
}
```

**Fields:**

| Field | Type | Meaning |
|---|---|---|
| `submitted` | bool | Always `true` on success |
| `nextSeasonDate` | string (ISO date) | When next season starts (current EndDate + 1 day) |

#### Response (400 Bad Request)

**When array is empty or null:**
```json
{
  "error": "RESPONSES_REQUIRED"
}
```

**When no active season:**
```json
{
  "error": "NO_ACTIVE_SEASON"
}
```

**Scenario:** User tries to submit after season expired (but before next season created at 01:00 UTC).

#### Response Codes

| Code | Meaning |
|---|---|
| `200 OK` | Responses saved successfully |
| `400 Bad Request` | Empty body or no active season |
| `401 Unauthorized` | Missing or invalid JWT |

#### Side Effects

On success, triggers (fire-and-forget):
1. **Analytics Event:** `SeasonResponseSubmitted` with `{ seasonNumber }`
2. **Cache Invalidation:** `CacheKeys.PillarEmbedding(userId)` deleted
3. **Re-Embedding:** `IUserVectorBuilder.BuildAndSaveV1Async(userId)` called
4. **Notification:** SignalR event `SeasonResponseSubmitted` sent to user

**Failures in side effects are logged but don't block the 200 response.**

---

## Implementation Notes

### Auth

Both endpoints use shared `GetUserId(ClaimsPrincipal)` logic:

```csharp
private static int GetUserId(ClaimsPrincipal user)
{
    var uid = user.FindFirstValue("uid");
    if (int.TryParse(uid, out var id)) return id;
    var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
    if (int.TryParse(sub, out id)) return id;
    throw new UnauthorizedAccessException("Missing user id claim");
}
```

**Claim Priority:**
1. `"uid"` (Woven-specific claim)
2. `"sub"` (standard JWT subject)
3. `ClaimTypes.NameIdentifier` (legacy fallback)

**Note:** This is a local copy. Should migrate to `EndpointHelper.GetUserId()` for consistency.

### Idempotency

**Not Implemented** — no idempotency key support.

**Implications:**
- Re-submitting same request → upserts existing responses (safe)
- No way to detect accidental duplicate POST (e.g., double-click)
- Mitigated by upsert semantics (same data → same result)

**Future Enhancement:** Add `X-Idempotency-Key` header support (see `IdempotencyService` pattern from trial/pop endpoints).

---

## Usage Examples

### Frontend Flow (TypeScript)

```typescript
// 1. Fetch current season on page load
const { season, userStatus, responseCount } = await this.http.get('/seasons/current').toPromise();

if (!season) {
  // Show "No active season" message
  return;
}

if (userStatus === 'answered') {
  // Show "You've already answered (responseCount/8 pillars)"
  return;
}

// 2. User answers questions, submit responses
const responses = [
  { pillarId: 'lifestyle', questionId: 'fq_lifestyle_1', response: '...' },
  { pillarId: 'energy', questionId: 'fq_energy_2', response: '...' },
  // ...
];

const result = await this.http.put('/seasons/current/responses', responses).toPromise();

if (result.submitted) {
  // Show success: "Submitted! Next season starts {result.nextSeasonDate}"
}
```

### cURL Examples

**Get current season:**
```bash
curl -X GET https://wooven.me/seasons/current \
  -H "Authorization: Bearer <jwt>"
```

**Submit responses:**
```bash
curl -X PUT https://wooven.me/seasons/current/responses \
  -H "Authorization: Bearer <jwt>" \
  -H "Content-Type: application/json" \
  -d '[
    {
      "pillarId": "lifestyle",
      "questionId": "fq_lifestyle_1",
      "response": "I prioritize early mornings and outdoor activities."
    },
    {
      "pillarId": "values",
      "questionId": "fq_values_3",
      "response": "Honesty and kindness matter most to me."
    }
  ]'
```

---

## Error Handling

### Client-Side Best Practices

**Scenario: Submit when season expired**
```typescript
try {
  await this.http.put('/seasons/current/responses', responses).toPromise();
} catch (err) {
  if (err.status === 400 && err.error.error === 'NO_ACTIVE_SEASON') {
    // Show: "Season ended. New season coming soon — check back tomorrow."
  } else {
    // Generic error handling
  }
}
```

**Scenario: Empty responses array**
```typescript
if (responses.length === 0) {
  // Don't submit — frontend validation
  return;
}
```

**Scenario: Re-embedding fails (backend side effect)**
- User still sees 200 success
- Embedding regenerates on next daily batch worker run
- No user-facing error (seamless degradation)

---

## Rate Limiting

**Not Implemented** — no rate limit on season endpoints.

**Risk Assessment:**
- `GET /seasons/current` — safe (read-only, cheap query)
- `PUT /seasons/current/responses` — low abuse risk (user can only submit for themselves, upsert semantics prevent spam)

**Future Consideration:** Add global rate limit (e.g., 10 writes/minute per user) if abuse observed.

---

## SignalR Events

### SeasonResponseSubmitted

**Event:** `"SeasonResponseSubmitted"`

**Payload:**
```json
{
  "submitted": true
}
```

**When Sent:** After successful `PUT /seasons/current/responses`

**Purpose:** Notify frontend to update UI (e.g., show "Responses saved" toast)

**Delivery:** Best-effort (fire-and-forget). Failures logged but don't block HTTP response.

### NewSeasonStarted

**Event:** `"NewSeasonStarted"`

**Payload:**
```json
{
  "seasonNumber": 6,
  "promptText": "What kind of energy do you want more of in your life this month?"
}
```

**When Sent:** SeasonTransitionWorker creates new season (01:00 UTC daily)

**Who Receives:** All users with `ProfileStatus = COMPLETE`

**Purpose:** Notify users that new season is available (re-engagement hook)

**Typical Frontend Handling:**
- Show in-app notification: "New season started: {promptText}"
- Badge on profile tab
- Optional push notification (if WebPush enabled)

---

## Database Queries (Performance)

### GET /seasons/current

**Query 1: Find active season**
```sql
SELECT * FROM seasons
WHERE start_date <= CURRENT_DATE AND end_date >= CURRENT_DATE
LIMIT 1;
```
**Index Used:** `(start_date, end_date)` composite index  
**Typical Rows Scanned:** 1 (at most 1 active season exists)

**Query 2: Count user responses**
```sql
SELECT COUNT(*) FROM user_season_responses
WHERE user_id = $1 AND season_id = $2;
```
**Index Used:** `(user_id, season_id, pillar_id)` unique index  
**Typical Rows Scanned:** 0-8 (one per pillar)

**Total Latency:** ~30ms

### PUT /seasons/current/responses

**Query 1: Find active season** (same as GET)

**Query 2: Load existing responses**
```sql
SELECT * FROM user_season_responses
WHERE user_id = $1 AND season_id = $2;
```
**Index Used:** `(user_id, season_id, pillar_id)` unique index  
**Typical Rows Returned:** 0-8

**Query 3: Upsert responses** (per pillar, batched in one transaction)
```sql
-- If exists (found in dictionary):
UPDATE user_season_responses
SET response = $1, question_id = $2, responded_at = NOW()
WHERE id = $3;

-- If not exists:
INSERT INTO user_season_responses (user_id, season_id, pillar_id, question_id, response)
VALUES ($1, $2, $3, $4, $5);
```
**Index Used:** Unique constraint enforces no duplicates  
**Typical Rows Written:** 1-8 (one per pillar in request)

**Total Latency:** ~100-150ms (for 8 pillars)

---

## Testing

### Manual Test Cases

**TC1: Get current season (none exists)**
```bash
GET /seasons/current
Expected: { season: null, userStatus: "not_started", responseCount: 0, signaturePrompt: null }
```

**TC2: Get current season (exists, user hasn't answered)**
```bash
GET /seasons/current
Expected: { season: {...}, userStatus: "unanswered", responseCount: 0, signaturePrompt: "..." }
```

**TC3: Submit responses (first time)**
```bash
PUT /seasons/current/responses
Body: [{ pillarId: "lifestyle", questionId: "fq_1", response: "test" }]
Expected: { submitted: true, nextSeasonDate: "2026-10-23" }
```

**TC4: Submit responses (update existing)**
```bash
# First submit (TC3)
# Then submit again with different response
PUT /seasons/current/responses
Body: [{ pillarId: "lifestyle", questionId: "fq_1", response: "updated" }]
Expected: { submitted: true, nextSeasonDate: "2026-10-23" }
Verify: Database shows updated response (not duplicate)
```

**TC5: Submit when no active season**
```bash
# Set all seasons' end_date to yesterday in DB
PUT /seasons/current/responses
Body: [{ pillarId: "lifestyle", questionId: "fq_1", response: "test" }]
Expected: 400 Bad Request { error: "NO_ACTIVE_SEASON" }
```

**TC6: Submit empty array**
```bash
PUT /seasons/current/responses
Body: []
Expected: 400 Bad Request { error: "RESPONSES_REQUIRED" }
```

### Integration Test (End-to-End)

**Scenario:** New season creation → user submits → embeddings updated → match scores refreshed

**Steps:**
1. Trigger `SeasonTransitionWorker.CheckAndTransitionSeasonAsync()` manually
2. Verify new season created in `seasons` table
3. Verify `NewSeasonStarted` SignalR event sent to all COMPLETE users
4. User calls `PUT /seasons/current/responses` with 8 pillars
5. Verify 8 rows in `user_season_responses` table
6. Verify `CacheKeys.PillarEmbedding(userId)` no longer in Redis
7. Wait for re-embedding to complete (background task)
8. Verify `user_vectors.v1_embedding` updated with new timestamp
9. Verify next day's `DailyDeckOrchestrator` uses updated embeddings

---

## Security Considerations

### Input Validation

**Current:**
- `pillarId` — not validated (no enum check, assumes frontend sends correct values)
- `questionId` — not validated (accepts any string)
- `response` — no max length (Postgres text column, unlimited)

**Risks:**
- Malicious client could submit invalid pillarId → orphaned data (doesn't match 8 pillars)
- Extremely long response (e.g., 1MB text) → database bloat

**Mitigation:**
- Pillar validation should be added (whitelist of 8 IDs)
- Response max length should be enforced (e.g., 2000 chars)
- Frontend already constrains these, but backend should validate

### Authorization

**Current:**
- User can only submit responses for themselves (userId extracted from JWT)
- No way to submit for another user (secure)

**Edge Case:**
- Admin endpoints don't exist for seasons (can't manually edit responses)
- If added, must require `ADMIN` role

### Data Privacy

**Current:**
- Season responses are never shown to other users (private)
- Used only for embedding generation (ML pipeline)
- No public leaderboard or "who answered" visibility

**Future Risk:**
- If seasonal insights added ("You and 30% of users prioritize X"), ensure aggregation (no individual data leak)

---

## Changelog

| Date | Change |
|---|---|
| 2026-10-07 | Initial documentation (API contracts, usage examples, testing) |

---

## Related Documentation

- [README.md](./README.md) — system overview
- [implementation.md](./implementation.md) — service internals
- [configuration.md](./configuration.md) — worker lifecycle
- [../../api/README.md](../../api/README.md) — global API conventions

---

**Last Updated:** 2026-10-07
