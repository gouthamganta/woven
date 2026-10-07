# Matches API Reference

**Base URL:** `https://api.wooven.me` (prod) / `http://localhost:5135` (dev)

---

## Overview

Matches are created when two users choose each other in Moments. Matches have balloon states (ACTIVE/CLOSED) and profile access levels (TEASER/FULL).

**Key Concepts:**
- **PURE Match** — Both users chose same type (◈ or ◇), full profile access immediately
- **EDGE Match** — Different choices, `edgeOwnerId` set to ◇ chooser, full access after 2-way messaging
- **Balloon State** — ACTIVE (can message) or CLOSED (expired/popped/ended)
- **Profile Access** — TEASER (limited) or FULL (complete profile + photos)

**Source:** [`backend/WovenBackend/Endpoints/MatchesEndpoints.cs`](../../backend/WovenBackend/Endpoints/MatchesEndpoints.cs)

---

## Endpoints

### GET /matches

**Description:** List all active matches (balloons) for current user.

**Authentication:** Required (JWT)

**Response (200 OK):**
```json
{
  "count": 2,
  "matches": [
    {
      "matchId": "uuid",
      "matchType": "PURE",
      "edgeOwnerId": null,
      "balloonState": "ACTIVE",
      "createdAt": "2026-10-07T12:00:00Z",
      "expiresAt": "2026-10-14T12:00:00Z",
      "bothMessagedAt": "2026-10-07T14:30:00Z",
      "findLoveAt": "2026-10-07T14:35:00Z",
      "showFindLove": false,
      "showBalloonTimer": true,
      "reflectionSecondsLeft": 180,
      "other": {
        "userId": 456,
        "fullName": "Alex",
        "isVerified": true,
        "displayPronouns": "they/them",
        "profilePhoto": "https://..."
      }
    }
  ]
}
```

**Source:** [`MatchesEndpoints.cs:26-96`](../../backend/WovenBackend/Endpoints/MatchesEndpoints.cs)

---

### GET /matches/{matchId}/profile-access

**Description:** Check profile access level for a match (TEASER vs FULL).

**Authentication:** Required (JWT)

**Response (200 OK) — PURE Match:**
```json
{
  "matchId": "uuid",
  "accessLevel": "FULL",
  "reason": "PURE_MATCH",
  "showBalloonTimer": true,
  "reflectionSecondsLeft": 180,
  "showFindLove": false
}
```

**Response (200 OK) — EDGE Match (Edge Owner):**
```json
{
  "matchId": "uuid",
  "accessLevel": "FULL",
  "reason": "EDGE_OWNER",
  "showBalloonTimer": true,
  "reflectionSecondsLeft": 180,
  "showFindLove": false
}
```

**Response (200 OK) — EDGE Match (Non-Owner, Not Unlocked):**
```json
{
  "matchId": "uuid",
  "accessLevel": "TEASER",
  "reason": "EDGE_AWAITING_2WAY",
  "unlocked": false
}
```

**Response (200 OK) — EDGE Match (Non-Owner, Unlocked):**
```json
{
  "matchId": "uuid",
  "accessLevel": "FULL",
  "reason": "EDGE_UNLOCKED_2WAY",
  "unlocked": true
}
```

**Access Rules:**
- **PURE** → FULL immediately
- **EDGE, edge owner** → FULL immediately
- **EDGE, non-owner** → TEASER until `bothMessagedAt` is set (2-way communication)

**Source:** [`MatchesEndpoints.cs:100-175`](../../backend/WovenBackend/Endpoints/MatchesEndpoints.cs)

---

### POST /matches/{matchId}/pop

**Description:** "Pop" the balloon (start trial period). Triggers when second user opens chat.

**Authentication:** Required (JWT)

**Idempotency:** Supports `X-Idempotency-Key` header

**Request:** Empty body

**Response (200 OK):**
```json
{
  "popped": true,
  "trialEndsAt": "2026-10-07T14:33:00Z"
}
```

**Side Effects:**
- Sets `TrialUserAOpenedAt` or `TrialUserBOpenedAt`
- When both timestamps set: `TrialEndsAt = now + 3 minutes`
- Creates chat thread if doesn't exist

**Errors:**
- `404 MATCH_NOT_FOUND`
- `422 BALLOON_NOT_ACTIVE` — Match already closed

**Source:** [`MatchesEndpoints.cs:177-230`](../../backend/WovenBackend/Endpoints/MatchesEndpoints.cs)

---

### POST /matches/{matchId}/unmatch

**Description:** Close a match gracefully (with optional rating).

**Authentication:** Required (JWT)

**Request:**
```json
{
  "rating": 4
}
```

**Fields:**
- `rating` (optional) — 1-5 star rating (platform-only signal)

**Response (200 OK):**
```json
{
  "unmatched": true
}
```

**Side Effects:**
- Sets `BalloonState = CLOSED`
- Records rating if provided
- Refunds 0.5 ghost sparks if 0 messages exchanged

**Source:** [`MatchesEndpoints.cs:232-290`](../../backend/WovenBackend/Endpoints/MatchesEndpoints.cs)

---

### POST /matches/{matchId}/flag

**Description:** Report a match for safety/policy violations.

**Authentication:** Required (JWT)

**Request:**
```json
{
  "reason": "inappropriate_content"
}
```

**Reasons:**
- `inappropriate_content`
- `harassment`
- `fake_profile`
- `spam`
- `other`

**Response (200 OK):**
```json
{
  "flagged": true
}
```

**Side Effects:**
- Creates `Block` record
- Sets `BalloonState = CLOSED`
- Triggers moderation queue review

**Safety Note:** Flags are NEVER used in compatibility scoring (isolated from ECHO pipeline).

**Source:** [`MatchesEndpoints.cs:292-350`](../../backend/WovenBackend/Endpoints/MatchesEndpoints.cs)

---

## Match Type Logic

**PURE Match:**
```csharp
// Both chose same positive type (◈ or ◇)
if (IsPure(choiceA, choiceB))
{
    matchType = MatchType.PURE;
    edgeOwnerId = null; // Full access for both
}
```

**EDGE Match:**
```csharp
// Different positive types
else
{
    matchType = MatchType.EDGE;
    edgeOwnerId = (userWhoChoseLogical).Id; // ◇ chooser gets full access immediately
}
```

**Source:** [`MomentsEndpoints.cs:18-29`](../../backend/WovenBackend/Endpoints/MomentsEndpoints.cs)

---

## Balloon Lifecycle

**States:**
1. `ACTIVE` — Can message, balloon timer active
2. `CLOSED` — Immutable, cannot reopen

**Closure Triggers:**
- Trial decision (END/BLOCK)
- Unmatch
- Expiration (7 days from creation)
- Flag/report

**Expiration:**
```csharp
match.ExpiresAt = match.CreatedAt.AddDays(7);
```

---

**Last Updated:** 2026-10-07  
**Source Files:**
- `backend/WovenBackend/Endpoints/MatchesEndpoints.cs`
- `backend/WovenBackend/Endpoints/MomentsEndpoints.cs` (match creation)
