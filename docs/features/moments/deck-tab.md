# Deck Tab — Daily 5 Candidates

**File:** `frontend/woven-frontend/src/app/pages/moments/moments.page.ts`  
**Endpoint:** `GET /moments`  
**Active Tab:** `'today'`

## Overview

The Deck tab shows users their daily curated set of 5 candidates, selected by ECHO's matching algorithm. Users respond with Magical ◈, Resonant ◇, or Pass. Each choice consumes 1 of the daily 5-action budget.

## Data flow

### 1. Load Deck

**Frontend** ([moments.page.ts:217-240](file://C:\Users\gauta\Desktop\Woven\frontend\woven-frontend\src\app\pages\moments\moments.page.ts#L217-L240)):
```typescript
async loadToday() {
  this.loading = true;
  try {
    const data = await firstValueFrom(this.moments.getMoments());
    this.todayCards = data.cards ?? [];
    this.budget = data.budget ?? null;
    this.sparkBalance = data.sparkBalance ?? null;
    this.moodLine = data.moodLine ?? null;
    this.respondedUserIds.clear();
    this.cardShownAt.clear();
    const now = Date.now();
    for (const c of this.todayCards) {
      this.cardShownAt.set(c.userId, now);
    }
  } catch {
    this.error = "Couldn't load today's moments.";
  } finally {
    this.loading = false;
    this.cdr.markForCheck();
    this.initCinematic();
  }
}
```

**Backend** ([MomentsEndpoints.cs:37-249](file://C:\Users\gauta\Desktop\Woven\backend\WovenBackend\Endpoints\MomentsEndpoints.cs#L37-L249)):
1. Get or create daily deck via `IDailyDeckOrchestrator.GetOrCreateDeckAsync(userId, today, ct)`
2. Filter out already-responded candidates (check `moment_responses` table)
3. Load candidate data: photos, tiles, ratings, ECHO explanations
4. Return cards with `budget`, `sparkBalance`, `moodLine`

### 2. Card display

Each card shows:
- **Cinematic intro** (if available): Ken Burns photo slideshow + curated quote + narration audio
- **Profile photo** — Best CLIP match via `IVisualPreferenceService.GetBestPhotoUrlAsync(viewerId, candidateId, ct)`
- **Name + verified badge** (if `isVerified = true`)
- **Location** — `city, state` (if available)
- **Red/Green flag bar** — Community rating (shown only if `rating.count >= 5`)
- **"New here" badge** — Shown when rating count < 5
- **ECHO explanation** — `reason.headline` + `reason.bullets` (what caught ECHO's eye)
- **Tiles strip** — Up to 3 tiles (active Commons tiles first, pinned highlights fill remaining slots)
- **Actions** — Magical ◈ / Resonant ◇ / Pass —

### 3. User chooses Magical or Resonant

**Frontend** ([moments.page.ts:242-267](file://C:\Users\gauta\Desktop\Woven\frontend\woven-frontend\src\app\pages\moments\moments.page.ts#L242-L267)):
```typescript
choose(card: MomentsCard, action: 'magical' | 'logical' | 'pass') {
  if (this.choosingCard.size > 0) return;
  this.haptic(action === 'pass' ? 'light' : 'medium');

  if (action === 'pass') {
    this.choosingCard.set(card.userId, 'pass');
    this.cdr.markForCheck();
    setTimeout(() => { this.choosingCard.delete(card.userId); this.sendPass(card); }, 260);
    return;
  }

  const choice = action === 'magical' ? 'MAGICAL' : 'LOGICAL';
  const shownAt = this.cardShownAt.get(card.userId) ?? Date.now();

  this.choosingCard.set(card.userId, action);
  this.cdr.markForCheck();

  setTimeout(() => {
    this.choosingCard.delete(card.userId);
    this.overlayCard = { userId: card.userId, fullName: card.fullName, profilePhoto: card.profilePhoto, bridgeQuestion: card.reason?.bridgeQuestion ?? null };
    this.overlayChoice = choice;
    this.overlaySource = 'TODAY';
    this.overlayTimeOnCardMs = Date.now() - shownAt;
    this.cdr.markForCheck();
  }, 420);
}
```

**Flow:**
1. User taps Magical/Resonant → card gets `.choosingMagical` or `.choosingLogical` class (flash animation, 420ms)
2. After animation, ChatNote overlay opens with `bridgeQuestion` suggestion (if available)
3. User writes note (20-150 chars) and submits

### 4. User chooses Pass

**Frontend** ([moments.page.ts:277-297](file://C:\Users\gauta\Desktop\Woven\frontend\woven-frontend\src\app\pages\moments\moments.page.ts#L277-L297)):
```typescript
private async sendPass(card: MomentsCard) {
  this.respondedUserIds.add(card.userId);
  this.checkDeckCompletion();
  this.cdr.markForCheck();
  try {
    const shownAt = this.cardShownAt.get(card.userId) ?? Date.now();
    await firstValueFrom(
      this.moments.respond({
        targetUserId: card.userId,
        choice: 'PASS',
        source: 'TODAY',
        timeOnCardMs: Date.now() - shownAt,
      })
    );
  } catch {
    this.respondedUserIds.delete(card.userId);
    this.showToast('Something went wrong. Try again.');
  } finally {
    this.cdr.markForCheck();
  }
}
```

**Backend** ([MomentsEndpoints.cs:492-516](file://C:\Users\gauta\Desktop\Woven\backend\WovenBackend\Endpoints\MomentsEndpoints.cs#L492-L516)):
1. Record `MomentResponse` with `choice = PASS`
2. No budget spend (Pass is free but counts toward daily cap)
3. No match check (Pass never triggers match)
4. Return `{ status: "RECORDED_PASS" }`

## Budget mechanics

**Daily cap:** 5 total actions (Magical, Resonant, Pass all count)

**Database:** `daily_interactions` table
```sql
CREATE TABLE daily_interactions (
  user_id INT NOT NULL,
  date_utc DATE NOT NULL,
  total_used SMALLINT DEFAULT 0,
  total_cap SMALLINT DEFAULT 5,
  PRIMARY KEY (user_id, date_utc)
);
```

**Redis fast-gate:** Before DB check, `InteractionBudgetService` checks Redis counter:
- Key: `spark:{userId}:{dateUtc}`
- If `cachedTotal >= 5`, reject immediately → no DB hit
- On successful spend, increment Redis counter (best-effort)

**Budget display** ([moments.page.html:7-9](file://C:\Users\gauta\Desktop\Woven\frontend\woven-frontend\src\app\pages\moments\moments.page.html#L7-L9)):
```html
<div class="budgetPill" *ngIf="budget && activeTab === 'today'">
  {{ budget.totalRemaining }} / {{ budget.totalCap }} left
</div>
```

## Deck refresh logic

**When deck is built:**
1. Midnight UTC (automatic via `DailyDeckOrchestrator` batch worker)
2. First `GET /moments` call of the day (lazy create if worker failed)

**When deck is filtered:**
- Already-responded candidates removed from returned `cards` array
- Backend query: `WHERE r.DateUtc == today AND r.FromUserId == userId` → `respondedSet`
- Deck items filtered: `filteredItems = deckResult.Items.Where(i => !respondedSet.Contains(i.CandidateId))`

**Empty deck states:**

1. **All 5 responded to** ([moments.page.html:25-31](file://C:\Users\gauta\Desktop\Woven\frontend\woven-frontend\src\app\pages\moments\moments.page.html#L25-L31)):
```html
<div class="empty" *ngIf="!loading && !error && visibleTodayCards.length === 0" [class.deckCompleted]="deckCompleted">
  <div class="emptyGlow" *ngIf="deckCompleted"></div>
  <div class="emptyIcon">◈</div>
  <div class="emptyTitle">{{ deckCompleted ? 'You showed up today.' : 'That\'s everyone for today.' }}</div>
  <div class="emptySub">{{ deckCompleted ? 'New deck arrives tomorrow.' : 'Check back tomorrow.' }}</div>
  <button class="refreshBtn" (click)="loadToday()">Refresh</button>
</div>
```

2. **No candidates built** (rare — ECHO pool exhausted):
```json
{ "dateUtc": "2026-08-17", "budget": {...}, "sparkBalance": 5.0, "count": 0, "cards": [] }
```

## Deck completion animation

**Frontend** ([moments.page.ts:269-275](file://C:\Users\gauta\Desktop\Woven\frontend\woven-frontend\src\app\pages\moments\moments.page.ts#L269-L275)):
```typescript
private checkDeckCompletion() {
  if (this.visibleTodayCards.length === 0 && this.todayCards.length > 0 && !this.deckCompleted) {
    this.deckCompleted = true;
    this.haptic('heavy');
    this.cdr.markForCheck();
  }
}
```

Triggers when last visible card disappears (all 5 responded to). Shows glow animation + "You showed up today." message.

## Cinematic intro feature

**When available:** `kenBurnsPhotoUrls`, `curatedQuote`, `narrationUrl` are populated (Build N+1 feature — currently null for all cards)

**Frontend** ([moments.page.ts:105-166](file://C:\Users\gauta\Desktop\Woven\frontend\woven-frontend\src\app\pages\moments\moments.page.ts#L105-L166)):
1. Auto-cycles through 2-3 photos every 2.5 seconds
2. Curated quote fades in after first photo
3. Narration audio pre-loaded, play/pause button shown if `narrationUrl` exists
4. Audio stops when card disappears (cleanup in `ngOnDestroy`)

## Special badges

### "Already in your corner"

**Condition:** Candidate already chose the viewer today (from any source)

**Backend** ([MomentsEndpoints.cs:118-122](file://C:\Users\gauta\Desktop\Woven\backend\WovenBackend\Endpoints\MomentsEndpoints.cs#L118-L122)):
```csharp
var theyChoseMe = await db.MomentResponses.AsNoTracking()
    .Where(r => candidateIds.Contains(r.FromUserId) && r.ToUserId == userId && IsPositiveExpression(r.Choice))
    .Select(r => r.FromUserId)
    .ToListAsync(ct);
```

**Display** ([moments.page.html:67](file://C:\Users\gauta\Desktop\Woven\frontend\woven-frontend\src\app\pages\moments\moments.page.html#L67)):
```html
<div class="cornerBadge" *ngIf="c.alreadyChoseYou">Already in your corner.</div>
```

Shows in top-right corner of card photo. No special priority in deck order — purely informational.

## Mood line

**Generated by ECHO** at deck build time (`DailyDeck.MoodLine` field)

**Examples:**
- "Strong pool today."
- "Exploratory picks — we're calibrating."
- null (silence — shown as empty in UI)

**Display** ([moments.page.html:33-35](file://C:\Users\gauta\Desktop\Woven\frontend\woven-frontend\src\app\pages\moments\moments.page.html#L33-L35)):
```html
<div class="moodLine" *ngIf="!loading && !error && visibleTodayCards.length > 0 && moodLine">
  {{ moodLine }}
</div>
```

Appears above card feed when non-null.

## Related files

**Frontend:**
- `frontend/woven-frontend/src/app/pages/moments/moments.page.ts` — Main component
- `frontend/woven-frontend/src/app/pages/moments/moments.page.html` — Template
- `frontend/woven-frontend/src/app/services/moments.service.ts` — HTTP service

**Backend:**
- `backend/WovenBackend/Endpoints/MomentsEndpoints.cs` — Endpoints
- `backend/WovenBackend/Services/Matchmaking/DailyDeckOrchestrator.cs` — Deck builder
- `backend/WovenBackend/Services/Moments/InteractionBudgetService.cs` — Daily cap enforcement
- `backend/WovenBackend/data/Entities/DailyDeck.cs` — Deck entity
- `backend/WovenBackend/data/Entities/Moments/MomentResponse.cs` — Response entity
