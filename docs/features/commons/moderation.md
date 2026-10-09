# Content Moderation — Safety & Quality

**Path:** `docs/features/commons/moderation.md`  
**Last Updated:** 2026-10-07  
**Feature:** Commons

---

## Overview

Every tile posted to Commons passes through **automated moderation** via OpenAI's Moderation API (`omni-moderation-latest`). The system uses a **queue-based worker pattern** to decouple moderation latency from the user-facing POST.

**Service:** `ModerationService.cs`  
**Worker:** `ModerationWorker.cs` (polls every 5 minutes)  
**Entity:** `ModerationQueue`  
**API:** OpenAI Moderation API (`/v1/moderations`)

---

## Moderation Flow

### 1. Tile Creation
**Endpoint:** `POST /tiles`  
**Service:** `TileService.CreateAsync`

```csharp
// Text tiles auto-approve in production (inline moderation)
var isText = req.ContentType.Equals("text", StringComparison.OrdinalIgnoreCase);
var isModerated = !isModerationEnabled || isText;

var tile = new Tile
{
    // ...
    IsModerated = isModerated  // true for text (prod), false for media
};
_db.Tiles.Add(tile);
await _db.SaveChangesAsync(ct);

// Enqueue media tiles for vision moderation
if (req.ContentType is "photo" or "video")
    await _moderationService.EnqueueAsync(tile.Id, userId, ct);
```

**Auto-approval conditions:**
- **Text tiles (production):** OpenAI Moderation API called inline (200-400ms). If API passes, `is_moderated = true` immediately.
- **Text tiles (dev):** `Moderation:IsModerationEnabled = false` → auto-approve without API call.
- **Media tiles:** Always queued for worker processing (OpenAI Vision moderation requires image download, slower).

### 2. Queue Enqueue
**Service:** `ModerationService.EnqueueAsync`

```csharp
public async Task EnqueueAsync(Guid tileId, int userId, CancellationToken ct)
{
    var isModerationEnabled = _config.GetValue<bool>("Moderation:IsModerationEnabled");
    if (!isModerationEnabled)
    {
        // Dev: auto-approve media tiles immediately
        var tile = await _db.Tiles.FirstOrDefaultAsync(t => t.Id == tileId, ct);
        if (tile is not null && !tile.IsModerated)
        {
            tile.IsModerated = true;
            await _db.SaveChangesAsync(ct);
        }
        return;
    }

    // Avoid duplicate queue entries
    var exists = await _db.ModerationQueues
        .AnyAsync(q => q.TileId == tileId && q.ReviewedAt == null, ct);
    if (exists) return;

    _db.ModerationQueues.Add(new ModerationQueue
    {
        TileId    = tileId,
        UserId    = userId,
        QueuedAt  = DateTimeOffset.UtcNow
    });
    await _db.SaveChangesAsync(ct);
}
```

**Idempotency:** Duplicate queue entries prevented via `TileId` + `ReviewedAt == null` check.

### 3. Worker Processing
**Worker:** `ModerationWorker.cs`  
**Schedule:** Every 5 minutes  
**Batch size:** 50 tiles per pass

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        if (!await _cache.AcquireLockAsync(LockKey, LockExpiry, stoppingToken))
            continue; // another pod is mid-pass

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var moderation = scope.ServiceProvider.GetRequiredService<IModerationService>();
            await moderation.ProcessPendingAsync(stoppingToken);
        }
        finally
        {
            await _cache.ReleaseLockAsync(LockKey, stoppingToken);
        }
    }
}
```

**Distributed lock:** Redis key `lock:moderation-pass` (4-minute expiry) ensures only one pod processes the queue at a time.

### 4. OpenAI Moderation API Call
**Service:** `ModerationService.CheckOpenAiModerationAsync`

```csharp
private async Task<bool> CheckOpenAiModerationAsync(string? text, CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(text)) return false;

    var body = JsonSerializer.Serialize(new { 
        input = text, 
        model = "omni-moderation-latest" 
    });
    using var content = new StringContent(body, Encoding.UTF8, "application/json");

    var response = await _http.PostAsync("https://api.openai.com/v1/moderations", content, ct);
    if (!response.IsSuccessStatusCode)
    {
        _logger.LogWarning("[Moderation] OpenAI API returned {Status}", response.StatusCode);
        return false; // fail-open (approve on API error)
    }

    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    return doc.RootElement
        .GetProperty("results")[0]
        .GetProperty("flagged")
        .GetBoolean();
}
```

**API Response:**
```json
{
  "id": "modr-...",
  "model": "omni-moderation-latest",
  "results": [
    {
      "flagged": false,
      "categories": { "hate": false, "violence": false, ... },
      "category_scores": { "hate": 0.0001, "violence": 0.0003, ... }
    }
  ]
}
```

**Fail-open:** If OpenAI API errors (non-200 status), the tile is **approved** (returns `false`). This prevents moderation outages from blocking all tile creation.

### 5. Decision & Tile Update
**Service:** `ModerationService.ProcessPendingAsync`

```csharp
foreach (var item in queue)
{
    try
    {
        var flagged = await CheckOpenAiModerationAsync(item.Tile.ContentText, ct);
        item.ReviewedAt = DateTimeOffset.UtcNow;

        if (flagged)
        {
            item.Decision        = "rejected";
            item.RejectReason    = "openai_moderation_flagged";
            item.Tile.IsExpired  = true;
            _logger.LogWarning("[Moderation] Tile {TileId} flagged by OpenAI", item.TileId);
        }
        else
        {
            item.Decision         = "approved";
            item.Tile.IsModerated = true;
            _logger.LogDebug("[Moderation] Tile {TileId} approved by OpenAI", item.TileId);
        }
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "[Moderation] OpenAI check failed for tile {TileId} — skipping", item.TileId);
    }
}

await _db.SaveChangesAsync(ct);
```

**Outcomes:**
- **Approved:** `is_moderated = true` → tile appears in feeds
- **Rejected:** `is_expired = true` → tile hidden from feeds, media blob cleaned by `MediaLifecycleWorker`

---

## Data Model

### ModerationQueue
**Table:** `moderation_queue`  
**File:** `backend/WovenBackend/data/Entities/ModerationQueue.cs`

```csharp
[Table("moderation_queue")]
public class ModerationQueue
{
    [Key][Column("id")] public Guid Id { get; set; }
    [Column("tile_id")] public Guid TileId { get; set; }
    public Tile Tile { get; set; }
    [Column("user_id")] public int UserId { get; set; }
    [Column("queued_at")] public DateTimeOffset QueuedAt { get; set; }
    [Column("reviewed_at")] public DateTimeOffset? ReviewedAt { get; set; }
    [Column("reviewer_id")] public int? ReviewerId { get; set; }
    [Column("decision")][MaxLength(20)] public string? Decision { get; set; } // 'approved' | 'rejected'
    [Column("reject_reason")][MaxLength(200)] public string? RejectReason { get; set; }
}
```

**Indexes:**
- Primary key on `id`
- Index on `tile_id` (FK to tiles)
- Index on `reviewed_at` (worker query filter: `WHERE reviewed_at IS NULL`)

---

## Configuration

**appsettings.json:**
```json
{
  "Moderation": {
    "IsModerationEnabled": true  // false in dev → auto-approve all
  },
  "OpenAI": {
    "ApiKey": "sk-..."  // stored in Azure Key Vault (prod)
  }
}
```

**Environment:** Azure Key Vault injects `OpenAI:ApiKey` at runtime (production).

**Dev mode:** `IsModerationEnabled = false` → all tiles auto-approve without API calls (faster dev iteration).

---

## Manual Review (Admin Endpoints)

### Admin Approval
**Endpoint:** `POST /admin/moderation/{queueItemId}/approve`  
**Service:** `ModerationService.ApproveAsync`

```csharp
public async Task<bool> ApproveAsync(Guid queueItemId, int reviewerId, CancellationToken ct)
{
    var item = await _db.ModerationQueues
        .Include(q => q.Tile)
        .FirstOrDefaultAsync(q => q.Id == queueItemId, ct);

    if (item is null || item.ReviewedAt is not null) return false;

    item.ReviewedAt = DateTimeOffset.UtcNow;
    item.ReviewerId = reviewerId;
    item.Decision   = "approved";
    item.Tile.IsModerated = true;

    await _db.SaveChangesAsync(ct);
    return true;
}
```

**Use case:** Human moderator overrides OpenAI's flagging decision.

### Admin Rejection
**Endpoint:** `POST /admin/moderation/{queueItemId}/reject`  
**Service:** `ModerationService.RejectAsync`

```csharp
public async Task<bool> RejectAsync(Guid queueItemId, int reviewerId, string reason, CancellationToken ct)
{
    var item = await _db.ModerationQueues
        .Include(q => q.Tile)
        .FirstOrDefaultAsync(q => q.Id == queueItemId, ct);

    if (item is null || item.ReviewedAt is not null) return false;

    item.ReviewedAt   = DateTimeOffset.UtcNow;
    item.ReviewerId   = reviewerId;
    item.Decision     = "rejected";
    item.RejectReason = reason[..Math.Min(reason.Length, 200)];

    item.Tile.IsExpired = true;  // soft-delete

    await _db.SaveChangesAsync(ct);
    return true;
}
```

**Use case:** Human moderator rejects content OpenAI approved.

### Pending Queue
**Endpoint:** `GET /admin/moderation/pending?limit=50`  
**Service:** `ModerationService.GetPendingAsync`

```csharp
return await _db.ModerationQueues
    .AsNoTracking()
    .Include(q => q.Tile)
    .Where(q => q.ReviewedAt == null && !q.Tile.IsExpired)
    .OrderBy(q => q.QueuedAt)
    .Take(limit)
    .Select(q => new ModerationQueueDto(
        q.Id, q.TileId, q.UserId,
        q.Tile.ContentType, q.Tile.ContentText, q.Tile.MediaUrl,
        q.QueuedAt))
    .ToListAsync(ct);
```

**Frontend (planned):** Admin dashboard at `/admin/moderation` to manually review flagged tiles.

---

## Image & Video Moderation

### Image Moderation
**Service:** `ModerationService.ModerateImageAsync`

```csharp
public async Task<ModerationImageResult> ModerateImageAsync(int userId, string imageUrl, CancellationToken ct)
{
    var body = JsonSerializer.Serialize(new
    {
        model = "omni-moderation-latest",
        input = new[] { new { type = "image_url", image_url = new { url = imageUrl } } }
    });
    using var content = new StringContent(body, Encoding.UTF8, "application/json");

    var response = await _http.PostAsync("https://api.openai.com/v1/moderations", content, ct);
    if (!response.IsSuccessStatusCode)
        return ModerationImageResult.ESCALATED;

    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    var flagged = doc.RootElement
        .GetProperty("results")[0]
        .GetProperty("flagged")
        .GetBoolean();

    return flagged ? ModerationImageResult.AUTO_REJECTED : ModerationImageResult.APPROVED;
}
```

**Return values:**
- `APPROVED` — safe to publish
- `AUTO_REJECTED` — OpenAI flagged, soft-delete tile
- `ESCALATED` — API error, queue for human review

**Use case:** Called by `ModerationWorker` for photo/video tiles. Requires image download from Azure Blob → slower than text moderation.

### Video Caption Extraction (Planned)
**Phase:** Not yet implemented  
**Design:** Video tiles → Azure Video Indexer API → extract captions → text moderation + embedding generation

---

## User Reporting (Planned)

### TileReport Entity
**Table:** `tile_reports`  
**Schema:**
```csharp
public class TileReport
{
    public Guid Id { get; set; }
    public Guid TileId { get; set; }
    public int ReporterId { get; set; }
    public string Reason { get; set; }  // 'spam' | 'offensive' | 'misleading' | 'other'
    public DateTimeOffset ReportedAt { get; set; }
}
```

**Frontend (planned):** Report button in tile drawer → `POST /tiles/{tileId}/report`

**Auto-threshold:** 3+ reports → auto-queue for manual review

---

## Design Rationale

### Why Auto-Approve Text in Production?
**Speed.** OpenAI Moderation API is fast (200-400ms), and text tiles are the majority of content. Inline moderation (vs. queuing) gives immediate feedback to users.

**Risk.** Text-only moderation is less risky than images (no CSAM risk, lower false-negative rate for hate speech).

### Why Queue Media Tiles?
**Latency.** Vision moderation requires:
1. Download image from Azure Blob (100-300ms)
2. OpenAI Vision API call (500-1000ms)

Queuing decouples this 1-2s latency from the user-facing POST.

### Why Fail-Open?
**Availability over safety.** If OpenAI API is down, blocking all tile creation harms UX more than occasional policy violations. Fail-open ensures the app remains functional during OpenAI outages.

**Mitigation:** User reporting + manual review backstop.

### Why 5-Minute Worker Interval?
**Balance.** Too frequent = wasted API calls (queue is usually empty). Too slow = delayed moderation (tiles stay hidden).

**Acceptable delay:** Media tiles take 5-15 minutes to appear in feeds. Given 48h TTL, this is <1% of tile lifetime.

---

## Metrics & Observability

### Serilog Logs
**Service:** `ModerationService`

```csharp
_logger.LogInformation("[Moderation] Queued tile {TileId} for user {UserId}", tileId, userId);
_logger.LogInformation("[Moderation] {Count} tiles pending AI review", queue.Count);
_logger.LogWarning("[Moderation] Tile {TileId} flagged by OpenAI moderation", item.TileId);
_logger.LogDebug("[Moderation] Tile {TileId} approved by OpenAI moderation", item.TileId);
```

**Correlation ID:** Every log line carries `{CorrelationId}` from `CorrelationIdMiddleware`.

### Key Metrics (Planned)
- **Moderation latency (p50/p95)** — time from queue-add to reviewed
- **Approval rate** — `approved / (approved + rejected)`
- **False positive rate** — manual overrides of OpenAI flags
- **API error rate** — OpenAI API failures

---

## Next Steps

- **Video caption moderation** — Extract captions from video tiles → text moderation
- **User reporting UI** — Frontend report button + admin review dashboard
- **Proactive detection** — Flag users with >3 rejected tiles in 7 days → shadow-ban
- **Appeal flow** — Users can request manual review of auto-rejected tiles

---

## Related Docs

- **[README.md](./README.md)** — Commons overview
- **[tiles.md](./tiles.md)** — Tile entity and lifecycle
- **[backend.md](./backend.md)** — ModerationService implementation
- **[api.md](./api.md)** — Admin moderation endpoints
