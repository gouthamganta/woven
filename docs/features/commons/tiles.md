# Tiles — Content Posts

**Path:** `docs/features/commons/tiles.md`  
**Last Updated:** 2026-08-17  
**Feature:** Commons

---

## What is a Tile?

A **Tile** is a single piece of user-generated content in Commons. Tiles are:
- **Anonymous** — no author name or profile photo shown
- **Ephemeral** — expire after 48 hours
- **Embeddable** — generate semantic embeddings for matching
- **Highlight-able** — can be pinned to profile after expiration

---

## Content Types

| Type | Field | Max Length | Notes |
|---|---|---|---|
| `text` | `content_text` | 150 chars | Auto-approved (inline moderation) |
| `photo` | `media_url` | — | Queues for OpenAI Vision moderation |
| `video` | `media_url` | — | Caption extraction → embedding |
| `voice` | `media_url` | — | SpeechBrain voice embedding (Phase 3D) |

**Validation:**
- `text` tiles require `content_text` (non-empty)
- `photo`, `video`, `voice` require `media_url` (non-empty)
- Invalid content type → `400 INVALID_CONTENT_TYPE`

---

## Tile Entity

**Table:** `tiles`  
**File:** `backend/WovenBackend/data/Entities/Moments/Tile.cs`

```csharp
public class Tile
{
    public Guid Id { get; set; }
    public int UserId { get; set; }
    public string ContentType { get; set; }        // 'text' | 'photo' | 'video' | 'voice'
    public string? ContentText { get; set; }
    public string? MediaUrl { get; set; }
    public Vector? Embedding { get; set; }         // 1536-dim text-embedding-3-small
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }  // always created_at + 48h
    public bool IsExpired { get; set; }
    public bool IsHighlighted { get; set; }        // true once pinned to any slot
    public bool IsModerated { get; set; }          // false until approved
    public Vector? VoiceEmbedding { get; set; }    // 192-dim ECAPA-TDNN (Phase 3D)
}
```

**Indexes:**
- Primary key on `id` (uuid)
- Index on `user_id` (for "my tiles" queries)
- Index on `is_expired, is_moderated` (for feed eligibility)
- Index on `expires_at` (for expiry worker)

---

## Creation Flow

### 1. Frontend Request
```typescript
// frontend/woven-frontend/src/app/services/commons.service.ts
createTile(contentType: string, contentText?: string, mediaUrl?: string) {
  return this.http.post<{ tileId: string }>(`${environment.apiUrl}/tiles`, {
    contentType,
    contentText: contentText ?? null,
    mediaUrl: mediaUrl ?? null,
  });
}
```

### 2. Backend Validation
**Endpoint:** `POST /tiles`  
**Service:** `TileService.CreateAsync`  
**File:** `backend/WovenBackend/Services/Tiles/TileService.cs`

```csharp
public async Task<CreateTileResult> CreateAsync(int userId, CreateTileRequest req, CancellationToken ct)
{
    // 1. Validate content type
    if (!ValidContentTypes.Contains(req.ContentType))
        return new CreateTileResult(false, null, "INVALID_CONTENT_TYPE");

    // 2. Validate required fields
    if (req.ContentType == "text" && string.IsNullOrWhiteSpace(req.ContentText))
        return new CreateTileResult(false, null, "TEXT_REQUIRED");
    if (req.ContentType != "text" && string.IsNullOrWhiteSpace(req.MediaUrl))
        return new CreateTileResult(false, null, "MEDIA_URL_REQUIRED");

    // 3. Check active tile limit
    var activeCount = await _db.Tiles.CountAsync(t => t.UserId == userId && !t.IsExpired, ct);
    if (activeCount >= MaxActiveTiles) // 10
        return new CreateTileResult(false, null, "ACTIVE_TILE_LIMIT_REACHED");

    // 4. Auto-moderation logic
    var isModerationEnabled = _config.GetValue<bool>("Moderation:IsModerationEnabled");
    var isText = req.ContentType.Equals("text", StringComparison.OrdinalIgnoreCase);
    var isModerated = !isModerationEnabled || isText; // text auto-approves in prod

    // 5. Create tile
    var tile = new Tile
    {
        UserId = userId,
        ContentType = req.ContentType.ToLowerInvariant(),
        ContentText = req.ContentText?.Trim(),
        MediaUrl = req.MediaUrl,
        CreatedAt = now,
        ExpiresAt = now + TimeSpan.FromHours(48),
        IsExpired = false,
        IsHighlighted = false,
        IsModerated = isModerated
    };
    _db.Tiles.Add(tile);
    await _db.SaveChangesAsync(ct);

    // 6. Enqueue embedding (Service Bus or in-process Channel)
    if (!string.IsNullOrEmpty(tile.ContentText) || tile.ContentType is "photo" or "video")
        await _embeddingQueue.EnqueueAsync(tile.Id, ct);

    return new CreateTileResult(true, tile.Id, null);
}
```

**Errors:**
- `INVALID_CONTENT_TYPE` — contentType not in `{text, photo, video, voice}`
- `TEXT_REQUIRED` — text tile with empty contentText
- `MEDIA_URL_REQUIRED` — media tile with empty mediaUrl
- `ACTIVE_TILE_LIMIT_REACHED` — user has 10+ non-expired tiles

### 3. Embedding Generation
**Worker:** `TileEmbeddingService`  
**Queue:** Azure Service Bus (prod) or in-process Channel (dev)  
**File:** `backend/WovenBackend/Services/Tiles/TileEmbeddingService.cs`

**Triggers:**
- Text tiles: OpenAI `text-embedding-3-small` on `content_text`
- Photo tiles: Vision model caption → embedding
- Video tiles: Caption extraction → embedding
- Voice tiles: Whisper transcription → embedding (future)

**Output:** 1536-dimensional vector stored in `Tile.embedding`

**Why asynchronous?**  
Embedding generation is slow (150-300ms per call) and non-critical for tile creation. Queueing decouples latency from the user-facing POST.

---

## Tile Expiration

### Auto-Expiry Worker
**Service:** `TileExpiryWorker`  
**Schedule:** Every 6 hours  
**File:** `backend/WovenBackend/Services/Tiles/TileExpiryWorker.cs`

```csharp
var expired = await _db.Tiles
    .Where(t => !t.IsExpired && t.ExpiresAt < DateTimeOffset.UtcNow)
    .ToListAsync(ct);

foreach (var tile in expired)
    tile.IsExpired = true;

await _db.SaveChangesAsync(ct);
```

**Why batch?**  
Simpler than per-tile timers. 6-hour granularity is acceptable for 48-hour TTL.

**Effects:**
- Expired tiles no longer appear in feeds (`is_expired = true` filtered in `CommonsFeedService`)
- Expired tiles become eligible for highlighting
- Media URLs for expired, non-highlighted tiles are cleaned by `MediaLifecycleWorker` (separate service)

---

## Highlights (Profile Pinning)

After a tile expires, it can be **pinned to one of 9 highlight slots** on the user's profile. Highlights are permanent until unpinned.

### Highlight Flow
**Endpoint:** `POST /tiles/{tileId}/highlight`  
**Service:** `TileService.HighlightAsync`

```csharp
public async Task<HighlightResult> HighlightAsync(int userId, Guid tileId, int slot, CancellationToken ct)
{
    // 1. Validate slot (1-9)
    if (slot < 1 || slot > 9)
        return new HighlightResult(false, "INVALID_SLOT");

    // 2. Validate ownership + expiration
    var tile = await _db.Tiles.FirstOrDefaultAsync(t => t.Id == tileId && t.UserId == userId, ct);
    if (tile is null)
        return new HighlightResult(false, "TILE_NOT_FOUND");
    if (!tile.IsExpired)
        return new HighlightResult(false, "TILE_MUST_BE_EXPIRED");

    // 3. Evict current occupant of target slot (if any)
    var occupant = await _db.Highlights
        .FirstOrDefaultAsync(h => h.UserId == userId && h.SlotNumber == slot && h.TileId != tileId, ct);
    if (occupant is not null)
    {
        _db.Highlights.Remove(occupant);
        // Clear is_highlighted on evicted tile if no other slots remain
    }

    // 4. Upsert highlight
    var existing = await _db.Highlights
        .FirstOrDefaultAsync(h => h.TileId == tileId && h.UserId == userId, ct);
    if (existing is not null)
        existing.SlotNumber = slot;
    else
        _db.Highlights.Add(new Highlight { UserId = userId, TileId = tileId, SlotNumber = slot });

    tile.IsHighlighted = true;
    await _db.SaveChangesAsync(ct);
}
```

**Highlight Entity:**
```csharp
[Table("highlights")]
public class Highlight
{
    public int UserId { get; set; }
    public Guid TileId { get; set; }
    public int SlotNumber { get; set; }  // 1-9
    public DateTimeOffset PinnedAt { get; set; }
}
```

**Business Rules:**
- Only expired tiles can be highlighted
- Slots 1-9 (unique per user)
- Evicting a slot clears `is_highlighted` on the old tile if it has no other slots
- Highlighted tiles are **protected from blob cleanup** (media URLs persist)

---

## Deletion

**Endpoint:** `DELETE /tiles/{tileId}`  
**Service:** `TileService.DeleteAsync`

**Logic:**
```csharp
public async Task<bool> DeleteAsync(int userId, Guid tileId, CancellationToken ct)
{
    var tile = await _db.Tiles
        .FirstOrDefaultAsync(t => t.Id == tileId && t.UserId == userId, ct);

    if (tile is null) return false;
    if (tile.IsExpired) return false;        // already gone
    if (tile.IsHighlighted) return false;    // must unhighlight first

    // Soft-delete: mark expired so MediaLifecycleWorker cleans blob
    tile.IsExpired = true;
    await _db.SaveChangesAsync(ct);
    return true;
}
```

**Constraints:**
- Can only delete **your own** tiles
- Cannot delete already-expired tiles (idempotent)
- Cannot delete highlighted tiles (must unhighlight first)

**Soft-delete rationale:**  
Hard-deleting rows breaks referential integrity (tile_views, tile_orbits FK to tiles). Soft-deleting (`is_expired = true`) keeps data for analytics while hiding from feeds.

---

## Listing My Tiles

**Endpoint:** `GET /tiles/mine`  
**Service:** `TileService.GetMineAsync`

**Returns:**
```typescript
interface TileDto {
  id: string;
  contentType: string;
  contentText: string | null;
  mediaUrl: string | null;
  createdAt: string;
  expiresAt: string;
  isExpired: boolean;
  isHighlighted: boolean;
  isModerated: boolean;
  highlightSlot: number | null; // 1-9 if highlighted
}
```

**Query:**
```csharp
var tiles = await _db.Tiles
    .Where(t => t.UserId == userId)
    .OrderByDescending(t => t.CreatedAt)
    .Take(50)
    .ToListAsync(ct);

var slotMap = await _db.Highlights
    .Where(h => h.UserId == userId && tileIds.Contains(h.TileId))
    .ToDictionaryAsync(h => h.TileId, h => h.SlotNumber, ct);
```

**Limit:** 50 most recent tiles (expired + active)

---

## Constants

```csharp
// TileService.cs
private static readonly HashSet<string> ValidContentTypes =
    new(StringComparer.OrdinalIgnoreCase) { "text", "photo", "video", "voice" };

private const int MaxActiveTiles = 10;
private static readonly TimeSpan TileLifetime = TimeSpan.FromHours(48);
```

---

## Related Entities

### TileView
**Table:** `tile_views`  
Tracks every time a user opens a tile in the drawer. Used for:
- Energy meter (increments on view)
- Dwell signals (if duration ≥ 8000ms)

### TileOrbit
**Table:** `tile_orbits`  
Records **◈** interactions on tiles. See **[orbit.md](./orbit.md)** for mechanics.

---

## Design Notes

### Why 48-Hour TTL?
**Ephemerality creates urgency.** Instagram Stories proved that short-lived content drives engagement. 48 hours is long enough for discovery (multiple feed sessions) but short enough to feel ephemeral.

### Why 10 Active Tile Limit?
**Prevents spam.** Without a cap, a single user could flood Commons with hundreds of tiles. 10 strikes a balance between expression freedom and quality control.

### Why Auto-Approve Text?
**Speed.** OpenAI Moderation API is fast (200-400ms) but adds latency. Text tiles run moderation inline and auto-approve when the API passes. Media tiles queue for vision moderation (slower, requires image download).

### Why Highlights?
**Permanence after ephemerality.** Once a tile expires, it's gone from Commons. Highlights let users archive their best content as permanent profile elements (similar to Instagram Highlights).

---

## Next Steps

- **Voice embedding matching** — VoiceEmbedding field exists, not used in feed ranking yet
- **Video caption embeddings** — Tile creation flow exists, caption extraction not wired
- **Highlight gallery UI** — Backend ready, frontend `/you/tiles` page not built

---

## Related Docs

- **[README.md](./README.md)** — Commons overview
- **[orbit.md](./orbit.md)** — Orbit gravity mechanics
- **[moderation.md](./moderation.md)** — Content moderation flow
- **[backend.md](./backend.md)** — TileService implementation
- **[api.md](./api.md)** — Full endpoint specs
