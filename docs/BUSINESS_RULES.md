# Woven Business Rules

> Complete reference for all business logic rules in Woven.
> These rules define the core product behavior and must not be violated.

---

## 1. Interaction Budget System

The budget system is the core mechanic that makes Woven different from swipe apps.

### Budget Rules

| Rule | Value | Rationale |
|------|-------|-----------|
| Daily Budget | Configured per tier | Forces intentional choices |
| Reset Time | Midnight UTC | Consistent global reset |
| Carry Over | No | Fresh start each day |
| Negative Balance | Not allowed | Must have budget to act |

### Spending Costs

| Action | Cost | Notes |
|--------|------|-------|
| YES on Moment | 1 | Standard interaction |
| NO on Moment | 1 | Standard interaction |
| HOLD on Moment | 0 | Save for later, no cost |
| SKIP Moment | 0 | Won't see again, no cost |
| Pop Balloon | 0 | Already paid during Moment |
| Send Message | 0 | After connection, unlimited |

### Budget Code Pattern

```csharp
public record SpendResult(bool Success, int Remaining, string? Error);

public class InteractionBudgetService
{
    public async Task<SpendResult> TrySpend(Guid userId, int cost)
    {
        var budget = await GetOrCreateBudget(userId);

        // Check if enough budget
        if (budget.Remaining < cost)
            return new SpendResult(false, budget.Remaining, "Not enough budget");

        // Deduct
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
                Remaining = _config.DailyBudget
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
| Display Threshold | count >= 5 | Prevents gaming |
| Display Format | Show when threshold met | Privacy protection |

### Rating Visibility Logic

```typescript
// ✅ CORRECT - Check threshold before showing
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
<!-- In template -->
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
  // Intent layer - What they're looking for
  intent: {
    lookingFor: 'serious' | 'casual' | 'friendship' | 'unsure';
    timeline: 'now' | 'soon' | 'someday';
  };

  // Foundational pillars - Core compatibility
  foundational: {
    values: string[];        // e.g., ['family', 'career', 'adventure']
    dealbreakers: string[];  // Hard no's
    lifestyle: string[];     // Living preferences
  };

  // Lifestyle layer - Day-to-day compatibility
  lifestyle: {
    schedule: 'early_bird' | 'night_owl' | 'flexible';
    socialLevel: 'introvert' | 'ambivert' | 'extrovert';
    activityLevel: 'low' | 'moderate' | 'high';
  };

  // Pulse layer - Current mood/state (changes daily)
  pulse: {
    battery: 'full' | 'half' | 'low';
    tone: 'playful' | 'chill' | 'serious';
    role: 'lead' | 'follow' | 'either';
  };
}
```

### Match Scoring

```csharp
public class MatchScoringService : IMatchScoringService
{
    public double CalculateScore(UserVector a, UserVector b)
    {
        var scores = new List<(double weight, double score)>
        {
            (0.30, ScoreIntent(a.Intent, b.Intent)),
            (0.35, ScoreFoundational(a.Foundational, b.Foundational)),
            (0.20, ScoreLifestyle(a.Lifestyle, b.Lifestyle)),
            (0.15, ScorePulse(a.Pulse, b.Pulse))
        };

        return scores.Sum(s => s.weight * s.score);
    }

    private double ScoreIntent(Intent a, Intent b)
    {
        // Exact match on lookingFor = high score
        if (a.LookingFor == b.LookingFor)
            return 1.0;

        // Compatible combinations
        if (IsCompatible(a.LookingFor, b.LookingFor))
            return 0.7;

        return 0.3;
    }

    // ... other scoring methods
}
```

### Match Types

| Type | Condition | Description |
|------|-----------|-------------|
| PURE | Same Moment choice | "You both felt the same vibe" |
| EDGE | Different Moment choice | "Opposites can attract" |

---

## 4. Moment Themes

Moments are hypothetical questions that drive matching.

### Theme Structure

```typescript
interface MomentTheme {
  id: string;
  question: string;      // The hypothetical question
  optionYes: {
    label: string;       // e.g., "Brunch"
    emoji: string;       // e.g., "☀️"
    description: string; // What this choice means
  };
  optionNo: {
    label: string;       // e.g., "Dinner"
    emoji: string;       // e.g., "🌙"
    description: string;
  };
  category: string;      // For grouping/filtering
  activeFrom: Date;
  activeTo: Date;
}
```

### Theme Display Rules

```typescript
// Micro line for current theme
get microLine(): string {
  return 'One hypothetical date. What would it be?';
}

// Action labels - must feel like hypothetical choices
<div class="action yes">
  <div class="emoji">{{ theme.optionYes.emoji }}</div>
  <div class="label">{{ theme.optionYes.label }}</div>
</div>

<div class="action no">
  <div class="emoji">{{ theme.optionNo.emoji }}</div>
  <div class="label">{{ theme.optionNo.label }}</div>
</div>

<div class="action hold">
  <div class="label">can't decide</div>
</div>
```

---

## 5. Balloon Rules

Balloons are created when two users both make choices on the same Moment.

### Balloon Creation

```csharp
public async Task<Balloon?> TryCreateBalloon(Guid momentId, Guid userA, Guid userB)
{
    // Get both users' choices
    var choiceA = await GetChoice(momentId, userA);
    var choiceB = await GetChoice(momentId, userB);

    // Both must have made a choice (not HOLD, SKIP, or PENDING)
    if (!IsValidChoice(choiceA) || !IsValidChoice(choiceB))
        return null;

    // Determine match type
    var matchType = choiceA.Choice == choiceB.Choice
        ? MatchType.PURE
        : MatchType.EDGE;

    // Create balloon
    var balloon = new Balloon
    {
        Id = Guid.NewGuid(),
        UserAId = userA,
        UserBId = userB,
        MomentId = momentId,
        MatchType = matchType,
        Status = BalloonStatus.ACTIVE,
        CreatedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.AddHours(72)
    };

    _db.Balloons.Add(balloon);
    await _db.SaveChangesAsync();

    return balloon;
}
```

### Balloon Expiration

| Rule | Value |
|------|-------|
| Default Lifetime | 72 hours |
| Pop Deadline | Before expiration |
| After Expiration | Auto-closes, match lost |

### Balloon Actions

| Action | Effect | Who Can Do |
|--------|--------|------------|
| Pop | Start trial period | Either user |
| Let Expire | Balloon closes silently | Passive |
| Unmatch | Balloon closes, recorded | Either user |
| Block | Balloon closes, user blocked | Either user |

---

## 6. Trial Period Rules

After popping a balloon, users enter a trial communication period.

### Trial Rules

| Rule | Value |
|------|-------|
| Duration | 24-48 hours (configurable) |
| Message Limit | Limited exchanges |
| Pass Threshold | Both users must engage |
| Result | Pass → Full connection, Fail → Match ends |

### Trial Engagement Check

```csharp
public async Task<TrialResult> EvaluateTrial(Guid trialId)
{
    var trial = await _db.Trials.FindAsync(trialId);
    var messages = await _db.Messages
        .Where(m => m.TrialId == trialId)
        .GroupBy(m => m.SenderId)
        .Select(g => new { UserId = g.Key, Count = g.Count() })
        .ToListAsync();

    var userAEngaged = messages.Any(m => m.UserId == trial.UserAId && m.Count >= _config.MinMessages);
    var userBEngaged = messages.Any(m => m.UserId == trial.UserBId && m.Count >= _config.MinMessages);

    if (userAEngaged && userBEngaged)
        return TrialResult.PASSED;

    if (DateTime.UtcNow > trial.EndsAt)
        return TrialResult.FAILED;

    return TrialResult.IN_PROGRESS;
}
```

---

## 7. Communication Rules

Rules for messaging between users.

### Message Access

| Stage | Can Message |
|-------|-------------|
| Before Match | No |
| Balloon Active | No (balloon must be popped) |
| Trial Period | Yes, limited |
| Full Connection | Yes, unlimited |

### Message Validation

```csharp
public async Task<SendResult> ValidateSend(Guid senderId, Guid threadId)
{
    var thread = await _db.Threads.FindAsync(threadId);

    // Check user is participant
    if (!IsParticipant(thread, senderId))
        return SendResult.Fail("Not authorized");

    // Check thread status allows messaging
    if (!CanMessage(thread.Status))
        return SendResult.Fail("Cannot message in current state");

    // Check rate limits
    if (await IsRateLimited(senderId))
        return SendResult.Fail("Rate limited");

    return SendResult.Success();
}
```

---

## 8. Profile Visibility Rules

Who can see what profile information.

### Visibility Matrix

| Viewer | Profile Visible | Photos | Full Name | Last Active |
|--------|----------------|--------|-----------|-------------|
| Stranger | No | No | No | No |
| Moment Candidate | Yes (limited) | Yes | First name only | No |
| Balloon Match | Yes | Yes | First name | General |
| Trial Match | Yes | Yes | Full name | Yes |
| Connected | Yes | Yes | Full name | Yes |

### Profile Privacy

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
            // Hide: LastName, Location details, Last active
        },
        Relationship.CONNECTED => new ProfileView
        {
            // Full profile visible
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

## 9. Block & Report Rules

User safety and moderation rules.

### Block Effects

| Effect | Description |
|--------|-------------|
| Mutual Invisibility | Neither user sees the other |
| End All Connections | Active balloons/threads close |
| Prevent Future Matches | Never shown to each other |
| One-way | Blocked user doesn't know |

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

## Business Rule Validation Checklist

When implementing features, verify:

- [ ] Budget is checked before any costly action
- [ ] Ratings only show when count >= 5
- [ ] State machines are respected
- [ ] Profile visibility follows relationship rules
- [ ] Blocked users cannot see or interact with each other
- [ ] Trial rules are enforced
- [ ] Moment themes feel like hypothetical questions
- [ ] All timing rules use UTC
