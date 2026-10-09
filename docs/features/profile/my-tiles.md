# My Tiles Page (`/you/tiles`)

**Component:** `MyTilesPageComponent`  
**Route:** `/you/tiles`  
**Status:** ACTIVE  
**Last Updated:** 2026-08-17

---

## Purpose

Manage user's Commons tiles: post new tiles, view active/expired tiles, pin up to 9 tiles to profile as "Highlights".

---

## Layout Structure

```
┌─────────────────────────────────────┐
│  ← My Tiles                         │
├─────────────────────────────────────┤
│  ┌─────────────────────────────┐    │
│  │ Share a thought…            │    │
│  │                             │    │
│  │              120/150 | Post │    │
│  └─────────────────────────────┘    │
├─────────────────────────────────────┤
│  HIGHLIGHTED           2/9 slots    │
│  ┌─────────────────────────────┐    │
│  │ Slot 1            ◎ 12      │    │
│  │ ┌─────────────────────────┐ │    │
│  │ │  Tile content           │ │    │
│  │ └─────────────────────────┘ │    │
│  │                 [ Unpin ]   │    │
│  └─────────────────────────────┘    │
│  ┌─────────────────────────────┐    │
│  │ Slot 3            ◎ 5       │    │
│  │ ┌─────────────────────────┐ │    │
│  │ │  Tile content           │ │    │
│  │ └─────────────────────────┘ │    │
│  │                 [ Unpin ]   │    │
│  └─────────────────────────────┘    │
├─────────────────────────────────────┤
│  ACTIVE                    3 live   │
│  ┌─────────────────────────────┐    │
│  │ 14h left          ◎ 8       │    │
│  │ ┌─────────────────────────┐ │    │
│  │ │  Tile content           │ │    │
│  │ └─────────────────────────┘ │    │
│  └─────────────────────────────┘    │
├─────────────────────────────────────┤
│  EXPIRED        Pin your best ones  │
│  ┌─────────────────────────────┐    │
│  │ Expired           ◎ 3       │    │
│  │ ┌─────────────────────────┐ │    │
│  │ │  Tile content           │ │    │
│  │ └─────────────────────────┘ │    │
│  │         [ Pin to profile ]  │    │
│  └─────────────────────────────┘    │
└─────────────────────────────────────┘

        [ Posted to Commons ]  ← toast
```

---

## Sections

### 1. Compose Box

**Layout:**
- Textarea: `--font-display`, 15px, auto-resize (2 rows → expand)
- Character counter: `120/150` (warns red at >130)
- Post button: Plum gradient, disabled if <10 or >150 chars

**Validation:**
- Min: 10 characters
- Max: 150 characters
- Trim whitespace before submit

**Post flow:**
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
    await this.load();  // Refresh tile list
  } catch {
    this.showToast('Could not post — try again');
  } finally {
    this.posting = false;
    this.cdr.markForCheck();
  }
}
```

**Endpoint:** `POST /tiles`
```json
{
  "contentType": "text",
  "contentText": "Your thought here…",
  "mediaUrl": null
}
```

**Response:**
```json
{
  "tileId": "550e8400-e29b-41d4-a716-446655440000"
}
```

**Behavior:**
- Optimistic: clears input immediately on success
- Shows toast: "Posted to Commons"
- Reloads tile list to show new active tile

---

### 2. HIGHLIGHTED Section

**Purpose:** Permanent profile tiles (up to 9 slots)

**Header:**
- Label: "HIGHLIGHTED"
- Count: "2/9 slots" (dynamic)

**Tile card:**
- Status chip: `Slot 1` (gold theme)
- Orbit count: `◎ 12` (if any orbits received)
- Tile content: image or text
- Action: `[ Unpin ]` button

**Sorting:**
```typescript
get highlighted() {
  return this.allTiles
    .filter(t => t.isHighlighted)
    .sort((a, b) => (a.highlightSlot ?? 0) - (b.highlightSlot ?? 0));
}
```

**Unpin flow:**
```typescript
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

**Endpoint:** `DELETE /tiles/{id}/highlight`

**Backend behavior:**
- Sets `isHighlighted = false`, `highlightSlot = null`
- Tile moves to EXPIRED section (assuming 48h passed)

---

### 3. ACTIVE Section

**Purpose:** Live tiles (not yet expired, not highlighted)

**Header:**
- Label: "ACTIVE"
- Count: "3 live"

**Status chip:**
- Time remaining: `14h left` / `45m left` / `Expiring`
- Plum theme (`rgba(127, 119, 221, 0.12)`)

**Time calculation:**
```typescript
hoursLeft(expiresAt: string): string {
  const ms = new Date(expiresAt).getTime() - Date.now();
  if (ms <= 0) return 'Expiring';
  const h = Math.floor(ms / 3600000);
  const m = Math.floor((ms % 3600000) / 60000);
  return h > 0 ? `${h}h left` : `${m}m left`;
}
```

**Filtering:**
```typescript
get active() {
  return this.allTiles.filter(t => !t.isHighlighted && !t.isExpired);
}
```

**No actions** — Active tiles cannot be pinned or removed

---

### 4. EXPIRED Section

**Purpose:** Tiles past 48h lifespan (eligible for pinning)

**Header:**
- Label: "EXPIRED"
- Hint: "Pin your best ones"

**Status chip:**
- Text: "Expired"
- Neutral theme (`rgba(200, 180, 160, 0.08)`)

**Action:** `[ Pin to profile ]` button
- Opens slot picker sheet
- Disabled if 9 slots already occupied
- Text changes to "Slots full" when all 9 slots used

**Filtering:**
```typescript
get expired() {
  return this.allTiles.filter(t => !t.isHighlighted && t.isExpired);
}
```

---

## Slot Picker Sheet

**Trigger:** Click "Pin to profile" on an expired tile

**Layout:**
- Bottom sheet overlay
- Title: "Choose a slot"
- Subtitle: "Slot 1 appears first on your profile"
- 3×3 grid of slot buttons (1-9)
- Cancel button (full-width, bottom)

**Slot button states:**
- **Available:** Default styling, clickable
- **Occupied:** Gold border + `•` indicator (top-right), can overwrite
- **Working:** Disabled while request pending

**Pin flow:**
```typescript
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
```

**Endpoint:** `POST /tiles/{id}/highlight`
```json
{
  "slotNumber": 3
}
```

**Response:**
```json
{
  "slot": 3
}
```

**Backend behavior:**
- If slot already occupied: **overwrites** previous tile
- Previous tile: `isHighlighted = false`, `highlightSlot = null`
- New tile: `isHighlighted = true`, `highlightSlot = {slot}`

**Occupied slots:**
```typescript
get occupiedSlots(): Set<number> {
  return new Set(this.highlighted.map(t => t.highlightSlot!).filter(Boolean));
}
```

---

## Orbit Count Display

**What it shows:** Number of times other users orbited this tile

**Data source:**
```typescript
private async load() {
  const [tileRes, orbits] = await Promise.all([
    firstValueFrom(this.tiles.getMine()),
    firstValueFrom(this.tiles.getReceivedOrbits()).catch(() => [] as any[])
  ]);
  this.allTiles = tileRes.tiles;
  this.orbitMap = new Map<string, number>();
  for (const o of orbits as ReceivedOrbit[]) {
    this.orbitMap.set(o.tileId, (this.orbitMap.get(o.tileId) ?? 0) + 1);
  }
}
```

**Endpoint:** `GET /orbit/received`
```json
[
  {
    "id": "uuid",
    "orbiterId": 123,
    "tileId": "tile-uuid",
    "relationshipType": "STRANGER",
    "orbitedAt": "2026-08-15T10:30:00Z"
  }
]
```

**Display:**
- Template: `◎ {{ orbitMap.get(t.id) }}`
- Shown on all tiles (highlighted, active, expired)
- Hidden if count is 0

---

## Empty State

**Trigger:** `!loading && allTiles.length === 0`

**Layout:**
- Icon: `◇` (40px, `--text-dim`)
- Title: "No tiles yet" (20px display font)
- Subtitle: "Post your first thought above — it'll appear anonymously in Commons for 48 hours."

---

## Loading State

**Shown while:** `loading === true`

**Animation:**
- Three dots, plum color
- Bounce animation (staggered 0.2s delays)

```css
@keyframes dotBounce {
  0%, 60%, 100% { transform: translateY(0); opacity: 0.4; }
  30% { transform: translateY(-6px); opacity: 1; }
}
```

---

## Toast Notifications

**Messages:**
- "Posted to Commons" (after successful post)
- "Pinned to slot 3" (after successful pin)
- "Removed from highlights" (after unpin)
- "Could not post — try again" (on post error)
- "Could not pin tile" (on pin error)
- "Could not unpin" (on unpin error)

**Styling:**
- Fixed position: `bottom: 80px` (above tab bar)
- Center-aligned: `left: 50%; transform: translateX(-50%)`
- Background: `--bg-elevated`
- Border: `--border-soft`
- Auto-dismiss: 2.5s

**Implementation:**
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

---

## Tile Card Styling

### Card structure
```html
<div class="tileCard">
  <div class="cardHead">
    <span class="statusChip">Slot 1</span>
    <span class="orbitCount" *ngIf="orbitMap.get(t.id)">
      ◎ {{ orbitMap.get(t.id) }}
    </span>
  </div>
  <div class="cardBody">
    <img *ngIf="t.mediaUrl" [src]="t.mediaUrl" class="cardImg" />
    <p *ngIf="t.contentText" class="cardText">{{ t.contentText }}</p>
  </div>
  <div class="cardFoot">
    <button>Action</button>
  </div>
</div>
```

### Status chip variants
```css
.statusChip.active {
  background: rgba(127, 119, 221, 0.12);
  border: 1px solid rgba(127, 119, 221, 0.25);
  color: var(--plum-300);
}

.statusChip.expired {
  background: rgba(200, 180, 160, 0.08);
  border: 1px solid var(--border-subtle);
  color: var(--text-dim);
}

.statusChip.pinned {
  background: rgba(212, 160, 23, 0.12);
  border: 1px solid rgba(212, 160, 23, 0.25);
  color: var(--gold-300);
}
```

### Text vs. Image tiles
- **Image:** Full-bleed, `max-height: 200px`, `object-fit: cover`
- **Text:** Centered, 15px display font, `white-space: pre-wrap`

---

## Data Model

### MyTile (Frontend)
```typescript
export interface MyTile {
  id: string;
  contentType: string;
  contentText: string | null;
  mediaUrl: string | null;
  createdAt: string;
  expiresAt: string;
  isExpired: boolean;
  isHighlighted: boolean;
  isModerated: boolean;
  highlightSlot: number | null;
}
```

### ReceivedOrbit (Frontend)
```typescript
export interface ReceivedOrbit {
  id: string;
  orbiterId: number;
  tileId: string;
  relationshipType: string;
  orbitedAt: string;
}
```

---

## Endpoints

| Endpoint | Method | Purpose |
|---|---|---|
| `/tiles/mine` | GET | Fetch user's tiles |
| `/tiles` | POST | Create new tile |
| `/tiles/{id}/highlight` | POST | Pin tile to slot |
| `/tiles/{id}/highlight` | DELETE | Unpin tile |
| `/orbit/received` | GET | Fetch orbits received |

---

## Accessibility

### Keyboard Navigation
- All buttons focusable
- Slot picker: grid navigation (tab order)

### Screen Readers
- Status chips: announced as text
- Orbit count: `◎` read as "orbit"
- Button states: disabled buttons not focusable

---

## Error Handling

| Error | Handling |
|---|---|
| Post fails | Toast: "Could not post — try again" |
| Pin fails | Toast: backend error message or fallback |
| Unpin fails | Toast: "Could not unpin" |
| Load tiles fails | Toast: "Could not load tiles" |
| Load orbits fails | Non-fatal, orbits remain 0 |

---

## Design Rules

**No hover lifts** — Buttons use shadow/glow on hover only, no `translateY`

**Optimistic UI:**
- Clear compose input before server confirms
- Disable buttons during mutation
- Show `"…"` text while working

**Change detection:**
- Strategy: `OnPush`
- Call `cdr.markForCheck()` after all state changes

---

## Future Enhancements

| Feature | Notes |
|---|---|
| Image tile upload | Currently text-only |
| Tile editing | No edit after post |
| Tile deletion | Expires naturally at 48h |
| Orbit details | Who orbited? (privacy concern) |
| Slot reordering | Drag-and-drop to rearrange |
| Tile analytics | Views, dwell time, orbit rate |
| Batch operations | Select multiple → pin/unpin |
