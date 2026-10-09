# User Reporting System

**Last Updated:** 2026-10-07  
**Status:** ❌ **NOT IMPLEMENTED** (schema exists, endpoints missing)

---

## Overview

The user reporting system allows users to flag inappropriate content:

- **Tiles** (Commons posts)
- **Messages** (chat)
- **Profiles** (inappropriate photos, fake profiles)

**Current state:**
- `TileReport` entity exists in DB
- No user-facing endpoints yet
- Admin can view reports via `/admin/tiles/{tileId}/reports`

---

## Database Schema

### TileReports

```sql
CREATE TABLE tile_reports (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tile_id     uuid NOT NULL REFERENCES tiles(id),
    reporter_id int NOT NULL,
    reason      varchar(100) NOT NULL,
    reported_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_tile_reports_tile ON tile_reports (tile_id);
```

**Columns:**
- `id` — Report UUID
- `tile_id` — Reported tile
- `reporter_id` — User who reported
- `reason` — Freeform text (max 100 chars)
- `reported_at` — Timestamp

---

## Planned Endpoints (Not Yet Built)

### 1. Report Tile

```http
POST /tiles/{tileId}/report
Authorization: Bearer <jwt>
Content-Type: application/json

{
  "reason": "inappropriate_content"
}
```

**Response (201 Created):**
```json
{
  "reportId": "a1b2c3d4-...",
  "status": "SUBMITTED"
}
```

**Errors:**
- `404` — Tile not found
- `400` — Reason missing or > 100 chars
- `409` — Already reported by this user (prevent spam)

### 2. Report User

```http
POST /users/{userId}/report
Authorization: Bearer <jwt>
Content-Type: application/json

{
  "reason": "fake_profile",
  "context": "Duplicate photos across accounts"
}
```

**Response (201 Created):**
```json
{
  "reportId": "b2c3d4e5-...",
  "status": "SUBMITTED"
}
```

### 3. Report Message

```http
POST /chats/{threadId}/messages/{messageId}/report
Authorization: Bearer <jwt>
Content-Type: application/json

{
  "reason": "harassment"
}
```

**Response (201 Created):**
```json
{
  "reportId": "c3d4e5f6-...",
  "status": "SUBMITTED"
}
```

---

## Planned Report Reasons

### Tiles
- `inappropriate_content` — Offensive, sexual, violent
- `spam` — Promotional, low-quality
- `misinformation` — False claims
- `other` — Freeform text

### Profiles
- `fake_profile` — Catfish, stolen photos
- `inappropriate_photos` — Violates content policy
- `underage` — User claims to be <18
- `other`

### Messages
- `harassment` — Bullying, threats
- `spam` — Unsolicited links, promotions
- `sexual_content` — Unwanted sexual messages
- `other`

---

## Anti-Spam Protection

### Rate Limiting
- **Max 5 reports/day per user**
- Returns `429 Too Many Requests` if exceeded

### Duplicate Prevention
```csharp
var alreadyReported = await _db.TileReports
    .AnyAsync(r => r.TileId == tileId && r.ReporterId == userId, ct);

if (alreadyReported)
    return Results.Conflict(new { error = "ALREADY_REPORTED" });
```

### Abuse Detection
- Users who file >20 reports/month flagged for review
- Repeated false reports → account warning → temp ban

---

## Implementation Plan

### Phase 1: Tile Reporting (MVP)

**Backend:**
1. Create `TileEndpoints.MapTileReportEndpoints()`
2. Add `POST /tiles/{tileId}/report` endpoint
3. Validate: tile exists, not expired, not already reported by user
4. Insert into `tile_reports`
5. If report count > 3 → auto-expire tile + queue for review

**Frontend:**
1. Add "Report" button to tile overflow menu (⋮)
2. Show modal: "Why are you reporting this?"
   - Radio buttons: Inappropriate / Spam / Other
   - Text input (optional, 200 chars max)
3. Submit → toast confirmation: "Thanks for reporting. We'll review this."

### Phase 2: Message Reporting

**Backend:**
1. Create `message_reports` table (same schema as `tile_reports`)
2. Add `POST /chats/{threadId}/messages/{messageId}/report`
3. If user reports 2+ messages from same person → auto-block prompt

**Frontend:**
1. Long-press message → "Report" option
2. Same modal flow as tiles

### Phase 3: Profile Reporting

**Backend:**
1. Create `user_reports` table
2. Add `POST /users/{userId}/report`
3. Trigger catfish detection check on `fake_profile` reports

**Frontend:**
1. Add "Report User" to profile overflow menu
2. Modal: Reason + optional context

---

## Admin Review Workflow

### Current Implementation

**Endpoint:** `GET /admin/tiles/{tileId}/reports`

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

**Response:**
```json
{
  "count": 3,
  "reports": [
    {
      "id": "a1b2c3d4-...",
      "tileId": "tile-123",
      "reporterId": 42,
      "reason": "inappropriate_content",
      "reportedAt": "2026-10-07T14:23:00Z"
    },
    ...
  ]
}
```

### Planned Review UI

**Admin dashboard (`/admin/moderation/reports`):**
- List tiles sorted by report count (desc)
- Click tile → view all reports + tile content
- Actions: Approve (dismiss reports) | Remove Tile | Ban User

---

## Auto-Moderation Rules

### Tile Auto-Expiry
```csharp
// After inserting report:
var reportCount = await _db.TileReports
    .CountAsync(r => r.TileId == tileId, ct);

if (reportCount >= 3)
{
    var tile = await _db.Tiles.FirstOrDefaultAsync(t => t.Id == tileId, ct);
    if (tile != null)
    {
        tile.IsExpired = true;
        await _db.SaveChangesAsync(ct);
        _logger.LogWarning("[Reports] Auto-expired tile {TileId} (3+ reports)", tileId);
    }
}
```

### User Trust Impact
```csharp
// If user's tiles get 3+ reports in 7 days → trust penalty
var recentReports = await _db.TileReports
    .Where(r => r.Tile.UserId == userId && r.ReportedAt >= DateTimeOffset.UtcNow.AddDays(-7))
    .CountAsync(ct);

if (recentReports >= 3)
{
    await _trust.FlagAsync(userId, "REPEATED_VIOLATIONS", 0.15f, ct);
}
```

---

## Notification Flow

### Reporter
**On submit:**
```
✓ Thanks for reporting. We'll review this and take action if needed.
```

**No follow-up notifications** (privacy — don't reveal action taken).

### Reported User
**If content removed:**
```
One of your posts was removed for violating our Community Guidelines. 
Repeated violations may result in account restrictions.
[View Guidelines]
```

**No notification** if report dismissed (avoid harassment via false reports).

---

## Privacy & Anonymity

### Reporter Anonymity
- Reported user **never sees** who reported them
- Admin dashboard shows reporter IDs (for abuse detection)

### Reporter Abuse
- If user files 5+ false reports → warning email
- If user files 10+ false reports → 7-day report ban
- Tracked via `reporter_trust_score` (future)

---

## Testing Plan

### Unit Tests
```csharp
[Fact]
public async Task Report_Tile_Success()
{
    var response = await _client.PostAsJsonAsync($"/tiles/{tileId}/report", 
        new { reason = "inappropriate_content" });
    
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    
    var report = await _db.TileReports.FirstOrDefaultAsync(r => r.TileId == tileId);
    Assert.NotNull(report);
    Assert.Equal(userId, report.ReporterId);
}

[Fact]
public async Task Report_Tile_DuplicateFails()
{
    // First report succeeds
    await _client.PostAsJsonAsync($"/tiles/{tileId}/report", new { reason = "spam" });
    
    // Second report fails
    var response = await _client.PostAsJsonAsync($"/tiles/{tileId}/report", new { reason = "spam" });
    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
}

[Fact]
public async Task Report_Tile_AutoExpiresAt3Reports()
{
    var tileId = await CreateTileAsync();
    
    // 3 different users report the same tile
    for (int i = 1; i <= 3; i++)
    {
        await ReportAsUserAsync(i, tileId, "inappropriate_content");
    }
    
    var tile = await _db.Tiles.FindAsync(tileId);
    Assert.True(tile.IsExpired);
}
```

### Integration Tests
1. Report tile → verify `tile_reports` insert
2. Report 3x → verify tile auto-expires
3. Report rate limit → 6th report/day returns 429
4. Report expired tile → 404 (can't report deleted content)

---

## Metrics & Analytics

### Track:
1. **Report volume** — reports/day, breakdown by reason
2. **False positive rate** — % reports dismissed by admins
3. **Auto-expiry rate** — % tiles removed via 3-report rule
4. **Repeat offenders** — users with 3+ tiles removed in 30 days
5. **Reporter abuse** — users filing 10+ reports/month

### Alerts:
- Spike in reports (>2σ above baseline) → possible brigading
- High false positive rate (>60%) → adjust auto-expiry threshold

---

## Production Checklist

- [ ] Implement `POST /tiles/{tileId}/report` endpoint
- [ ] Add duplicate prevention (user + tile)
- [ ] Add rate limiting (5 reports/day)
- [ ] Wire up auto-expiry at 3 reports
- [ ] Add trust penalty for reported users
- [ ] Build frontend "Report" button
- [ ] Add admin reports dashboard
- [ ] Write unit + integration tests
- [ ] Monitor report volume (alerts)
- [ ] Document community guidelines

---

## Future Enhancements

1. **Machine learning flagging**  
   - Train model on past reports → auto-flag suspicious content

2. **Reporter reputation**  
   - High-accuracy reporters get priority review

3. **Appeal system**  
   - Users can contest removed content

4. **Collaborative filtering**  
   - Users who block/report similar content → personalized feed filtering

---

## Related Documentation

- [Moderation Overview](./README.md)
- [AI Moderation](./ai-moderation.md)
- [Review Queue](./review-queue.md)
- [Block System](./block-system.md)
- [Trust System](../trust/README.md)
