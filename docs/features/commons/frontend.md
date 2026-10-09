# Frontend — Commons UI & State

**Path:** `docs/features/commons/frontend.md`  
**Last Updated:** 2026-10-07  
**Feature:** Commons

---

## Overview

The Commons frontend is a **session-cached, infinite-scroll feed** with drawer-based tile detail view and inline compose. Built with Angular 21, OnPush change detection, and optimistic UI patterns.

**Component:** `CommonsPageComponent`  
**Services:** `CommonsService`, `TilesService`  
**Route:** `/commons`

---

## Component Architecture

### CommonsPageComponent
**File:** `frontend/woven-frontend/src/app/pages/commons/commons.page.ts`  
**Change Detection:** `OnPush` (manual `cdr.markForCheck()` after async updates)

**State:**
```typescript
tiles: CommonsTile[] = [];           // all loaded tiles (pages 1-N)
loading = true;                      // initial load
loadingMore = false;                 // pagination
refreshing = false;                  // refresh button spinner
energyDepleted = false;              // 429 state
error = '';                          // error message

page = 1;                            // current page number
sessionId = '';                      // session UUID (Redis cache key)
hasMore = true;                      // pagination flag

composeText = '';                    // compose textarea
posting = false;                     // POST in flight

readonly DAILY_CAP = 100;            // energy limit
tilesViewedToday = 0;                // local counter

activeTile: CommonsTile | null = null;  // drawer state
private drawerOpenAt = 0;            // performance.now() timestamp

orbitedTiles = new Set<string>();    // tile IDs user has orbited
orbitingTiles = new Set<string>();   // tiles with orbit POST in flight
```

---

## Session Management

### Session ID (Feed Caching)
**Storage:** `sessionStorage`  
**Key:** `woven_commons_session`

```typescript
async ngOnInit() {
  // Restore session from sessionStorage so page refresh reuses Redis-cached feed
  this.sessionId = sessionStorage.getItem(SESSION_KEY) ?? '';
  await this.loadFeed();
}
```

**Flow:**
1. First visit → `sessionId = ''` → backend generates new UUID → stored in `sessionStorage`
2. Same session → reuse UUID → backend returns cached feed from Redis (2h TTL)
3. Refresh button → clears `sessionStorage` → new UUID → recomputed feed

**Why sessionStorage?**  
Persists across page refreshes but not across tabs. Prevents cache pollution when user opens Commons in multiple tabs.

---

## Feed Loading

### Initial Load
```typescript
async loadFeed() {
  this.loading = true;
  this.error = '';
  this.energyDepleted = false;
  this.cdr.markForCheck();

  try {
    const res = await firstValueFrom(
      this.commons.getFeed(1, this.sessionId || undefined)
    );
    this.sessionId = res.sessionId;
    sessionStorage.setItem(SESSION_KEY, res.sessionId);
    this.page = 1;
    this.tiles = res.tiles;
    this.hasMore = res.tiles.length === 20;
    this.tilesViewedToday += res.tiles.length;
  } catch (err: any) {
    if (err?.status === 429) {
      this.energyDepleted = true;
      this.tilesViewedToday = this.DAILY_CAP;
    } else {
      this.error = "Couldn't load Commons right now.";
    }
  } finally {
    this.loading = false;
    this.cdr.markForCheck();
  }
}
```

**Error states:**
- **429 (ENERGY_DEPLETED):** Show "You've seen it all today" message
- **Other errors:** Show generic error message

### Pagination (Load More)
```typescript
async loadMore() {
  if (this.loadingMore || !this.hasMore) return;
  this.loadingMore = true;
  this.cdr.markForCheck();

  try {
    const res = await firstValueFrom(this.commons.getFeed(this.page + 1, this.sessionId));
    this.page++;
    this.tiles = [...this.tiles, ...res.tiles];  // append to existing
    this.hasMore = res.tiles.length === 20;
    this.tilesViewedToday += res.tiles.length;
  } catch (err: any) {
    if (err?.status === 429) {
      this.energyDepleted = true;
      this.hasMore = false;
    }
  } finally {
    this.loadingMore = false;
    this.cdr.markForCheck();
  }
}
```

**Pagination logic:**
- Each page = 20 tiles
- `hasMore = false` when page returns <20 tiles OR 429 error
- Tiles accumulated in single array (no page-level data structure)

### Refresh
```typescript
async refresh() {
  if (this.refreshing) return;
  this.refreshing = true;
  sessionStorage.removeItem(SESSION_KEY);  // clear cache key
  this.sessionId = '';
  this.cdr.markForCheck();

  try {
    await firstValueFrom(this.commons.refresh());  // POST /commons/refresh
  } catch { /* ignore */ }

  this.tiles = [];
  this.tilesViewedToday = 0;
  await this.loadFeed();
  this.refreshing = false;
  this.cdr.markForCheck();
}
```

**Refresh button animation:**
```scss
.refreshBtn.spinning { animation: spin 0.7s linear infinite; }
@keyframes spin { to { transform: rotate(360deg); } }
```

---

## UI Layout

### 3×3 Grid
**Template:**
```html
<div class="grid">
  <div class="cell" *ngFor="let t of tiles" (click)="openTile(t)">
    <!-- Photo -->
    <img *ngIf="t.mediaUrl && t.contentType !== 'video'"
         class="cellImg" [src]="t.mediaUrl" loading="lazy" />

    <!-- Video -->
    <div class="cellVideo" *ngIf="t.mediaUrl && t.contentType === 'video'">
      <video [src]="t.mediaUrl" muted playsinline preload="none"></video>
      <div class="playIcon">▶</div>
    </div>

    <!-- Text -->
    <div class="cellText" *ngIf="!t.mediaUrl">
      <span class="cellPreview">{{ t.contentText | slice:0:80 }}</span>
    </div>

    <!-- Resonance dot -->
    <div class="dot" [class.resonant]="isResonant(t)" [class.discovery]="!isResonant(t)"></div>
  </div>
</div>
```

**Grid CSS:**
```scss
.grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 3px;
}

.cell {
  aspect-ratio: 1;
  background: var(--bg-elevated);
  border-radius: 4px;
  overflow: hidden;
  cursor: pointer;
}
```

**Resonance indicator:**
```typescript
isResonant(tile: CommonsTile) { return tile.similarity >= 0.65; }
```

**Dot colors:**
- **Resonant (≥0.65):** Gold (var(--gold-400))
- **Discovery (<0.65):** Plum (var(--plum-400))

---

## Tile Drawer

### Open/Close
```typescript
openTile(tile: CommonsTile) {
  this.activeTile = tile;
  this.drawerOpenAt = performance.now();  // start dwell timer
  this.cdr.markForCheck();
}

closeDrawer() {
  if (!this.activeTile) return;
  this.recordDwell(this.activeTile.tileId);  // flush dwell to backend
  this.activeTile = null;
  this.drawerOpenAt = 0;
  this.cdr.markForCheck();
}
```

**Cleanup on destroy:**
```typescript
ngOnDestroy() {
  // Flush dwell if drawer is still open on page exit
  if (this.activeTile && this.drawerOpenAt > 0) {
    this.recordDwell(this.activeTile.tileId);
  }
}
```

### Dwell Tracking
```typescript
private recordDwell(tileId: string) {
  const duration = this.drawerOpenAt > 0
    ? Math.round(performance.now() - this.drawerOpenAt)
    : undefined;
  this.commons.recordView(tileId, duration).subscribe({ error: () => {} });
}
```

**Backend signal logic:**
- `durationMs >= 8000ms` → `TileDwell` or `VoiceDwell` signal recorded
- `durationMs < 8000ms` → view counted, no signal

**Why `performance.now()`?**  
Higher resolution than `Date.now()` (microsecond vs millisecond precision). Prevents clock skew issues.

---

## Orbit Interaction

### Orbit Button
**States:**
- **Idle:** ◎ Orbit (clickable)
- **Sending:** … (disabled)
- **Success:** ◈ Orbiting (disabled, permanent)

**Template:**
```html
<button
  class="orbitBtn"
  [class.active]="orbitedTiles.has(activeTile.tileId)"
  [disabled]="orbitedTiles.has(activeTile.tileId) || orbitingTiles.has(activeTile.tileId)"
  (click)="orbitTile(activeTile)"
>
  <span class="orbitIcon">{{ orbitedTiles.has(activeTile.tileId) ? '◈' : '◎' }}</span>
  <span>{{ orbitedTiles.has(activeTile.tileId) ? 'Orbiting' : (orbitingTiles.has(activeTile.tileId) ? '…' : 'Orbit') }}</span>
</button>
```

### Orbit Action
```typescript
orbitTile(tile: CommonsTile) {
  if (this.orbitedTiles.has(tile.tileId) || this.orbitingTiles.has(tile.tileId)) return;

  this.orbitingTiles.add(tile.tileId);  // optimistic loading state
  this.cdr.markForCheck();

  this.commons.orbitTile(tile.tileId).subscribe({
    next: res => {
      this.orbitingTiles.delete(tile.tileId);
      this.orbitedTiles.add(tile.tileId);  // permanent disabled state
      this.showToast(res.mutualDetected 
        ? '◈ A mutual pull — you both feel it' 
        : '◎ Orbiting this thought');
      this.cdr.markForCheck();
    },
    error: (err: any) => {
      this.orbitingTiles.delete(tile.tileId);
      if (err?.error?.error === 'ALREADY_ORBITED')
        this.orbitedTiles.add(tile.tileId);  // idempotent recovery
      else if (err?.status === 429)
        this.showToast('Orbit limit reached for today');
      else if (err?.error?.error === 'CANNOT_ORBIT_OWN_TILE')
        this.showToast("Can't orbit your own tile");
      this.cdr.markForCheck();
    }
  });
}
```

**Error recovery:**
- `ALREADY_ORBITED` → mark as orbited locally (server state wins)
- `429` → rate limit toast (50/day)
- `CANNOT_ORBIT_OWN_TILE` → user tried to orbit their own content

---

## Compose Tile

### Inline Textarea
**Template:**
```html
<div class="compose">
  <textarea
    class="composeInput"
    [(ngModel)]="composeText"
    placeholder="Share a thought, anonymously…"
    maxlength="150"
    rows="2"
  ></textarea>
  <div class="composeFooter">
    <span class="composeCount" [class.warn]="composeText.length > 130">
      {{ composeText.length }}/150
    </span>
    <button class="composeBtn" (click)="postTile()"
      [disabled]="composeText.trim().length < 10 || posting">
      {{ posting ? '…' : 'Post' }}
    </button>
  </div>
</div>
```

**Character limit:**
- **Min:** 10 characters (enforced client-side)
- **Max:** 150 characters (enforced via `maxlength` + server validation)
- **Warn:** Yellow text when >130 chars

### Post Action
```typescript
async postTile() {
  const text = this.composeText.trim();
  if (text.length < 10 || text.length > 150 || this.posting) return;

  this.posting = true;
  this.cdr.markForCheck();

  try {
    await firstValueFrom(this.commons.createTile('text', text));
    this.composeText = '';  // clear textarea
    this.showToast('Posted anonymously');
  } catch {
    this.showToast('Could not post — try again');
  } finally {
    this.posting = false;
    this.cdr.markForCheck();
  }
}
```

**Note:** Newly posted tile does NOT appear in feed immediately. It requires:
1. Moderation approval (text tiles auto-approve in prod, <500ms)
2. Embedding generation (async worker, 1-5 seconds)
3. Feed refresh (user must manually refresh or wait for cache expiry)

**Why not optimistic insert?**  
Feed is server-ranked. Inserting at position 0 would break sort order (tiles are scored by similarity + recency + CF, not chronological).

---

## Energy Bar

**Template:**
```html
<div class="energyWrap">
  <div class="energyTrack">
    <div class="energyFill" [style.width.%]="energyPct"></div>
  </div>
  <span class="energyLabel">{{ DAILY_CAP - tilesViewedToday }} views left today</span>
</div>
```

**Percentage calculation:**
```typescript
get energyPct() {
  return Math.max(0, Math.min(100, ((this.DAILY_CAP - this.tilesViewedToday) / this.DAILY_CAP) * 100));
}
```

**Gradient fill:**
```scss
.energyFill {
  height: 100%;
  background: linear-gradient(90deg, var(--plum-400), var(--gold-400));
  border-radius: 9999px;
  transition: width 0.4s ease;
}
```

**Hidden when:**
- Initial loading (`loading = true`)
- No tiles loaded (`tiles.length === 0`)
- Energy depleted (replaced by 429 message)

---

## Services

### CommonsService
**File:** `frontend/woven-frontend/src/app/services/commons.service.ts`

```typescript
@Injectable({ providedIn: 'root' })
export class CommonsService {
  constructor(private http: HttpClient) {}

  getFeed(page = 1, sessionId?: string) {
    const params: any = { page };
    if (sessionId) params['sessionId'] = sessionId;
    return this.http.get<CommonsFeedResponse>(`${environment.apiUrl}/commons`, { params });
  }

  refresh() {
    return this.http.post<{ refreshed: boolean }>(`${environment.apiUrl}/commons/refresh`, {});
  }

  recordView(tileId: string, durationMs?: number) {
    return this.http.post<{ recorded: boolean }>(
      `${environment.apiUrl}/commons/${tileId}/view`,
      durationMs != null ? { durationMs } : {}
    );
  }

  createTile(contentType: string, contentText?: string, mediaUrl?: string) {
    return this.http.post<{ tileId: string }>(`${environment.apiUrl}/tiles`, {
      contentType,
      contentText: contentText ?? null,
      mediaUrl: mediaUrl ?? null,
    });
  }

  orbitTile(tileId: string) {
    return this.http.post<{ relationshipType: string; mutualDetected: boolean }>(
      `${environment.apiUrl}/orbit/${tileId}`,
      {}
    );
  }
}
```

### TilesService
**File:** `frontend/woven-frontend/src/app/services/tiles.service.ts`

```typescript
@Injectable({ providedIn: 'root' })
export class TilesService {
  constructor(private http: HttpClient) {}

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
}
```

---

## State Messages

### Energy Depleted (429)
```html
<div class="stateBlock" *ngIf="!loading && energyDepleted">
  <div class="stateIcon">◇</div>
  <div class="stateTitle">You've seen it all today</div>
  <div class="stateSub">Commons resets at midnight UTC.</div>
</div>
```

### Empty Feed
```html
<div class="stateBlock" *ngIf="!loading && !error && !energyDepleted && tiles.length === 0">
  <div class="stateIcon">◇</div>
  <div class="stateTitle">Nothing here yet</div>
  <div class="stateSub">As more people join, their anonymous thoughts will appear here.</div>
</div>
```

### End of Feed
```html
<div class="endLine" *ngIf="!hasMore && !energyDepleted && tiles.length">
  You're all caught up ◇
</div>
```

---

## Toast Notifications

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

**Toast messages:**
- `"Posted anonymously"` — tile created successfully
- `"◎ Orbiting this thought"` — orbit success (no mutual)
- `"◈ A mutual pull — you both feel it"` — mutual orbit detected
- `"Orbit limit reached for today"` — 50/day rate limit
- `"Can't orbit your own tile"` — self-orbit attempt
- `"Could not post — try again"` — POST /tiles error

**CSS:**
```scss
.toast {
  position: fixed; bottom: 80px; left: 50%; transform: translateX(-50%);
  background: var(--bg-overlay); color: var(--text-primary);
  padding: 10px 20px; border-radius: 999px;
  border: 1px solid var(--border-soft);
  backdrop-filter: blur(12px);
  animation: toastIn 0.2s ease;
}
```

---

## Design Notes

### Why OnPush Change Detection?
**Performance.** Commons can display 100+ tiles (5 pages). Default change detection would re-render on every Angular event (mouse move, HTTP response, timer tick). OnPush requires manual `markForCheck()` but reduces render cycles by 90%.

### Why Not Virtual Scroll?
**Simplicity.** CDK virtual scroll adds complexity (viewport height tracking, buffer tuning). 3×3 grid with `loading="lazy"` images is fast enough for 100 tiles (tested on mid-range Android).

### Why Drawer Instead of Modal?
**Mobile-first.** Bottom drawer is the native mobile pattern (iOS/Android). Modal with backdrop is desktop convention. Drawer slides up from bottom → finger-friendly on phone.

### Why No Tile Animation on Load?
**Jank.** Staggered entrance animations (fade-in, slide-up) cause layout shift and frame drops on low-end devices. Instant render is smoother.

---

## Next Steps

- **Pull-to-refresh** — Native mobile gesture (not yet implemented)
- **My Tiles page** — `/you/tiles` to view/highlight own tiles (backend ready, frontend stub exists)
- **Orbit count badge** — Show "◈ 5 orbits" on own tiles
- **Video autoplay** — Muted autoplay on scroll-into-view (currently manual play button)

---

## Related Docs

- **[README.md](./README.md)** — Commons overview
- **[tiles.md](./tiles.md)** — Tile entity and lifecycle
- **[orbit.md](./orbit.md)** — Orbit gravity mechanics
- **[backend.md](./backend.md)** — Backend services
- **[api.md](./api.md)** — Full endpoint specs
