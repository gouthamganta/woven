# Woven Business Rules

> Complete reference for all business logic rules in Woven.
> These rules define the core product behavior and must not be violated.

---

## 1. Interaction Budget System

The budget system is the core mechanic that makes Woven different from swipe apps.

### Budget Caps

| Tier | Total/Day | Save (⏳)/Day | Notes |
|------|-----------|--------------|-------|
| Default | 5 | 2 | Standard users |
| Boosted | Configurable | Configurable | Special events or admin grants |

- **Reset Time**: Midnight UTC — fresh start every day, no carry-over
- **Negative Balance**: Not allowed — must have budget to act

### Spending Costs

| Action | Cost | Notes |
|--------|------|-------|
| Magical (◈) on Moment | 1 | Standard interaction (stored as YES) |
| Logical (◇) on Moment | 1 | Standard interaction (stored as NO) |
| Save (⏳ Hold) on Moment | 1 | Goes to pending queue; separate Save cap of 2/day |
| Skip Moment | 0 | Won't see again, no cost |
| Pop Balloon | 0 | Already paid during Moment choice |
| Send Message | 0 | After connection, unlimited |

### Budget Code Pattern

```csharp
public record SpendResult(bool Success, int Remaining, string? Error);

public class InteractionBudgetService
{
    public async Task<SpendResult> TrySpend(Guid userId, int cost)
    {
        var budget = await GetOrCreateBudget(userId);

        if (budget.Remaining < cost)
            return new SpendResult(false, budget.Remaining, "Not enough budget");

        budget.Remaining -= cost;
        budget.LastSpent = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return new SpendResult(true, budget.Remaining, null);
    }

    public async Task<int> GetRemaining(Guid userId)
    {
        var budget = await GetOrCreateBudget(userId);
        return budget.Remaining;
    }

    private async Task<UserBudget> GetOrCreateBudget(Guid userId)
    {
        var today = DateTime.UtcNow.Date;
        var budget = await _db.UserBudgets
            .FirstOrDefaultAsync(b => b.UserId == userId && b.Date == today);

        if (budget == null)
        {
            budget = new UserBudget
            {
                UserId = userId,
                Date = today,
                Remaining = _config.DailyBudget  // Default: 5
            };
            _db.UserBudgets.Add(budget);
        }

        return budget;
    }
}
```

### Frontend Budget Display

```typescript
// Always show remaining budget prominently
<div class="budget-display">
  {{ budget.remaining }} / {{ budget.total }} left today
</div>

// Disable actions when budget is 0
<button [disabled]="budget.remaining < 1" (click)="makeChoice()">
  Choose
</button>
```

---

## 2. Rating System

Users rate each other after interactions. Ratings affect visibility and matching.

### Rating Rules

| Rule | Value | Rationale |
|------|-------|-----------|
| Scale | -100 to +100 | Granular sentiment |
| Display Threshold | count >= 5 | Prevents gaming with small samples |
| Display Format | Show when threshold met | Privacy protection |

### Rating Visibility Logic

```typescript
interface RatingDisplay {
  show: boolean;
  value?: number;
  count?: number;
}

function getRatingDisplay(rating: Rating): RatingDisplay {
  if (!rating || rating.count < 5) {
    return { show: false };
  }
  return {
    show: true,
    value: rating.value,
    count: rating.count
  };
}
```

```html
<div class="rating" *ngIf="rating.show">
  {{ rating.value | number:'1.0-0' }}
</div>

<div class="rating-new" *ngIf="!rating.show">
  <span class="new-dot"></span>
  <span>New</span>
</div>
```

### Rating Categories

| Category | Score Range | Display |
|----------|-------------|---------|
| Excellent | 80 to 100 | ★★★★★ |
| Good | 60 to 79 | ★★★★ |
| Average | 40 to 59 | ★★★ |
| Below Average | 20 to 39 | ★★ |
| Poor | -100 to 19 | ★ |

---

## 3. Matching Algorithm

Woven uses AI-powered matching based on UserVectors.

### UserVector Components

```typescript
interface UserVector {
  // Intent layer — What they're looking for (weight: 0.30)
  intent: {
    lookingFor: 'serious' | 'casual' | 'friendship' | 'unsure';
    timeline: 'now' | 'soon' | 'someday';
  };

  // Foundational pillars — Core compatibility (weight: 0.35)
  foundational: {
    values: string[];        // e.g., ['family', 'career', 'adventure']
    dealbreakers: string[];  // Hard no's
    lifestyle: string[];     // Living preferences
  };

  // Lifestyle layer — Day-to-day compatibility (weight: 0.20)
  lifestyle: {
    schedule: 'early_bird' | 'night_owl' | 'flexible';
    socialLevel: 'introvert' | 'ambivert' | 'extrovert';
    activityLevel: 'low' | 'moderate' | 'high';
  };

  // Pulse layer — Current mood/state, changes daily (weight: 0.15)
  pulse: {
    battery: 'full' | 'half' | 'low';
    tone: 'playful' | 'chill' | 'serious';
    role: 'lead' | 'follow' | 'either';
  };
}
```

### Match Scoring (14 Components)

| Component | Weight | Description |
|-----------|--------|-------------|
| Intent alignment | 0.12 | Same lookingFor + timeline |
| Foundational overlap | 0.15 | Shared values/pillars |
| Dealbreaker check | 0.08 | Hard-stop veto if triggered |
| Lifestyle fit | 0.10 | Schedule/social/activity alignment |
| Pulse sync | 0.07 | Current mood and role compatibility |
| Photo aesthetic | 0.06 | Visual preference learned signal |
| Location proximity | 0.05 | Distance preference |
| Age fit | 0.05 | Age range preference |
| Voice energy | 0.04 | Speech pattern similarity |
| Conversation fit | 0.06 | Message style compatibility |
| Commons overlap | 0.05 | Shared community spaces |
| Season momentum | 0.04 | Current season activity level |
| New user boost | 0.04 | Temporary visibility boost for new users |
| Under-exposure boost | 0.09 | Corrects deck monopolization |

Weights are dynamic — when a component has no data, its weight redistributes to other available components. Weekly gradient descent (WeightLearningService, Sunday 04:00 UTC) adjusts weights based on Pearson correlation with connection outcomes, clamped to [0.01, 0.50].

### Deck Composition (5 cards/day)

| Slot | Bucket | Source |
|------|--------|--------|
| 2 | CORE_FIT | Top intent + foundational scorers |
| 1 | LIFESTYLE_FIT | Strong lifestyle + pulse match |
| 1 | CONVERSATION_FIT | High conversation/commons signal |
| 1 | EXPLORER | Random diversity pick |

### Match Types

| Type | Condition | Description |
|------|-----------|-------------|
| PURE | Both chose same side (both Magical ◈ or both Logical ◇) | "You both felt the same vibe" |
| EDGE | Different sides (one Magical ◈, one Logical ◇) | "Opposites can attract" |

For EDGE matches: the "edge owner" (randomly assigned) gets full profile access immediately. The non-owner sees limited access (1 photo, no bio) until `BothMessagedAt` is set.

### Trust Gate

Users with `TrustScore < 0.25` are excluded from the candidate pool entirely. They do not appear in anyone's deck. The trust score is recalculated nightly by TrustScoreWorker.

---

## 4. Moment Themes

Moments are hypothetical questions that drive matching.

### Theme Structure

```typescript
interface MomentTheme {
  id: string;
  question: string;       // e.g., "Which calls to you?"
  left: {
    label: string;        // e.g., "Logical (◇)"
    emoji: string;        // e.g., "◇"
    choice: string;       // "LOGICAL" (stored as NO in DB)
  };
  mid: {
    label: string;        // "Hold"
    emoji: string;        // "⏳"
    choice: string;       // "PENDING"
  };
  right: {
    label: string;        // e.g., "Magical (◈)"
    emoji: string;        // e.g., "◈"
    choice: string;       // "MAGICAL" (stored as YES in DB)
  };
}
```

### Choice Mapping (API → DB)

| API Value | DB Enum | Product Label | Budget Cost |
|-----------|---------|---------------|-------------|
| `"MAGICAL"` | `YES` | Magical (◈) — heart leads | 1 |
| `"LOGICAL"` | `NO` | Logical (◇) — head leads | 1 |
| `"PENDING"` | `PENDING` | Save (⏳ Hold) | 1 (separate Save cap) |
| `"SKIP"` | — | Skip | 0 |

### Theme Display Rules

```html
<!-- Current theme layout -->
<div class="action logical">
  <div class="emoji">◇</div>
  <div class="label">Logical (◇)</div>
</div>

<div class="action save">
  <div class="label">can't decide</div>
</div>

<div class="action magical">
  <div class="emoji">◈</div>
  <div class="label">Magical (◈)</div>
</div>
```

---

## 5. Balloon Rules

Balloons are created when two users both make a Magical or Logical choice on the same Moment.

### Balloon Creation

```csharp
public async Task<Balloon?> TryCreateBalloon(Guid momentId, Guid userA, Guid userB)
{
    var choiceA = await GetChoice(momentId, userA);
    var choiceB = await GetChoice(momentId, userB);

    // Both must have made a definitive choice (not PENDING/SKIP)
    if (!IsValidChoice(choiceA) || !IsValidChoice(choiceB))
        return null;

    // PURE = same choice, EDGE = different choices
    var matchType = choiceA.Choice == choiceB.Choice
        ? MatchType.PURE
        : MatchType.EDGE;

    var balloon = new Balloon
    {
        Id = Guid.NewGuid(),
        UserAId = userA,
        UserBId = userB,
        MomentId = momentId,
        MatchType = matchType,
        Status = BalloonStatus.ACTIVE,
        CreatedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.AddDays(7)  // 7-day TTL
    };

    _db.Balloons.Add(balloon);
    await _db.SaveChangesAsync();

    return balloon;
}
```

### Balloon Expiration

| Rule | Value |
|------|-------|
| Default Lifetime | **7 days** |
| Pop Deadline | Before expiration |
| After Expiration | Auto-closes via BalloonExpiryWorker (runs every 60 s) |

### Balloon Actions

| Action | Effect | Who Can Do |
|--------|--------|------------|
| Pop | Start 1-minute trial period | Either user |
| Let Expire | Balloon closes silently after 7 days | Passive |
| Unmatch | Balloon closes immediately, recorded | Either user |
| Block | Balloon closes, user added to block list | Either user |

---

## 6. Trial Period Rules

After popping a balloon, users enter a 1-minute trial communication period.

### Trial Rules

| Rule | Value |
|------|-------|
| Duration | **1 minute** |
| Both must decide | CONTINUE or END within 1 minute |
| Result if both CONTINUE | FindLoveAt = now; full connection unlocked |
| Result if either END | Match closes as UNMATCH |
| Result if timeout | Match closes as UNMATCH |

### Trial Engagement Check

```csharp
public async Task<TrialResult> EvaluateTrial(Guid matchId)
{
    var match = await _db.Matches.FindAsync(matchId);

    if (!match.IsTrial || match.TrialEndsAt == null)
        return TrialResult.NOT_IN_TRIAL;

    var userADecided = match.UserADecision != null;
    var userBDecided = match.UserBDecision != null;

    if (userADecided && userBDecided)
    {
        if (match.UserADecision == "CONTINUE" && match.UserBDecision == "CONTINUE")
            return TrialResult.PASSED;
        else
            return TrialResult.FAILED;
    }

    if (DateTime.UtcNow > match.TrialEndsAt)
        return TrialResult.TIMED_OUT;

    return TrialResult.IN_PROGRESS;
}
```

---

## 7. Find Love Unlock

Find Love is the final stage of a connection — unlocked 5 minutes after both users have sent at least one message.

### Find Love Rules

| Rule | Value |
|------|-------|
| Trigger | `BothMessagedAt` is set when both users have sent ≥ 1 message |
| Unlock Delay | 5 minutes after `BothMessagedAt` |
| Content | AI-generated date idea, date coordination UI |
| Who Sees It | Both users; shown via `showFindLove = true` |
| Alternative Path | If trial passes (both CONTINUE), FindLoveAt = now |

---

## 8. Communication Rules

Rules for messaging between users.

### Message Access

| Stage | Can Message |
|-------|-------------|
| Before Match | No |
| Balloon Active (not popped) | No — balloon must be popped first |
| Trial Period (1 min) | Yes, within trial window |
| Full Connection | Yes, unlimited |

### Message Validation

```csharp
public async Task<SendResult> ValidateSend(Guid senderId, Guid threadId)
{
    var thread = await _db.Threads.FindAsync(threadId);

    if (!IsParticipant(thread, senderId))
        return SendResult.Fail("Not authorized");

    if (!CanMessage(thread.Status))
        return SendResult.Fail("Cannot message in current state");

    if (await IsRateLimited(senderId))
        return SendResult.Fail("Rate limited");

    return SendResult.Success();
}
```

---

## 9. Profile Visibility Rules

Who can see what profile information.

### Visibility Matrix

| Viewer | Profile | Photos | Full Name | Last Active |
|--------|---------|--------|-----------|-------------|
| Stranger | No | No | No | No |
| Moment Candidate | Limited | Yes | First name only | No |
| Balloon Match — PURE | Yes | Yes | First name | General |
| Balloon Match — EDGE (owner) | Yes | Yes | First name | General |
| Balloon Match — EDGE (non-owner) | Limited | 1 photo | First name | No |
| Trial / Connected | Yes | Yes | Full name | Yes |

### Profile Privacy Code Pattern

```csharp
public ProfileView GetProfileView(Guid viewerId, Guid profileId)
{
    var relationship = GetRelationship(viewerId, profileId);

    return relationship switch
    {
        Relationship.STRANGER => null,
        Relationship.MOMENT_CANDIDATE => new ProfileView
        {
            FirstName = profile.FirstName,
            Photos = profile.Photos,
            Bio = profile.Bio,
            // Hide: LastName, Location, Last active
        },
        Relationship.CONNECTED => new ProfileView
        {
            FirstName = profile.FirstName,
            LastName = profile.LastName,
            Photos = profile.Photos,
            Bio = profile.Bio,
            Location = profile.Location,
            LastActive = profile.LastActive
        },
        _ => null
    };
}
```

---

## 10. Block & Report Rules

User safety and moderation rules.

### Block Effects

| Effect | Description |
|--------|-------------|
| Mutual Invisibility | Neither user sees the other in any deck or search |
| End All Connections | Active balloons/threads close immediately |
| Prevent Future Matches | Never shown to each other again |
| One-way | Blocked user doesn't know they're blocked |

### Report Flow

```
User Reports → Queued for Review → Moderator Action
                                        ↓
                              ┌─────────┴─────────┐
                              ↓                   ↓
                          Dismissed           Action Taken
                                                  ↓
                                    ┌─────────────┼─────────────┐
                                    ↓             ↓             ↓
                                 Warning      Suspend         Ban
```

---

## 11. Trust & Ghost Scoring

### Trust Score

- **Range**: 0.0 – 1.0
- **Threshold**: Users with `TrustScore < 0.25` are hidden from all candidate pools
- **Inputs**: Account age, verification status, report count, response consistency, rating history
- **Update**: Recalculated nightly by `TrustScoreWorker`

```csharp
// Trust gate enforcement in DailyDeckOrchestrator
var candidates = await _db.Users
    .Where(u => u.TrustScore >= 0.25)
    .Where(u => /* other filters */)
    .ToListAsync();
```

### Ghost Score

- **Purpose**: Detects users who appear but never engage (ghost behavior)
- **Threshold**: Calculated after 5+ balloon matches
- **Inputs**: Read-without-reply rate, average response time, abandonment rate
- **Effect**: High ghost score reduces appearance frequency in others' decks
- **Update**: Recalculated by `GhostScoreWorker` after each match closes

---

## 12. Seasons

Seasons add a recurring social layer to the platform.

### Season Rules

| Rule | Value |
|------|-------|
| Duration | **21 days** per season |
| Theme | Set by platform admin |
| Participation | Automatic — all active users participate |
| Season Score | Accumulates from Moments, messages, connections |
| Season Rank | Displayed on profile during active season |
| Reset | Scores reset when new season starts |
| History | Past seasons archived and visible on profile |

---

## 13. Foundational Questions

Deep-compatibility questions answered during onboarding; expire and refresh periodically.

### Expiry Rules

| Question Type | Expiry |
|--------------|--------|
| Core foundational | **60 days** — fundamental values rarely change |
| Lifestyle | **45 days** — living preferences shift occasionally |
| Current state | **15 days** — mood/role/energy shifts frequently |

When a foundational question expires, the user is prompted to re-answer it. Until re-answered, that component weight redistributes to other available components.

---

## 14. Energy Meter & Orbit Limits

### Energy Meter (Tiles)

- **Cap**: 100 tile interactions per day
- **Reset**: Midnight UTC
- **Purpose**: Limits how many profiles a user can browse in the Commons/Tiles feed
- **Cost**: 1 energy per tile view (browsing Commons)

### Orbit Rate Limit

- **Cap**: 50 Orbit actions per day
- **Reset**: Midnight UTC
- **Purpose**: Limits how many users can be "orbited" (followed/circled) per day
- **Effect**: Orbit does not cost interaction budget; it has its own separate cap

---

## 15. Visual Preference Learning

The system learns photo aesthetic preferences from swipe behavior.

### Rules

| Rule | Value |
|------|-------|
| Minimum Decisions | **10** — system ignores visual preference component until 10 photo decisions recorded |
| Learning Signal | Whether user chose Magical/Logical vs Save/Skip on a given card |
| Model | Embedding similarity between photo embedding and user's "liked" cluster |
| Update | Recalculated weekly by `VisualPreferenceLearner` |
| Weight | 0.06 in scoring formula (0.0 until minimum decisions reached) |

---

## Business Rule Validation Checklist

When implementing features, verify:

- [ ] Budget is checked before any costly action (Magical, Logical, Save)
- [ ] Save cap (2/day) checked separately from total cap (5/day)
- [ ] Ratings only show when count >= 5
- [ ] State machines are respected (no invalid transitions)
- [ ] Profile visibility follows relationship tier rules
- [ ] EDGE match: non-owner sees limited view until BothMessagedAt
- [ ] Blocked users cannot see or interact with each other
- [ ] Trial duration is 1 minute (not hours)
- [ ] Find Love unlocks 5 minutes after BothMessagedAt
- [ ] Balloon TTL is 7 days (not 72 hours)
- [ ] Trust gate enforced: TrustScore < 0.25 excluded from pool
- [ ] Ghost score calculated only after 5+ matches
- [ ] Visual preference ignored until 10 decisions recorded
- [ ] Energy cap 100/day for tile views
- [ ] Orbit cap 50/day, separate from interaction budget
- [ ] Season length is 21 days
- [ ] Foundational Q expiry: 15/45/60 days by type
- [ ] All timing rules use UTC
- [ ] Moment themes feel like hypothetical questions (not preference surveys)
