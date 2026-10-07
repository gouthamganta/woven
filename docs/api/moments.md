# Moments API Reference

**Base URL:** `https://api.wooven.me` (prod) / `http://localhost:5135` (dev)

---

## Overview

Moments is the daily discovery feature. Users receive a curated deck of candidates each day and can respond with choices (◈ Magical, ◇ Resonant, or Pass).

**Key Concepts:**
- **Deck** — Daily curated list of candidates (regenerated at midnight UTC)
- **Daily Budget** — 50 total interactions per day across both tabs
- **Buckets** — SPARK (high match score), EXPLORER (diverse), DRAWN (they chose you first)
- **Match Creation** — PURE (same choice) vs EDGE (different choices)

**Source:** [`backend/WovenBackend/Endpoints/MomentsEndpoints.cs`](../../backend/WovenBackend/Endpoints/MomentsEndpoints.cs)

---

## Endpoints

### GET /moments

**Description:** Returns today's deck with cards and user data. Filters out already-responded candidates.

**Authentication:** Required (JWT)

**Response (200 OK):**
```json
{
  "dateUtc": "2026-10-07",
  "budget": {
    "totalCap": 50,
    "totalUsed": 12,
    "totalRemaining": 38
  },
  "sparkBalance": 8,
  "count": 15,
  "cards": [
    {
      "candidateId": 456,
      "bucket": "SPARK",
      "explanation": {
        "title": "Deep conversationalist",
        "why": "Both value growth over comfort...",
        "bridgeQuestion": "How do you approach vulnerability in relationships?"
      },
      "profile": {
        "fullName": "Alex",
        "isVerified": true,
        "gender": "NON_BINARY",
        "displayPronouns": "they/them",
        "location": {
          "city": "Seattle",
          "state": "WA"
        }
      },
      "photos": [
        {"url": "https://...", "isBestForMe": true},
        {"url": "https://...", "isBestForMe": false}
      ],
      "tiles": [
        {
          "tileId": "uuid",
          "contentType": "TEXT",
          "contentText": "Coffee is life",
          "isActive": true
        }
      ],
      "theyChoseMe": false,
      "communityRating": 4.2
    }
  ]
}
```

**Fields:**
- `bucket` — One of `SPARK`, `EXPLORER`, `DRAWN`
- `theyChoseMe` — If true, this candidate already chose you (DRAWN tab)
- `communityRating` — Platform-only signal (never shown to users in UI)

**Source:** [`MomentsEndpoints.cs:37-219`](../../backend/WovenBackend/Endpoints/MomentsEndpoints.cs)

---

### POST /moments/respond

**Description:** Record a choice on a candidate (MAGICAL, LOGICAL, PASS). Costs 1 spark if from DRAWN bucket.

**Authentication:** Required (JWT)

**Idempotency:** Supports `X-Idempotency-Key` header for DRAWN actions (spark deduction)

**Request:**
```json
{
  "targetUserId": 456,
  "choice": "MAGICAL",
  "source": "today",
  "timeOnCardMs": 12500
}
```

**Fields:**
- `choice` — One of `MAGICAL`, `LOGICAL`, `PASS`
- `source` — `"today"` (main deck) or `"liked-you"` (DRAWN tab)
- `timeOnCardMs` — Optional engagement metric

**Response (200 OK) — No Match:**
```json
{
  "matched": false,
  "choiceRecorded": "MAGICAL"
}
```

**Response (200 OK) — Match Created:**
```json
{
  "matched": true,
  "choiceRecorded": "MAGICAL",
  "matchId": "uuid",
  "matchType": "PURE",
  "edgeOwnerId": null,
  "expiresAt": "2026-10-14T12:00:00Z",
  "other": {
    "userId": 456,
    "fullName": "Alex",
    "isVerified": true,
    "profilePhoto": "https://..."
  }
}
```

**Match Types:**
- `PURE` — Both chose same type (◈ or ◇)
- `EDGE` — Different types, edge_owner_id set to user who chose ◇

**Errors:**
- `400 INSUFFICIENT_SPARKS` — Not enough sparks for DRAWN action
- `422 DAILY_LIMIT_REACHED` — 50 interactions used today
- `422 ALREADY_RESPONDED` — Already responded to this candidate today

**Source:** [`MomentsEndpoints.cs:222-400`](../../backend/WovenBackend/Endpoints/MomentsEndpoints.cs)

---

### POST /moments/choose

**Description:** Respond with a ChatNote attached (short reflective message, not shown to match).

**Authentication:** Required (JWT)

**Request:**
```json
{
  "targetUserId": 456,
  "choice": "MAGICAL",
  "noteText": "Their answer about vulnerability really resonated",
  "source": "today",
  "timeOnCardMs": 15000
}
```

**Response:** Same as `/moments/respond`

**Note:** ChatNote is background signal only (used by ECHO ML pipeline, never shown to users).

**Source:** [`MomentsEndpoints.cs:403-500`](../../backend/WovenBackend/Endpoints/MomentsEndpoints.cs)

---

## Daily Budget

**Rule:** 50 total interactions per calendar day (UTC) across both tabs.

**Enforcement:**
```csharp
var budgetRow = await db.DailyInteractions
    .SingleOrDefaultAsync(x => x.UserId == userId && x.DateUtc == today, ct);

if (budgetRow != null && budgetRow.TotalUsed >= MomentsRules.DailyTotalCap)
    return Results.UnprocessableEntity(new { error = "DAILY_LIMIT_REACHED" });
```

**Source:** [`MomentsEndpoints.cs:49-56`](../../backend/WovenBackend/Endpoints/MomentsEndpoints.cs)

---

## Spark Economy

**Spark Cost:**
- Deck (Today tab) responses: **FREE**
- DRAWN (Liked You tab) responses: **1 spark**

**Refund on Match Close:**
- 0.5 ghost sparks if match ends with 0 messages exchanged

**Source:** Referenced in [`SparkWalletService.cs`](../../backend/WovenBackend/Services/SparkWalletService.cs)

---

## Deck Regeneration

**Schedule:** Daily at 00:00 UTC via `DailyDeckOrchestrator`

**Buckets:**
- **SPARK (10 cards)** — High match scores, personalized
- **EXPLORER (5 cards)** — Diverse, low foundational overlap
- **DRAWN (variable)** — Users who already chose you

**Source:** [`DailyDeckOrchestrator.cs`](../../backend/WovenBackend/Services/Matchmaking/DailyDeckOrchestrator.cs)

---

## Match Explanation

**Generated by:** `MatchExplanationService` using OpenAI gpt-4.1-mini

**Fields:**
- `title` — 2-4 word headline
- `why` — 1-2 sentence explanation
- `bridgeQuestion` — Conversation starter

**Example:**
```json
{
  "title": "Deep conversationalist",
  "why": "Both value growth over comfort in relationships and prioritize emotional depth.",
  "bridgeQuestion": "How do you approach vulnerability when getting to know someone?"
}
```

**Source:** [`MatchExplanationService.cs`](../../backend/WovenBackend/Services/Matchmaking/MatchExplanationService.cs)

---

**Last Updated:** 2026-10-07  
**Source Files:**
- `backend/WovenBackend/Endpoints/MomentsEndpoints.cs`
- `backend/WovenBackend/Services/Matchmaking/DailyDeckOrchestrator.cs`
- `backend/WovenBackend/Services/Matchmaking/MatchExplanationService.cs`
