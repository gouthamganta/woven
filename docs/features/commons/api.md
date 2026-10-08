# Commons API Reference

**Feature:** Commons (content feed)  
**Endpoints:** `/commons/*`

---

## Overview

The Commons API provides endpoints for:
- Creating and posting content tiles
- Fetching the personalized feed
- Orbit interactions (◈ on tiles)
- Content moderation and reporting

For detailed implementation, see:
- [Backend Implementation](./backend.md)
- [Frontend Implementation](./frontend.md)
- [Feed Algorithm](./feed-algorithm.md)
- [Orbit Mechanics](./orbit.md)

---

## Endpoints

### POST /commons/tiles

Create a new content tile.

**Request:**
```json
{
  "content": "Your tile content here",
  "mediaUrls": ["https://..."],
  "expiresAt": "2026-10-14T00:00:00Z"
}
```

**Response:**
```json
{
  "tileId": 123,
  "status": "PENDING_MODERATION",
  "createdAt": "2026-10-07T12:00:00Z"
}
```

**Evidence:** [CommonsTilesEndpoints.cs](../../../backend/WovenBackend/Endpoints/CommonsTilesEndpoints.cs)

---

### GET /commons/feed

Get personalized feed of tiles.

**Query params:**
- `cursor` — pagination cursor (optional)
- `limit` — number of tiles (default 20, max 50)

**Response:**
```json
{
  "tiles": [...],
  "nextCursor": "abc123",
  "hasMore": true
}
```

**Evidence:** [CommonsEndpoints.cs](../../../backend/WovenBackend/Endpoints/CommonsEndpoints.cs)

---

### POST /commons/tiles/{tileId}/orbit

Add orbit interaction (◈) to a tile.

**Request:** (no body)

**Response:**
```json
{
  "orbited": true,
  "orbitGravity": 42
}
```

**Evidence:** [OrbitService.cs](../../../backend/WovenBackend/Services/Commons/OrbitService.cs)

---

## Related

- [API Overview](../../api/README.md)
- [Commons Backend](./backend.md)
- [Moderation System](./moderation.md)
