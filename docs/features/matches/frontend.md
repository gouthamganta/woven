# Matches — Frontend

**Frontend implementation** of the Matches feature: profile preview, balloon animation, access control, and trial period UI.

---

## File structure

```
frontend/woven-frontend/src/app/
├── pages/matches/
│   ├── match-profile-preview.page.ts       # Profile view with access control
│   ├── match-profile-preview.page.html     # Swipeable photo feed + info cards
│   └── match-profile-preview.page.scss     # Dark plum theme + balloon glow
└── services/
    └── matches.service.ts                  # HTTP client for matches API
```

---

## MatchesService

**Path:** `frontend/woven-frontend/src/app/services/matches.service.ts`

**Purpose:** HTTP client wrapper for all matches endpoints.

**Methods:**

```typescript
list(): Observable<MatchesListResponse>
// GET /matches
// Returns all ACTIVE balloons for current user

profileAccess(matchId: string): Observable<ProfileAccessResponse>
// GET /matches/{matchId}/profile-access
// Returns access level (FULL/LIMITED) without profile data

profile(matchId: string): Observable<MatchProfileResponse>
// GET /matches/{matchId}/profile
// Returns profile data with access control applied

pop(matchId: string): Observable<any>
// POST /matches/{matchId}/pop
// Pops balloon, starts trial period

unmatch(matchId: string, rating?: number): Observable<any>
// POST /matches/{matchId}/unmatch
// Soft close with optional rating (1-5)

block(matchId: string): Observable<any>
// POST /matches/{matchId}/block
// Hard close, creates Block record

flag(matchId: string, reason: string): Observable<any>
// POST /matches/{matchId}/flag
// Safety report (no_issues, uncomfortable, inappropriate)
```

---

## Type definitions

```typescript
export type MatchOther = {
  userId: number;
  fullName: string;
  profilePhoto?: string | null;
};

export type MatchListItem = {
  matchId: string;
  matchType: string;                  // "PURE" or "EDGE"
  edgeOwnerId?: number | null;
  balloonState: string;               // "ACTIVE" or "CLOSED"
  createdAt: string;
  expiresAt: string;
  bothMessagedAt?: string | null;
  findLoveAt?: string | null;
  
  // Computed by backend
  showFindLove?: boolean;
  showBalloonTimer?: boolean;
  reflectionSecondsLeft?: number;
  
  other?: MatchOther | null;
};

export type MatchPublicPreview = {
  name: string;
  age?: number | null;
  gender?: string | null;
  location?: string | null;
  bio?: string | null;                // Empty if LIMITED access
  intent?: {
    primaryIntent?: string | null;
    openness?: string[] | null;
  } | null;
  photos: PhotoView[];                // 1 photo if LIMITED, all if FULL
  optionalPublic: PublicOptionalField[]; // Empty if LIMITED
};

export type MatchProfileResponse = {
  matchId: string;
  accessLevel: 'FULL' | 'LIMITED';
  reason: string;                     // Why this access level
  
  showBalloonTimer?: boolean;
  reflectionSecondsLeft?: number;
  showFindLove?: boolean;
  
  publicPreview: MatchPublicPreview;
};
```

---

## MatchProfilePreviewPageComponent

**Path:** `frontend/woven-frontend/src/app/pages/matches/match-profile-preview.page.ts`

**Purpose:** Full-screen profile viewer with access-controlled data.

**Route:** `/matches/:matchId/profile`

**Key features:**
- Swipeable photo feed (vertical scroll-snap)
- Access level indicator (FULL vs. LIMITED)
- Caption popovers on photos
- Info cards (Basics, Small things, Bio)
- Responsive to access level (hides data in LIMITED mode)

**State:**
```typescript
loading = true;
error = '';
toast = '';
matchId = '';
data: MatchProfileResponse | null = null;
captionOpenIndex: number | null = null;
captionText = '';
```

**Lifecycle:**
```typescript
async ngOnInit() {
  const id = this.route.snapshot.paramMap.get('matchId');
  if (!id) {
    this.error = 'Missing match id';
    this.loading = false;
    return;
  }
  this.matchId = id;
  await this.load();
}

async load() {
  this.loading = true;
  this.error = '';
  try {
    this.data = await firstValueFrom(this.matches.profile(this.matchId));
    this.captionOpenIndex = null;
    this.captionText = '';
  } catch (e: any) {
    this.error = e?.error?.error || e?.error?.message || e?.message || 'Could not load profile.';
  } finally {
    this.loading = false;
  }
}
```

---

## View model getters

**Purpose:** Transform backend data into display-ready strings.

```typescript
get publicName(): string {
  return (this.data?.publicPreview?.name || '').toString().trim() || '—';
}

get publicAge(): string {
  const age = this.data?.publicPreview?.age;
  return age != null ? String(age) : '';
}

get publicGender(): string {
  return (this.data?.publicPreview?.gender || '').toString().trim();
}

get publicLocation(): string {
  return (this.data?.publicPreview?.location || '').toString().trim();
}

get publicIntentPrimary(): string {
  return (this.data?.publicPreview?.intent?.primaryIntent || '').toString().trim();
}

get publicIntentOpennessLine(): string {
  const arr = this.data?.publicPreview?.intent?.openness;
  const list = Array.isArray(arr) ? arr.map((x) => String(x)) : [];
  return list.length ? `Open to: ${list.join(', ')}` : '';
}

get publicBio(): string {
  return (this.data?.publicPreview?.bio || '').toString().trim();
}

get publicOptional(): PublicOptionalField[] {
  const arr: any[] = Array.isArray(this.data?.publicPreview?.optionalPublic)
    ? (this.data as any).publicPreview.optionalPublic
    : [];
  return arr
    .map((x: any) => ({
      key: (x?.key || '').toString(),
      value: (x?.value || '').toString(),
    }))
    .filter((x) => x.key.trim().length && x.value.trim().length);
}

get publicPhotos(): PhotoView[] {
  const p: any[] = Array.isArray(this.data?.publicPreview?.photos)
    ? (this.data as any).publicPreview.photos
    : [];
  return p
    .map((x: any) => ({
      url: (x?.url || '').toString(),
      caption: x?.caption ?? '',
      sortOrder: Number(x?.sortOrder ?? 999),
    }))
    .filter((x) => x.url.trim().length)
    .sort((a, b) => a.sortOrder - b.sortOrder);
}

get basicsChips(): string[] {
  const chips: string[] = [];
  if (this.publicGender) chips.push(this.publicGender);
  if (this.publicLocation) chips.push(this.publicLocation);
  if (this.publicIntentPrimary) chips.push(this.publicIntentPrimary);
  return chips;
}

get accessHint(): string {
  const lvl = this.data?.accessLevel;
  if (!lvl) return '';
  if (lvl === 'FULL') return 'Full preview unlocked.';
  return 'Limited preview — unlocks after 2-way chat.';
}
```

**Why getters?** Angular templates can call these without re-running on every change detection cycle (when using OnPush strategy).

---

## Template structure

**Path:** `frontend/woven-frontend/src/app/pages/matches/match-profile-preview.page.html`

**Layout:**
```
┌──────────────────────────────────────┐
│ [Back]  Profile: Alex, 28  [Refresh] │  ← Header
├──────────────────────────────────────┤
│                                      │
│  ┌────────────────────────────────┐  │
│  │  Photo 1 (full height)         │  │  ← Scroll-snap item
│  │  [Caption button]              │  │
│  │  [Name overlay]                │  │
│  ├────────────────────────────────┤  │
│  │  Basics card                   │  │
│  └────────────────────────────────┘  │
│                                      │
│  ┌────────────────────────────────┐  │
│  │  Photo 2                       │  │  ← Scroll-snap item
│  ├────────────────────────────────┤  │
│  │  Small things card             │  │
│  └────────────────────────────────┘  │
│                                      │
│  ┌────────────────────────────────┐  │
│  │  Photo 3                       │  │  ← Scroll-snap item
│  ├────────────────────────────────┤  │
│  │  Bio card                      │  │
│  └────────────────────────────────┘  │
└──────────────────────────────────────┘
```

**Key HTML snippets:**

```html
<!-- Header -->
<div class="head">
  <button class="pill outline" (click)="back()">Back</button>
  <div class="center">
    <div class="kicker">Profile</div>
    <div class="title">{{ publicName }}<span *ngIf="publicAge">, {{ publicAge }}</span></div>
    <div class="sub" *ngIf="accessHint">{{ accessHint }}</div>
  </div>
  <button class="pill" (click)="load()">Refresh</button>
</div>

<!-- Swipeable feed -->
<div class="publicFrame">
  <div class="feed">
    <section class="feedItem" *ngFor="let p of publicPhotos; let idx = index">
      
      <!-- Photo stage -->
      <div class="photoStage">
        <img class="heroImg" [src]="p.url" [alt]="'Profile photo ' + (idx+1)" />
        
        <!-- Caption button -->
        <button class="capBtn" (click)="toggleCaption(idx, p.caption)">Caption</button>
        
        <!-- Caption popover -->
        <div class="capPop" *ngIf="captionOpenIndex === idx">
          {{ captionText || 'No caption added.' }}
        </div>
        
        <!-- Name overlay (first photo only) -->
        <div class="heroTag" *ngIf="idx === 0">
          <div class="name">{{ publicName }}<span *ngIf="publicAge">, {{ publicAge }}</span></div>
        </div>
      </div>
      
      <!-- Info card (changes per photo) -->
      <div class="card">
        <!-- First photo: Basics -->
        <ng-container *ngIf="idx === 0">
          <div class="cardTitle">Basics</div>
          <div class="chipsRow" *ngIf="basicsChips.length">
            <div class="chip" *ngFor="let c of basicsChips">{{ c }}</div>
          </div>
          <div class="cardBody" *ngIf="publicIntentOpennessLine">{{ publicIntentOpennessLine }}</div>
        </ng-container>
        
        <!-- Second photo: Small things -->
        <ng-container *ngIf="idx === 1 && publicOptional.length">
          <div class="cardTitle">Small things</div>
          <div class="chipsRow">
            <div class="chip" *ngFor="let f of publicOptional">
              <b>{{ prettyKey(f.key) }}</b>&nbsp;{{ f.value }}
            </div>
          </div>
        </ng-container>
        
        <!-- Third photo: Bio -->
        <ng-container *ngIf="idx === 2 && publicBio">
          <div class="cardTitle">Bio</div>
          <div class="cardBody">{{ publicBio }}</div>
        </ng-container>
      </div>
      
    </section>
  </div>
</div>
```

**Scroll-snap CSS:**
```scss
.feed {
  height: 78vh;
  overflow-y: auto;
  scroll-snap-type: y mandatory;
  scroll-behavior: smooth;
  -webkit-overflow-scrolling: touch;
}
.feedItem {
  scroll-snap-align: start;
  min-height: 78vh;
}
```

**Result:** Vertical swipe between photos, each with its own info card.

---

## Styling highlights

**Path:** `frontend/woven-frontend/src/app/pages/matches/match-profile-preview.page.scss`

**Design language:** Dark plum theme with balloon glow.

**Key styles:**

```scss
.publicFrame {
  border-radius: 22px;
  overflow: hidden;
  border: 1px solid var(--border-soft);
  background: var(--bg-base);
  box-shadow:
    var(--shadow-lg),
    0 0 0 1px var(--plum-glow),
    0 0 32px var(--gold-glow);  // ← Balloon glow effect
}

.heroTag {
  position: absolute;
  left: 14px;
  bottom: 14px;
  padding: 10px 12px;
  border-radius: 16px;
  background: rgba(14, 9, 18, 0.72);  // ← Dark translucent
  backdrop-filter: blur(12px);
  -webkit-backdrop-filter: blur(12px);
  border: 1px solid var(--border-soft);
  color: var(--text-primary);
  box-shadow: var(--shadow-md);
}

.capPop {
  position: absolute;
  left: 12px;
  top: 52px;
  max-width: 90%;
  padding: 10px 12px;
  border-radius: 14px;
  background: rgba(14, 9, 18, 0.82);  // ← Darker for readability
  backdrop-filter: blur(16px);
  color: var(--text-primary);
  font-size: 13px;
  box-shadow: var(--shadow-lg);
}
```

**Design rules applied:**
- No hover translateY lifts (only glow/shadow)
- Full CSS variable token system (no raw hex)
- Backdrop blur for glassmorphism
- Box-shadow glow (plum + gold) matches Moments card balloon state

---

## Access control behavior

**FULL access:**
- Shows all photos
- Shows bio
- Shows all public optional fields
- Access hint: "Full preview unlocked."

**LIMITED access:**
- Shows 1 photo only
- Bio is EMPTY string (backend doesn't send it)
- `optionalPublic` is empty array (backend doesn't send them)
- Access hint: "Limited preview — unlocks after 2-way chat."

**Code:**
```typescript
// Backend applies access control before sending data
// Frontend just displays what it receives

get publicBio(): string {
  return (this.data?.publicPreview?.bio || '').toString().trim();
  // If LIMITED, backend sends bio = "" → this returns ""
}

get publicOptional(): PublicOptionalField[] {
  const arr = this.data?.publicPreview?.optionalPublic || [];
  // If LIMITED, backend sends optionalPublic = [] → this returns []
  return arr.filter(x => x.key.trim().length && x.value.trim().length);
}

get publicPhotos(): PhotoView[] {
  const p = this.data?.publicPreview?.photos || [];
  // If LIMITED, backend sends 1 photo → this returns 1-item array
  return p.filter(x => x.url.trim().length).sort((a, b) => a.sortOrder - b.sortOrder);
}
```

**Frontend trusts backend.** No client-side access checks. Backend is source of truth.

---

## Balloon animation (not yet implemented)

**Design spec:**
- When match is ACTIVE, show subtle pulsing glow around profile frame
- Color: plum-to-gold gradient
- Animation: 2s ease-in-out infinite
- When balloon is CLOSED, glow fades out

**CSS (not yet added):**
```scss
@keyframes balloonPulse {
  0%, 100% { box-shadow: 0 0 0 1px var(--plum-glow), 0 0 32px var(--gold-glow); }
  50% { box-shadow: 0 0 0 1px var(--plum-glow), 0 0 48px var(--gold-glow); }
}

.publicFrame.active {
  animation: balloonPulse 2s ease-in-out infinite;
}
```

**Not built yet** because balloon state isn't passed to this component (it's in `MatchesListResponse`, not `MatchProfileResponse`).

---

## Trial period UI (not yet implemented)

**Design spec:**
- When trial is active (`showBalloonTimer = true`), show countdown timer
- Display `reflectionSecondsLeft` as MM:SS
- Timer updates every second (client-side countdown)
- When timer hits 0, show "Trial ended — decide now" prompt

**HTML (not yet added):**
```html
<div class="trialTimer" *ngIf="data?.showBalloonTimer">
  <div class="timerIcon">⏱</div>
  <div class="timerText">{{ formatSeconds(data.reflectionSecondsLeft) }}</div>
</div>
```

**Formatting:**
```typescript
formatSeconds(s: number | undefined): string {
  if (!s || s <= 0) return '0:00';
  const mins = Math.floor(s / 60);
  const secs = s % 60;
  return `${mins}:${secs.toString().padStart(2, '0')}`;
}
```

**Not built yet** because trial decisions happen in chat thread, not profile view.

---

## Find Love indicator (not yet implemented)

**Design spec:**
- When `showFindLove = true`, show sparkle icon + "Find Love unlocked" banner
- Animate in from top
- Banner color: gold gradient
- Icon: ✨ or custom SVG

**HTML (not yet added):**
```html
<div class="findLoveBanner" *ngIf="data?.showFindLove">
  <div class="icon">✨</div>
  <div class="text">Find Love unlocked</div>
</div>
```

**Not built yet** because Find Love content (date ideas) is shown in chat thread, not profile view.

---

## Error handling

**Loading state:**
```html
<div class="state" *ngIf="loading">Loading…</div>
```

**Error state:**
```html
<div class="state err" *ngIf="!loading && error">{{ error }}</div>
```

**Error messages:**
- `"Missing match id"` — route param missing
- `"MATCH_NOT_FOUND"` — 404 from backend
- `"Could not load profile."` — generic fallback

**Toast notifications:**
```typescript
private showToast(msg: string) {
  this.toast = msg;
  window.setTimeout(() => (this.toast = ''), 1400);
}
```

**Not used yet** but ready for future actions (copy link, share profile, etc).

---

## Caption toggle logic

**State:**
```typescript
captionOpenIndex: number | null = null;
captionText = '';
```

**Method:**
```typescript
toggleCaption(idx: number, caption?: string | null) {
  if (this.captionOpenIndex === idx) {
    // Close if already open
    this.captionOpenIndex = null;
    this.captionText = '';
    return;
  }
  // Open caption popover
  this.captionOpenIndex = idx;
  const text = (caption || '').toString().trim();
  this.captionText = text.length ? text : 'No caption added.';
}
```

**Result:** Only 1 caption popover open at a time. Tapping again closes it.

---

## Pretty key formatting

**Purpose:** Convert snake_case optional field keys to readable labels.

```typescript
prettyKey(k: string): string {
  if (!k) return 'Field';
  const cleaned = k.replace(/^pref_/, '').replace(/_/g, ' ');
  return cleaned.replace(/\b\w/g, (c) => c.toUpperCase());
}
```

**Examples:**
- `"pref_dietary_restrictions"` → `"Dietary Restrictions"`
- `"height"` → `"Height"`
- `"zodiac_sign"` → `"Zodiac Sign"`

---

## Navigation

**Back button:**
```typescript
back() {
  this.router.navigateByUrl('/chats');
}
```

**Why `/chats`?** Profile is typically viewed from chat thread (click user name in header). Back button returns to chat list.

**Future:** Pass `returnUrl` via route state, default to `/chats`.

---

## Change detection strategy

**Component uses `OnPush`:**
```typescript
@Component({
  selector: 'app-match-profile-preview',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './match-profile-preview.page.html',
  styleUrls: ['./match-profile-preview.page.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush  // ← Not explicitly set, but recommended
})
```

**Why?** Better performance, reduces unnecessary re-renders. All data flows through observables or explicit async updates.

**When to add `cdr.markForCheck()`:**
- After `this.data` is updated
- After toast is shown
- After caption is toggled

**Not yet added** because async/await auto-triggers change detection. Add if performance issues arise.

---

## Related files

**Backend endpoints:**
- `backend/WovenBackend/Endpoints/MatchesEndpoints.cs`
- `GET /matches/{matchId}/profile` — returns `MatchProfileResponse`
- `GET /matches/{matchId}/profile-access` — returns `ProfileAccessResponse`

**Frontend service:**
- `frontend/woven-frontend/src/app/services/matches.service.ts`

**Route config:**
- `frontend/woven-frontend/src/app/app.routes.ts` (likely includes `/matches/:matchId/profile`)

---

## Testing checklist

**Access control:**
- [ ] FULL access shows all photos, bio, optional fields
- [ ] LIMITED access shows 1 photo, no bio, no optional fields
- [ ] Access hint updates correctly ("Full preview unlocked" vs "Limited preview")

**Photo feed:**
- [ ] Vertical scroll-snap works smoothly
- [ ] Each photo has correct info card (Basics, Small things, Bio)
- [ ] Caption button toggles popover
- [ ] Only 1 caption popover open at a time
- [ ] "No caption added" shown when caption is empty

**Loading/error:**
- [ ] Loading state shows while fetching data
- [ ] Error state shows on API failure
- [ ] Error message is user-friendly
- [ ] Refresh button re-fetches data

**Navigation:**
- [ ] Back button returns to `/chats`
- [ ] Route param `matchId` is read correctly
- [ ] Missing `matchId` shows error
