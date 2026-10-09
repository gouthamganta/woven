# Moderation API Reference

**Last Updated:** 2026-10-07

---

## Overview

Complete API reference for moderation, blocking, and reporting endpoints in Woven.

**Base URL (local dev):** `http://localhost:5135`  
**Base URL (production):** `https://wooven.me/api`

---

## User-Facing Endpoints

### Block User

Block another user, preventing all future interactions.

```http
POST /matches/{matchId}/block
Authorization: Bearer <jwt>
```

**Path Parameters:**
- `matchId` (uuid) — Match ID to block

**Response (200 OK):**
```json
{
  "blocked": true,
  "matchId": "a1b2c3d4-e5f6-...",
  "closedAt": "2026-10-07T14:23:00Z"
}
```

**Errors:**
- `404` — Match not found
- `403` — Not a participant in this match
- `401` — Unauthorized (invalid/missing JWT)

**Side effects:**
- Creates `Block` record (blocker → blocked)
- Closes match if `BalloonState = ACTIVE`
- Records `MatchOutcome` (type: `BLOCK`)
- Blocked user excluded from future decks

**Example:**
```bash
curl -X POST http://localhost:5135/matches/abc123/block \
  -H "Authorization: Bearer eyJhbGc..."
```

---

### Report Tile (Planned)

Report inappropriate content on Commons.

```http
POST /tiles/{tileId}/report
Authorization: Bearer <jwt>
Content-Type: application/json

{
  "reason": "inappropriate_content"
}
```

**Path Parameters:**
- `tileId` (uuid) — Tile ID to report

**Request Body:**
| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `reason` | string | Yes | One of: `inappropriate_content`, `spam`, `misinformation`, `other` |
| `context` | string | No | Additional details (max 200 chars) |

**Response (201 Created):**
```json
{
  "reportId": "r1s2t3u4-...",
  "status": "SUBMITTED"
}
```

**Errors:**
- `404` — Tile not found
- `400` — Invalid reason or missing field
- `409` — Already reported by this user
- `429` — Too many reports (rate limit: 5/day)

**Side effects:**
- Creates `TileReport` record
- If report count ≥3 → tile auto-expires
- If user has 3+ tiles reported in 7 days → trust penalty

---

### List Blocked Users (Planned)

Get list of users you've blocked.

```http
GET /blocks
Authorization: Bearer <jwt>
```

**Response (200 OK):**
```json
{
  "blocked": [
    {
      "userId": 42,
      "firstName": "John",
      "blockedAt": "2026-09-15T10:00:00Z"
    },
    {
      "userId": 73,
      "firstName": "Jane",
      "blockedAt": "2026-10-01T14:30:00Z"
    }
  ]
}
```

**Errors:**
- `401` — Unauthorized

---

### Unblock User (Planned)

Remove a block.

```http
DELETE /blocks/{userId}
Authorization: Bearer <jwt>
```

**Path Parameters:**
- `userId` (int) — User ID to unblock

**Response (200 OK):**
```json
{
  "unblocked": true,
  "userId": 42
}
```

**Errors:**
- `404` — Block not found
- `401` — Unauthorized

**Note:** Does **not** reopen old matches. Old matches remain `CLOSED`.

---

## Admin Endpoints

All admin endpoints require `Admin` role claim in JWT.

### Get Pending Queue

Fetch pending moderation items.

```http
GET /admin/moderation/queue
Authorization: Bearer <admin-jwt>
```

**Query Parameters:**
| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `limit` | int | 50 | Max items to return (1-200) |

**Response (200 OK):**
```json
{
  "count": 12,
  "items": [
    {
      "id": "a1b2c3d4-...",
      "tileId": "tile-123",
      "userId": 42,
      "contentType": "text",
      "contentText": "Check out this deal!",
      "mediaUrl": null,
      "queuedAt": "2026-10-07T10:00:00Z"
    },
    {
      "id": "b2c3d4e5-...",
      "tileId": "tile-456",
      "userId": 73,
      "contentType": "photo",
      "contentText": "Beach vibes 🌊",
      "mediaUrl": "https://wovenprodblob.blob.core.windows.net/tiles/photo-456.jpg",
      "queuedAt": "2026-10-07T09:30:00Z"
    }
  ]
}
```

**Errors:**
- `403` — Not an admin
- `401` — Unauthorized

**Example:**
```bash
curl http://localhost:5135/admin/moderation/queue \
  -H "Authorization: Bearer <admin-jwt>"
```

---

### Approve Queue Item

Approve a pending tile.

```http
POST /admin/moderation/{itemId}/approve
Authorization: Bearer <admin-jwt>
```

**Path Parameters:**
- `itemId` (uuid) — Queue item ID (not tile ID)

**Response (200 OK):**
```json
{
  "approved": "a1b2c3d4-..."
}
```

**Errors:**
- `404` — Queue item not found or already reviewed
- `403` — Not an admin
- `401` — Unauthorized

**Side effects:**
- Sets `reviewed_at = now()`, `reviewer_id`, `decision = 'approved'`
- Sets `tile.IsModerated = true`
- Tile visible in Commons feed

**Example:**
```bash
curl -X POST http://localhost:5135/admin/moderation/a1b2c3d4.../approve \
  -H "Authorization: Bearer <admin-jwt>"
```

---

### Reject Queue Item

Reject a pending tile.

```http
POST /admin/moderation/{itemId}/reject
Authorization: Bearer <admin-jwt>
Content-Type: application/json

{
  "reason": "spam"
}
```

**Path Parameters:**
- `itemId` (uuid) — Queue item ID

**Request Body:**
| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `reason` | string | Yes | Rejection reason (max 200 chars) |

**Response (200 OK):**
```json
{
  "rejected": "a1b2c3d4-..."
}
```

**Errors:**
- `404` — Queue item not found or already reviewed
- `400` — Reason missing or too long
- `403` — Not an admin
- `401` — Unauthorized

**Side effects:**
- Sets `reviewed_at = now()`, `reviewer_id`, `decision = 'rejected'`
- Sets `tile.IsExpired = true` (soft delete)
- Stores `reject_reason`
- Tile hidden from Commons feed

**Example:**
```bash
curl -X POST http://localhost:5135/admin/moderation/a1b2c3d4.../reject \
  -H "Authorization: Bearer <admin-jwt>" \
  -H "Content-Type: application/json" \
  -d '{"reason": "spam"}'
```

---

### Get Tile Reports

Fetch all reports for a specific tile.

```http
GET /admin/tiles/{tileId}/reports
Authorization: Bearer <admin-jwt>
```

**Path Parameters:**
- `tileId` (uuid) — Tile ID

**Response (200 OK):**
```json
{
  "count": 2,
  "reports": [
    {
      "id": "r1s2t3u4-...",
      "tileId": "tile-123",
      "reporterId": 84,
      "reason": "inappropriate_content",
      "reportedAt": "2026-10-07T11:00:00Z"
    },
    {
      "id": "r2s3t4u5-...",
      "tileId": "tile-123",
      "reporterId": 91,
      "reason": "spam",
      "reportedAt": "2026-10-07T11:15:00Z"
    }
  ]
}
```

**Errors:**
- `403` — Not an admin
- `401` — Unauthorized

**Example:**
```bash
curl http://localhost:5135/admin/tiles/tile-123/reports \
  -H "Authorization: Bearer <admin-jwt>"
```

---

### Get User Trust Score

Fetch trust score details for a user.

```http
GET /admin/trust/{userId}
Authorization: Bearer <admin-jwt>
```

**Path Parameters:**
- `userId` (int) — User ID

**Response (200 OK):**
```json
{
  "userId": 42,
  "email": "user@example.com",
  "trustScore": 0.35,
  "updatedAt": "2026-10-06T08:00:00Z"
}
```

**Errors:**
- `404` — User not found
- `403` — Not an admin
- `401` — Unauthorized

**Example:**
```bash
curl http://localhost:5135/admin/trust/42 \
  -H "Authorization: Bearer <admin-jwt>"
```

---

### Reset User Trust Score

Reset user's trust score to default (0.5).

```http
POST /admin/trust/{userId}/reset
Authorization: Bearer <admin-jwt>
```

**Path Parameters:**
- `userId` (int) — User ID

**Response (200 OK):**
```json
{
  "userId": 42,
  "trustScore": 0.5,
  "updatedAt": "2026-10-07T14:30:00Z"
}
```

**Errors:**
- `404` — User not found
- `403` — Not an admin
- `401` — Unauthorized

**Side effects:**
- Sets `user.TrustScore = 0.5`
- Sets `user.TrustUpdatedAt = now()`

**Example:**
```bash
curl -X POST http://localhost:5135/admin/trust/42/reset \
  -H "Authorization: Bearer <admin-jwt>"
```

---

## Service Layer (Internal)

### IModerationService

**File:** `Services/Moderation/IModerationService.cs`

```csharp
public interface IModerationService
{
    Task<ModerationImageResult> ModerateImageAsync(int userId, string imageUrl, CancellationToken ct = default);
    Task EnqueueAsync(Guid tileId, int userId, CancellationToken ct = default);
    Task ProcessPendingAsync(CancellationToken ct = default);
    Task<bool> ApproveAsync(Guid queueItemId, int reviewerId, CancellationToken ct = default);
    Task<bool> RejectAsync(Guid queueItemId, int reviewerId, string reason, CancellationToken ct = default);
    Task<List<ModerationQueueDto>> GetPendingAsync(int limit = 50, CancellationToken ct = default);
    Task<List<TileReportDto>> GetReportsAsync(Guid tileId, CancellationToken ct = default);
}
```

**Methods:**

#### ModerateImageAsync
Check image via OpenAI Moderation API.

**Returns:**
- `APPROVED` — Safe image
- `ESCALATED` — API error, allow but flag for review
- `AUTO_REJECTED` — Flagged by OpenAI

**Usage:**
```csharp
var result = await _moderation.ModerateImageAsync(userId, photoUrl, ct);
if (result == ModerationImageResult.AUTO_REJECTED)
    return Results.BadRequest(new { error = "PHOTO_INAPPROPRIATE" });
```

#### EnqueueAsync
Add tile to moderation queue.

**Behavior:**
- If `Moderation:IsModerationEnabled = false` → auto-approves
- If already in queue → no-op (idempotent)

**Usage:**
```csharp
await _moderation.EnqueueAsync(tileId, userId, ct);
```

#### ProcessPendingAsync
Process pending queue items via OpenAI API (called by worker).

**Behavior:**
- Fetches 50 oldest pending items
- Calls `CheckOpenAiModerationAsync()` for each
- Updates `decision` + `tile.IsModerated` or `tile.IsExpired`

**Usage:**
```csharp
await _moderation.ProcessPendingAsync(ct);
```

#### ApproveAsync
Admin manually approves a queue item.

**Returns:** `true` if approved, `false` if not found or already reviewed.

**Usage:**
```csharp
var ok = await _moderation.ApproveAsync(itemId, reviewerId, ct);
if (!ok) return Results.NotFound();
```

#### RejectAsync
Admin manually rejects a queue item.

**Returns:** `true` if rejected, `false` if not found or already reviewed.

**Usage:**
```csharp
var ok = await _moderation.RejectAsync(itemId, reviewerId, "spam", ct);
if (!ok) return Results.NotFound();
```

#### GetPendingAsync
Fetch pending queue items (admin UI).

**Returns:** List of `ModerationQueueDto`.

**Usage:**
```csharp
var pending = await _moderation.GetPendingAsync(50, ct);
```

#### GetReportsAsync
Fetch all reports for a tile (admin UI).

**Returns:** List of `TileReportDto`.

**Usage:**
```csharp
var reports = await _moderation.GetReportsAsync(tileId, ct);
```

---

## Configuration

### appsettings.json

```json
{
  "Moderation": {
    "IsModerationEnabled": false  // true in production
  },
  "OpenAI": {
    "ApiKey": "sk-..."  // from User Secrets / Key Vault
  }
}
```

**Local dev (User Secrets):**
```bash
cd backend/WovenBackend
dotnet user-secrets set "OpenAI:ApiKey" "sk-..."
```

**Production (Azure Key Vault):**
```json
{
  "OpenAI": {
    "ApiKey": "@Microsoft.KeyVault(SecretUri=https://woven-prod-kv.vault.azure.net/secrets/OpenAI-ApiKey/)"
  }
}
```

---

## Error Codes

| Code | HTTP Status | Description |
|------|-------------|-------------|
| `MATCH_NOT_FOUND` | 404 | Match ID doesn't exist |
| `QUEUE_ITEM_NOT_FOUND_OR_ALREADY_REVIEWED` | 404 | Queue item ID invalid or already processed |
| `REASON_REQUIRED` | 400 | Rejection reason missing |
| `ALREADY_REPORTED` | 409 | User already reported this tile |
| `PHOTO_INAPPROPRIATE` | 400 | Photo auto-rejected by AI moderation |
| `ACTIVE_TILE_LIMIT_REACHED` | 400 | User has 10 active tiles (max) |
| `USER_NOT_FOUND` | 404 | User ID doesn't exist |

---

## Rate Limits

| Endpoint | Limit | Window |
|----------|-------|--------|
| `POST /tiles/{tileId}/report` | 5 requests | 24 hours |
| `POST /matches/{matchId}/block` | None | — |
| Admin endpoints | None | — |

**429 Response:**
```json
{
  "error": "RATE_LIMIT_EXCEEDED",
  "retryAfter": 3600
}
```

---

## Related Documentation

- [Moderation Overview](./README.md)
- [AI Moderation](./ai-moderation.md)
- [User Reports](./user-reports.md)
- [Block System](./block-system.md)
- [Review Queue](./review-queue.md)
- [Trust System](../trust/README.md)
