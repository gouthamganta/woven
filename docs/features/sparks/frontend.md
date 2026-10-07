# Frontend: Spark Display

**Last Updated:** 2026-08-17

---

## User-Facing Display

### Location

**Page:** Moments (`/moments`)  
**Tab:** Drawn (Liked You)  
**Element:** Header text next to tab title

**HTML:** `frontend/woven-frontend/src/app/pages/moments/moments.page.html`

```html
<span class="sparkBalance" *ngIf="sparkBalance !== null">· ◈ {{ sparkBalance }} left</span>
```

**Example renders:**
- `· ◈ 7.5 left` (user has 7.5 sparks)
- `· ◈ 0 left` (user out of sparks)
- `· ◈ 10 left` (max balance)

**Not shown when:**
- `sparkBalance === null` (data not loaded yet)
- User on Today tab (spark balance only relevant to Drawn tab)

---

## Component State

### TypeScript

**File:** `frontend/woven-frontend/src/app/pages/moments/moments.page.ts`

```typescript
export class MomentsPageComponent implements OnInit, OnDestroy {
  sparkBalance: number | null = null;
  
  async ngOnInit() {
    await this.loadToday();  // Fetches spark balance
  }
  
  private async loadToday() {
    const data = await firstValueFrom(this.moments.getMoments());
    this.sparkBalance = data.sparkBalance ?? null;
    this.cdr.markForCheck();
  }
}
```

**State variable:** `sparkBalance: number | null`  
**Initial:** `null` (not loaded)  
**Updated:** After `GET /moments` response

---

## Data Flow

### 1. Initial Load

**Trigger:** User navigates to `/moments`  
**Request:** `GET /moments`  
**Response:**

```json
{
  "dateUtc": "2026-08-17",
  "budget": { "totalCap": 20, "totalUsed": 3, "totalRemaining": 17 },
  "sparkBalance": 7.5,
  "moodLine": "Your deck is refreshed",
  "count": 12,
  "cards": [...]
}
```

**Frontend:**
```typescript
this.sparkBalance = data.sparkBalance ?? null;  // 7.5
```

### 2. After Drawn Action

**Trigger:** User taps MAGICAL (◈) or LOGICAL (◇) on Drawn card  
**Request:** `POST /moments/choose`

**Request body:**
```json
{
  "targetUserId": 42,
  "choice": "MAGICAL",
  "noteText": "Love your energy!",
  "source": "LIKED_YOU",
  "timeOnCardMs": 8450
}
```

**Response (success):**
```json
{
  "status": "MATCH_CREATED",
  "matchId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "matchType": "EDGE",
  "edgeOwnerId": 42,
  "sparkBalance": 6.5
}
```

**Frontend:**
```typescript
async submitNote() {
  const res = await firstValueFrom(this.moments.choose(req));
  if (res?.sparkBalance != null) {
    this.sparkBalance = res.sparkBalance;  // Update UI immediately
    this.cdr.markForCheck();
  }
}
```

**Response (insufficient sparks):**
```json
{
  "error": "INSUFFICIENT_SPARKS",
  "sparkBalance": 0
}
```

**Frontend:** Shows toast error (implementation varies).

---

## Service Layer

### MomentsService

**File:** `frontend/woven-frontend/src/app/services/moments.service.ts`

```typescript
export type MomentsResponse = {
  dateUtc: string;
  budget: MomentsBudget;
  sparkBalance?: number;  // ✅ Included in /moments response
  moodLine?: string | null;
  count: number;
  cards: MomentsCard[];
};

export type ChooseResult = {
  status: string;
  matchId?: string;
  matchType?: string;
  edgeOwnerId?: number | null;
  reason?: string | null;
  error?: string;
  sparkBalance?: number;  // ✅ Included in /moments/choose response
};

@Injectable({ providedIn: 'root' })
export class MomentsService {
  getMoments(): Observable<MomentsResponse> {
    return this.http.get<MomentsResponse>(`${environment.apiUrl}/moments`);
  }

  choose(req: ChooseRequest): Observable<ChooseResult> {
    return this.http.post<ChooseResult>(`${environment.apiUrl}/moments/choose`, req);
  }
}
```

**No dedicated `/sparks/balance` endpoint call** — balance piggybacked on existing requests.

---

## Styling

### SCSS

**File:** `frontend/woven-frontend/src/app/pages/moments/moments.page.scss`

```scss
.sparkBalance {
  font-size: 0.9rem;
  color: var(--text-secondary);  // Muted color
  font-weight: 400;
  margin-left: 0.5rem;
}
```

**Visual hierarchy:**
- **Tab title:** Bold, primary color
- **Spark count:** Smaller, secondary color (less prominent)

**Why muted?**  
Sparks are intentionally **not** a hero feature. They're background friction, not a selling point.

---

## User Interactions

### Scenario 1: Sufficient Sparks

**Initial state:** `sparkBalance = 3.5`

**User action:**
1. Switches to Drawn tab
2. Sees "◈ 3.5 left" in header
3. Taps MAGICAL on a card
4. Submits chat note

**Result:**
- Backend deducts 1 spark
- Response includes `sparkBalance: 2.5`
- Frontend updates UI → "◈ 2.5 left"
- Match created, user navigates to chat thread

### Scenario 2: Insufficient Sparks

**Initial state:** `sparkBalance = 0`

**User action:**
1. Switches to Drawn tab
2. Sees "◈ 0 left" in header
3. Taps MAGICAL on a card

**Result:**
- Backend rejects with `INSUFFICIENT_SPARKS`
- Frontend shows toast: "Not enough sparks — come back tomorrow"
- Card remains in Drawn deck (action not recorded)

**UX note:** No paywall prompt. User must wait for daily earn (5 sparks at midnight UTC).

### Scenario 3: Balance Increases (Ghost Refund)

**Initial state:** `sparkBalance = 2.5`

**Background event:**
- Match closes with no messages → ghost refund fires (+0.5 sparks)
- User opens app next session

**User action:**
1. Navigates to `/moments`
2. `GET /moments` returns `sparkBalance: 3.0`

**Result:**
- UI shows "◈ 3.0 left"
- User sees increase, assumes daily earn or doesn't notice

**No notification** — refunds are silent.

---

## Edge Cases

### Balance Not Loaded

**Scenario:** API slow, user sees page before data arrives

**State:** `sparkBalance === null`  
**Render:** Spark count hidden (`*ngIf="sparkBalance !== null"`)  
**Effect:** User sees only tab title, no balance indicator

**Once loaded:** Balance appears via `cdr.markForCheck()`.

### Negative Balance (Bug State)

**Should never happen** — backend enforces `balance >= 0` before deduction.

**If it does:**
- Frontend renders "◈ -0.5 left" (no special handling)
- Visual bug, but no functional breakage

**Fix:** Backend validation prevents this (serializable transaction).

### Fractional Display

**Values:** `0.5`, `1.5`, `7.5`, etc.

**Display:** Full decimal shown (`{{ sparkBalance }}` = raw number)  
**Example:** `◈ 7.5 left`

**No rounding** — 0.5 increments are intentional (ghost refunds).

---

## Future Enhancements

### Low Spark Warning

**Proposed:** When `sparkBalance < 2`, show yellow glow on spark icon.

**Why:** Nudge users to be thoughtful about last few sparks.

**Not implemented** — current design keeps sparks minimal.

### Separate `/sparks/balance` Endpoint

**Current:** Balance fetched via `/moments`  
**Proposed:** Dedicated `GET /sparks/balance` for real-time polling

**Why:** Refresh balance without reloading entire deck  
**Status:** Not needed — balance updates on actions already

---

## Related Files

- **Component:** `frontend/woven-frontend/src/app/pages/moments/moments.page.ts`
- **Template:** `frontend/woven-frontend/src/app/pages/moments/moments.page.html`
- **Styles:** `frontend/woven-frontend/src/app/pages/moments/moments.page.scss`
- **Service:** `frontend/woven-frontend/src/app/services/moments.service.ts`
