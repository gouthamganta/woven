# Chats API Reference

**Base URL:** `https://api.wooven.me` (prod) / `http://localhost:5135` (dev)

---

## Overview

Chat threads are created when matches occur. Features include text messages, voice notes, trial periods, and balloon mechanics (reflection window before "Find Love" unlock).

**Key Concepts:**
- **Thread** — Chat conversation tied to a match
- **Trial Period** — 3-minute window after both users open chat (decide to CONTINUE/END)
- **Balloon** — 5-minute reflection window after both users message
- **Find Love** — Final unlock stage (profile access + date planning)

**Source:** [`backend/WovenBackend/Endpoints/ChatEndpoints.cs`](../../backend/WovenBackend/Endpoints/ChatEndpoints.cs)

---

## Endpoints

### GET /chats

**Description:** List all active chat threads (active balloons only).

**Authentication:** Required (JWT)

**Response (200 OK):**
```json
{
  "meUserId": 123,
  "count": 3,
  "chats": [
    {
      "threadId": "uuid",
      "matchId": "uuid",
      "matchType": "PURE",
      "edgeOwnerId": null,
      "expiresAt": "2026-10-14T12:00:00Z",
      "bothMessagedAt": "2026-10-07T14:30:00Z",
      "findLoveAt": "2026-10-07T14:35:00Z",
      "showFindLove": false,
      "showBalloonTimer": true,
      "reflectionSecondsLeft": 180,
      "title": "A moment with Alex",
      "other": {
        "userId": 456,
        "fullName": "Alex",
        "isVerified": true,
        "displayPronouns": "they/them",
        "profilePhoto": "https://...",
        "lastActiveAt": "2026-10-07T14:32:00Z"
      },
      "lastMessage": {
        "body": "That's a great perspective!",
        "createdAt": "2026-10-07T14:32:00Z",
        "senderUserId": 456,
        "messageType": "TEXT",
        "metaJson": null
      },
      "isTrial": false,
      "trialEndsAt": null,
      "trialSecondsLeft": 0,
      "updatedAt": "2026-10-07T14:32:00Z"
    }
  ]
}
```

**Fields:**
- `showBalloonTimer` — True when balloon countdown is active
- `showFindLove` — True when Find Love button should appear
- `reflectionSecondsLeft` — Seconds until Find Love unlocks
- `isTrial` — True if in 3-minute trial period
- `messageType` — `TEXT` or `VOICE`

**Source:** [`ChatEndpoints.cs:32-146`](../../backend/WovenBackend/Endpoints/ChatEndpoints.cs)

---

### POST /chats/start

**Description:** Get or create thread ID for a match.

**Authentication:** Required (JWT)

**Request:**
```json
{
  "matchId": "uuid"
}
```

**Response (200 OK):**
```json
{
  "threadId": "uuid",
  "created": false
}
```

**Errors:**
- `404 MATCH_NOT_FOUND` — Match does not exist
- `403 Forbidden` — User is not part of this match

**Source:** [`ChatEndpoints.cs:148-175`](../../backend/WovenBackend/Endpoints/ChatEndpoints.cs)

---

### GET /chats/{threadId}

**Description:** Fetch messages in a thread with pagination.

**Authentication:** Required (JWT)

**Query Parameters:**
- `limit` (optional, default 50) — Messages per page
- `before` (optional) — Cursor for pagination (message ID)

**Response (200 OK):**
```json
{
  "threadId": "uuid",
  "messages": [
    {
      "id": "uuid",
      "senderUserId": 456,
      "body": "Hey! How's your week going?",
      "messageType": "TEXT",
      "metaJson": null,
      "createdAt": "2026-10-07T14:30:00Z"
    },
    {
      "id": "uuid",
      "senderUserId": 123,
      "body": "",
      "messageType": "VOICE",
      "metaJson": "{\"audioUrl\":\"https://...\",\"durationSecs\":15}",
      "createdAt": "2026-10-07T14:31:00Z"
    }
  ],
  "hasMore": false,
  "nextCursor": null
}
```

**Voice Note `metaJson`:**
```json
{
  "audioUrl": "https://storage.blob.core.windows.net/...",
  "durationSecs": 15
}
```

**Source:** [`ChatEndpoints.cs:177-239`](../../backend/WovenBackend/Endpoints/ChatEndpoints.cs)

---

### POST /chats/{threadId}/message

**Description:** Send a text message. Triggers balloon mechanics if both users have messaged.

**Authentication:** Required (JWT)

**Request:**
```json
{
  "body": "That's a great perspective!"
}
```

**Validation:**
- `body` 1-1000 characters
- Cannot be only whitespace

**Response (200 OK):**
```json
{
  "messageId": "uuid",
  "createdAt": "2026-10-07T14:32:00Z",
  "bothMessagedAt": "2026-10-07T14:32:00Z",
  "findLoveAt": "2026-10-07T14:37:00Z",
  "showBalloonTimer": true
}
```

**Balloon Trigger:**
- Sets `bothMessagedAt` when both users have sent ≥1 message
- Sets `findLoveAt` = `bothMessagedAt + 5 minutes`

**Source:** [`ChatEndpoints.cs:241-340`](../../backend/WovenBackend/Endpoints/ChatEndpoints.cs)

---

### POST /chats/{threadId}/voice-message

**Description:** Send a voice note (audio uploaded via `/media/upload-token` first).

**Authentication:** Required (JWT)

**Request:**
```json
{
  "audioUrl": "https://storage.blob.core.windows.net/...",
  "durationSecs": 15
}
```

**Validation:**
- `durationSecs` 1-120 seconds (max 2 minutes)
- Audio must be uploaded to blob storage first

**Response (200 OK):**
```json
{
  "messageId": "uuid",
  "createdAt": "2026-10-07T14:33:00Z"
}
```

**Source:** [`ChatEndpoints.cs:342-400`](../../backend/WovenBackend/Endpoints/ChatEndpoints.cs)

---

### POST /chats/{threadId}/messages/{messageId}/voice-listened

**Description:** Track voice note listen completion (ML signal for ECHO).

**Authentication:** Required (JWT)

**Request:** Empty body

**Response (200 OK):**
```json
{
  "recorded": true
}
```

**ML Signal:** `VoiceNoteListenComplete` (weight 0.05 in connection score)

**Source:** [`ChatEndpoints.cs:402-450`](../../backend/WovenBackend/Endpoints/ChatEndpoints.cs)

---

### POST /chats/{threadId}/trial-decision

**Description:** Submit trial period decision (CONTINUE, END, BLOCK).

**Authentication:** Required (JWT)

**Idempotency:** Supports `X-Idempotency-Key` header

**Request:**
```json
{
  "decision": "CONTINUE",
  "endReason": null
}
```

**Decisions:**
- `CONTINUE` — Move forward with match
- `END` — Close gracefully (reasons: `no_spark`, `wrong_timing`, `not_my_type`)
- `BLOCK` — Close + prevent future matches

**Response (200 OK):**
```json
{
  "decision": "CONTINUE",
  "balloonState": "ACTIVE"
}
```

**Errors:**
- `422 TRIAL_NOT_ACTIVE` — Trial period has ended

**Source:** [`ChatEndpoints.cs:452-550`](../../backend/WovenBackend/Endpoints/ChatEndpoints.cs)

---

## Trial Period Mechanics

**Trigger:** Starts when **second user** opens the chat thread

**Duration:** 3 minutes from `trialEndsAt`

**Decision States:**
- Both CONTINUE → match stays active
- Either END/BLOCK → match closes (`BalloonState = CLOSED`)
- No decision before expiry → auto-closes

**Tracking:**
```csharp
match.TrialUserAOpenedAt = DateTime.UtcNow; // First user
match.TrialUserBOpenedAt = DateTime.UtcNow; // Second user
match.TrialEndsAt = DateTime.UtcNow.AddMinutes(3);
```

---

## Balloon Mechanics

**Trigger:** Both users have sent ≥1 message

**Duration:** 5 minutes reflection window

**Timeline:**
1. `bothMessagedAt` — Timestamp when both users messaged
2. `findLoveAt` = `bothMessagedAt + 5 minutes`
3. UI shows countdown timer (`showBalloonTimer = true`)
4. After 5 minutes: `showFindLove = true` (unlock profile + date planning)

**Implementation:**
```csharp
if (senderHasMessaged && otherHasMessaged && match.BothMessagedAt == null)
{
    match.BothMessagedAt = DateTime.UtcNow;
    match.FindLoveAt = DateTime.UtcNow.AddMinutes(5);
}
```

---

**Last Updated:** 2026-10-07  
**Source File:** `backend/WovenBackend/Endpoints/ChatEndpoints.cs`
