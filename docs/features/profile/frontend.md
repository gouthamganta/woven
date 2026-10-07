# Profile Feature — Frontend Implementation

**Last Updated:** 2026-08-17  
**Framework:** Angular 21  
**Change Detection:** OnPush (all components)  
**Status:** ACTIVE

---

## Component Overview

| Component | Route | File |
|---|---|---|
| `ProfilePageComponent` | `/you` | `pages/profile/profile.ts` |
| `SettingsPageComponent` | `/you/settings` | `pages/settings/settings.ts` |
| `MyTilesPageComponent` | `/you/tiles` | `pages/my-tiles/my-tiles.page.ts` |

All components:
- Standalone: `standalone: true`
- Change detection: `ChangeDetectionStrategy.OnPush`
- Manual change detection: `cdr.markForCheck()` after async updates

---

## ProfilePageComponent

**File:** `frontend/woven-frontend/src/app/pages/profile/profile.ts`

### Imports

```typescript
import { Component, ChangeDetectionStrategy, ChangeDetectorRef, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { OnboardingService } from '../../onboarding/onboarding.service';
import { TilesService, MyTile } from '../../services/tiles.service';
```

### State

```typescript
loading = true;
error = '';
profile: any = null;
allPhotos: any[] = [];
highlights: MyTile[] = [];
recentTiles: MyTile[] = [];
```

### Lifecycle

```typescript
async ngOnInit() {
  try {
    const [profileRes] = await Promise.all([
      firstValueFrom(this.onboarding.getReview()),
      this.loadTiles(),
    ]);
    this.profile = profileRes.publicPreview ?? profileRes.self ?? null;
    const photos = profileRes.publicPreview?.photos 
                ?? profileRes.self?.photos 
                ?? profileRes.photos ?? [];
    this.allPhotos = [...photos].sort((a: any, b: any) => 
      (a.sortOrder ?? 0) - (b.sortOrder ?? 0)
    );
  } catch {
    this.error = "Couldn't load your profile.";
  } finally {
    this.loading = false;
    this.cdr.markForCheck();
  }
}
```

**Key patterns:**
- `Promise.all` for parallel requests
- Fallback chain: `publicPreview ?? self ?? null`
- Photos sorted by `sortOrder` (ascending)
- Non-fatal tile loading (wrapped in separate method)

### Tile Loading

```typescript
private async loadTiles() {
  try {
    const res = await firstValueFrom(this.tilesService.getMine());
    this.highlights = res.tiles
      .filter(t => t.isHighlighted)
      .sort((a, b) => (a.highlightSlot ?? 0) - (b.highlightSlot ?? 0));
    this.recentTiles = res.tiles
      .filter(t => !t.isHighlighted && !t.isExpired)
      .slice(0, 5);
  } catch { /* non-fatal */ }
}
```

**Filters:**
- Highlights: `isHighlighted === true`, sorted by slot
- Recent: `!isHighlighted && !isExpired`, top 5

### Computed Properties

```typescript
get primaryPhoto() {
  return this.allPhotos[0] ?? null;
}

get chips(): { label: string; value: string }[] {
  const fields: any[] = this.profile?.optionalPublic ?? [];
  const labelMap: Record<string, string> = {
    job: 'Job', hometown: 'From', education: 'Education',
    pref_height: 'Height', children: 'Children', pets: 'Pets',
    // ... more mappings
  };
  return fields
    .filter((f: any) => f.key && f.value && f.value !== 'hobbies')
    .map((f: any) => ({ label: labelMap[f.key] ?? f.key, value: f.value }));
}
```

### Actions

```typescript
goEdit() { this.router.navigateByUrl('/onboarding/review'); }
goTiles() { this.router.navigateByUrl('/you/tiles'); }
goSettings() { this.router.navigateByUrl('/you/settings'); }
```

### Template Structure

```html
<div class="page">
  <div class="loading" *ngIf="loading">...</div>
  <div class="errState" *ngIf="!loading && error">...</div>
  
  <div class="body" *ngIf="!loading && profile">
    <!-- Hero photo with overlay -->
    <div class="photoWrap">...</div>
    
    <!-- Photo strip (if >1 photo) -->
    <div class="photoStrip" *ngIf="allPhotos.length > 1">...</div>
    
    <!-- Tiles block -->
    <div class="tilesBlock" *ngIf="highlights.length > 0 || recentTiles.length > 0">
      <div class="tilesRow" *ngIf="highlights.length > 0">...</div>
      <div class="tilesRow" *ngIf="recentTiles.length > 0">...</div>
    </div>
    
    <!-- Intent, bio, details sections -->
    <div class="section" *ngIf="profile.intent?.primaryIntent">...</div>
    <div class="section" *ngIf="profile.bio">...</div>
    <div class="section" *ngIf="chips.length">...</div>
    
    <!-- Actions -->
    <div class="actions">
      <button class="btn" (click)="goEdit()">Edit Profile</button>
      <button class="btn ghost" (click)="goTiles()">My Tiles</button>
      <button class="btn ghost" (click)="goSettings()">Settings</button>
    </div>
  </div>
</div>
```

---

## SettingsPageComponent

**File:** `frontend/woven-frontend/src/app/pages/settings/settings.ts`

### Imports

```typescript
import { Component, ChangeDetectionStrategy, ChangeDetectorRef, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { PushService } from '../../services/push.service';
import { CoachingService } from '../../services/coaching.service';
import { firstValueFrom } from 'rxjs';
```

### State

```typescript
blockedUsers: BlockedUser[] = [];
showBlocks = false;
loadingBlocks = false;
unblocking: number | null = null;

notifEnabled = false;
notifPending = false;
notifSupported = true;

coachingEnabled = true;
coachingPending = false;

deleteStep = 0;  // 0 = closed, 1 = first confirm, 2 = second confirm
deleting = false;
```

### Lifecycle

```typescript
async ngOnInit() {
  this.notifSupported = await this.push.isSupported();
  this.notifEnabled = this.notifSupported && await this.push.isSubscribed();
  this.cdr.markForCheck();
}
```

### Blocked Users

```typescript
toggleBlocks() {
  this.showBlocks = !this.showBlocks;
  if (this.showBlocks && this.blockedUsers.length === 0 && !this.loadingBlocks) {
    this.fetchBlocks();
  }
  this.cdr.markForCheck();
}

private fetchBlocks() {
  this.loadingBlocks = true;
  const token = localStorage.getItem('accessToken') ?? '';
  this.http.get<BlockedUser[]>(`${this.api}/me/blocks`, {
    headers: { Authorization: `Bearer ${token}` }
  }).subscribe({
    next: users => {
      this.blockedUsers = users;
      this.loadingBlocks = false;
      this.cdr.markForCheck();
    },
    error: () => {
      this.loadingBlocks = false;
      this.cdr.markForCheck();
    }
  });
}

unblock(user: BlockedUser) {
  this.unblocking = user.userId;
  const token = localStorage.getItem('accessToken') ?? '';
  this.http.delete(`${this.api}/me/blocks/${user.userId}`, {
    headers: { Authorization: `Bearer ${token}` }
  }).subscribe({
    next: () => {
      this.blockedUsers = this.blockedUsers.filter(u => u.userId !== user.userId);
      this.unblocking = null;
      this.cdr.markForCheck();
    },
    error: () => {
      this.unblocking = null;
      this.cdr.markForCheck();
    }
  });
}
```

**Key patterns:**
- Lazy loading on first expand
- Optimistic UI (filter on success)
- No error toast (silent failure)

### Push Notifications

```typescript
async toggleNotif() {
  if (this.notifPending || !this.notifSupported) return;
  this.notifPending = true;
  this.cdr.markForCheck();
  try {
    if (this.notifEnabled) {
      await this.push.unregister();
      this.notifEnabled = false;
    } else {
      this.notifEnabled = await this.push.register();
    }
  } finally {
    this.notifPending = false;
    this.cdr.markForCheck();
  }
}
```

### Coaching Toggle

```typescript
async toggleCoaching() {
  if (this.coachingPending) return;
  this.coachingPending = true;
  this.cdr.markForCheck();
  try {
    if (this.coachingEnabled) {
      await firstValueFrom(this.coachingSvc.optOut());
      this.coachingEnabled = false;
    } else {
      await firstValueFrom(this.coachingSvc.optIn());
      this.coachingEnabled = true;
    }
  } catch { /* non-critical */ }
  finally {
    this.coachingPending = false;
    this.cdr.markForCheck();
  }
}
```

### Delete Account

```typescript
startDelete() {
  this.deleteStep = 1;
  this.cdr.markForCheck();
}

cancelDelete() {
  this.deleteStep = 0;
  this.cdr.markForCheck();
}

confirmDelete() {
  this.deleting = true;
  const token = localStorage.getItem('accessToken') ?? '';
  this.http.delete(`${this.api}/me/account`, {
    headers: { Authorization: `Bearer ${token}` }
  }).subscribe({
    next: () => {
      localStorage.removeItem('accessToken');
      localStorage.removeItem('user');
      this.router.navigateByUrl('/login');
    },
    error: () => {
      this.deleting = false;
      this.deleteStep = 0;
      this.cdr.markForCheck();
    }
  });
}
```

**Delete flow:**
1. `deleteStep = 1` → first confirmation
2. User confirms → `deleteStep = 2` → second confirmation
3. User confirms → calls `confirmDelete()`
4. On success → clears localStorage → navigates to `/login`

---

## MyTilesPageComponent

**File:** `frontend/woven-frontend/src/app/pages/my-tiles/my-tiles.page.ts`

### Imports

```typescript
import { Component, ChangeDetectionStrategy, ChangeDetectorRef, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { TilesService, MyTile, ReceivedOrbit } from '../../services/tiles.service';
```

### State

```typescript
allTiles: MyTile[] = [];
orbitMap = new Map<string, number>();
loading = true;
working: string | null = null;
slotWorking: number | null = null;
toast = '';
private toastTimer: ReturnType<typeof setTimeout> | null = null;

composeText = '';
posting = false;

pickingFor: MyTile | null = null;
readonly slots = [1, 2, 3, 4, 5, 6, 7, 8, 9];
```

### Lifecycle

```typescript
async ngOnInit() {
  await this.load();
}

private async load() {
  this.loading = true;
  this.cdr.markForCheck();
  try {
    const [tileRes, orbits] = await Promise.all([
      firstValueFrom(this.tiles.getMine()),
      firstValueFrom(this.tiles.getReceivedOrbits()).catch(() => [] as any[])
    ]);
    this.allTiles = tileRes.tiles;
    this.orbitMap = new Map<string, number>();
    for (const o of orbits as ReceivedOrbit[]) {
      this.orbitMap.set(o.tileId, (this.orbitMap.get(o.tileId) ?? 0) + 1);
    }
  } catch {
    this.showToast('Could not load tiles');
  } finally {
    this.loading = false;
    this.cdr.markForCheck();
  }
}
```

**Key patterns:**
- Parallel requests: tiles + orbits
- Non-fatal orbit fetch (`.catch(() => [])`)
- Orbit aggregation in `Map<tileId, count>`

### Computed Properties

```typescript
get highlighted() {
  return this.allTiles
    .filter(t => t.isHighlighted)
    .sort((a, b) => (a.highlightSlot ?? 0) - (b.highlightSlot ?? 0));
}

get active() {
  return this.allTiles.filter(t => !t.isHighlighted && !t.isExpired);
}

get expired() {
  return this.allTiles.filter(t => !t.isHighlighted && t.isExpired);
}

get occupiedSlots(): Set<number> {
  return new Set(this.highlighted.map(t => t.highlightSlot!).filter(Boolean));
}
```

### Post Tile

```typescript
async post() {
  const text = this.composeText.trim();
  if (text.length < 10 || text.length > 150 || this.posting) return;
  this.posting = true;
  this.cdr.markForCheck();
  try {
    await firstValueFrom(this.tiles.create('text', text));
    this.composeText = '';
    this.showToast('Posted to Commons');
    await this.load();
  } catch {
    this.showToast('Could not post — try again');
  } finally {
    this.posting = false;
    this.cdr.markForCheck();
  }
}
```

### Pin/Unpin

```typescript
openSlotPicker(tile: MyTile) {
  this.pickingFor = tile;
  this.cdr.markForCheck();
}

closeSlotPicker() {
  this.pickingFor = null;
  this.slotWorking = null;
  this.cdr.markForCheck();
}

async confirmPin(slot: number) {
  if (!this.pickingFor || this.slotWorking !== null) return;
  const tile = this.pickingFor;
  this.slotWorking = slot;
  this.working = tile.id;
  this.cdr.markForCheck();
  try {
    await firstValueFrom(this.tiles.highlight(tile.id, slot));
    this.closeSlotPicker();
    this.showToast(`Pinned to slot ${slot}`);
    await this.load();
  } catch (err: any) {
    this.closeSlotPicker();
    this.showToast(err?.error?.error ?? 'Could not pin tile');
  } finally {
    this.working = null;
    this.cdr.markForCheck();
  }
}

async unpinTile(tile: MyTile) {
  if (this.working) return;
  this.working = tile.id;
  this.cdr.markForCheck();
  try {
    await firstValueFrom(this.tiles.unhighlight(tile.id));
    this.showToast('Removed from highlights');
    await this.load();
  } catch {
    this.showToast('Could not unpin');
  } finally {
    this.working = null;
    this.cdr.markForCheck();
  }
}
```

### Toast

```typescript
private showToast(msg: string) {
  this.toast = msg;
  this.cdr.markForCheck();
  if (this.toastTimer) clearTimeout(this.toastTimer);
  this.toastTimer = setTimeout(() => {
    this.toast = '';
    this.cdr.markForCheck();
  }, 2500);
}
```

### Time Remaining

```typescript
hoursLeft(expiresAt: string): string {
  const ms = new Date(expiresAt).getTime() - Date.now();
  if (ms <= 0) return 'Expiring';
  const h = Math.floor(ms / 3600000);
  const m = Math.floor((ms % 3600000) / 60000);
  return h > 0 ? `${h}h left` : `${m}m left`;
}
```

---

## Services

### OnboardingService

**File:** `frontend/woven-frontend/src/app/onboarding/onboarding.service.ts`

```typescript
getReview() {
  return this.http.get<ReviewResponse>(`${environment.apiUrl}/onboarding/review`);
}
```

**Response shape:**
```typescript
export interface ReviewResponse {
  profileStatus?: string;
  self?: any;
  publicPreview?: any;
  basics?: any;
  intent?: any;
  photos?: any[];
  bio?: string;
  weeklyVibe?: string;
  foundational?: any;
  [key: string]: any;
}
```

### TilesService

**File:** `frontend/woven-frontend/src/app/services/tiles.service.ts`

```typescript
getMine() {
  return this.http.get<{ count: number; tiles: MyTile[] }>(`${this.api}/tiles/mine`);
}

create(contentType: string, contentText?: string, mediaUrl?: string) {
  return this.http.post<{ tileId: string }>(`${this.api}/tiles`, {
    contentType,
    contentText: contentText ?? null,
    mediaUrl: mediaUrl ?? null,
  });
}

highlight(tileId: string, slotNumber: number) {
  return this.http.post<{ slot: number }>(`${this.api}/tiles/${tileId}/highlight`, { slotNumber });
}

unhighlight(tileId: string) {
  return this.http.delete(`${this.api}/tiles/${tileId}/highlight`);
}

getReceivedOrbits() {
  return this.http.get<ReceivedOrbit[]>(`${this.api}/orbit/received`);
}
```

### PushService

**File:** `frontend/woven-frontend/src/app/services/push.service.ts`

```typescript
async isSupported(): Promise<boolean> {
  return 'serviceWorker' in navigator && 'PushManager' in window;
}

async isSubscribed(): Promise<boolean> {
  const registration = await navigator.serviceWorker.ready;
  const subscription = await registration.pushManager.getSubscription();
  return subscription !== null;
}

async register(): Promise<boolean> {
  // Request permission, subscribe, send to backend
}

async unregister(): Promise<void> {
  // Unsubscribe, delete from backend
}
```

### CoachingService

**File:** `frontend/woven-frontend/src/app/services/coaching.service.ts`

```typescript
optOut() {
  return this.http.post(`${this.api}/coaching/opt-out`, {});
}

optIn() {
  return this.http.delete(`${this.api}/coaching/opt-out`);
}
```

---

## Shared Patterns

### Change Detection

All components use `ChangeDetectionStrategy.OnPush`:

```typescript
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  // ...
})
export class XxxComponent {
  constructor(private cdr: ChangeDetectorRef) {}
  
  async someAsyncMethod() {
    // ... async work
    this.cdr.markForCheck();  // REQUIRED
  }
}
```

**When to call `markForCheck()`:**
- After `async/await` state changes
- After RxJS `.subscribe()` callbacks
- After `setTimeout` / interval callbacks
- After manual DOM mutations

### firstValueFrom Pattern

**Don't:**
```typescript
this.http.get(...).subscribe(res => { /* ... */ });
```

**Do:**
```typescript
const res = await firstValueFrom(this.http.get(...));
```

**Benefits:**
- Works inside `async` functions
- Better error handling (try/catch)
- No subscription leak risk
- Cleaner code

### Optimistic UI

**Pattern:**
1. Update local state immediately
2. Disable button / show loading
3. Fire request
4. On success: keep state, reload if needed
5. On error: revert state, show toast

**Example (compose box):**
```typescript
async post() {
  this.posting = true;  // Disable button
  this.cdr.markForCheck();
  try {
    await firstValueFrom(this.tiles.create('text', text));
    this.composeText = '';  // Optimistic clear
    this.showToast('Posted to Commons');
    await this.load();  // Reload to show new tile
  } catch {
    this.showToast('Could not post — try again');
  } finally {
    this.posting = false;  // Re-enable button
    this.cdr.markForCheck();
  }
}
```

### Error Handling

**Non-critical errors:**
- Silent catch (tile loading, orbit fetching)
- No user feedback
- Degrades gracefully (empty arrays)

**User-facing errors:**
- Toast notification
- Generic messages ("Could not post — try again")
- No technical details exposed

### localStorage Auth

**Token access:**
```typescript
const token = localStorage.getItem('accessToken') ?? '';
this.http.get(url, {
  headers: { Authorization: `Bearer ${token}` }
})
```

**Logout:**
```typescript
localStorage.removeItem('accessToken');
localStorage.removeItem('user');
this.router.navigateByUrl('/login');
```

**Known gap:** JWT in localStorage (XSS risk)
- See `COOKIE_AUTH_MIGRATION.md` for HttpOnly cookie migration plan

---

## Styling Conventions

### CSS Variables (Design Tokens)

**Colors:**
- `--gold-400`, `--gold-300` — Primary actions, verified badge
- `--plum-400`, `--plum-300` — Recent tiles, coaching
- `--rose-400`, `--rose-300` — Notifications, errors
- `--text-primary`, `--text-secondary`, `--text-muted`, `--text-dim`
- `--bg-base`, `--bg-surface`, `--bg-elevated`
- `--border-subtle`, `--border-soft`, `--border-medium`

**Typography:**
- `--font-display` (Raleway) — Names, titles, bios
- `--font-ui` (Inter) — Body text, labels
- `--text-sm`, `--text-lg`, `--text-xl` — Font sizes

**Spacing:**
- `--radius-md`, `--radius-lg`, `--radius-xl`, `--radius-full` — Border radius

### No Hover Lifts

**Design rule:** Buttons never use `translateY` on hover

**Allowed:**
```css
.btn:hover {
  box-shadow: 0 4px 16px var(--gold-glow);
}
```

**Not allowed:**
```css
.btn:hover {
  transform: translateY(-1px);  /* ❌ */
}
```

**Exceptions:** None

---

## Testing Considerations

### Unit Tests (Not yet implemented)

**Target coverage:**
- Computed properties (`primaryPhoto`, `chips`, `highlighted`, etc.)
- State transitions (`deleteStep`, `showBlocks`, etc.)
- Time calculations (`hoursLeft`)

### Integration Tests (Not yet implemented)

**User flows:**
- Profile load → view → navigate to tiles
- Settings → toggle push → verify backend call
- My Tiles → post → pin → unpin

---

## Performance

### Lazy Loading
- Blocked users: fetched on-demand (first expand)
- Photos: pre-loaded (above fold)
- Tiles: loaded once on init

### Change Detection Optimization
- `OnPush` strategy reduces dirty checking
- Manual `markForCheck()` only when needed
- No unnecessary re-renders

### Bundle Size
- `CommonModule` imported (Angular built-in)
- `FormsModule` only in MyTiles (two-way binding)
- No third-party UI libraries

---

## Known Gaps

| Gap | Impact |
|---|---|
| No retry logic | Failed requests require manual refresh |
| No offline support | App breaks without connection |
| No loading skeletons | Spinner UX only |
| No infinite scroll | All tiles loaded at once |
| No photo compression | Large images slow down page |
| No lazy image loading | All images loaded immediately |
| JWT in localStorage | XSS vulnerability (see COOKIE_AUTH_MIGRATION.md) |
