# Orbit Gravity — ◈ Interaction

**Path:** `docs/features/commons/orbit.md`  
**Last Updated:** 2026-08-17  
**Feature:** Commons

---

## What is Orbit?

**Orbit** is the ◈ interaction on a tile — Woven's equivalent of a "like" but with behavioral matching semantics. When you orbit a tile:
1. The system determines if it's a **romantic** or **social** signal based on your preferences
2. If romantic, it updates **orbit_gravity** (a recency-decayed affinity score)
3. If mutual, it creates a **CandidateSignal** (romantic) or **FriendBridge** (social)

**Key principle:** Orbits are **fully anonymous**. The tile owner does not see who orbited their tile unless a mutual is detected (and even then, identity is revealed only after the match/bridge is accepted).

---

## Orbit Mechanics

### Endpoint
**Route:** `POST /orbit/{tileId}`  
**Service:** `OrbitService.OrbitTileAsync`  
**File:** `backend/WovenBackend/Services/Orbit/OrbitService.cs`

### Rate Limit
**50 orbits per user per day** (resets midnight UTC)

```csharp
var rlKey = $"rl:orbit:{userId}:{DateOnly.FromDateTime(DateTime.UtcNow)}";
var allowed = await cache.CheckRateLimitAsync(rlKey, 50, CacheTtl.UntilMidnightUtc(), ct);
if (!allowed)
    return Results.StatusCode(429); // Retry-After header set
```

**Frontend handling:**
```typescript
orbitTile(tileId: string) {
  return this.http.post<{ relationshipType: string; mutualDetected: boolean }>(
    `${environment.apiUrl}/orbit/${tileId}`,
    {}
  );
}
```

---

## Relationship Type Detection

Every orbit is classified as **romantic** or **social** based on the orbiter's preferences:

```csharp
private static string DetermineRelationshipType(
    UserPreference? orbiterPref,
    UserProfile? orbiterProfile,
    UserProfile? ownerProfile)
{
    if (orbiterPref == null || orbiterProfile == null || ownerProfile == null)
        return "social";

    // Parse InterestedIn (JSON array → HashSet)
    var interestedIn = ParseInterestedIn(orbiterPref.InterestedInJson);
    if (interestedIn.Count == 0) return "social";

    // 1. Gender match
    if (!interestedIn.Contains(ownerProfile.Gender))
        return "social";

    // 2. Age match
    if (ownerProfile.Age < orbiterPref.AgeMin || ownerProfile.Age > orbiterPref.AgeMax)
        return "social";

    // 3. Distance match (only if both have lat/lng)
    if (orbiterProfile.Lat.HasValue && ownerProfile.Lat.HasValue)
    {
        var dist = Haversine(orbiterProfile.Lat, orbiterProfile.Lng, 
                             ownerProfile.Lat, ownerProfile.Lng);
        if (dist > orbiterPref.DistanceMiles)
            return "social";
    }

    return "romantic";
}
```

**Fallback to social:**
- Missing preference data
- Gender mismatch
- Age outside bounds
- Distance exceeds max

**Why this matters:**  
Romantic orbits feed into the ECHO matchmaking pipeline (via `orbit_gravity` and `CandidateSignal`). Social orbits create friend connections outside the dating flow.

---

## Data Model

### TileOrbit
**Table:** `tile_orbits`  
**File:** `backend/WovenBackend/data/Entities/TileOrbit.cs`

```csharp
[Table("tile_orbits")]
public class TileOrbit
{
    public Guid Id { get; set; }
    public int OrbiterId { get; set; }
    public Guid TileId { get; set; }
    public int TileOwnerId { get; set; }
    public string RelationshipType { get; set; } // 'romantic' | 'social'
    public DateTimeOffset OrbitedAt { get; set; }
}
```

**Indexes:**
- Primary key on `id`
- **UNIQUE** constraint on `(orbiter_id, tile_id)` — prevents duplicate orbits
- Index on `tile_owner_id` — for "orbits received" queries
- Index on `relationship_type` — for mutual detection

### OrbitGravity
**Table:** `orbit_gravity`  
**File:** `backend/WovenBackend/data/Entities/OrbitGravity.cs`

```csharp
[Table("orbit_gravity")]
public class OrbitGravity
{
    public int UserId { get; set; }
    public int CandidateId { get; set; }
    public double Score { get; set; }            // exponentially decayed
    public DateTimeOffset LastOrbitAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

**Primary Key:** `(user_id, candidate_id)`

**Purpose:**  
Tracks cumulative affinity from orbits. Each orbit increments score by `1.0`, but **previous score decays exponentially** based on time since `last_orbit_at`.

---

## Orbit Gravity Formula

```csharp
private const double GravityDecayRate = 0.1;  // e^(-0.1 × days)
private const double GravityIncrement = 1.0;

private async Task UpsertOrbitGravityAsync(int userId, int candidateId, CancellationToken ct)
{
    var now = DateTimeOffset.UtcNow;
    var existing = await _db.OrbitGravities
        .FirstOrDefaultAsync(g => g.UserId == userId && g.CandidateId == candidateId, ct);

    if (existing == null)
    {
        // First orbit
        _db.OrbitGravities.Add(new OrbitGravity
        {
            UserId = userId,
            CandidateId = candidateId,
            Score = GravityIncrement, // 1.0
            LastOrbitAt = now,
            UpdatedAt = now
        });
    }
    else
    {
        // Decay previous score, then increment
        var daysSinceLast = (now - existing.LastOrbitAt).TotalDays;
        var decayed = existing.Score * Math.Exp(-GravityDecayRate * daysSinceLast);
        existing.Score = decayed + GravityIncrement;
        existing.LastOrbitAt = now;
        existing.UpdatedAt = now;
    }

    await _db.SaveChangesAsync(ct);
}
```

**Example:**
- Day 0: orbit → score = 1.0
- Day 7: orbit → score = `1.0 × e^(-0.1 × 7) + 1.0` = `0.4966 + 1.0` = **1.50**
- Day 14: orbit → score = `1.50 × e^(-0.1 × 7) + 1.0` = `0.7449 + 1.0` = **1.74**
- Day 90 (no orbit): score = `1.74 × e^(-0.1 × 90)` = **0.0014** (effectively zero)

**Why decay?**  
Old signals lose relevance. If someone orbited my tiles 3 months ago and stopped, that affinity is stale.

**Matchmaking integration:**  
`OrbitGravity.score` is used in `MatchScoringService` as **Component #15 (OrbitAffinity)**. Higher scores → higher match rank.

---

## Mutual Detection

### Romantic Mutual
**Condition:** Tile owner has orbited **any** of orbiter's tiles (where `relationship_type = 'romantic'`)

**Effect:**
```csharp
if (ownerOrbitedOrbiterTile)
{
    mutualDetected = true;

    // Write CandidateSignal boost (both directions)
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    _db.CandidateSignals.AddRange(
        new CandidateSignal
        {
            FromUserId = orbiterId,
            ToUserId = tile.UserId,
            Type = "YES",
            MetaJson = """{"source":"orbit_mutual"}""",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            DateUtc = today,
            CreatedAt = DateTime.UtcNow
        },
        new CandidateSignal { /* reverse */ }
    );
    await _db.SaveChangesAsync(ct);
}
```

**CandidateSignal impact:**
- Boosts both users into each other's **Drawn tab** (mutual-like bucket)
- 7-day TTL (expires if no further action)
- Processed by `CandidatePoolService` during daily deck generation

**Anonymous guarantee:**  
No notification is sent. Users discover the mutual only when they both appear in Drawn.

### Social Mutual
**Condition:** Tile owner has orbited any of orbiter's tiles (where `relationship_type = 'social'`)

**Effect:**
```csharp
if (mutualSocialOrbit)
{
    mutualDetected = true;

    var bridge = new FriendBridge
    {
        UserAId = orbiterId,
        UserBId = tile.UserId,
        Status = "pending_both",
        CreatedAt = DateTimeOffset.UtcNow
    };
    _db.FriendBridges.Add(bridge);
    await _db.SaveChangesAsync(ct);

    // Send FriendBridge proposal notification (both users)
    _ = Task.Run(async () =>
    {
        await _notify.SendFriendBridgeProposalAsync(orbiterId, tile.UserId, bridge.Id);
    });
}
```

**FriendBridge flow:**
1. Mutual social orbit detected → `FriendBridge` created with `status = "pending_both"`
2. Both users receive notification: "You and [anonymous] both felt a connection"
3. Either user can accept or decline via:
   - `POST /orbit/bridges/{bridgeId}/accept`
   - `POST /orbit/bridges/{bridgeId}/decline`
4. On accept: `status = "active"`, identities revealed, friend connection established
5. On decline: `status = "declined"`, bridge deleted

---

## Frontend Flow

### Orbit Button
**Location:** Tile drawer (bottom right)

```html
<button
  class="orbitBtn"
  [class.active]="orbitedTiles.has(activeTile.tileId)"
  [disabled]="orbitedTiles.has(activeTile.tileId) || orbitingTiles.has(activeTile.tileId)"
  (click)="orbitTile(activeTile)"
>
  <span class="orbitIcon">{{ orbitedTiles.has(activeTile.tileId) ? '◈' : '◎' }}</span>
  <span>{{ orbitedTiles.has(activeTile.tileId) ? 'Orbiting' : 'Orbit' }}</span>
</button>
```

**States:**
- **Idle:** ◎ Orbit (clickable)
- **Sending:** … (disabled)
- **Success:** ◈ Orbiting (disabled, permanent)

### Orbit Action
```typescript
orbitTile(tile: CommonsTile) {
  if (this.orbitedTiles.has(tile.tileId) || this.orbitingTiles.has(tile.tileId)) return;
  this.orbitingTiles.add(tile.tileId);
  this.cdr.markForCheck();

  this.commons.orbitTile(tile.tileId).subscribe({
    next: res => {
      this.orbitingTiles.delete(tile.tileId);
      this.orbitedTiles.add(tile.tileId);
      this.showToast(res.mutualDetected 
        ? '◈ A mutual pull — you both feel it' 
        : '◎ Orbiting this thought');
      this.cdr.markForCheck();
    },
    error: (err: any) => {
      this.orbitingTiles.delete(tile.tileId);
      if (err?.error?.error === 'ALREADY_ORBITED')
        this.orbitedTiles.add(tile.tileId);
      else if (err?.status === 429)
        this.showToast('Orbit limit reached for today');
      else if (err?.error?.error === 'CANNOT_ORBIT_OWN_TILE')
        this.showToast("Can't orbit your own tile");
      this.cdr.markForCheck();
    }
  });
}
```

**Error handling:**
- `ALREADY_ORBITED` → mark as orbited (idempotent recovery)
- `429` → rate limit toast
- `CANNOT_ORBIT_OWN_TILE` → user tried to orbit their own content
- `TILE_NOT_FOUND` → tile expired or deleted

**Mutual toast:**  
`"◈ A mutual pull — you both feel it"` appears when the backend detects a mutual orbit (romantic or social).

---

## Received Orbits

**Endpoint:** `GET /orbit/received`

**Returns:**
```json
[
  {
    "id": "uuid",
    "orbiterId": 123,
    "tileId": "uuid",
    "relationshipType": "romantic",
    "orbitedAt": "2026-08-17T10:30:00Z"
  }
]
```

**Purpose:**  
Let users see that their tiles are being orbited (though orbiter identity remains hidden until mutual).

**Frontend (planned):**  
`/you/tiles` page will show orbit counts per tile.

---

## Friend Bridges

**Endpoint:** `GET /orbit/bridges`

**Returns:**
```json
[
  {
    "id": "uuid",
    "userAId": 123,
    "userBId": 456,
    "status": "pending_both",
    "createdAt": "2026-08-17T10:00:00Z",
    "acceptedAt": null
  }
]
```

**Statuses:**
- `pending_both` — mutual orbit detected, awaiting accept/decline
- `active` — accepted, friend connection established
- `declined` — declined by either party

**Accept/Decline:**
```typescript
// Accept
POST /orbit/bridges/{bridgeId}/accept
→ { accepted: true }

// Decline
POST /orbit/bridges/{bridgeId}/decline
→ { declined: true }
```

**On accept:**
- `status = "active"`
- `accepted_at = now`
- Identities revealed
- Friend connection created (future: chat unlock, profile visibility)

**On decline:**
- Bridge deleted
- No further action

---

## Use Cases

### 1. Cold-Start Romantic Interest
**Scenario:** New user, no matches yet. Sees a resonant tile, orbits it.

**Flow:**
1. Orbit recorded as `romantic` (within preferences)
2. `OrbitGravity` upserted (score = 1.0)
3. No mutual yet → no immediate effect
4. Tomorrow: `DailyDeckOrchestrator` runs, sees orbit_gravity score → boosts tile owner in candidate pool
5. Tile owner appears in deck → user sees them → can express interest via Moments flow

**Why this works:**  
Orbits act as **implicit YES signals** without requiring a match first.

### 2. Mutual Orbit Detection
**Scenario:** Alice orbits Bob's tile (romantic). Later, Bob orbits Alice's tile (romantic).

**Flow:**
1. Alice orbits → `orbit_gravity` updated
2. Bob orbits → mutual detected → `CandidateSignal` written (both directions)
3. Next deck refresh → Alice + Bob appear in each other's **Drawn tab**
4. Either can open balloon → trial period → match

**Anonymous preservation:**  
Neither knows the other orbited until they both appear in Drawn.

### 3. Social Connection
**Scenario:** Alice orbits Bob's tile, but Bob is outside her dating preferences (e.g., same gender, she seeks opposite).

**Flow:**
1. Orbit recorded as `social`
2. No `orbit_gravity` update
3. If Bob later orbits Alice's tile (also social) → `FriendBridge` created
4. Both receive notification: "A connection was made"
5. Accept → friend connection established (platonic)

**Why separate paths?**  
Dating and friendship are fundamentally different relationship modes. Mixing them creates UX confusion.

---

## Design Rationale

### Why Exponential Decay?
**Recency bias.** Human interest fades over time. A 3-month-old orbit is weak signal. Decay ensures fresh orbits weigh more.

### Why Anonymous?
**Reduces pressure.** Seeing "123 people orbited this" creates performance anxiety. Full anonymity until mutual preserves authentic expression.

### Why 50/day Limit?
**Prevents spam.** Without a cap, users could orbit every tile in Commons. 50 is generous (5× typical daily usage) but prevents abuse.

### Why Mutual-Only Identity Reveal?
**Privacy.** One-sided orbits are exploratory. Mutual orbits signal genuine interest. Revealing identity only after mutual respects both parties' agency.

---

## Next Steps

- **Orbit count UI** — Show "◈ 5 orbits" on `/you/tiles` page
- **Friend bridge acceptance flow** — Frontend UI for accepting/declining bridges
- **Orbit affinity weight tuning** — `OrbitAffinity` component currently fixed weight; could be learned

---

## Related Docs

- **[README.md](./README.md)** — Commons overview
- **[tiles.md](./tiles.md)** — Tile entity and lifecycle
- **[feed-algorithm.md](./feed-algorithm.md)** — How orbit_gravity feeds into CF scoring
- **[backend.md](./backend.md)** — OrbitService implementation
- **[api.md](./api.md)** — Full endpoint specs
