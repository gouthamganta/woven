# Drawn Tab — Mutual Likes (Liked You)

**File:** `frontend/woven-frontend/src/app/pages/moments/moments.page.ts`  
**Endpoint:** `GET /moments/liked-you`  
**Active Tab:** `'liked-you'`

## Overview

The Drawn tab shows people who chose the viewer (with Magical or Resonant) in the last 7 days. Each action costs **1 spark** from the user's spark wallet. There is no Pass option on Drawn — users can only choose Magical ◈ or Resonant ◇.

## Why "Drawn"

Marketing name for the mutual-like tab. Internal ID: `'liked-you'`. Implies "people drawn to you" without explicitly saying "these people swiped right on you."

## Data flow

### 1. Load Drawn tab

**Frontend** ([moments.page.ts:95-101](file://C:\Users\gauta\Desktop\Woven\frontend\woven-frontend\src\app\pages\moments\moments.page.ts#L95-L101)):
```typescript
async switchTab(tab: Tab) {
  this.activeTab = tab;
  this.cdr.markForCheck();
  if (tab === 'liked-you' && !this.likedYouLoaded) {
    await this.loadLikedYou();
  }
}

async loadLikedYou() {
  this.loadingLikedYou = true;
  try {
    const data = await firstValueFrom(this.moments.getLikedYou());
    this.likedYouCards = data.cards ?? [];
    this.likedYouLoaded = true;
  } catch {
    this.likedYouError = "Couldn't load who liked you.";
  } finally {
    this.loadingLikedYou = false;
    this.cdr.markForCheck();
  }
}
```

**Lazy loading:** Drawn tab only fetches when user switches to it (not on initial page load).

**Backend** ([MomentsEndpoints.cs:251-429](file://C:\Users\gauta\Desktop\Woven\backend\WovenBackend\Endpoints\MomentsEndpoints.cs#L251-L429)):
1. Get cutoff timestamp: `DateTimeOffset.UtcNow.AddDays(-7)`
2. Query `moment_responses` where:
   - `ToUserId == me` (they chose me)
   - `IsPositiveExpression(r.Choice)` (MAGICAL, LOGICAL, or legacy YES)
   - `CreatedAt >= cutoff` (last 7 days)
   - Exclude blocked users (both directions)
   - Exclude already-matched users (`BalloonState.ACTIVE`)
   - Exclude today's Deck candidates (no overlap)
3. Deduplicate by `FromUserId` (keep most recent choice if they chose multiple times)
4. Load user data: photos, tiles, ratings, location
5. Calculate `expiresInHours` for each card: `(createdAt + 7 days - now).TotalHours`

### 2. Card display

Each card shows:
- **Profile photo** (first photo by `sort_order`)
- **Name + verified badge**
- **Location** — `city, state` (if available)
- **Expiry badge** — "7d left", "3h left", "Expiring soon"
- **Red/Green flag bar** (if `rating.count >= 5`)
- **"New here" badge** (if `rating.count < 5`)
- **Prompt text** — "They chose you. Do you feel it?" (replaces ECHO explanation)
- **Tiles strip** — Up to 3 tiles (same logic as Deck tab)
- **Actions** — Magical ◈ / Resonant ◇ (no Pass)

**Key differences from Deck tab:**
- No "Already in your corner" badge (they are literally in your corner — that's why they're here)
- No bridge question suggestion (ChatNote overlay simpler on Drawn)
- Expiry countdown instead of score/bucket
- Prompt replaces ECHO explanation

### 3. User chooses Magical or Resonant

**Frontend** ([moments.page.ts:321-337](file://C:\Users\gauta\Desktop\Woven\frontend\woven-frontend\src\app\pages\moments\moments.page.ts#L321-L337)):
```typescript
chooseLikedYou(card: LikedYouCard, action: 'magical' | 'logical') {
  if (this.choosingCard.size > 0) return;
  this.haptic('medium');
  const choice = action === 'magical' ? 'MAGICAL' : 'LOGICAL';

  this.choosingCard.set(card.userId, action);
  this.cdr.markForCheck();

  setTimeout(() => {
    this.choosingCard.delete(card.userId);
    this.overlayCard = { userId: card.userId, fullName: card.fullName, profilePhoto: card.profilePhoto, bridgeQuestion: null };
    this.overlayChoice = choice;
    this.overlaySource = 'LIKED_YOU';
    this.overlayTimeOnCardMs = null;
    this.cdr.markForCheck();
  }, 420);
}
```

**Differences from Deck `choose()`:**
- No Pass option
- `bridgeQuestion` always null (not shown on Drawn)
- `timeOnCardMs` null (Drawn cards don't track dwell time)
- `source = 'LIKED_YOU'`

### 4. Spark spend + match check

**Backend** ([MomentsEndpoints.cs:724-736](file://C:\Users\gauta\Desktop\Woven\backend\WovenBackend\Endpoints\MomentsEndpoints.cs#L724-L736)):
```csharp
if (isFromLikedYou)
{
    var sparkSpend = await sparks.TrySpendAsync(me, ct);
    if (!sparkSpend.Allowed)
        return Results.BadRequest(new { error = sparkSpend.DenyReason, sparkBalance = sparkSpend.Balance });
}
```

**Spark wallet logic** ([SparkWalletService.cs:28-64](file://C:\Users\gauta\Desktop\Woven\backend\WovenBackend\Services\Moments\SparkWalletService.cs#L28-L64)):
- **Daily earn:** 5 sparks/day (added at first call each day, not at midnight)
- **Max wallet:** 10 sparks (cap prevents infinite hoarding)
- **Spend:** 1 spark/action (atomic transaction, SERIALIZABLE isolation)
- **Ghost refund:** 0.5 sparks if match ends with no messages

## Spark economy details

### Earning sparks

**When:** First API call of the day that touches `SparkWalletService.GetBalanceAsync()` or `TrySpendAsync()`

**How much:** 5 sparks (stored as 50 tenths internally)

**Cap:** 10 sparks max — excess earned is truncated

**Table:** `spark_wallets`
```sql
CREATE TABLE spark_wallets (
  user_id INT PRIMARY KEY,
  balance_tenths INT DEFAULT 50,  -- 5.0 sparks
  last_earned_date DATE,
  updated_at TIMESTAMPTZ
);
```

### Spending sparks

**Transaction isolation:** SERIALIZABLE (prevents double-spend race conditions)

**Flow:**
1. Begin transaction
2. Earn daily sparks if due (inside transaction to prevent double-earn)
3. Check balance >= 10 tenths (1.0 spark)
4. If insufficient → rollback, return `{ allowed: false, denyReason: "INSUFFICIENT_SPARKS", balance }`
5. Deduct 10 tenths
6. Commit transaction
7. Return `{ allowed: true, denyReason: null, balance }`

### Ghost refund

**Trigger:** Match ends (`BalloonState.CLOSED`) with `BothMessagedAt == null` (neither user sent a message)

**Amount:** 0.5 sparks (5 tenths)

**Implementation:** Called from all 3 unmatch close paths:
- `POST /matches/{matchId}/pop` (balloon popped, trial started, user decided END/BLOCK)
- `BalloonExpiryWorker` (36h expiry, no messages exchanged)
- `POST /chats/{threadId}/unmatch` (explicit unmatch before trial)

**Code** ([SparkWalletService.cs:67-73](file://C:\Users\gauta\Desktop\Woven\backend\WovenBackend\Services\Moments\SparkWalletService.cs#L67-L73)):
```csharp
public async Task GhostRefundAsync(int userId, CancellationToken ct = default)
{
    var wallet = await EnsureWalletAsync(userId, ct);
    wallet.BalanceTenths = Math.Min(MaxBalanceTenths, wallet.BalanceTenths + GhostRefundTenths);
    wallet.UpdatedAt = DateTimeOffset.UtcNow;
    await _db.SaveChangesAsync(ct);
}
```

## 7-day expiry logic

**Calculation** ([MomentsEndpoints.cs:399-407](file://C:\Users\gauta\Desktop\Woven\backend\WovenBackend\Endpoints\MomentsEndpoints.cs#L399-L407)):
```csharp
var likedCutoff = DateTimeOffset.UtcNow;
var cards = unique
    .Where(r => userMap.ContainsKey(r.FromUserId))
    .Select(r =>
    {
        var u = userMap[r.FromUserId];
        var expiresAt = r.CreatedAt.AddDays(7);
        var hoursLeft = (int)Math.Max(0, (expiresAt - likedCutoff).TotalHours);
        // ...
        expiresInHours  = hoursLeft
    })
```

**Display** ([moments.page.ts:408-412](file://C:\Users\gauta\Desktop\Woven\frontend\woven-frontend\src\app\pages\moments\moments.page.ts#L408-L412)):
```typescript
expiryLabel(hours: number): string {
  if (hours <= 0) return 'Expiring soon';
  if (hours < 24) return `${hours}h left`;
  return `${Math.floor(hours / 24)}d left`;
}
```

**No cleanup worker:** Expired Drawn candidates simply stop appearing in `GET /moments/liked-you` results (cutoff filter). No DB cleanup needed.

## Exclusion filters

**Who is excluded from Drawn tab:**

1. **Blocked users** (both directions)
   - Query: `SELECT blocked_id FROM blocks WHERE blocker_id = me UNION SELECT blocker_id FROM blocks WHERE blocked_id = me`
   - Prevents harassment via repeated choices

2. **Already-matched users** (active balloons)
   - Query: `SELECT user_a_id, user_b_id FROM matches WHERE balloon_state = ACTIVE`
   - No point showing someone you already matched with

3. **Today's Deck candidates**
   - Query: `SELECT shown_user_id FROM candidate_exposures WHERE viewer_user_id = me AND date_utc = today`
   - Prevents duplicates across tabs (Deck takes priority)

4. **Users who chose >7 days ago**
   - Cutoff: `DateTimeOffset.UtcNow.AddDays(-7)`
   - Creates scarcity + urgency

## Spark balance display

**Frontend** ([moments.page.html:174-177](file://C:\Users\gauta\Desktop\Woven\frontend\woven-frontend\src\app\pages\moments\moments.page.html#L174-L177)):
```html
<div class="likedYouNote" *ngIf="!loadingLikedYou && visibleLikedYouCards.length > 0">
  Each action costs 1 spark
  <span class="sparkBalance" *ngIf="sparkBalance !== null">· ◈ {{ sparkBalance }} left</span>
</div>
```

**Updated after spend:** Backend returns `sparkBalance` in response → frontend updates local state:
```typescript
if (res?.sparkBalance != null) this.sparkBalance = res.sparkBalance;
```

## Empty state

**Frontend** ([moments.page.html:168-172](file://C:\Users\gauta\Desktop\Woven\frontend\woven-frontend\src\app\pages\moments\moments.page.html#L168-L172)):
```html
<div class="empty" *ngIf="!loadingLikedYou && !likedYouError && visibleLikedYouCards.length === 0">
  <div class="emptyIcon">◇</div>
  <div class="emptyTitle">No one yet.</div>
  <div class="emptySub">People who choose you appear here for 7 days.</div>
</div>
```

## Match creation from Drawn tab

**Same logic as Deck tab** — see `match-creation.md` for details.

**Key difference:** Source tracking
- `MomentResponse.Source = "LIKED_YOU"`
- Used for analytics (conversion rate by source)
- Does not affect match eligibility (ECHO still checks both responses exist)

## Related files

**Frontend:**
- `frontend/woven-frontend/src/app/pages/moments/moments.page.ts:305-319` — `loadLikedYou()`
- `frontend/woven-frontend/src/app/pages/moments/moments.page.ts:321-337` — `chooseLikedYou()`
- `frontend/woven-frontend/src/app/pages/moments/moments.page.html:162-272` — Template

**Backend:**
- `backend/WovenBackend/Endpoints/MomentsEndpoints.cs:251-429` — `GET /moments/liked-you`
- `backend/WovenBackend/Services/Moments/SparkWalletService.cs` — Spark economy
- `backend/WovenBackend/data/Entities/SparkWallet.cs` — Wallet entity
