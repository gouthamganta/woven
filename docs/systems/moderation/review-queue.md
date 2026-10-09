# Manual Review Queue

**Last Updated:** 2026-10-07  
**Status:** ✅ Backend complete, ❌ Admin UI not built

---

## Overview

The manual review queue allows admin moderators to:

1. **Review AI-escalated content** — content flagged by OpenAI but not auto-rejected
2. **Process user reports** — tiles reported 1-2 times (below auto-expiry threshold)
3. **Investigate trust flags** — users flagged by catfish detection, velocity checks
4. **Make final decisions** — approve or reject with reason

**Flow:**
```
Content Created → AI Moderation → Flagged? → Review Queue → Admin Decision
                                    ↓
                                  Clean? → Auto-Approved
```

---

## Database Schema

### ModerationQueue

```sql
CREATE TABLE moderation_queue (
    id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tile_id      uuid NOT NULL REFERENCES tiles(id),
    user_id      int NOT NULL,
    queued_at    timestamptz NOT NULL DEFAULT now(),
    reviewed_at  timestamptz,
    reviewer_id  int,
    decision     varchar(20),      -- 'approved' | 'rejected'
    reject_reason varchar(200)
);

CREATE INDEX idx_moderation_queue_pending 
    ON moderation_queue (queued_at) 
    WHERE reviewed_at IS NULL;
```

**States:**
- **Pending:** `reviewed_at IS NULL`
- **Reviewed:** `reviewed_at NOT NULL`, `decision` set

---

## Admin Endpoints

### 1. Get Pending Queue

```http
GET /admin/moderation/queue
Authorization: Bearer <admin-jwt>
```

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
      "contentText": "Check out this amazing deal! [link]",
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

**Implementation:**

```csharp
group.MapGet("/moderation/queue", async (
    IModerationService moderation,
    CancellationToken ct) =>
{
    var items = await moderation.GetPendingAsync(50, ct);
    return Results.Ok(new { count = items.Count, items });
});
```

---

### 2. Approve Item

```http
POST /admin/moderation/{itemId}/approve
Authorization: Bearer <admin-jwt>
```

**Response (200 OK):**
```json
{
  "approved": "a1b2c3d4-..."
}
```

**Response (404 Not Found):**
```json
{
  "error": "QUEUE_ITEM_NOT_FOUND_OR_ALREADY_REVIEWED"
}
```

**Implementation:**

```csharp
group.MapPost("/moderation/{itemId:guid}/approve", async (
    Guid itemId,
    IModerationService moderation,
    HttpContext http,
    CancellationToken ct) =>
{
    var reviewerId = GetUserId(http.User);
    var ok = await moderation.ApproveAsync(itemId, reviewerId, ct);
    return ok
        ? Results.Ok(new { approved = itemId })
        : Results.NotFound(new { error = "QUEUE_ITEM_NOT_FOUND_OR_ALREADY_REVIEWED" });
});
```

**Effect:**
- Sets `reviewed_at = now()`, `reviewer_id`, `decision = 'approved'`
- Sets `tile.IsModerated = true`
- Tile becomes visible in Commons feed

---

### 3. Reject Item

```http
POST /admin/moderation/{itemId}/reject
Authorization: Bearer <admin-jwt>
Content-Type: application/json

{
  "reason": "spam"
}
```

**Response (200 OK):**
```json
{
  "rejected": "a1b2c3d4-..."
}
```

**Response (400 Bad Request):**
```json
{
  "error": "REASON_REQUIRED"
}
```

**Implementation:**

```csharp
group.MapPost("/moderation/{itemId:guid}/reject", async (
    Guid itemId,
    RejectRequest req,
    IModerationService moderation,
    HttpContext http,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Reason))
        return Results.BadRequest(new { error = "REASON_REQUIRED" });

    var reviewerId = GetUserId(http.User);
    var ok = await moderation.RejectAsync(itemId, reviewerId, req.Reason, ct);
    return ok
        ? Results.Ok(new { rejected = itemId })
        : Results.NotFound(new { error = "QUEUE_ITEM_NOT_FOUND_OR_ALREADY_REVIEWED" });
});
```

**Effect:**
- Sets `reviewed_at = now()`, `reviewer_id`, `decision = 'rejected'`
- Sets `tile.IsExpired = true` (soft delete, hidden from feed)
- Stores `reject_reason` (max 200 chars)

---

### 4. Get Tile Reports

```http
GET /admin/tiles/{tileId}/reports
Authorization: Bearer <admin-jwt>
```

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

**Implementation:**

```csharp
group.MapGet("/tiles/{tileId:guid}/reports", async (
    Guid tileId,
    IModerationService moderation,
    CancellationToken ct) =>
{
    var reports = await moderation.GetReportsAsync(tileId, ct);
    return Results.Ok(new { count = reports.Count, reports });
});
```

**Use case:** When reviewing a queued tile, admin can see if users also reported it.

---

## Worker (Background Processing)

### ModerationWorker

**File:** `Services/Moderation/ModerationWorker.cs`

**Schedule:** Every 5 minutes  
**Lock:** Redis-based distributed lock (`lock:moderation-pass`)

**Flow:**
1. Acquire lock (4-min expiry)
2. Fetch pending items (`reviewed_at IS NULL`, limit 50)
3. For each item:
   - Call `CheckOpenAiModerationAsync(tile.ContentText)`
   - If flagged → `decision = 'rejected'`, `tile.IsExpired = true`
   - If clean → `decision = 'approved'`, `tile.IsModerated = true`
4. Save changes
5. Release lock

**Dev mode (moderation disabled):**
- Auto-approves all pending items in bulk (no OpenAI calls)

---

## Admin Dashboard (Planned UI)

### Layout

```
┌────────────────────────────────────────────────────────────┐
│  Moderation Queue (12 pending)                             │
├────────────────────────────────────────────────────────────┤
│                                                            │
│  [Filter: All | Text | Photo | Video]  [Sort: Oldest ▼]  │
│                                                            │
│  ┌──────────────────────────────────────────────────────┐ │
│  │ User #42 • Text • 2h ago                             │ │
│  │ "Check out this amazing deal! [link]"                │ │
│  │ [View Profile] [Approve] [Reject ▼]                  │ │
│  └──────────────────────────────────────────────────────┘ │
│                                                            │
│  ┌──────────────────────────────────────────────────────┐ │
│  │ User #73 • Photo • 3h ago • 2 reports                │ │
│  │ [Show Image]                                          │ │
│  │ "Beach vibes 🌊"                                      │ │
│  │ Reports: inappropriate_content, spam                  │ │
│  │ [View Profile] [Approve] [Reject ▼]                  │ │
│  └──────────────────────────────────────────────────────┘ │
│                                                            │
└────────────────────────────────────────────────────────────┘
```

### Rejection Dropdown

```
Reject ▼
├─ Spam
├─ Inappropriate content
├─ Fake/misleading
├─ Harassment
├─ Other (specify)
```

---

## Authorization

### Admin Role Requirement

**All moderation endpoints require `Admin` policy:**

```csharp
var group = app.MapGroup("/admin").RequireAuthorization("Admin");
```

**Policy definition (Program.cs):**
```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => 
        policy.RequireClaim("role", "admin"));
});
```

**Admin users:** Set `role = 'admin'` claim in JWT during login.

---

## Metrics & Analytics

### Queue Health

**Track:**
- **Queue depth** — # pending items
- **Avg time to review** — `reviewed_at - queued_at`
- **Approval rate** — % approved vs rejected
- **Reviewer activity** — decisions/hour per admin

**Alerts:**
- Queue depth >100 → page on-call moderator
- Avg review time >24h → hire more mods

### Decision Quality

**Track:**
- **User appeals** — users contest rejections
- **Re-queued items** — approved tile later reported + removed
- **Inter-rater reliability** — do different mods agree on same content?

---

## Testing Plan

### Unit Tests

```csharp
[Fact]
public async Task GetPendingQueue_ReturnsUnreviewedItems()
{
    await EnqueueForReviewAsync(tileId: "tile-1", userId: 1);
    await EnqueueForReviewAsync(tileId: "tile-2", userId: 2);
    await ApproveAsync("tile-1"); // Mark tile-1 as reviewed

    var response = await _adminClient.GetAsync("/admin/moderation/queue");
    var data = await response.Content.ReadFromJsonAsync<QueueResponse>();

    Assert.Equal(1, data.Count); // Only tile-2 pending
    Assert.Equal("tile-2", data.Items[0].TileId);
}

[Fact]
public async Task ApproveItem_MarksReviewed()
{
    var itemId = await EnqueueForReviewAsync(tileId: "tile-1", userId: 1);

    var response = await _adminClient.PostAsync($"/admin/moderation/{itemId}/approve", null);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var queueItem = await _db.ModerationQueues.FindAsync(itemId);
    Assert.NotNull(queueItem.ReviewedAt);
    Assert.Equal("approved", queueItem.Decision);

    var tile = await _db.Tiles.FirstAsync(t => t.Id == "tile-1");
    Assert.True(tile.IsModerated);
}

[Fact]
public async Task RejectItem_ExpiresTile()
{
    var itemId = await EnqueueForReviewAsync(tileId: "tile-1", userId: 1);

    var response = await _adminClient.PostAsJsonAsync(
        $"/admin/moderation/{itemId}/reject", 
        new { reason = "spam" });
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var tile = await _db.Tiles.FirstAsync(t => t.Id == "tile-1");
    Assert.True(tile.IsExpired);

    var queueItem = await _db.ModerationQueues.FindAsync(itemId);
    Assert.Equal("spam", queueItem.RejectReason);
}
```

### Integration Tests

1. Worker processes pending items → OpenAI flagged items rejected
2. Admin approves item → tile visible in Commons
3. Admin rejects item → tile expired, hidden from feed
4. Non-admin user calls endpoint → 403 Forbidden

---

## Operational Runbook

### Daily Tasks

**Morning (9am):**
1. Check queue depth (`/admin/moderation/queue`)
2. Review items flagged overnight
3. Prioritize: user-reported items first

**Afternoon (3pm):**
1. Second pass on queue
2. Escalate edge cases to team Slack

**End of day (6pm):**
1. Clear remaining queue (target: <10 pending)
2. Log any recurring violators

### Escalation Triggers

**Immediate escalation (ping @safety-team):**
- CSAM (child safety) content
- Credible threats of violence
- Coordinated spam/harassment campaign

**Weekly review:**
- Users with 3+ rejected tiles in 7 days
- Tiles with 5+ reports (investigate brigading)

---

## Production Checklist

- [x] `GET /admin/moderation/queue` endpoint
- [x] `POST /admin/moderation/{itemId}/approve` endpoint
- [x] `POST /admin/moderation/{itemId}/reject` endpoint
- [x] `GET /admin/tiles/{tileId}/reports` endpoint
- [x] Admin authorization policy
- [x] ModerationWorker background service
- [ ] Build admin dashboard UI
- [ ] Train moderation team
- [ ] Set up alerting (queue depth >100)
- [ ] Document escalation procedures
- [ ] Weekly metrics dashboard (approval rate, avg review time)

---

## Future Enhancements

1. **Batch actions**  
   - Select multiple items → approve/reject all

2. **Review notes**  
   - Admins leave notes for each other: "Borderline, needs second opinion"

3. **Auto-routing**  
   - Easy items (low confidence flags) → junior mods
   - Hard items (high severity) → senior mods

4. **ML-assisted prioritization**  
   - Train model on past decisions → surface high-risk items first

5. **Audit log**  
   - Track all admin actions (who approved/rejected what, when)

---

## Related Documentation

- [Moderation Overview](./README.md)
- [AI Moderation](./ai-moderation.md)
- [User Reports](./user-reports.md)
- [API Reference](./api.md)
- [Trust System](../trust/README.md)
