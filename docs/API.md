# Woven API Reference

Base URL: `http://localhost:5135` (development) or `https://api.your-domain.com` (production)

## Authentication

All protected endpoints require a JWT token:

```
Authorization: Bearer <token>
```

---

## Auth Endpoints

### POST /auth/google

Authenticate with a Google ID token.

**Request:**
```json
{
  "idToken": "google_id_token_from_frontend"
}
```

**Response (200):**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIs...",
  "userId": 123,
  "fullName": "John Doe",
  "email": "john@example.com",
  "isNewUser": false,
  "onboardingComplete": true
}
```

**Errors:**
- `400` - Missing or invalid ID token
- `401` - Token verification failed

---

## Moments Endpoints

### GET /moments

Get today's deck of candidate matches.

**Response (200):**
```json
{
  "dateUtc": "2025-01-25",
  "theme": {
    "id": "BRUNCH_DINNER",
    "question": "If we grabbed a meal together...",
    "left": { "label": "Brunch", "emoji": "☕", "choice": "NO" },
    "mid": { "label": "Hold", "emoji": "⏳", "choice": "PENDING" },
    "right": { "label": "Dinner", "emoji": "🍽️", "choice": "YES" }
  },
  "budget": {
    "totalCap": 5,
    "totalUsed": 2,
    "totalRemaining": 3,
    "pendingCap": 2,
    "pendingUsed": 1,
    "pendingRemaining": 1
  },
  "count": 5,
  "cards": [
    {
      "userId": 456,
      "fullName": "Jane Smith",
      "age": 28,
      "gender": "female",
      "location": { "city": "Austin", "state": "TX" },
      "profilePhoto": "https://...",
      "score": 0.85,
      "bucket": "HIGH",
      "reason": {
        "headline": "You both love hiking and indie music",
        "bullets": ["Shared love of nature", "Similar taste in music"],
        "tone": "Adventurous match"
      },
      "rating": {
        "average": 45,
        "count": 12,
        "show": true
      }
    }
  ]
}
```

### POST /moments/respond

Respond to a candidate (YES, NO, or PENDING).

**Request:**
```json
{
  "targetUserId": 456,
  "choice": "YES",
  "source": null
}
```

**Response (200):**
```json
{
  "status": "PURE_MATCH_CREATED",
  "matchId": "550e8400-e29b-41d4-a716-446655440000",
  "totalUsed": 3,
  "pendingUsed": 1
}
```

**Status values:**
- `RECORDED_WAITING` - Response saved, waiting for other user
- `PENDING_SAVED` - Saved to pending queue
- `PURE_MATCH_CREATED` - Both chose same side
- `EDGE_MATCH_CREATED` - Different sides, edge match created
- `MATCH_NOT_CREATED` - Match blocked (already exists, etc.)

### GET /moments/pending

Get users saved in the pending queue.

**Response (200):**
```json
{
  "count": 3,
  "cards": [
    {
      "userId": 789,
      "fullName": "Alex Johnson",
      "age": 30,
      "profilePhoto": "https://...",
      "savedAt": "2025-01-24T15:30:00Z"
    }
  ]
}
```

---

## Chat Endpoints

### GET /chats

List all active chat threads.

**Response (200):**
```json
{
  "meUserId": 123,
  "count": 2,
  "chats": [
    {
      "threadId": "thread-uuid",
      "matchId": "match-uuid",
      "matchType": "PURE",
      "expiresAt": "2025-01-26T12:00:00Z",
      "findLoveAt": "2025-01-25T16:00:00Z",
      "showFindLove": true,
      "showBalloonTimer": false,
      "reflectionSecondsLeft": 0,
      "isTrial": false,
      "trialEndsAt": null,
      "trialSecondsLeft": 0,
      "title": "A moment with Jane",
      "other": {
        "userId": 456,
        "fullName": "Jane Smith",
        "profilePhoto": "https://..."
      },
      "lastMessage": {
        "body": "Hey! How's your day?",
        "createdAt": "2025-01-25T14:30:00Z",
        "senderUserId": 456
      }
    }
  ]
}
```

### GET /chats/{threadId}

Get a specific chat thread with messages.

**Response (200):**
```json
{
  "meUserId": 123,
  "threadId": "thread-uuid",
  "matchId": "match-uuid",
  "balloonState": "ACTIVE",
  "expiresAt": "2025-01-26T12:00:00Z",
  "findLoveAt": "2025-01-25T16:00:00Z",
  "showFindLove": true,
  "showBalloonTimer": false,
  "reflectionSecondsLeft": 0,
  "dateIdea": "Try the new coffee shop on 6th Street",
  "isTrial": false,
  "trialEndsAt": null,
  "trialSecondsLeft": 0,
  "canMakeDecision": false,
  "isUserA": true,
  "userADecision": null,
  "userBDecision": null,
  "other": {
    "userId": 456,
    "fullName": "Jane Smith",
    "profilePhoto": "https://..."
  },
  "messages": [
    {
      "messageId": "msg-uuid",
      "senderUserId": 456,
      "body": "Hey! How's your day?",
      "messageType": "",
      "meta": null,
      "createdAt": "2025-01-25T14:30:00Z"
    }
  ]
}
```

### POST /chats/{threadId}/messages

Send a message in a chat thread.

**Request:**
```json
{
  "body": "I'm doing great! How about you?"
}
```

**Response (200):**
```json
{
  "status": "SENT",
  "messageId": "new-msg-uuid",
  "createdAt": "2025-01-25T14:35:00Z"
}
```

### POST /chats/{threadId}/trial-decision

Submit a trial period decision (after popping a balloon).

**Request (User A - must include rating):**
```json
{
  "decision": "CONTINUE",
  "rating": 75
}
```

**Request (User B - no rating):**
```json
{
  "decision": "CONTINUE"
}
```

**Response (200):**
```json
{
  "status": "MATCH_CONTINUES",
  "findLoveAt": "2025-01-25T15:00:00Z"
}
```

**Status values:**
- `DECISION_RECORDED` - Waiting for other user
- `MATCH_CONTINUES` - Both chose CONTINUE, Find Love unlocked
- `MATCH_ENDED` - At least one chose END, match closed

---

## Match Endpoints

### GET /matches

List all matches (active and closed).

### GET /matches/{matchId}/profile

Get the other user's profile in a match.

### POST /matches/{matchId}/pop

Start a trial period (instead of closing the balloon).

**Response (200):**
```json
{
  "status": "TRIAL_STARTED",
  "matchId": "match-uuid",
  "trialEndsAt": "2025-01-25T15:01:00Z"
}
```

### POST /matches/{matchId}/unmatch

Unmatch from a connection, optionally with a rating.

**Request:**
```json
{
  "rating": -25
}
```

**Response (200):**
```json
{
  "status": "UNMATCHED",
  "closedAt": "2025-01-25T15:00:00Z"
}
```

### POST /matches/{matchId}/block

Block a user and close the match.

---

## Game Endpoints

### GET /games/availability/{matchId}

Check which games are available.

### POST /games/sessions

Create a new game session.

### POST /games/sessions/{sessionId}/accept

Accept a game invitation.

### POST /games/sessions/{sessionId}/reject

Reject a game invitation.

### POST /games/sessions/{sessionId}/rounds/{roundNumber}/answer

Submit an answer for a game round.

### GET /games/sessions/{sessionId}/result

Get the final game result.

---

## Health Endpoint

### GET /health

Check API and database health.

**Response (200):**
```json
{
  "status": "ok",
  "database": "connected"
}
```

**Response (503 - Database unavailable):**
```json
{
  "status": "degraded",
  "database": "unavailable"
}
```

---

## Error Responses

All errors follow this format:

```json
{
  "error": "ERROR_CODE"
}
```

**Common error codes:**
- `MATCH_NOT_FOUND` - Match doesn't exist
- `THREAD_NOT_FOUND` - Chat thread doesn't exist
- `BALLOON_NOT_ACTIVE` - Match is already closed
- `CANNOT_POP_NOW` - Trial already started or Find Love unlocked
- `NOT_IN_TRIAL` - Trying to make decision outside trial
- `TRIAL_NOT_ENDED` - Trial period still active
- `RATING_REQUIRED_FOR_USER_A` - User A must provide rating
- `RATING_OUT_OF_RANGE` - Rating must be -100 to +100
- `BUDGET_EXCEEDED` - Daily interaction limit reached
