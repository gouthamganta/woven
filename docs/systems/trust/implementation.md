# Trust System Implementation

**Last Updated:** 2026-10-07

---

## Services

### GhostDetectionService

Detects ghost behavior and triggers spark refunds.

**Interface:**
```csharp
public interface IGhostDetectionService
{
    Task<bool> IsGhostMatchAsync(int matchId, CancellationToken ct);
    Task ProcessGhostRefundAsync(int matchId, CancellationToken ct);
}
```

**Ghost Detection Logic:**
```csharp
public async Task<bool> IsGhostMatchAsync(int matchId, CancellationToken ct)
{
    var match = await _db.Matches
        .Include(m => m.ChatThread)
        .FirstOrDefaultAsync(m => m.Id == matchId, ct);

    if (match == null) return false;

    // Ghost = match closed with BOTH users never messaging
    return match.BalloonState == BalloonState.CLOSED 
        && match.BothMessagedAt == null;
}
```

**Refund Logic:**
```csharp
public async Task ProcessGhostRefundAsync(int matchId, CancellationToken ct)
{
    var match = await _db.Matches.FindAsync(matchId);
    if (match == null) return;

    // Refund 0.5 sparks to both users
    await _sparkWallet.RefundAsync(match.UserAId, 0.5f, $"Ghost refund: match {matchId}", ct);
    await _sparkWallet.RefundAsync(match.UserBId, 0.5f, $"Ghost refund: match {matchId}", ct);

    // Log analytics event
    await _analytics.TrackAsync(new AnalyticsEvent
    {
        Type = "GhostRefundProcessed",
        UserId = match.UserAId,
        Context = new { matchId, refundAmount = 0.5 }
    }, ct);
}
```

**Trigger Points:**
1. Trial auto-close (no messages exchanged)
2. Trial END decision (user declines match)
3. Manual unmatch (before first exchange)
4. Graceful close (balloon expires, no messages)

---

### TrustScoreService

Calculates user trust scores based on behavior.

**Interface:**
```csharp
public interface ITrustScoreService
{
    Task<decimal> CalculateTrustScoreAsync(int userId, CancellationToken ct);
    Task UpdateTrustScoreAsync(int userId, CancellationToken ct);
}
```

**Trust Score Components:**

1. **Completion Rate** (40%)
   - Onboarding completed: +10
   - Profile photos added: +10
   - Foundational questions answered: +10
   - Profile details filled: +10

2. **Engagement Quality** (30%)
   - Matches that reached trial: +5 per match
   - Messages sent in trials: +2 per message
   - Voice notes sent: +3 per note
   - Dates arranged (Find Love): +10 per date

3. **Community Signals** (20%)
   - Positive feedback received: +5 per 5⭐
   - Negative feedback received: -10 per 1⭐
   - Ghost rate: -5 per ghost (>50% ghost rate)
   - Response time: +5 if avg <1 hour

4. **Safety & Compliance** (10%)
   - Verified badge: +10
   - No blocks received: +5
   - No reports received: +5
   - Blocks given (reasonable): 0 penalty

**Score Range:** 0-100

**Formula:**
```csharp
trustScore = (
    completionScore * 0.4 +
    engagementScore * 0.3 +
    communityScore * 0.2 +
    safetyScore * 0.1
);
```

---

### VerificationService

Handles user verification (planned feature).

**Interface:**
```csharp
public interface IVerificationService
{
    Task<bool> RequestVerificationAsync(int userId, CancellationToken ct);
    Task<VerificationStatus> GetStatusAsync(int userId, CancellationToken ct);
}
```

**Verification Methods (Planned):**
1. Photo verification (selfie match)
2. Phone number verification (SMS)
3. Social media link verification

**Status:** Not yet implemented

---

## Database Entities

### Block

```csharp
[Table("blocks")]
public class Block
{
    public int Id { get; set; }
    public int BlockerId { get; set; }
    public int BlockedId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Reason { get; set; }
    
    public User? Blocker { get; set; }
    public User? Blocked { get; set; }
}
```

**Effects:**
- Blocker cannot see Blocked in deck
- Blocked cannot see Blocker in deck
- Existing match closes immediately
- Chat thread becomes read-only

---

### TrustScore (Computed, not stored)

Trust scores are computed on-demand, not persisted.

**Caching:**
- Redis cache, 1-hour TTL
- Key: `trust-score:{userId}`
- Invalidated on: feedback received, match closed, block

---

## Integration Points

**Matchmaking:**
```csharp
// CandidatePoolService.cs
var candidates = await _db.Users
    .Where(u => !_db.Blocks.Any(b => 
        (b.BlockerId == userId && b.BlockedId == u.Id) ||
        (b.BlockerId == u.Id && b.BlockedId == userId)
    ))
    .ToListAsync(ct);
```

**Match Closure:**
```csharp
// MatchesEndpoints.cs - POST /matches/{matchId}/unmatch
if (await _ghostDetection.IsGhostMatchAsync(matchId, ct))
{
    await _ghostDetection.ProcessGhostRefundAsync(matchId, ct);
}
```

**Deck Selection:**
```csharp
// DeckSelectionService.cs
var trustScore = await _trust.CalculateTrustScoreAsync(candidateId, ct);
if (trustScore < 30)
{
    // Deprioritize low-trust users
    candidate.Priority = Priority.Low;
}
```

---

## Testing

**Unit Tests:**
```csharp
[Fact]
public async Task IsGhostMatch_NoMessages_ReturnsTrue()
{
    // Arrange
    var match = new Match
    {
        BalloonState = BalloonState.CLOSED,
        BothMessagedAt = null
    };
    _db.Matches.Add(match);
    await _db.SaveChangesAsync();

    // Act
    var isGhost = await _service.IsGhostMatchAsync(match.Id);

    // Assert
    Assert.True(isGhost);
}

[Fact]
public async Task IsGhostMatch_HasMessages_ReturnsFalse()
{
    // Arrange
    var match = new Match
    {
        BalloonState = BalloonState.CLOSED,
        BothMessagedAt = DateTimeOffset.UtcNow
    };
    _db.Matches.Add(match);
    await _db.SaveChangesAsync();

    // Act
    var isGhost = await _service.IsGhostMatchAsync(match.Id);

    // Assert
    Assert.False(isGhost);
}
```

---

## Performance

**Ghost Detection:**
- Query time: 5-10ms (single match lookup)
- Refund time: 20ms (2 wallet updates + analytics)

**Trust Score Calculation:**
- Cold: 150-200ms (aggregation queries)
- Cached: <5ms (Redis lookup)
- Cache hit rate: 90%+

---

## Monitoring

**Metrics:**
- Ghost refunds issued per day
- Trust score distribution (histogram)
- Verification requests per day
- Block rate (blocks per active match)

**Alerts:**
- Ghost rate > 20%
- Trust score recalculation > 500ms p95
- Verification queue backlog > 100

---

## Related Documentation

- [Ghost Detection](anti-ghosting.md)
- [Trust Scoring](trust-score.md)
- [Verification](verification.md)
- [Photo Verification](photo-verification.md)
- [Trust System README](README.md)
