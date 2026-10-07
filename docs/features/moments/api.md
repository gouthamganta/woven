# Moments API Reference

**Base URL:** `http://localhost:5135` (dev) / `https://api.wooven.me` (prod)  
**Auth:** JWT Bearer token (required on all endpoints)

## Endpoints

### GET /moments

Load today's daily deck (5 candidates), budget, and spark balance.

**Auth:** Required  
**Rate limit:** None (cached by ECHO orchestrator)

#### Response 200 OK

```json
{
  "dateUtc": "2026-08-17",
  "budget": {
    "totalCap": 5,
    "totalUsed": 2,
    "totalRemaining": 3
  },
  "sparkBalance": 5.0,
  "moodLine": "Strong pool today.",
  "count": 3,
  "cards": [
    {
      "userId": 456,
      "fullName": "Sarah Chen",
      "isVerified": true,
      "displayPronouns": "she/her",
      "gender": "Woman",
      "location": {
        "city": "Austin",
        "state": "TX"
      },
      "profilePhoto": "https://blob.core.windows.net/photos/456-primary.jpg",
      "score": 87.5,
      "bucket": "CORE_FIT",
      "alreadyChoseYou": false,
      "reason": {
        "headline": "Deep alignment on values + energy",
        "bullets": [
          "Both prioritize emotional depth over surface-level chat",
          "Shared love of hiking + outdoor exploration",
          "Similar communication style — thoughtful, intentional"
        ],
        "tone": "warm",
        "bridgeQuestion": "What's your favorite hiking trail and why?"
      },
      "rating": {
        "average": 75,
        "count": 12,
        "show": true
      },
      "photos": [
        "https://blob.core.windows.net/photos/456-1.jpg",
        "https://blob.core.windows.net/photos/456-2.jpg"
      ],
      "highlightedTiles": [
        {
          "id": "tile-789",
          "contentType": "photo",
          "mediaUrl": "https://blob.core.windows.net/tiles/789.jpg",
          "isActive": true
        },
        {
          "id": "tile-790",
          "contentType": "text",
          "contentText": "Looking for someone who values deep conversation over small talk.",
          "isActive": false
        }
      ],
      "kenBurnsPhotoUrls": null,
      "curatedQuote": null,
      "narrationUrl": null,
      "narrationExposed": false
    }
  ]
}
```

#### Response 200 OK (empty deck)

```json
{
  "dateUtc": "2026-08-17",
  "budget": {
    "totalCap": 5,
    "totalUsed": 5,
    "totalRemaining": 0
  },
  "sparkBalance": 3.5,
  "moodLine": null,
  "count": 0,
  "cards": []
}
```

#### Field notes

- **`alreadyChoseYou`** — `true` if candidate already chose viewer (from any source)
- **`rating.show`** — `true` only if `rating.count >= 5` (community threshold)
- **`bridgeQuestion`** — ECHO-suggested conversation starter (may be null)
- **`highlightedTiles`** — Up to 3 tiles (active first, pinned highlights fill)
  - **`isActive: true`** — Live in Commons right now
  - **`isActive: false`** — Pinned highlight (may be expired)
- **`kenBurnsPhotoUrls`** — Build N+1 feature (currently null for all cards)
- **`moodLine`** — ECHO's one-line read on deck quality (null = silence)

---

### GET /moments/liked-you

Load users who chose viewer in last 7 days (Drawn tab).

**Auth:** Required  
**Rate limit:** None

#### Response 200 OK

```json
{
  "count": 2,
  "cards": [
    {
      "userId": 789,
      "fullName": "Mike Torres",
      "isVerified": false,
      "location": {
        "city": "Brooklyn",
        "state": "NY"
      },
      "profilePhoto": "https://blob.core.windows.net/photos/789-primary.jpg",
      "likedAt": "2026-08-15T18:30:00Z",
      "expiresInHours": 42,
      "photos": [
        "https://blob.core.windows.net/photos/789-1.jpg"
      ],
      "highlightedTiles": [
        {
          "id": "tile-991",
          "contentType": "photo",
          "mediaUrl": "https://blob.core.windows.net/tiles/991.jpg",
          "isActive": true
        }
      ],
      "rating": null
    }
  ]
}
```

#### Response 200 OK (empty)

```json
{
  "count": 0,
  "cards": []
}
```

#### Field notes

- **`likedAt`** — When they chose viewer (ISO 8601 UTC)
- **`expiresInHours`** — Hours remaining in 7-day window (floor 0)
- **`rating`** — `null` if `count < 5` (not shown on Drawn cards)

**Exclusions (not shown):**
- Blocked users (both directions)
- Already-matched users (`BalloonState.ACTIVE`)
- Users in today's Deck (no overlap)
- Choices older than 7 days

---

### POST /moments/respond

Record Pass, Magical, or Resonant choice (no ChatNote). Legacy endpoint — prefer `/moments/choose` for positive choices.

**Auth:** Required  
**Rate limit:** None (budget/spark enforcement via service layer)

#### Request body

```json
{
  "targetUserId": 456,
  "choice": "PASS",
  "source": "TODAY",
  "timeOnCardMs": 8500
}
```

**Fields:**
- **`targetUserId`** (int, required) — User being responded to
- **`choice`** (string, required) — `"MAGICAL"`, `"LOGICAL"`, `"PASS"`, `"YES"` (legacy), `"NO"` (legacy)
- **`source`** (string, optional) — `"TODAY"` or `"LIKED_YOU"` (for analytics)
- **`timeOnCardMs`** (int, optional) — Dwell time in milliseconds (Deck tab only)

#### Response 200 OK (Pass recorded)

```json
{
  "status": "RECORDED_PASS"
}
```

#### Response 200 OK (Waiting for counterpart)

```json
{
  "status": "RECORDED_WAITING",
  "sparkBalance": 4.0
}
```

**`sparkBalance`** only included when `source = "LIKED_YOU"` (Drawn tab spend).

#### Response 200 OK (Pure match created)

```json
{
  "status": "PURE_MATCH_CREATED",
  "matchId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "matchType": "PURE",
  "edgeOwnerId": null,
  "reason": null,
  "sparkBalance": 4.0
}
```

#### Response 200 OK (Edge match created)

```json
{
  "status": "EDGE_MATCH_CREATED",
  "matchId": "f47ac10b-58cc-4372-a567-0e02b2c3d480",
  "matchType": "EDGE",
  "edgeOwnerId": 123,
  "reason": null,
  "sparkBalance": 4.0
}
```

#### Response 400 Bad Request (Insufficient sparks)

```json
{
  "error": "INSUFFICIENT_SPARKS",
  "sparkBalance": 0.5
}
```

#### Response 400 Bad Request (Daily cap reached)

```json
{
  "error": "DAILY_TOTAL_CAP_REACHED",
  "totalUsed": 5
}
```

#### Response 400 Bad Request (Blocked)

```json
{
  "error": "BLOCKED"
}
```

#### Response 409 Conflict (Already responded today)

```json
{
  "error": "ALREADY_RESPONDED_TODAY"
}
```

Only returned if upgrading from PASS on Deck tab (Drawn tab allows overwrite).

---

### POST /moments/choose

Record Magical or Resonant choice + ChatNote (20-150 chars). Atomic commitment point — match only created when both users have submitted note.

**Auth:** Required  
**Rate limit:** None (budget/spark enforcement via service layer)  
**Idempotency:** `X-Idempotency-Key` header supported (Drawn tab only, optional)

#### Request body

```json
{
  "targetUserId": 456,
  "choice": "MAGICAL",
  "noteText": "Your hiking photos made me want to say… let's trade trail recommendations!",
  "source": "TODAY",
  "timeOnCardMs": 12500
}
```

**Fields:**
- **`targetUserId`** (int, required) — User being chosen
- **`choice`** (string, required) — `"MAGICAL"` or `"LOGICAL"` only (Pass not allowed)
- **`noteText`** (string, required) — Opening note, 20-150 chars (trimmed)
- **`source`** (string, optional) — `"TODAY"` or `"LIKED_YOU"`
- **`timeOnCardMs`** (int, optional) — Dwell time in milliseconds (Deck tab only)

#### Response 200 OK (Note recorded, waiting for counterpart response)

```json
{
  "status": "RECORDED_WAITING"
}
```

Counterpart hasn't chosen viewer yet (no `MomentResponse` exists).

#### Response 200 OK (Note recorded, waiting for counterpart note)

```json
{
  "status": "WAITING_FOR_OTHER_NOTE"
}
```

Counterpart chose viewer but hasn't submitted note yet (via legacy `/respond` or hasn't completed `/choose`).

#### Response 200 OK (Pure match created)

```json
{
  "status": "PURE_MATCH_CREATED",
  "matchId": "f47ac10b-58cc-4372-a567-0e02b2c3d481",
  "matchType": "PURE",
  "edgeOwnerId": null,
  "reason": null
}
```

Both users chose same type (both Magical or both Resonant), both submitted notes.

#### Response 200 OK (Edge match created)

```json
{
  "status": "EDGE_MATCH_CREATED",
  "matchId": "f47ac10b-58cc-4372-a567-0e02b2c3d482",
  "matchType": "EDGE",
  "edgeOwnerId": 789,
  "reason": null
}
```

Users chose different types (one Magical, one Resonant), both submitted notes. `edgeOwnerId` randomly assigned.

#### Response 400 Bad Request (Note length invalid)

```json
{
  "error": "NOTE_LENGTH_INVALID",
  "min": 20,
  "max": 150
}
```

#### Response 400 Bad Request (Insufficient sparks)

```json
{
  "error": "INSUFFICIENT_SPARKS",
  "sparkBalance": 0.5
}
```

Drawn tab only — user doesn't have 1 spark to spend.

#### Response 409 Conflict (Note already submitted)

```json
{
  "error": "NOTE_ALREADY_SUBMITTED"
}
```

User already submitted a `ChatNote` for this target (one note per target per user).

#### Response 409 Conflict (Already responded today)

```json
{
  "error": "ALREADY_RESPONDED_TODAY"
}
```

Deck tab only — user already made a positive choice today (unless upgrading from PASS).

---

## Status codes

| Code | Meaning |
|------|---------|
| 200  | Success — check `status` field for operation result |
| 400  | Bad request — validation error, insufficient budget/sparks, blocked |
| 401  | Unauthorized — missing or invalid JWT token |
| 409  | Conflict — duplicate submission (already responded today, note already submitted) |
| 500  | Internal server error — check logs, retry with exponential backoff |

---

## Common error codes

### Budget/spark errors

| Error | Endpoint | Source | Meaning |
|-------|----------|--------|---------|
| `INSUFFICIENT_SPARKS` | `/respond`, `/choose` | `LIKED_YOU` | User has < 1 spark in wallet |
| `DAILY_TOTAL_CAP_REACHED` | `/respond`, `/choose` | `TODAY` | User spent 5 actions today (Deck tab) |

### Validation errors

| Error | Endpoint | Meaning |
|-------|----------|---------|
| `INVALID_TARGET` | All | `targetUserId <= 0` or `targetUserId == me` |
| `TARGET_NOT_FOUND` | All | `targetUserId` doesn't exist in `users` table |
| `BLOCKED` | All | Either user blocked the other (both directions checked) |
| `INVALID_CHOICE` | `/respond`, `/choose` | Choice not in `[MAGICAL, LOGICAL, PASS, YES, NO]` |
| `NOTE_LENGTH_INVALID` | `/choose` | Note < 20 or > 150 chars (after trim) |

### Duplicate submission errors

| Error | Endpoint | Meaning |
|-------|----------|---------|
| `ALREADY_RESPONDED_TODAY` | `/respond`, `/choose` | User already made positive choice today (Deck tab only, unless upgrading from PASS) |
| `NOTE_ALREADY_SUBMITTED` | `/choose` | User already has a `ChatNote` for this target (one per target per user) |

### Match creation errors

| Error | Endpoint | Meaning |
|-------|----------|---------|
| `CANNOT_MATCH_SELF` | — | Internal error — user tried to match with self (should never reach client) |
| `ACTIVE_MATCH_ALREADY_EXISTS` | — | Internal error — race condition (SERIALIZABLE transaction should prevent) |

---

## Match status values

Returned in `status` field of `/respond` and `/choose` responses.

| Status | Meaning |
|--------|---------|
| `RECORDED_PASS` | Pass choice recorded (no match check) |
| `RECORDED_WAITING` | Positive choice recorded, waiting for counterpart response |
| `WAITING_FOR_OTHER_NOTE` | Both chose each other, but counterpart hasn't submitted note yet |
| `PURE_MATCH_CREATED` | Match created — both chose same type (both Magical or both Resonant) |
| `EDGE_MATCH_CREATED` | Match created — different types (one Magical, one Resonant) |
| `MATCH_NOT_CREATED` | Match creation failed (see `reason` field — e.g., `ACTIVE_MATCH_ALREADY_EXISTS`) |

---

## Idempotency

**Header:** `X-Idempotency-Key`  
**Supported endpoints:** `POST /moments/respond` (Drawn tab only), `POST /moments/choose` (Drawn tab only)  
**Purpose:** Prevent double-spend on Drawn tab actions (1 spark deducted)

**How it works:**
1. Client generates unique key (16+ char hex recommended)
2. Client includes `X-Idempotency-Key: {key}` header
3. Backend checks `idempotency_records` table for existing key + user + operation
4. If found → return cached response (same status code + body)
5. If not found → process request, store response in `idempotency_records` (24h TTL)

**Example:**
```bash
curl -X POST https://api.wooven.me/moments/choose \
  -H "Authorization: Bearer {jwt}" \
  -H "X-Idempotency-Key: 7f3a8c9e2b1d4f6a" \
  -H "Content-Type: application/json" \
  -d '{
    "targetUserId": 456,
    "choice": "MAGICAL",
    "noteText": "Your hiking photos made me want to say… let's trade trail recommendations!",
    "source": "LIKED_YOU"
  }'
```

**Retry behavior:**
- Same key + user + operation → cached response (no spark spend, no duplicate match)
- Same key + different user → treated as new request (keys scoped per user)
- Same key + different operation → treated as new request (keys scoped per operation)

**Not used on Deck tab** — daily cap prevents duplicate submission (already responded today check), and retries are cheap (no spark spend).

---

## Rate limiting

**None currently enforced.** Budget/spark limits act as natural rate limiting:
- Deck tab: 5 actions/day
- Drawn tab: limited by spark wallet (earn 5/day, max 10 wallet)

Future: May add IP-based rate limiting (100 req/min per IP) to prevent abuse.

---

## Examples

### Complete flow: Deck tab match

**1. Load today's deck**
```bash
GET /moments
Authorization: Bearer {jwt}

→ 200 OK
{
  "cards": [{ "userId": 456, ... }],
  "budget": { "totalRemaining": 5 }
}
```

**2. User chooses Magical**
```bash
POST /moments/choose
Authorization: Bearer {jwt}
{
  "targetUserId": 456,
  "choice": "MAGICAL",
  "noteText": "Your hiking photos made me want to say… let's trade trail recommendations!",
  "source": "TODAY",
  "timeOnCardMs": 12500
}

→ 200 OK
{
  "status": "WAITING_FOR_OTHER_NOTE"
}
```

**3. Counterpart chooses Resonant (later that day)**
```bash
POST /moments/choose
Authorization: Bearer {jwt}
{
  "targetUserId": 123,
  "choice": "LOGICAL",
  "noteText": "I'd love to talk about outdoor adventures!",
  "source": "TODAY",
  "timeOnCardMs": 9800
}

→ 200 OK
{
  "status": "EDGE_MATCH_CREATED",
  "matchId": "f47ac10b-58cc-4372-a567-0e02b2c3d483",
  "matchType": "EDGE",
  "edgeOwnerId": 456
}
```

**4. Frontend navigates to chat**
```bash
POST /chats/start
Authorization: Bearer {jwt}
{
  "matchId": "f47ac10b-58cc-4372-a567-0e02b2c3d483"
}

→ 200 OK
{
  "threadId": "thread-abc123"
}
```

### Complete flow: Drawn tab match

**1. Load Drawn tab**
```bash
GET /moments/liked-you
Authorization: Bearer {jwt}

→ 200 OK
{
  "cards": [{ "userId": 789, "expiresInHours": 42 }]
}
```

**2. User chooses Magical (spends 1 spark)**
```bash
POST /moments/choose
Authorization: Bearer {jwt}
X-Idempotency-Key: 7f3a8c9e2b1d4f6a
{
  "targetUserId": 789,
  "choice": "MAGICAL",
  "noteText": "Something tells me we'd vibe really well.",
  "source": "LIKED_YOU"
}

→ 200 OK
{
  "status": "PURE_MATCH_CREATED",
  "matchId": "f47ac10b-58cc-4372-a567-0e02b2c3d484",
  "matchType": "PURE",
  "edgeOwnerId": null
}
```

Both users chose Magical → pure match created immediately (counterpart already chose + submitted note from Deck tab).

---

## Related documentation

- **Deck tab mechanics:** `deck-tab.md`
- **Drawn tab mechanics:** `drawn-tab.md`
- **ChatNote overlay:** `chat-note.md`
- **Match creation logic:** `match-creation.md`
- **Frontend implementation:** `frontend.md`
- **Backend implementation:** `backend.md`
