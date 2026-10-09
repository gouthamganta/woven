# Pure vs Edge Matches

Woven has two match types: **Pure** and **Edge**. The type is determined by the choices both users made on the Deck tab.

---

## Choice mechanics

When users see someone on the Deck tab, they choose one of three actions:

| Choice | Symbol | Internal ID | Meaning |
|---|---|---|---|
| **Magical** | ◈ | `MAGICAL` | Strong romantic interest |
| **Resonant** | ◇ | `RESONANT` | Genuine interest, open to exploring |
| ~~Pass~~ | — | `PASS` | Not a match (no visible action) |

**Match creation:** A match is created only when BOTH users choose either MAGICAL or RESONANT (mutual interest).

---

## Match type determination

```csharp
if (userAChoice == userBChoice)
{
    // Both chose the same option (both ◈ or both ◇)
    matchType = MatchType.PURE;
    edgeOwnerId = null;
}
else
{
    // One chose ◈, the other chose ◇
    matchType = MatchType.EDGE;
    edgeOwnerId = (userAChoice == "MAGICAL") ? userAId : userBId;
}
```

**Key insight:** The match type reveals nothing about individual choices — users never learn what the other person chose unless they discuss it.

---

## Pure matches

**Definition:** Both users made the SAME choice (both ◈ or both ◇).

**Characteristics:**
- `edge_owner_id` is `NULL`
- Both users get **FULL** profile access immediately
- No asymmetric access rules
- Perceived as stronger mutual interest

**Access logic:**
```csharp
if (match.MatchType == MatchType.PURE)
{
    accessLevel = "FULL";
    reason = "PURE_MATCH";
}
```

**What users see:**
- All photos (not just 1)
- Full bio
- All public optional fields
- Complete intent details (primary + openness)

**UI hint:** No special badge or indicator. Users experience this as "instant full access."

---

## Edge matches

**Definition:** Users made DIFFERENT choices (one ◈, one ◇).

**Characteristics:**
- `edge_owner_id` is set to the user who chose MAGICAL (◈)
- Asymmetric access rules
- Edge owner gets FULL access immediately
- Non-owner gets LIMITED access until 2-way messaging

**Edge owner privilege:**
```csharp
if (match.EdgeOwnerId == currentUserId)
{
    accessLevel = "FULL";
    reason = "EDGE_OWNER";
}
```

**Non-owner access progression:**
```
Limited access (1 photo, no bio)
         ↓
Both users exchange messages (BothMessagedAt set)
         ↓
Full access unlocked
```

**Why the asymmetry?**
- Edge owner showed stronger initial interest (MAGICAL vs RESONANT)
- Gives edge owner advantage in deciding whether to invest
- Non-owner earns full access through engagement (messaging)

---

## Access level comparison

| Feature | Pure Match | Edge Owner | Edge Non-owner (before messaging) | Edge Non-owner (after messaging) |
|---|---|---|---|---|
| Profile photos | All | All | 1 only | All |
| Bio | Full | Full | Hidden | Full |
| Optional fields | All public | All public | Hidden | All public |
| Intent | Full | Full | Full | Full |
| Access reason | `PURE_MATCH` | `EDGE_OWNER` | `WAIT_TWO_WAY_MESSAGE` | `TWO_WAY_MESSAGE` |

**Note:** Intent (primary + openness) is always visible, even for LIMITED access. This is intentional — intent alignment is core to Woven's matching.

---

## Backend implementation

**Entity:** `backend/WovenBackend/data/Entities/Moments/Match.cs`

```csharp
public enum MatchType { PURE = 1, EDGE = 2 }

[Table("matches")]
public class Match
{
    [Column("match_type")]
    public MatchType MatchType { get; set; }

    [Column("edge_owner_id")]
    public int? EdgeOwnerId { get; set; }  // NULL for PURE, userId for EDGE
}
```

**Access logic:** `backend/WovenBackend/Endpoints/MatchesEndpoints.cs`

```csharp
// GET /matches/{matchId}/profile
string accessLevel;
string reason;

if (match.MatchType == MatchType.PURE)
{
    accessLevel = "FULL";
    reason = "PURE_MATCH";
}
else if (match.EdgeOwnerId == currentUserId)
{
    accessLevel = "FULL";
    reason = "EDGE_OWNER";
}
else
{
    // Edge non-owner: check if 2-way messaging unlocked
    var unlocked = match.BothMessagedAt != null;
    accessLevel = unlocked ? "FULL" : "LIMITED";
    reason = unlocked ? "TWO_WAY_MESSAGE" : "WAIT_TWO_WAY_MESSAGE";
}
```

---

## Edge owner identification

**Edge owner is the user who chose MAGICAL (◈).**

```csharp
// When creating match
if (userAChoice == "MAGICAL" && userBChoice == "RESONANT")
{
    edgeOwnerId = userAId;  // A chose ◈
}
else if (userAChoice == "RESONANT" && userBChoice == "MAGICAL")
{
    edgeOwnerId = userBId;  // B chose ◈
}
```

**Edge owner never changes.** Even if the non-owner unlocks full access later, the `edge_owner_id` field remains set.

---

## 2-way messaging unlock

**For Edge non-owners, full access unlocks when `BothMessagedAt` is set.**

**How BothMessagedAt is set:**
- Backend tracks when each user sends their first message
- When BOTH users have sent at least 1 message → `BothMessagedAt` timestamp is set
- This also triggers `FindLoveAt = BothMessagedAt + 12 hours`

**Code location:** `backend/WovenBackend/Services/Chat/ChatService.cs`

```csharp
// After saving new message
if (match.BothMessagedAt == null)
{
    var userASentMessage = await db.ChatMessages
        .AnyAsync(m => m.ThreadId == threadId && m.SenderId == match.UserAId);
    var userBSentMessage = await db.ChatMessages
        .AnyAsync(m => m.ThreadId == threadId && m.SenderId == match.UserBId);

    if (userASentMessage && userBSentMessage)
    {
        match.BothMessagedAt = DateTimeOffset.UtcNow;
        match.FindLoveAt = match.BothMessagedAt.Value.AddHours(12);
    }
}
```

---

## Frontend experience

**Pure match user journey:**
1. User sees match on Deck tab with explanation
2. User chooses MAGICAL or RESONANT
3. Match created (if mutual interest)
4. User sees "New match!" notification
5. User opens match profile → **FULL access immediately**

**Edge owner journey:**
1. User chooses MAGICAL (◈)
2. Candidate chose RESONANT (◇)
3. Match created (Edge match, user owns edge)
4. User opens match profile → **FULL access immediately**

**Edge non-owner journey:**
1. User chooses RESONANT (◇)
2. Candidate chose MAGICAL (◈)
3. Match created (Edge match, candidate owns edge)
4. User opens match profile → **LIMITED access** (1 photo, no bio)
5. User sends message, candidate replies
6. `BothMessagedAt` set
7. User opens match profile again → **FULL access unlocked**

---

## UI indicators

**Current state (as of 2026-08-17):**
- No visible badge or label distinguishes Pure vs Edge matches
- Users infer match type by access level on profile page
- Access hint text: "Full preview unlocked." or "Limited preview — unlocks after 2-way chat."

**Future considerations:**
- Could add subtle badge on match list ("Mutual ◈" vs "You ◇ · Them ◈")
- Could show edge owner icon in chat header
- User research needed to determine if this adds value or creates confusion

---

## Signal tracking

Edge match type is tracked in ECHO's preference learning pipeline:

**MatchSignalLogs event:**
```
EventType: MatchCreated
MetadataJson: { "matchType": "EDGE", "edgeOwnerId": 123, "viewerIsOwner": false }
```

**ECHO learns:**
- Do users respond faster to Edge matches where they own the edge?
- Do Edge non-owners engage less before unlock?
- Does Pure vs Edge predict match outcome quality?

---

## Why this design?

**Intentionality gradient:**
- MAGICAL (◈) signals stronger interest than RESONANT (◇)
- User who showed more interest gets more information (edge owner privilege)
- User who showed less interest earns information through engagement

**Prevents gaming:**
- Users can't "test the waters" with RESONANT and then upgrade to MAGICAL
- Each choice is locked in when made
- No visibility into what the other person chose

**Encourages engagement:**
- Edge non-owners are motivated to message first (to unlock full profile)
- Creates natural conversation starter ("I'd love to learn more about you")

**Behavioral signal:**
- Edge match type is a rich signal for ECHO's learning
- Helps ECHO understand who users choose MAGICAL vs RESONANT for
- Feeds into preference vector updates

---

## Database queries

**Find all Pure matches for a user:**
```sql
SELECT * FROM matches
WHERE (user_a_id = $1 OR user_b_id = $1)
  AND match_type = 1  -- PURE
  AND balloon_state = 1;  -- ACTIVE
```

**Find Edge matches where user owns the edge:**
```sql
SELECT * FROM matches
WHERE edge_owner_id = $1
  AND match_type = 2  -- EDGE
  AND balloon_state = 1;  -- ACTIVE
```

**Find Edge matches where user does NOT own edge:**
```sql
SELECT * FROM matches
WHERE (user_a_id = $1 OR user_b_id = $1)
  AND edge_owner_id != $1
  AND match_type = 2  -- EDGE
  AND balloon_state = 1;  -- ACTIVE
```

---

## Edge cases

**What if both users choose PASS?**
- No match created
- No signal logged (PASS is absence of choice)

**What if one user deletes their account after match creation?**
- Match remains in database (soft delete)
- Other user sees "This user is no longer active" on match list
- Balloon auto-closes after 96h expiration

**Can edge ownership transfer?**
- No. `edge_owner_id` is immutable once set.
- Even if non-owner unlocks full access, edge owner remains unchanged.

**What if a user blocks the other before 2-way messaging?**
- Edge non-owner never unlocks full access
- Match closes immediately (ClosedReason = BLOCK)
- Block record prevents future matching

---

## Related files

**Backend:**
- `backend/WovenBackend/data/Entities/Moments/Match.cs` — Match entity
- `backend/WovenBackend/Endpoints/MatchesEndpoints.cs` — Access logic
- `backend/WovenBackend/Services/Moments/MatchService.cs` — Match creation

**Frontend:**
- `frontend/woven-frontend/src/app/services/matches.service.ts` — API client
- `frontend/woven-frontend/src/app/pages/matches/match-profile-preview.page.ts` — Profile view

**Database:**
- Migration: `20240325000001_AddMatchTypeAndEdgeOwner.cs` (original creation)
- Table: `matches` (columns: `match_type`, `edge_owner_id`)

---

## Testing checklist

**Pure match:**
- [ ] Both users chose MAGICAL → Pure match created, both get full access
- [ ] Both users chose RESONANT → Pure match created, both get full access
- [ ] `edge_owner_id` is NULL
- [ ] Access reason is "PURE_MATCH"

**Edge match (owner):**
- [ ] User chose MAGICAL, candidate chose RESONANT → Edge match, user owns edge
- [ ] `edge_owner_id` equals user ID
- [ ] User gets full access immediately
- [ ] Access reason is "EDGE_OWNER"

**Edge match (non-owner before messaging):**
- [ ] User chose RESONANT, candidate chose MAGICAL → Edge match, candidate owns edge
- [ ] `edge_owner_id` equals candidate ID
- [ ] User gets LIMITED access (1 photo, no bio)
- [ ] Access reason is "WAIT_TWO_WAY_MESSAGE"

**Edge match (non-owner after messaging):**
- [ ] User and candidate both sent messages → `BothMessagedAt` set
- [ ] User refreshes profile → FULL access granted
- [ ] Access reason is "TWO_WAY_MESSAGE"

**Edge ownership immutability:**
- [ ] `edge_owner_id` never changes after match creation
- [ ] Even after non-owner unlocks full access, `edge_owner_id` remains set
