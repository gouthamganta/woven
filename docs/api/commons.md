# Commons API Reference

**Base URL:** `https://api.wooven.me` (prod) / `http://localhost:5135` (dev)

---

## Overview

Commons is the content feed where users post and browse tiles (text, photos, polls). Features energy-based browsing limits and orbit interactions.

**Key Concepts:**
- **Tile** — Content post (TEXT, PHOTO, POLL)
- **Orbit** — Like/reaction (similar to Moments ◈ choice)
- **Energy** — Daily browse limit (prevents infinite scrolling)
- **Feed** — Personalized content feed with pagination

**Source:** [`backend/WovenBackend/Endpoints/CommonsEndpoints.cs`](../../backend/WovenBackend/Endpoints/CommonsEndpoints.cs)

---

## Endpoints

### GET /commons

**Description:** Fetch personalized content feed with pagination.

**Authentication:** Required (JWT)

**Query Parameters:**
- `page` (optional, default 1) — Page number
- `sessionId` (optional) — Session UUID for consistency

**Response (200 OK):**
```json
{
  "page": 1,
  "sessionId": "uuid",
  "count": 20,
  "tiles": [
    {
      "tileId": "uuid",
      "userId": 789,
      "author": {
        "fullName": "Jordan",
        "profilePhoto": "https://...",
        "isVerified": true
      },
      "contentType": "TEXT",
      "contentText": "Coffee is life ☕",
      "mediaUrl": null,
      "orbitGravity": 42,
      "createdAt": "2026-10-07T10:00:00Z",
      "hasOrbited": false
    }
  ]
}
```

**Fields:**
- `orbitGravity` — Orbit count (likes)
- `hasOrbited` — True if current user orbited this tile

**Errors:**
- `429 ENERGY_DEPLETED` — Daily browse limit reached

**Source:** [`CommonsEndpoints.cs:14-40`](../../backend/WovenBackend/Endpoints/CommonsEndpoints.cs)

---

### POST /commons/refresh

**Description:** Trigger feed refresh (clears cached feed for user).

**Authentication:** Required (JWT)

**Request:** Empty body

**Response (200 OK):**
```json
{
  "refreshed": true
}
```

**Source:** [`CommonsEndpoints.cs:43-51`](../../backend/WovenBackend/Endpoints/CommonsEndpoints.cs)

---

### POST /commons/{tileId}/view

**Description:** Track tile view (dwell time for ML signals).

**Authentication:** Required (JWT)

**Request:**
```json
{
  "durationMs": 3500
}
```

**Fields:**
- `durationMs` (optional) — Time spent viewing tile

**Response (200 OK):**
```json
{
  "recorded": true
}
```

**ML Signal:** Used by `CollaborativeFilteringService` for shared tile affinity scoring.

**Source:** [`CommonsEndpoints.cs:54-65`](../../backend/WovenBackend/Endpoints/CommonsEndpoints.cs)

---

## Tile Endpoints (from TileEndpoints.cs)

### POST /tiles

**Description:** Create a new content tile.

**Authentication:** Required (JWT)

**Request:**
```json
{
  "contentType": "TEXT",
  "contentText": "Coffee is life ☕",
  "mediaUrl": null
}
```

**Content Types:**
- `TEXT` — Text-only post (1-280 characters)
- `PHOTO` — Photo with optional caption
- `POLL` — Poll with 2-4 options (not yet implemented)

**Response (201 Created):**
```json
{
  "tileId": "uuid"
}
```

**Source:** [`TileEndpoints.cs:14-27`](../../backend/WovenBackend/Endpoints/TileEndpoints.cs)

---

### GET /tiles/mine

**Description:** Fetch current user's tiles.

**Authentication:** Required (JWT)

**Response (200 OK):**
```json
{
  "count": 5,
  "tiles": [
    {
      "tileId": "uuid",
      "contentType": "TEXT",
      "contentText": "Coffee is life ☕",
      "mediaUrl": null,
      "orbitGravity": 42,
      "createdAt": "2026-10-07T10:00:00Z",
      "isExpired": false,
      "isModerated": true
    }
  ]
}
```

**Source:** [`TileEndpoints.cs:30-38`](../../backend/WovenBackend/Endpoints/TileEndpoints.cs)

---

### POST /tiles/{tileId}/highlight

**Description:** Highlight a tile (pin to profile, slots 1-3).

**Authentication:** Required (JWT)

**Request:**
```json
{
  "slotNumber": 1
}
```

**Slots:** 1, 2, or 3 (profile highlight positions)

**Response (200 OK):**
```json
{
  "slot": 1
}
```

**Errors:**
- `400` — Invalid slot or tile already highlighted in another slot

**Source:** [`TileEndpoints.cs:41-54`](../../backend/WovenBackend/Endpoints/TileEndpoints.cs)

---

### DELETE /tiles/{tileId}/highlight

**Description:** Remove tile from highlights.

**Authentication:** Required (JWT)

**Response (204 No Content)**

**Errors:**
- `404 HIGHLIGHT_NOT_FOUND`

**Source:** [`TileEndpoints.cs:57-68`](../../backend/WovenBackend/Endpoints/TileEndpoints.cs)

---

### DELETE /tiles/{tileId}

**Description:** Delete a tile (soft delete, sets `isExpired`).

**Authentication:** Required (JWT)

**Response (204 No Content)**

**Errors:**
- `400 TILE_NOT_FOUND_OR_NOT_DELETABLE`

**Source:** [`TileEndpoints.cs:71-82`](../../backend/WovenBackend/Endpoints/TileEndpoints.cs)

---

## Energy System

**Daily Limit:** Prevents infinite scrolling (exact limit configured in `CommonsFeedService`)

**Reset:** Midnight UTC

**429 Response:**
```json
{
  "error": "ENERGY_DEPLETED",
  "message": "Daily browse limit reached. Resets at midnight UTC."
}
```

---

## Orbit Mechanics

**Orbit** = like/react to a tile

**Effects:**
- Increments `orbitGravity` counter
- Creates `Interaction` record (ML signal)
- Used for `SharedTileAffinity` matchmaking component

**Implementation:** Handled by `OrbitEndpoints.cs` (separate from Commons)

---

**Last Updated:** 2026-10-07  
**Source Files:**
- `backend/WovenBackend/Endpoints/CommonsEndpoints.cs`
- `backend/WovenBackend/Endpoints/TileEndpoints.cs`
- `backend/WovenBackend/Services/Commons/CommonsFeedService.cs`
