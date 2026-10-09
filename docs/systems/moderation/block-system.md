# Block System

**Last Updated:** 2026-10-07  
**Status:** ✅ Production

---

## Overview

The block system allows users to permanently remove another user from all interactions:

- **Blocks future matches** — blocked user never appears in Deck/Drawn
- **Closes active match** — if balloon is active, closes with `BLOCK` reason
- **Hides all content** — blocked user's tiles hidden from Commons
- **Prevents messaging** — no new messages can be sent
- **One-way action** — blocker decides, blocked user has no input

**Key principle:** No notifications. Blocking is invisible to the blocked user.

---

## Database Schema

### Blocks Table

```sql
CREATE TABLE blocks (
    blocker_id int NOT NULL,
    blocked_id int NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (blocker_id, blocked_id)
);

CREATE INDEX idx_blocks_blocker ON blocks (blocker_id);
CREATE INDEX idx_blocks_blocked ON blocks (blocked_id);
```

**Columns:**
- `blocker_id` — User who initiated the block
- `blocked_id` — User who was blocked
- `created_at` — Timestamp

**Constraints:**
- Composite PK prevents duplicate blocks
- No FK cascade (blocks persist even if users deleted)

---

## Blocking Flow

### User Initiates Block

**Endpoint:** `POST /matches/{matchId}/block`

```http
POST /matches/abc123/block
Authorization: Bearer <jwt>
```

**Response (200 OK):**
```json
{
  "blocked": true,
  "matchId": "abc123",
  "closedAt": "2026-10-07T14:23:00Z"
}
```

**Implementation:**

```csharp
group.MapPost("/{matchId:guid}/block", async (
    Guid matchId, 
    WovenDbContext db, 
    HttpContext http, 
    CancellationToken ct) =>
{
    var me = GetUserId(http.User);

    var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == matchId, ct);
    if (match == null) return Results.NotFound(new { error = "MATCH_NOT_FOUND" });
    if (!IsParticipant(match, me)) return Results.Forbid();

    var otherId = match.UserAId == me ? match.UserBId : match.UserAId;
    var now = MomentsRules.NowUtc();

    // Insert block record (idempotent)
    var alreadyBlocked = await db.Blocks.AnyAsync(b => b.BlockerId == me && b.BlockedId == otherId, ct);
    if (!alreadyBlocked)
    {
        db.Blocks.Add(new Block
        {
            BlockerId = me,
            BlockedId = otherId,
            CreatedAt = now
        });
    }

    // Close match if active
    if (match.BalloonState == BalloonState.ACTIVE)
    {
        match.BalloonState = BalloonState.CLOSED;
        match.ClosedReason = ClosedReason.BLOCK;
        match.ClosedAt = now;
    }

    await db.SaveChangesAsync(ct);

    // Track outcome (non-blocking)
    var outcomeService = http.RequestServices.GetRequiredService<IMatchOutcomeService>();
    _ = Task.Run(async () =>
    {
        try
        {
            await outcomeService.RecordBlockAsync(me, otherId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Matches] Failed to record block outcome");
        }
    });

    return Results.Ok(new { blocked = true, matchId, closedAt = match.ClosedAt });
})
.WithName("MatchBlock");
```

---

## Block Effects

### 1. Matchmaking Exclusion

**CandidatePoolService.cs:**
```csharp
var blockedIds = await db.Blocks
    .Where(b => b.BlockerId == userId || b.BlockedId == userId)
    .Select(b => b.BlockerId == userId ? b.BlockedId : b.BlockerId)
    .Distinct()
    .ToListAsync(ct);

var pool = await db.Users
    .Where(u => u.Id != userId && !blockedIds.Contains(u.Id))
    // ... rest of filters
    .ToListAsync(ct);
```

**Effect:** Blocked users never appear in daily deck or Drawn tab.

### 2. Match Closure

**If match is `ACTIVE` at block time:**
```csharp
match.BalloonState = BalloonState.CLOSED;
match.ClosedReason = ClosedReason.BLOCK;
match.ClosedAt = DateTimeOffset.UtcNow;
```

**If match already `CLOSED`:**
- No-op (block record created, match state unchanged)

### 3. Chat Suppression

**Frontend guards:**
```typescript
// chats-list.component.ts
if (thread.isBlocked) {
  return; // Don't render thread
}
```

**Backend:** No explicit chat filtering (blocked matches auto-excluded from chat list queries).

### 4. Commons Feed Filtering

**Planned (not yet implemented):**
```csharp
var blockedIds = await GetBlockedIdsAsync(userId, ct);

var feed = await db.Tiles
    .Where(t => !t.IsExpired && !blockedIds.Contains(t.UserId))
    .OrderByDescending(t => t.CreatedAt)
    .ToListAsync(ct);
```

---

## Unblocking

### Current Implementation
**Not supported.** Blocks are permanent.

### Planned Implementation

**Endpoint:** `DELETE /blocks/{userId}`

```http
DELETE /blocks/42
Authorization: Bearer <jwt>
```

**Response (200 OK):**
```json
{
  "unblocked": true,
  "userId": 42
}
```

**Implementation:**
```csharp
group.MapDelete("/blocks/{blockedUserId:int}", async (
    int blockedUserId,
    WovenDbContext db,
    HttpContext http,
    CancellationToken ct) =>
{
    var me = GetUserId(http.User);
    
    var block = await db.Blocks
        .FirstOrDefaultAsync(b => b.BlockerId == me && b.BlockedId == blockedUserId, ct);
    
    if (block == null)
        return Results.NotFound(new { error = "BLOCK_NOT_FOUND" });
    
    db.Blocks.Remove(block);
    await db.SaveChangesAsync(ct);
    
    return Results.Ok(new { unblocked = true, userId = blockedUserId });
});
```

**Note:** Unblocking does **not** reopen closed matches. Old matches stay `CLOSED`.

---

## Blocked Users List

### Endpoint (Planned)

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

**Implementation:**
```csharp
group.MapGet("/blocks", async (
    WovenDbContext db,
    HttpContext http,
    CancellationToken ct) =>
{
    var me = GetUserId(http.User);
    
    var blocked = await db.Blocks
        .Include(b => b.BlockedUser)
        .Where(b => b.BlockerId == me)
        .OrderByDescending(b => b.CreatedAt)
        .Select(b => new
        {
            userId = b.BlockedId,
            firstName = b.BlockedUser.FirstName,
            blockedAt = b.CreatedAt
        })
        .ToListAsync(ct);
    
    return Results.Ok(new { blocked });
});
```

---

## Frontend Integration

### Current UI Locations

1. **Match profile** (not yet wired)
   - Overflow menu → "Block User"
   - Confirm modal: "Block [FirstName]? They won't be able to see or match with you."
   - On confirm → `POST /matches/{matchId}/block`

2. **Chat thread** (not yet wired)
   - Overflow menu → "Block User"
   - Same confirm flow

3. **Settings → Blocked Users** (not yet built)
   - List of blocked users
   - Unblock button per user

### Planned Dialogs

**Block confirmation:**
```typescript
async confirmBlock(matchId: string, firstName: string) {
  const result = await this.alertCtrl.create({
    header: `Block ${firstName}?`,
    message: 'They won\'t be able to see or match with you. This match will be closed.',
    buttons: [
      { text: 'Cancel', role: 'cancel' },
      {
        text: 'Block',
        role: 'destructive',
        handler: async () => {
          await this.blockUser(matchId);
        }
      }
    ]
  });
  await result.present();
}

async blockUser(matchId: string) {
  await this.http.post(`/matches/${matchId}/block`, {}).toPromise();
  await this.router.navigate(['/moments']); // Redirect away
  await this.toastCtrl.create({
    message: 'User blocked',
    duration: 2000
  }).then(t => t.present());
}
```

---

## Privacy & Notifications

### Blocked User Experience

**What they see:**
- Match disappears from their list (no notification)
- Messages fail silently (or show "This match has ended")
- They never appear in blocker's deck again

**What they DON'T see:**
- No "You have been blocked" notification
- No indication that blocking occurred
- Match just looks like it naturally ended

**Rationale:** Prevent retaliation, harassment via alt accounts.

### Blocker Experience

**What they see:**
- Match closes immediately
- "User blocked" confirmation toast
- Blocked user hidden from all feeds

---

## Analytics & Safety

### Metrics to Track

1. **Block rate** — % of matches that end in blocks
2. **Repeat blockers** — users blocking >5 people/month (possible abuse)
3. **Frequently blocked users** — users blocked by >3 people (red flag)
4. **Block timing** — avg messages before block (quality signal)

### Safety Triggers

**High block rate on a user → trust penalty:**
```csharp
var timesBlocked = await db.Blocks
    .CountAsync(b => b.BlockedId == userId, ct);

if (timesBlocked >= 3)
{
    await _trust.FlagAsync(userId, "FREQUENTLY_BLOCKED", 0.10f, ct);
    _logger.LogWarning("[Blocks] User {UserId} blocked by {Count} people", userId, timesBlocked);
}
```

**User blocking >10 people/month → investigate:**
```csharp
var recentBlocks = await db.Blocks
    .CountAsync(b => b.BlockerId == userId && b.CreatedAt >= DateTimeOffset.UtcNow.AddMonths(-1), ct);

if (recentBlocks >= 10)
{
    _logger.LogWarning("[Blocks] User {UserId} has blocked {Count} people this month", userId, recentBlocks);
    // Flag for admin review (possible abuse)
}
```

---

## Outcome Tracking

### MatchOutcomeService Integration

**File:** `Services/Matchmaking/MatchOutcomeService.cs`

```csharp
public async Task RecordBlockAsync(int blockerId, int blockedId, CancellationToken ct = default)
{
    var outcome = new MatchOutcome
    {
        UserId = blockerId,
        PartnerId = blockedId,
        OutcomeType = "BLOCK",
        OutcomeAt = DateTimeOffset.UtcNow,
        MetaJson = JsonSerializer.Serialize(new { blockedBy = blockerId })
    };
    
    _db.MatchOutcomes.Add(outcome);
    await _db.SaveChangesAsync(ct);
    
    _logger.LogInformation("[Outcomes] Recorded BLOCK: {BlockerId} → {BlockedId}", blockerId, blockedId);
}
```

**Purpose:** Feed into ECHO learning (blocks = strong negative signal).

---

## Testing

### Unit Tests

```csharp
[Fact]
public async Task Block_User_Success()
{
    var matchId = await CreateActiveMatchAsync(userA: 1, userB: 2);
    
    var response = await _client.PostAsJsonAsync($"/matches/{matchId}/block", new { });
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    
    var block = await _db.Blocks.FirstOrDefaultAsync(b => b.BlockerId == 1 && b.BlockedId == 2);
    Assert.NotNull(block);
    
    var match = await _db.Matches.FindAsync(matchId);
    Assert.Equal(BalloonState.CLOSED, match.BalloonState);
    Assert.Equal(ClosedReason.BLOCK, match.ClosedReason);
}

[Fact]
public async Task Block_Idempotent()
{
    var matchId = await CreateActiveMatchAsync(userA: 1, userB: 2);
    
    // Block twice
    await _client.PostAsJsonAsync($"/matches/{matchId}/block", new { });
    var response = await _client.PostAsJsonAsync($"/matches/{matchId}/block", new { });
    
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    
    var blockCount = await _db.Blocks.CountAsync(b => b.BlockerId == 1 && b.BlockedId == 2);
    Assert.Equal(1, blockCount); // Only one record
}

[Fact]
public async Task Blocked_User_Not_In_Deck()
{
    await BlockUserAsync(blocker: 1, blocked: 2);
    
    var deck = await BuildDeckAsync(userId: 1);
    Assert.DoesNotContain(deck, m => m.CandidateId == 2);
}
```

### Integration Tests

1. Block user → verify `blocks` insert + match closure
2. Blocked user excluded from deck generation
3. Block idempotency (double block = single record)
4. Unblock → verify `blocks` delete (when implemented)
5. Blocked users list → verify correct filtering

---

## Production Checklist

- [x] `POST /matches/{matchId}/block` endpoint
- [x] Match closure on block
- [x] Matchmaking exclusion (candidate pool)
- [ ] Chat filtering (blocked users hidden)
- [ ] Commons filtering (blocked users' tiles hidden)
- [ ] `GET /blocks` endpoint (list blocked users)
- [ ] `DELETE /blocks/{userId}` endpoint (unblock)
- [ ] Frontend UI (block button + confirm modal)
- [ ] Settings page (blocked users list)
- [ ] Analytics dashboard (block rate, repeat blockers)

---

## Future Enhancements

1. **Mutual blocks**  
   - If A blocks B, auto-block B → A (prevent one-sided abuse)

2. **Temporary blocks**  
   - "Pause this match for 30 days" (less nuclear than permanent block)

3. **Block reasons**  
   - Collect structured data: harassment, catfish, not interested
   - Feed into trust scoring & safety ML

4. **Block appeal**  
   - Frequently blocked users can request review

---

## Related Documentation

- [Moderation Overview](./README.md)
- [User Reports](./user-reports.md)
- [Trust System](../trust/README.md)
- [Match Lifecycle](../matchmaking/match-lifecycle.md)
- [API Reference](./api.md)
