# Backend — Services & Endpoints

**Path:** `docs/features\commons\backend.md`  
**Last Updated:** 2026-10-07  
**Feature:** Commons

---

## Overview

The Commons backend consists of **3 endpoint groups** (CommonsEndpoints, TileEndpoints, OrbitEndpoints) and **5 core services** (CommonsFeedService, TileService, OrbitService, ModerationService, TileEmbeddingService).

**Pattern:** Minimal API (ASP.NET Core 10)  
**Database:** PostgreSQL 16 + pgvector 0.3.2  
**Cache:** Redis (StackExchange.Redis 2.8.16)

---

## Endpoints

### CommonsEndpoints
**File:** `backend/WovenBackend/Endpoints/CommonsEndpoints.cs`  
**Route:** `/commons`

```csharp
public static void MapCommonsEndpoints(this WebApplication app)
{
    var group = app.MapGroup("/commons").RequireAuthorization();

    // GET /commons?page=1&sessionId={uuid}
    group.MapGet("", async (
        int? page,
        Guid? sessionId,
        ICommonsFeedService feed,
        HttpContext http,
        CancellationToken ct) =>
    {
        var userId = GetUserId(http.User);
        var pageNum = Math.Max(1, page ?? 1);
        var session = sessionId ?? Guid.NewGuid();

        var result = await feed.GetFeedAsync(userId, pageNum, session, ct);

        if (result.EnergyDepleted)
            return Results.Json(
                new { error = "ENERGY_DEPLETED", message = "Daily browse limit reached. Resets at midnight UTC." },
                statusCode: 429);

        return Results.Ok(new
        {
            page = pageNum,
            sessionId = session,
            count = result.Tiles.Count,
            tiles = result.Tiles
        });
    });

    // POST /commons/refresh
    group.MapPost("/refresh", async (
        ICommonsFeedService feed,
        HttpContext http,
        CancellationToken ct) =>
    {
        var userId = GetUserId(http.User);
        await feed.RefreshFeedAsync(userId, ct);
        return Results.Ok(new { refreshed = true });
    });

    // POST /commons/{tileId}/view  body: { durationMs?: int }
    group.MapPost("/{tileId:guid}/view", async (
        Guid tileId,
        ViewRequest? req,
        ICommonsFeedService feed,
        HttpContext http,
        CancellationToken ct) =>
    {
        var userId = GetUserId(http.User);
        await feed.RecordViewAsync(userId, tileId, req?.DurationMs, ct);
        return Results.Ok(new { recorded = true });
    });
}
```

**GetUserId helper:**
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

**Note:** This pattern is duplicated across all 3 endpoint files (not yet refactored to `EndpointHelper.GetUserId`). See CLAUDE.md note about future refactor.

---

### TileEndpoints
**File:** `backend/WovenBackend/Endpoints/TileEndpoints.cs`  
**Route:** `/tiles`

```csharp
public static void MapTileEndpoints(this WebApplication app)
{
    var group = app.MapGroup("/tiles").RequireAuthorization();

    // POST /tiles
    group.MapPost("", async (
        CreateTileRequest req,
        ITileService tiles,
        HttpContext http,
        CancellationToken ct) =>
    {
        var userId = GetUserId(http.User);
        var result = await tiles.CreateAsync(userId, req, ct);

        return result.Success
            ? Results.Created($"/tiles/{result.TileId}", new { tileId = result.TileId })
            : Results.BadRequest(new { error = result.Error });
    });

    // GET /tiles/mine
    group.MapGet("/mine", async (
        ITileService tiles,
        HttpContext http,
        CancellationToken ct) =>
    {
        var userId  = GetUserId(http.User);
        var myTiles = await tiles.GetMineAsync(userId, ct);
        return Results.Ok(new { count = myTiles.Count, tiles = myTiles });
    });

    // POST /tiles/{tileId}/highlight
    group.MapPost("/{tileId:guid}/highlight", async (
        Guid tileId,
        HighlightRequest req,
        ITileService tiles,
        HttpContext http,
        CancellationToken ct) =>
    {
        var userId = GetUserId(http.User);
        var result = await tiles.HighlightAsync(userId, tileId, req.SlotNumber, ct);

        return result.Success
            ? Results.Ok(new { slot = req.SlotNumber })
            : Results.BadRequest(new { error = result.Error });
    });

    // DELETE /tiles/{tileId}/highlight
    group.MapDelete("/{tileId:guid}/highlight", async (
        Guid tileId,
        ITileService tiles,
        HttpContext http,
        CancellationToken ct) =>
    {
        var userId = GetUserId(http.User);
        var ok = await tiles.UnhighlightAsync(userId, tileId, ct);
        return ok ? Results.NoContent() : Results.NotFound(new { error = "HIGHLIGHT_NOT_FOUND" });
    });

    // DELETE /tiles/{tileId}
    group.MapDelete("/{tileId:guid}", async (
        Guid tileId,
        ITileService tiles,
        HttpContext http,
        CancellationToken ct) =>
    {
        var userId = GetUserId(http.User);
        var ok = await tiles.DeleteAsync(userId, tileId, ct);
        return ok
            ? Results.NoContent()
            : Results.BadRequest(new { error = "TILE_NOT_FOUND_OR_NOT_DELETABLE" });
    });
}
```

---

### OrbitEndpoints
**File:** `backend/WovenBackend/Endpoints/OrbitEndpoints.cs`  
**Route:** `/orbit`

```csharp
public static void MapOrbitEndpoints(this WebApplication app)
{
    var group = app.MapGroup("/orbit").RequireAuthorization();

    // POST /orbit/{tileId}
    group.MapPost("/{tileId:guid}", async (
        Guid tileId,
        IOrbitService orbit,
        ICacheService cache,
        HttpContext http,
        CancellationToken ct) =>
    {
        var userId = GetUserId(http.User);

        // Rate limit: 50 orbits per user per day
        var rlKey = $"rl:orbit:{userId}:{DateOnly.FromDateTime(DateTime.UtcNow)}";
        var allowed = await cache.CheckRateLimitAsync(rlKey, 50, CacheTtl.UntilMidnightUtc(), ct);
        if (!allowed)
        {
            http.Response.Headers["Retry-After"] = ((int)CacheTtl.UntilMidnightUtc().TotalSeconds).ToString();
            return Results.StatusCode(429);
        }

        try
        {
            var result = await orbit.OrbitTileAsync(userId, tileId, ct);
            return Results.Ok(new
            {
                relationshipType = result.RelationshipType,
                mutualDetected = result.MutualDetected
            });
        }
        catch (InvalidOperationException ex) when (
            ex.Message is "TILE_NOT_FOUND" or "CANNOT_ORBIT_OWN_TILE" or "ALREADY_ORBITED")
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    });

    // GET /orbit/bridges
    group.MapGet("/bridges", async (
        WovenDbContext db,
        HttpContext http,
        CancellationToken ct) =>
    {
        var userId = GetUserId(http.User);
        var bridges = await db.FriendBridges.AsNoTracking()
            .Where(b => b.UserAId == userId || b.UserBId == userId)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new { b.Id, b.UserAId, b.UserBId, b.Status, b.CreatedAt, b.AcceptedAt })
            .ToListAsync(ct);

        return Results.Ok(bridges);
    });

    // POST /orbit/bridges/{bridgeId}/accept
    group.MapPost("/bridges/{bridgeId:guid}/accept", async (
        Guid bridgeId,
        IFriendBridgeService bridgeService,
        HttpContext http,
        CancellationToken ct) =>
    {
        var userId = GetUserId(http.User);
        try
        {
            await bridgeService.AcceptBridgeAsync(userId, bridgeId, ct);
            return Results.Ok(new { accepted = true });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    });

    // POST /orbit/bridges/{bridgeId}/decline
    group.MapPost("/bridges/{bridgeId:guid}/decline", async (
        Guid bridgeId,
        IFriendBridgeService bridgeService,
        HttpContext http,
        CancellationToken ct) =>
    {
        var userId = GetUserId(http.User);
        try
        {
            await bridgeService.DeclineBridgeAsync(userId, bridgeId, ct);
            return Results.Ok(new { declined = true });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    });

    // GET /orbit/received
    group.MapGet("/received", async (
        WovenDbContext db,
        HttpContext http,
        CancellationToken ct) =>
    {
        var userId = GetUserId(http.User);
        var orbits = await db.TileOrbits.AsNoTracking()
            .Where(o => o.TileOwnerId == userId)
            .OrderByDescending(o => o.OrbitedAt)
            .Take(50)
            .Select(o => new { o.Id, o.OrbiterId, o.TileId, o.RelationshipType, o.OrbitedAt })
            .ToListAsync(ct);

        return Results.Ok(orbits);
    });
}
```

---

## Services

### CommonsFeedService
**File:** `backend/WovenBackend/Services/Commons/CommonsFeedService.cs`  
**Interface:** `ICommonsFeedService`

**Primary methods:**
```csharp
Task<CommonsFeedResult> GetFeedAsync(int userId, int page, Guid sessionId, CancellationToken ct);
Task RecordViewAsync(int userId, Guid tileId, int? durationMs, CancellationToken ct);
Task RefreshFeedAsync(int userId, CancellationToken ct);
```

**Responsibilities:**
- Fetch eligible tiles (moderated, not expired, not blocked, not viewed today)
- Score tiles using 5-component similarity + recency + CF
- Bucket into resonant (≥0.65) vs discovery (<0.65)
- Interleave 70/30
- Cache 200-tile pool in Redis (2h TTL)
- Record tile views + dwell signals
- Enforce energy cap (100 tiles/day)

**See:** **[feed-algorithm.md](./feed-algorithm.md)** for full scoring logic.

---

### TileService
**File:** `backend/WovenBackend/Services/Tiles/TileService.cs`  
**Interface:** `ITileService`

**Primary methods:**
```csharp
Task<CreateTileResult> CreateAsync(int userId, CreateTileRequest req, CancellationToken ct);
Task<List<TileDto>> GetMineAsync(int userId, CancellationToken ct);
Task<HighlightResult> HighlightAsync(int userId, Guid tileId, int slot, CancellationToken ct);
Task<bool> UnhighlightAsync(int userId, Guid tileId, CancellationToken ct);
Task<bool> DeleteAsync(int userId, Guid tileId, CancellationToken ct);
```

**CreateAsync logic:**
1. Validate content type (`text | photo | video | voice`)
2. Validate required fields (text tiles need `content_text`, media tiles need `media_url`)
3. Check active tile limit (10 max per user)
4. Auto-approve text tiles (inline OpenAI moderation in prod)
5. Queue media tiles for worker moderation
6. Enqueue tile for embedding generation (Service Bus or in-process Channel)

**HighlightAsync logic:**
1. Validate slot (1-9)
2. Validate ownership + expiration (can only highlight expired tiles)
3. Evict current occupant of target slot (if any)
4. Upsert highlight record
5. Set `is_highlighted = true` on tile (protects from blob cleanup)

**DeleteAsync logic:**
- Soft-delete: `is_expired = true` (preserves FK integrity)
- Cannot delete highlighted tiles (must unhighlight first)
- Cannot delete already-expired tiles (idempotent)

**See:** **[tiles.md](./tiles.md)** for entity details.

---

### OrbitService
**File:** `backend/WovenBackend/Services/Orbit/OrbitService.cs`  
**Interface:** `IOrbitService`

**Primary method:**
```csharp
Task<OrbitResult> OrbitTileAsync(int userId, Guid tileId, CancellationToken ct);
```

**Logic:**
1. Validate tile exists and is not user's own tile
2. Check duplicate orbit (unique constraint on `orbiter_id + tile_id`)
3. Determine relationship type (romantic vs social) based on viewer's preferences:
   - Gender match
   - Age in range
   - Distance within max
4. Insert `TileOrbit` record
5. If romantic: upsert `OrbitGravity` (exponential decay + increment)
6. Check mutual detection:
   - Romantic mutual: both orbited each other's tiles (romantic type) → create `CandidateSignal` (7-day TTL)
   - Social mutual: both orbited each other's tiles (social type) → create `FriendBridge`
7. Return `{ relationshipType, mutualDetected }`

**OrbitGravity decay formula:**
```csharp
var daysSinceLast = (now - existing.LastOrbitAt).TotalDays;
var decayed = existing.Score * Math.Exp(-0.1 * daysSinceLast);
existing.Score = decayed + 1.0;
```

**See:** **[orbit.md](./orbit.md)** for full mechanics.

---

### ModerationService
**File:** `backend/WovenBackend/Services/Moderation/ModerationService.cs`  
**Interface:** `IModerationService`

**Primary methods:**
```csharp
Task EnqueueAsync(Guid tileId, int userId, CancellationToken ct);
Task ProcessPendingAsync(CancellationToken ct);
Task<bool> ApproveAsync(Guid queueItemId, int reviewerId, CancellationToken ct);
Task<bool> RejectAsync(Guid queueItemId, int reviewerId, string reason, CancellationToken ct);
Task<ModerationImageResult> ModerateImageAsync(int userId, string imageUrl, CancellationToken ct);
```

**ProcessPendingAsync logic:**
1. Fetch 50 pending items from `moderation_queue`
2. Call OpenAI Moderation API (`/v1/moderations` with `omni-moderation-latest`)
3. If flagged: set `is_expired = true`, `decision = "rejected"`
4. If passed: set `is_moderated = true`, `decision = "approved"`
5. Write `reviewed_at` timestamp

**Worker:** `ModerationWorker` runs every 5 minutes, distributed-lock protected.

**See:** **[moderation.md](./moderation.md)** for full flow.

---

### TileEmbeddingService
**File:** `backend/WovenBackend/Services/Tiles/TileEmbeddingService.cs`  
**Interface:** `ITileEmbeddingService`

**Responsibility:**  
Generate 1536-dim embedding for tile content via OpenAI `text-embedding-3-small` and store in `Tile.embedding`.

**Triggers:**
- Text tiles → embed `content_text`
- Photo tiles → (planned) Vision caption → embed caption
- Video tiles → (planned) Extract caption → embed caption
- Voice tiles → (planned) Whisper transcription → embed transcript

**Queue:** Azure Service Bus (prod) or in-process Channel (dev)  
**Batch size:** 1 tile per message (parallel processing across workers)

**Why asynchronous?**  
Embedding generation is slow (150-300ms per call). Queueing decouples latency from user-facing POST.

---

## Data Entities

### Tile
**File:** `backend/WovenBackend/data/Entities/Moments/Tile.cs`

```csharp
[Table("tiles")]
public class Tile
{
    [Key][Column("id")] public Guid Id { get; set; }
    [Column("user_id")] public int UserId { get; set; }
    [Column("content_type")][MaxLength(20)] public string ContentType { get; set; }
    [Column("content_text")] public string? ContentText { get; set; }
    [Column("media_url")][MaxLength(2048)] public string? MediaUrl { get; set; }
    [Column("embedding")] public Vector? Embedding { get; set; }  // 1536-dim
    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; }
    [Column("expires_at")] public DateTimeOffset ExpiresAt { get; set; }
    [Column("is_expired")] public bool IsExpired { get; set; }
    [Column("is_highlighted")] public bool IsHighlighted { get; set; }
    [Column("is_moderated")] public bool IsModerated { get; set; }
    [Column("voice_embedding")] public Vector? VoiceEmbedding { get; set; }  // 192-dim (Phase 3D)
}
```

**Indexes:**
- Primary key on `id`
- Index on `user_id`
- Index on `is_expired, is_moderated` (feed eligibility filter)
- Index on `expires_at` (TileExpiryWorker)

---

### TileView
**Table:** `tile_views`

```csharp
public class TileView
{
    public int UserId { get; set; }
    public Guid TileId { get; set; }
    public DateTimeOffset ViewedAt { get; set; }
    public int? DurationMs { get; set; }
}
```

**Indexes:**
- Composite primary key on `(user_id, tile_id, viewed_at)`
- Index on `(user_id, viewed_at)` — viewed-today filter

---

### TileOrbit
**Table:** `tile_orbits`

```csharp
[Table("tile_orbits")]
public class TileOrbit
{
    [Key] public Guid Id { get; set; }
    public int OrbiterId { get; set; }
    public Guid TileId { get; set; }
    public int TileOwnerId { get; set; }
    public string RelationshipType { get; set; }  // 'romantic' | 'social'
    public DateTimeOffset OrbitedAt { get; set; }
}
```

**Indexes:**
- Primary key on `id`
- **UNIQUE** constraint on `(orbiter_id, tile_id)`
- Index on `tile_owner_id` (received orbits query)
- Index on `relationship_type` (mutual detection)

---

### OrbitGravity
**Table:** `orbit_gravity`

```csharp
[Table("orbit_gravity")]
public class OrbitGravity
{
    public int UserId { get; set; }
    public int CandidateId { get; set; }
    public double Score { get; set; }
    public DateTimeOffset LastOrbitAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

**Primary key:** `(user_id, candidate_id)`  
**Used by:** `MatchScoringService` (Component #15: OrbitAffinity)

---

### Highlight
**Table:** `highlights`

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

**Indexes:**
- Composite unique on `(user_id, tile_id)`
- Unique on `(user_id, slot_number)` — prevents slot collisions

---

### UserEnergyMeter
**Table:** `user_energy_meter`

```csharp
[Table("user_energy_meter")]
public class UserEnergyMeter
{
    public int UserId { get; set; }
    public DateOnly DateUtc { get; set; }
    public int TilesViewed { get; set; }
}
```

**Primary key:** `(user_id, date_utc)`  
**TTL:** No DB-level TTL (Redis cache expires at midnight UTC)

---

## Workers

### TileExpiryWorker
**Schedule:** Every 6 hours  
**What:** Marks tiles with `expires_at < now` as `is_expired = true`

```csharp
var expired = await _db.Tiles
    .Where(t => !t.IsExpired && t.ExpiresAt < DateTimeOffset.UtcNow)
    .ToListAsync(ct);

foreach (var tile in expired)
    tile.IsExpired = true;

await _db.SaveChangesAsync(ct);
```

---

### ModerationWorker
**Schedule:** Every 5 minutes  
**What:** Pulls pending items from `moderation_queue`, calls OpenAI Moderation API, approves or rejects

**See:** **[moderation.md](./moderation.md)**

---

### TileEmbeddingService (Queue Consumer)
**Trigger:** Service Bus message or in-process Channel message  
**What:** Generates 1536-dim embedding via `text-embedding-3-small`, stores in `Tile.embedding`

---

## Configuration

**Program.cs:**
```csharp
builder.Services.AddScoped<ICommonsFeedService, CommonsFeedService>();
builder.Services.AddScoped<ITileService, TileService>();
builder.Services.AddScoped<IOrbitService, OrbitService>();
builder.Services.AddScoped<IModerationService, ModerationService>();
builder.Services.AddScoped<ITileEmbeddingService, TileEmbeddingService>();

builder.Services.AddHostedService<TileExpiryWorker>();
builder.Services.AddHostedService<ModerationWorker>();
```

**appsettings.json:**
```json
{
  "Moderation": {
    "IsModerationEnabled": true  // false in dev → auto-approve all
  }
}
```

---

## Related Docs

- **[README.md](./README.md)** — Commons overview
- **[tiles.md](./tiles.md)** — Tile entity and lifecycle
- **[orbit.md](./orbit.md)** — Orbit gravity mechanics
- **[feed-algorithm.md](./feed-algorithm.md)** — Feed scoring logic
- **[moderation.md](./moderation.md)** — Content moderation
- **[api.md](./api.md)** — Full endpoint specs
