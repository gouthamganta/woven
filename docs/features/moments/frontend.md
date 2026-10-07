# Frontend Implementation — Angular Moments Page

**Directory:** `frontend/woven-frontend/src/app/pages/moments/`  
**Main Files:**
- `moments.page.ts` — Component logic
- `moments.page.html` — Template
- `moments.page.scss` — Styles
- `chat-note-overlay.component.ts` — ChatNote overlay (inline template/styles)

**Service:** `frontend/woven-frontend/src/app/services/moments.service.ts`

## Component structure

**Component:** `MomentsPageComponent` ([moments.page.ts:23-426](file://C:\Users\gauta\Desktop\Woven\frontend\woven-frontend\src\app\pages\moments\moments.page.ts#L23-L426))

### Configuration

```typescript
@Component({
  selector: 'app-moments-page',
  standalone: true,
  imports: [CommonModule, ChatNoteOverlayComponent],
  templateUrl: './moments.page.html',
  styleUrls: ['./moments.page.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,  // CRITICAL: requires manual cdr.markForCheck()
})
```

**OnPush strategy:** All async updates must call `this.cdr.markForCheck()` to trigger change detection.

### State properties

**Tab state:**
```typescript
activeTab: Tab = 'today';  // 'today' | 'liked-you'
type Tab = 'today' | 'liked-you';
```

**Deck tab state:**
```typescript
loading = true;
error = '';
todayCards: MomentsCard[] = [];
budget: MomentsBudget | null = null;
sparkBalance: number | null = null;
moodLine: string | null = null;
respondedUserIds = new Set<number>();       // Optimistic UI — hide responded cards
cardShownAt = new Map<number, number>();    // Track dwell time (timeOnCardMs)
```

**Drawn tab state:**
```typescript
loadingLikedYou = false;
likedYouCards: LikedYouCard[] = [];
likedYouLoaded = false;                     // Lazy load — only fetch when tab opened
likedYouError = '';
respondedLikedYouIds = new Set<number>();
```

**ChatNote overlay state:**
```typescript
overlayCard: OverlayCard | null = null;
overlayChoice: 'MAGICAL' | 'LOGICAL' | null = null;
overlaySource: 'TODAY' | 'LIKED_YOU' = 'TODAY';
overlayTimeOnCardMs: number | null = null;
```

**Animation state:**
```typescript
choosingCard = new Map<number, string>();   // userId → 'magical'|'logical'|'pass' (flash animation)
deckCompleted = false;                      // Show completion glow
toast = '';                                 // Toast message (auto-clear after 2.5s)
```

**Cinematic intro state:**
```typescript
cinematicPhoto = new Map<number, number>();         // userId → current photo index
quoteVisible   = new Set<number>();                 // userId → quote overlay visible
audioPlaying   = new Set<number>();                 // userId → audio currently playing
private cinematicIntervals = new Map<number, ReturnType<typeof setInterval>>();
private audioElements      = new Map<number, HTMLAudioElement>();
```

**Photo gallery state (long-press):**
```typescript
galleryPhotos: string[] = [];
galleryIndex = 0;
private longPressTimer?: ReturnType<typeof setTimeout>;
```

## Lifecycle hooks

### ngOnInit

```typescript
async ngOnInit() {
  await this.loadToday();
  this.initCinematic();
}
```

Loads Deck tab data on page mount. Cinematic intro auto-starts for all cards with `kenBurnsPhotoUrls`.

### ngOnDestroy

```typescript
ngOnDestroy() {
  this.cinematicIntervals.forEach(i => clearInterval(i));
  this.audioElements.forEach(a => { a.pause(); a.src = ''; });
  if (this.longPressTimer) clearTimeout(this.longPressTimer);
}
```

**Critical:** Cleans up intervals, audio elements, and timers to prevent memory leaks.

## Key methods

### Tab switching

```typescript
async switchTab(tab: Tab) {
  this.activeTab = tab;
  this.cdr.markForCheck();
  if (tab === 'liked-you' && !this.likedYouLoaded) {
    await this.loadLikedYou();
  }
}
```

**Lazy load:** Drawn tab only fetches on first switch.

### Load Deck (GET /moments)

```typescript
async loadToday() {
  this.loading = true;
  this.error = '';
  this.cdr.markForCheck();
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
      this.cardShownAt.set(c.userId, now);  // Start dwell timer
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

**Dwell time tracking:** `cardShownAt` captures when each card first appeared. Used to calculate `timeOnCardMs` on choice.

### Load Drawn (GET /moments/liked-you)

```typescript
async loadLikedYou() {
  this.loadingLikedYou = true;
  this.likedYouError = '';
  this.cdr.markForCheck();
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

### Choose (Deck tab)

```typescript
choose(card: MomentsCard, action: 'magical' | 'logical' | 'pass') {
  if (this.choosingCard.size > 0) return;  // Prevent double-tap
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
1. Set `choosingCard` → triggers `.choosingMagical`/`.choosingLogical`/`.choosingPass` CSS class
2. Wait 420ms (flash animation duration)
3. Pass → call `sendPass()` immediately; Magical/Resonant → open ChatNote overlay

### Send Pass (POST /moments/respond)

```typescript
private async sendPass(card: MomentsCard) {
  this.respondedUserIds.add(card.userId);  // Optimistic: hide card immediately
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
    this.respondedUserIds.delete(card.userId);  // Rollback on error
    this.showToast('Something went wrong. Try again.');
  } finally {
    this.cdr.markForCheck();
  }
}
```

**Optimistic UI:** Card disappears immediately. If API fails, card reappears + error toast.

### Choose Liked You (Drawn tab)

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
    this.overlayTimeOnCardMs = null;  // No dwell tracking on Drawn
    this.cdr.markForCheck();
  }, 420);
}
```

**Differences from Deck `choose()`:**
- No Pass option (Drawn tab only has Magical/Resonant)
- `bridgeQuestion = null` (no ECHO suggestions on Drawn)
- `timeOnCardMs = null` (no dwell tracking)

### Overlay submit handler (POST /moments/choose)

```typescript
async onOverlaySubmit(noteText: string) {
  if (!this.overlayCard || !this.overlayChoice) return;

  const { userId } = this.overlayCard;
  const choice = this.overlayChoice;
  const source = this.overlaySource;
  const timeOnCardMs = this.overlayTimeOnCardMs;

  // Optimistically hide and close overlay
  if (source === 'TODAY') {
    this.respondedUserIds.add(userId);
    this.checkDeckCompletion();
  } else {
    this.respondedLikedYouIds.add(userId);
  }
  this.overlayCard = null;
  this.overlayChoice = null;
  this.cdr.markForCheck();

  try {
    const res = await firstValueFrom(
      this.moments.choose({ targetUserId: userId, choice, noteText, source, timeOnCardMs })
    );

    if (res?.sparkBalance != null) this.sparkBalance = res.sparkBalance;

    if (res?.matchId && (res.status === 'PURE_MATCH_CREATED' || res.status === 'EDGE_MATCH_CREATED')) {
      const copy = this.matchRevealCopy(res.matchType ?? '', res.status);
      this.showToast(copy);
      const started = await firstValueFrom(this.chat.start(res.matchId));
      this.router.navigateByUrl(`/chats/${started.threadId}`);
      return;
    }

    if (res.status === 'WAITING_FOR_OTHER_NOTE') {
      this.showToast(choice === 'MAGICAL' ? '◈ Sent — waiting for them' : '◇ Sent — waiting for them');
    } else {
      this.showToast(choice === 'MAGICAL' ? '◈ Sent' : '◇ Sent');
    }
  } catch {
    if (source === 'TODAY') this.respondedUserIds.delete(userId);
    else this.respondedLikedYouIds.delete(userId);
    this.showToast('Something went wrong. Try again.');
  } finally {
    this.cdr.markForCheck();
  }
}
```

**Match created flow:**
1. Show toast ("You both felt it. ◈" or "Different angles, same pull. ◇◈")
2. Call `ChatService.start(matchId)` to create chat thread
3. Navigate to `/chats/{threadId}`

**Waiting flow:**
1. Show toast ("◈ Sent — waiting for them")
2. Card stays hidden (already added to `respondedUserIds`)

## Cinematic intro (Ken Burns feature)

**When available:** Cards with `kenBurnsPhotoUrls` (currently null — Build N+1 feature)

### Auto-start on init

```typescript
private initCinematic() {
  if (typeof window === 'undefined') return;
  for (const card of this.todayCards) {
    if (card.kenBurnsPhotoUrls?.length) this.startCinematicForCard(card);
  }
}
```

### Photo cycling

```typescript
private startCinematicForCard(card: MomentsCard) {
  const userId = card.userId;
  const photos = card.kenBurnsPhotoUrls!;
  if (this.cinematicIntervals.has(userId)) return;

  this.cinematicPhoto.set(userId, 0);
  let idx = 0;

  const tick = () => {
    idx = (idx + 1) % photos.length;
    this.cinematicPhoto.set(userId, idx);
    this.quoteVisible.add(userId);
    this.cdr.markForCheck();
  };
  const interval = setInterval(tick, 2500);
  this.cinematicIntervals.set(userId, interval);

  // Show quote after first photo if only 1 photo
  if (photos.length === 1) {
    setTimeout(() => { this.quoteVisible.add(userId); this.cdr.markForCheck(); }, 2500);
  }

  // Pre-load audio
  if (card.narrationUrl && typeof window !== 'undefined') {
    const audio = new Audio(card.narrationUrl);
    audio.preload = 'auto';
    audio.onended = () => { this.audioPlaying.delete(userId); this.cdr.markForCheck(); };
    this.audioElements.set(userId, audio);
  }
}
```

**Cycle duration:** 2500ms per photo

**Quote fade-in:** After first photo cycle completes

**Audio:** Pre-loaded via `new Audio()`, play/pause via button

### Narration toggle

```typescript
toggleNarration(card: MomentsCard, ev: MouseEvent) {
  ev.stopPropagation();
  const audio = this.audioElements.get(card.userId);
  if (!audio) return;
  if (audio.paused) {
    audio.play().then(() => { this.audioPlaying.add(card.userId); this.cdr.markForCheck(); }).catch(() => {});
  } else {
    audio.pause();
    this.audioPlaying.delete(card.userId);
    this.cdr.markForCheck();
  }
}
```

## Photo gallery (long-press)

**Trigger:** Long-press (500ms) on any card photo → opens fullscreen gallery

```typescript
onPhotoLongPressStart(card: MomentsCard | LikedYouCard, ev: Event) {
  if (this.longPressTimer) clearTimeout(this.longPressTimer);
  const photos = card.photos?.length ? card.photos : (card.profilePhoto ? [card.profilePhoto] : []);
  if (photos.length === 0) return;
  this.longPressTimer = setTimeout(() => {
    this.galleryPhotos = photos;
    this.galleryIndex  = 0;
    this.cdr.markForCheck();
  }, 500);
}

onPhotoLongPressEnd() {
  if (this.longPressTimer) {
    clearTimeout(this.longPressTimer);
    this.longPressTimer = undefined;
  }
}
```

**Gallery controls:**
- Left/right nav arrows (if `photos.length > 1`)
- Dot indicators (tap to jump to photo)
- Close button (top-right)

## Haptic feedback

```typescript
private haptic(intensity: 'light' | 'medium' | 'heavy' = 'light') {
  if (typeof navigator === 'undefined' || !('vibrate' in navigator)) return;
  const pattern: Record<string, number | number[]> = {
    light:  10,
    medium: [12, 8, 12],
    heavy:  [30, 10, 30],
  };
  try { navigator.vibrate(pattern[intensity]); } catch { /* ignore */ }
}
```

**Usage:**
- **Pass:** `light`
- **Magical/Resonant:** `medium`
- **Deck completion:** `heavy`

## MomentsService (HTTP)

**File:** `frontend/woven-frontend/src/app/services/moments.service.ts`

### Type definitions

```typescript
export type MomentAction = 'MAGICAL' | 'LOGICAL' | 'PASS';
export type MomentSource = 'TODAY' | 'LIKED_YOU' | null;

export type MomentsCard = {
  userId: number;
  fullName: string;
  profilePhoto?: string | null;
  gender?: string | null;
  displayPronouns?: string | null;
  location?: { city?: string | null; state?: string | null } | null;
  isVerified?: boolean | null;
  score?: number | null;
  bucket?: string | null;
  alreadyChoseYou?: boolean;
  reason?: MatchReason | null;
  rating?: { average: number; count: number; show: boolean } | null;
  photos?: string[] | null;
  highlightedTiles?: HighlightedTile[] | null;
  kenBurnsPhotoUrls?: string[] | null;
  curatedQuote?: string | null;
  narrationUrl?: string | null;
  narrationExposed?: boolean;
};

export type LikedYouCard = {
  userId: number;
  fullName: string;
  profilePhoto?: string | null;
  location?: { city?: string | null; state?: string | null } | null;
  isVerified?: boolean | null;
  likedAt: string;
  expiresInHours: number;
  photos?: string[] | null;
  highlightedTiles?: HighlightedTile[] | null;
  rating?: { average: number; count: number; show: boolean } | null;
};

export type MomentsBudget = {
  totalCap: number;
  totalUsed: number;
  totalRemaining: number;
};
```

### HTTP methods

```typescript
@Injectable({ providedIn: 'root' })
export class MomentsService {
  constructor(private http: HttpClient) {}

  getMoments(): Observable<MomentsResponse> {
    return this.http.get<MomentsResponse>(`${environment.apiUrl}/moments`);
  }

  getLikedYou(): Observable<LikedYouResponse> {
    return this.http.get<LikedYouResponse>(`${environment.apiUrl}/moments/liked-you`);
  }

  respond(req: RespondRequest): Observable<RespondResult> {
    return this.http.post<RespondResult>(`${environment.apiUrl}/moments/respond`, req);
  }

  choose(req: ChooseRequest): Observable<ChooseResult> {
    return this.http.post<ChooseResult>(`${environment.apiUrl}/moments/choose`, req);
  }
}
```

**Usage:** Always wrapped in `firstValueFrom()` for async/await syntax:
```typescript
const data = await firstValueFrom(this.moments.getMoments());
```

## Template structure

**File:** `frontend/woven-frontend/src/app/pages/moments/moments.page.html`

### Header

```html
<div class="head">
  <div class="headLeft">
    <div class="kicker">MOMENTS</div>
    <div class="budgetPill" *ngIf="budget && activeTab === 'today'">
      {{ budget.totalRemaining }} / {{ budget.totalCap }} left
    </div>
  </div>
  <div class="tabs">
    <button class="tab" [class.active]="activeTab === 'today'" (click)="switchTab('today')">Deck</button>
    <button class="tab" [class.active]="activeTab === 'liked-you'" (click)="switchTab('liked-you')">Drawn</button>
  </div>
</div>
```

### Deck tab

- **Loading state:** "Warming up…"
- **Error state:** Error message (red)
- **Empty state:** "You showed up today." (if `deckCompleted`) or "That's everyone for today."
- **Mood line:** ECHO's one-line read on deck quality
- **Card feed:** `*ngFor="let c of visibleTodayCards"`

### Card structure (Deck)

```html
<div class="card" *ngFor="let c of visibleTodayCards"
  [class.alreadyChoseYou]="c.alreadyChoseYou"
  [class.choosingMagical]="choosingCard.get(c.userId) === 'magical'"
  [class.choosingLogical]="choosingCard.get(c.userId) === 'logical'"
  [class.choosingPass]="choosingCard.get(c.userId) === 'pass'">

  <!-- Photo with cinematic intro -->
  <div class="photo" [class.cinematic]="hasCinematic(c)">
    <img [src]="hasCinematic(c) ? getCinematicPhoto(c) : (c.profilePhoto ?? '')" />
    <div class="cornerBadge" *ngIf="c.alreadyChoseYou">Already in your corner.</div>
    <div class="quoteOverlay" *ngIf="c.curatedQuote && quoteVisible.has(c.userId)">...</div>
    <button class="narrationBtn" *ngIf="c.narrationUrl" (click)="toggleNarration(c, $event)">...</button>
    <div class="nameOverlay">...</div>
  </div>

  <!-- Info panel -->
  <div class="info">
    <div class="flagBar" *ngIf="c.rating?.show">...</div>
    <div class="newBadge" *ngIf="!c.rating?.show">...</div>
    <div class="why" *ngIf="c.reason?.headline || c.reason?.bullets?.length">...</div>
    <div class="tilesStrip" *ngIf="c.highlightedTiles?.length">...</div>
    <div class="actions">
      <button class="action magical" (click)="choose(c, 'magical')">◈ Magical</button>
      <button class="action logical" (click)="choose(c, 'logical')">◇ Resonant</button>
      <button class="action pass" (click)="choose(c, 'pass')">— Pass</button>
    </div>
  </div>
</div>
```

### Drawn tab

Similar structure, but:
- **Expiry badge** instead of "Already in your corner"
- **Prompt text** ("They chose you. Do you feel it?") instead of ECHO explanation
- **No Pass button** (only Magical/Resonant)

### Overlays

1. **Photo gallery** — Fullscreen overlay, `*ngIf="galleryPhotos.length"`
2. **ChatNote overlay** — `<woven-chat-note-overlay>` component

## Related files

**Frontend:**
- `frontend/woven-frontend/src/app/pages/moments/moments.page.ts`
- `frontend/woven-frontend/src/app/pages/moments/moments.page.html`
- `frontend/woven-frontend/src/app/pages/moments/moments.page.scss`
- `frontend/woven-frontend/src/app/pages/moments/chat-note-overlay.component.ts`
- `frontend/woven-frontend/src/app/services/moments.service.ts`

**Backend:**
- `backend/WovenBackend/Endpoints/MomentsEndpoints.cs`
- `backend/WovenBackend/Services/Moments/`
