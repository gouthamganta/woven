# Profile Page (`/you`)

**Component:** `ProfilePageComponent`  
**Route:** `/you`  
**Status:** ACTIVE  
**Last Updated:** 2026-08-17

---

## Purpose

Display the user's own profile as others see it. Provides quick access to edit profile, manage tiles, and open settings.

---

## Layout Structure

```
┌─────────────────────────────────────┐
│  Hero Photo (3:4 aspect)            │
│  ┌─────────────────────────────┐    │
│  │                             │    │
│  │   Primary Photo             │    │
│  │                             │    │
│  │   ┌──────────────────────┐  │    │
│  │   │ Name + ✓ Badge       │  │    │
│  │   │ Location             │  │    │
│  │   │ Pronouns             │  │    │
│  │   └──────────────────────┘  │    │
│  └─────────────────────────────┘    │
├─────────────────────────────────────┤
│  Photo Strip (if >1 photo)          │
│  ┌──┐ ┌──┐ ┌──┐ ┌──┐ ┌──┐          │
│  │1 │ │2 │ │3 │ │4 │ │5 │  scroll→ │
│  └──┘ └──┘ └──┘ └──┘ └──┘          │
│  Main                               │
├─────────────────────────────────────┤
│  HIGHLIGHTS         My Tiles →      │
│  ┌──┐ ┌──┐ ┌──┐ ┌─┐                │
│  │1 │ │2 │ │3 │ │+│    scroll→     │
│  └──┘ └──┘ └──┘ └─┘                │
├─────────────────────────────────────┤
│  RECENT            See all →        │
│  ┌──┐ ┌──┐ ┌──┐ ┌──┐ ┌──┐          │
│  │• │ │• │ │• │ │• │ │• │  scroll→ │
│  └──┘ └──┘ └──┘ └──┘ └──┘          │
├─────────────────────────────────────┤
│  LOOKING FOR                        │
│  [ Committed Partnership ]          │
│  [ Open to exploring ] [ Curious ]  │
├─────────────────────────────────────┤
│  ABOUT                              │
│  Your bio text appears here…        │
├─────────────────────────────────────┤
│  DETAILS                            │
│  [ Job · Software Engineer ]        │
│  [ From · Boston ] [ Height · 5'9" ]│
│  [ Pets · Dogs ] …                  │
├─────────────────────────────────────┤
│  ┌─────────────────────────────┐    │
│  │   Edit Profile              │    │
│  └─────────────────────────────┘    │
│  ┌─────────────────────────────┐    │
│  │   My Tiles                  │    │
│  └─────────────────────────────┘    │
│  ┌─────────────────────────────┐    │
│  │   Settings                  │    │
│  └─────────────────────────────┘    │
└─────────────────────────────────────┘
```

---

## Data Loading

### ngOnInit Flow

```typescript
async ngOnInit() {
  try {
    const [profileRes] = await Promise.all([
      firstValueFrom(this.onboarding.getReview()),  // Profile data
      this.loadTiles(),                              // Tiles data
    ]);
    this.profile = profileRes.publicPreview ?? profileRes.self ?? null;
    const photos = profileRes.publicPreview?.photos 
                ?? profileRes.self?.photos 
                ?? profileRes.photos ?? [];
    this.allPhotos = [...photos].sort((a, b) => 
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

**Endpoints:**
- `GET /onboarding/review` — Profile data (`publicPreview` or `self`)
- `GET /tiles/mine` — User's tiles

**Loading strategy:**
- Parallel requests via `Promise.all`
- Tile loading is non-fatal (wrapped in `catch`)
- Photos sorted by `sortOrder` (ascending)

---

## Sections

### 1. Hero Photo

**Layout:**
- Full-width, `aspect-ratio: 3 / 4`
- Gradient overlay (bottom): `linear-gradient(to top, rgba(14,9,18,0.88) → transparent)`
- Fallback: First letter of name in 72px display font

**Overlay content:**
```html
<div class="nameRow">
  <span class="name">{{ profile.name }}</span>
  <span class="verifiedBadge" *ngIf="profile.isVerified">✓</span>
</div>
<div class="sub" *ngIf="profile.location">{{ profile.location }}</div>
<div class="sub" *ngIf="profile.displayPronouns">{{ profile.displayPronouns }}</div>
```

**Verified badge:**
- 22px circle, `--gold-400` background
- Only shown if `profile.isVerified === true`
- Currently unused (Phase 5A feature)

**Typography:**
- Name: `--font-display`, 32px, weight 300, letter-spacing `-0.025em`
- Location/pronouns: `--font-ui`, 13px, `--text-secondary`

---

### 2. Photo Strip

**Conditions:**
- Only shown if `allPhotos.length > 1`

**Layout:**
- Horizontal scroll, `scrollbar-width: none`
- Each thumb: 72×72px, `border-radius: 12px`
- Primary photo (index 0): border color `--gold-400`, "Main" label

**Code:**
```typescript
get primaryPhoto() {
  return this.allPhotos[0] ?? null;
}
```

---

### 3. Tiles Block

**Highlighted Tiles:**
- Section header: "HIGHLIGHTS" + "My Tiles →" link
- Horizontal scroll
- Up to 9 tiles (1-9 slots)
- Each tile shows slot number badge (top-right)
- `+` card at end if `highlights.length < 9`

**Recent Tiles:**
- Section header: "RECENT" + "See all →" link
- Shows 5 most recent active tiles
- Active pip (6px plum circle, top-left)
- Filtered: `!isHighlighted && !isExpired`

**Tile Card:**
- 80×108px
- Image tiles: full-bleed image
- Text tiles: centered, 10px display font, 6-line clamp
- Click → navigates to `/you/tiles`

**Data:**
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

---

### 4. Intent Section

**Shown if:** `profile.intent?.primaryIntent` exists

**Layout:**
- Section label: "LOOKING FOR"
- Primary intent chip (pill shape, gold theme)
- Openness tags (if any): secondary pills below

**Styling:**
```css
.intentChip {
  background: rgba(212, 160, 23, 0.1);
  border: 1px solid rgba(212, 160, 23, 0.3);
  color: var(--gold-300);
  padding: 8px 16px;
  border-radius: 999px;
}

.tag {
  border: 1px solid var(--border-subtle);
  color: var(--text-muted);
  padding: 5px 10px;
  border-radius: 999px;
}
```

---

### 5. Bio Section

**Shown if:** `profile.bio` exists

**Layout:**
- Section label: "ABOUT"
- Bio text: `white-space: pre-wrap` (preserves line breaks)

**Typography:**
- Font: `--font-display`, 15px, weight 400
- Line-height: 1.6
- Color: `--text-primary`

---

### 6. Details Section (Chips)

**Shown if:** `chips.length > 0`

**Chips computed:**
```typescript
get chips(): { label: string; value: string }[] {
  const fields: any[] = this.profile?.optionalPublic ?? [];
  const labelMap: Record<string, string> = {
    job: 'Job', hometown: 'From', education: 'Education', school: 'School',
    pref_height: 'Height', children: 'Children', pets: 'Pets', diet: 'Diet',
    pref_drinking: 'Drinking', pref_smoking: 'Smoking', habits: 'Exercise',
    love_language: 'Love language', languages: 'Languages', mbti: 'MBTI',
  };
  return fields
    .filter((f: any) => f.key && f.value && f.value !== 'hobbies')
    .map((f: any) => ({ label: labelMap[f.key] ?? f.key, value: f.value }));
}
```

**Chip styling:**
- Two-part: `chipKey` (dim) + `chipVal` (secondary, weight 600)
- Pill shape, border `--border-subtle`, background `--bg-elevated`

---

### 7. Actions

**Buttons:**
1. **Edit Profile** → `navigateByUrl('/onboarding/review')`
2. **My Tiles** → `navigateByUrl('/you/tiles')`
3. **Settings** → `navigateByUrl('/you/settings')`

**Styling:**
- Primary: Gold gradient + glow on hover
- Ghost: `--bg-surface`, border `--border-soft`, text `--text-secondary`
- All buttons: 48px min-height, 14px border-radius

**No hover lifts** — Design rule: hover = glow/shadow/color only

---

## Loading States

### Loading

```html
<div class="loading" *ngIf="loading">
  <div class="loadDot"></div>
  <div class="loadDot"></div>
  <div class="loadDot"></div>
</div>
```

**Animation:**
```css
@keyframes dotPulse {
  0%, 60%, 100% { transform: scale(1); opacity: 0.4; }
  30% { transform: scale(1.3); opacity: 1; }
}
```

**Dots:** 8px, `--gold-400`, staggered delays (0s / 0.2s / 0.4s)

---

### Error

```html
<div class="errState" *ngIf="!loading && error">{{ error }}</div>
```

**Styling:**
- Center-aligned, `--rose-300`, 13px UI font
- Padding: `32px 16px`

---

## Design Tokens

### Colors
- `--gold-400` — Primary action, verified badge
- `--plum-400` — Recent tile pip
- `--text-primary` — Name, bio
- `--text-secondary` — Location, pronouns, chip values
- `--text-muted` — Section labels, chip keys
- `--text-dim` — Fallback avatar
- `--bg-elevated` — Photo fallback, chip background
- `--border-subtle` — Photo thumbs, chips

### Spacing
- Page padding: `0 0 24px`
- Section padding: `18px 16px 0`
- Photo strip padding: `12px 16px 0`
- Actions padding: `24px 16px 8px`

### Typography
- **Display font** (Raleway): Name (32px), bio (15px), tile text (10px)
- **UI font** (Inter): All other text

---

## Navigation

| Action | Route |
|---|---|
| Edit Profile | `/onboarding/review` |
| My Tiles | `/you/tiles` |
| Settings | `/you/settings` |
| Tile card click | `/you/tiles` |
| Highlights link | `/you/tiles` |
| Recent link | `/you/tiles` |

---

## Accessibility

### Keyboard Navigation
- All buttons focusable
- Click handlers on divs should be buttons

### Screen Readers
- Alt text on images: `[alt]="profile.name"` / `[alt]="'Photo ' + (i + 1)"`
- Verified badge announced via `*ngIf` logic

### Pronouns
- Loaded from `profile.displayPronouns`
- Updated via `PUT /me/accessibility`

---

## Performance

### Change Detection
- Strategy: `OnPush`
- Manual triggers: `cdr.markForCheck()` after async state changes
- Loaded once on init, no polling

### Image Optimization
- Photos served as URLs (Azure Blob Storage)
- No lazy loading (above fold + small photo strip)

---

## Edge Cases

| Case | Handling |
|---|---|
| No photos | Shows first-letter fallback |
| No tiles | Tiles block hidden entirely |
| No bio | Section hidden |
| No intent | Section hidden |
| No optional fields | Details section hidden |
| Failed tile load | Non-fatal, highlights/recent remain empty |
| Failed profile load | Error state shown |

---

## Related Components

- **[Settings Page](./settings.md)** — Account management
- **[My Tiles Page](./my-tiles.md)** — Tile management
- **[Onboarding Review](../onboarding/review.md)** — Profile editing
