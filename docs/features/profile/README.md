# Profile Feature

**Last Updated:** 2026-08-17  
**Status:** ACTIVE  
**Routes:** `/you`, `/you/settings`, `/you/tiles`

---

## Overview

The Profile feature provides users with a personal space to view, manage, and control their Woven presence. It consists of three main pages:

1. **Profile Page** (`/you`) — View your public-facing profile
2. **Settings** (`/you/settings`) — Account, privacy, and preferences
3. **My Tiles** (`/you/tiles`) — Manage Commons posts and highlights

---

## Architecture

### Frontend Pages

| Route | Component | Purpose |
|---|---|---|
| `/you` | `ProfilePageComponent` | Display user's profile as others see it |
| `/you/settings` | `SettingsPageComponent` | Account, blocks, notifications, coaching |
| `/you/tiles` | `MyTilesPageComponent` | Manage tiles (active, expired, highlighted) |

### Backend Endpoints

**User Data** (`UserDataEndpoints.cs`)
- `GET /me/data-summary` — Data overview (tile/chat/photo counts)
- `GET /me/data-export` — Full GDPR export (rate-limited: 1/30 days)
- `GET /me/blocks` — List blocked users
- `DELETE /me/blocks/{userId}` — Unblock user
- `DELETE /me/account` — Hard delete account

**Preferences** (`MeEndpoints.cs`)
- `GET /me/accessibility` — Display pronouns, reduce motion, high contrast
- `PUT /me/accessibility` — Update accessibility settings
- `GET /me/insights` — User insights and opinion prompts
- `POST /me/insights/opinion` — Submit feedback (1/month limit)

**Push Notifications** (`PushEndpoints.cs`)
- `GET /me/vapid-public-key` — Web Push public key
- `POST /me/push-subscription` — Register browser subscription
- `DELETE /me/push-subscription` — Unregister

**Visual Preferences**
- `POST /me/visual-preference/reset` — Clear learned photo preferences
- `POST /me/voice-preference/reset` — Clear learned voice preferences

---

## Core Features

### 1. Profile Display (`/you`)

**What it shows:**
- Hero photo (primary photo with name overlay)
- Photo strip (thumbnails of all photos, "Main" indicator on first)
- Highlights strip (up to 9 pinned tiles)
- Recent tiles strip (5 most recent active tiles)
- Intent chip (looking for + openness tags)
- Bio (free text, `pre-wrap`)
- Detail chips (job, hometown, height, pets, etc.)

**Actions:**
- **Edit Profile** → `/onboarding/review`
- **My Tiles** → `/you/tiles`
- **Settings** → `/you/settings`

**Data source:**
- `GET /onboarding/review` — Returns `publicPreview` or `self` object
- `GET /tiles/mine` — Returns tiles array

**Loading pattern:**
- Parallel `Promise.all([getReview(), loadTiles()])`
- ChangeDetectionStrategy: `OnPush` — requires `cdr.markForCheck()`

---

### 2. Settings (`/you/settings`)

**Sections:**

#### ACCOUNT
- **Log out** — Clears `localStorage` tokens, redirects to `/login`
- **Delete account** — Two-step confirmation, hard delete via `DELETE /me/account`

#### PRIVACY
- **Blocked users** (collapsible)
  - Shows list of blocked users with avatar, name, blockedAt timestamp
  - Unblock action: `DELETE /me/blocks/{userId}`
  - Fetch on-demand (only when opened)

#### NOTIFICATIONS
- **Push notifications** (toggle)
  - Checks browser support via `PushService.isSupported()`
  - Register: `PushService.register()` → backend `POST /me/push-subscription`
  - Unregister: `PushService.unregister()` → backend `DELETE /me/push-subscription`
  - Disabled state if browser doesn't support Web Push API

#### FOR YOU
- **Weekly reflection** (toggle)
  - Coaching summaries opt-in/out
  - `CoachingService.optIn()` / `CoachingService.optOut()`
  - Default: enabled (`CoachingOptedOut = false` in User entity)

#### ABOUT
- Privacy Policy → `/privacy`
- Terms of Service → `/terms`

**Delete Flow:**
1. Click "Delete account" → `deleteStep = 1`
2. First confirmation: "Delete your account?" → "Yes, delete"
3. Second confirmation: "Are you absolutely sure?" → "Delete forever"
4. On confirm: `DELETE /me/account` → clears tokens → redirects to `/login`

**Backend behavior (hard delete):**
- Deletes all user blobs (photos, voice notes) via `IMediaService.DeleteAllForUserAsync`
- Anonymizes matches: sets `UserAId` or `UserBId` to `0` (preserves other participant's record)
- Bulk-deletes: tiles, chat messages, photo embeddings, vectors, preferences, weights
- Deletes user row from `users` table
- **Note:** AI embeddings retained by OpenAI/Replicate per their policies

---

### 3. My Tiles (`/you/tiles`)

**Layout:**
- **Compose box** (top) — Post new text tiles (10-150 chars)
- **HIGHLIGHTED** — Up to 9 pinned tiles (permanent until unpinned)
- **ACTIVE** — Live tiles (48h lifespan, not yet expired)
- **EXPIRED** — Tiles past 48h (can be pinned to profile)

**Actions:**

| Action | Endpoint | Notes |
|---|---|---|
| Post new tile | `POST /tiles` | 10-150 chars, appears in Commons for 48h |
| Pin to slot | `POST /tiles/{id}/highlight` | Choose slot 1-9 (slot 1 appears first on profile) |
| Unpin tile | `DELETE /tiles/{id}/highlight` | Returns to expired state |

**Slot picker:**
- Modal sheet with 3×3 grid (slots 1-9)
- Occupied slots show `•` indicator
- Occupied slots can be overwritten (replaces previous tile)
- Slot 1 is top-left, appears first on profile

**Orbit display:**
- Shows `◎ N` count for tiles that received orbits
- Orbits = "likes" from other users viewing in Commons
- Fetched via `GET /orbit/received`
- Displayed on all tiles (highlighted, active, expired)

**Empty state:**
- Icon: `◇`
- Title: "No tiles yet"
- Subtitle: "Post your first thought above — it'll appear anonymously in Commons for 48 hours."

---

## Data Models

### User Entity

```csharp
public class User
{
    public int Id { get; set; }
    public required string Email { get; set; }
    public string? PasswordHash { get; set; }
    public string? FullName { get; set; }
    public string? ProfilePhoto { get; set; }

    public ProfileStatus ProfileStatus { get; set; } = ProfileStatus.INCOMPLETE;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Phase 2B: trust scoring (0.0–1.0, default 0.5)
    public float TrustScore { get; set; } = 0.5f;
    public DateTime? TrustUpdatedAt { get; set; }

    // Phase 4A: anti-ghosting signals
    public float GhostScore { get; set; } = 0.5f;
    public DateTimeOffset? LastActiveAt { get; set; }

    // Phase 5A: identity verification
    public bool IsVerified { get; set; } = false;
    public DateTimeOffset? VerifiedAt { get; set; }
    public string? VerificationType { get; set; }

    // Weekly coaching opt-out
    public bool CoachingOptedOut { get; set; } = false;
}
```

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

---

## Security & Privacy

### Rate Limits
- **Data export:** 1 request per 30 days (cached with `ICacheService`)
- **Opinion submission:** 1 per calendar month (tracked per `yyyy-MM`)

### GDPR Compliance
- `GET /me/data-summary` — Lightweight overview of stored data
- `GET /me/data-export` — Full export (tiles, messages, visual prefs, profile)
- `DELETE /me/account` — Hard delete with anonymization
- **Third-party note:** Export response includes:
  > "AI processors (OpenAI, Replicate) may retain data per their own retention policies."

### Blocked Users
- Blocking happens during trial/chat (not from profile settings)
- Settings only provides **unblock** capability
- Blocks persist in `blocks` table (`blocker_id`, `blocked_id`, `created_at`)

---

## Design Patterns

### Change Detection
All profile pages use `ChangeDetectionStrategy.OnPush`:
- Call `cdr.markForCheck()` after async state changes
- Required for loading states, toggles, delete flow

### Optimistic UI
- **Unblock:** Shows loading (`"…"`) on clicked user row
- **Pin/unpin:** Disables buttons, shows `"…"` during request
- **Post tile:** Disables button, shows `"…"`, clears input on success

### Toast Notifications
- `showToast(msg)` — 2.5s auto-dismiss
- Examples: "Posted to Commons", "Pinned to slot 3", "Could not unpin"

### Empty States
- Blocked users list: "No blocked users"
- My Tiles: Centered empty state with icon + subtitle

---

## Related Docs

- **[Profile Page Details](./profile-page.md)** — `/you` layout and components
- **[Settings Page Details](./settings.md)** — Account, privacy, notifications
- **[My Tiles Details](./my-tiles.md)** — Tile management and highlighting
- **[Frontend Implementation](./frontend.md)** — Angular components
- **[Backend Implementation](./backend.md)** — Endpoints and entities

---

## Known Gaps

| Gap | Notes |
|---|---|
| No profile editing in settings | Must go to `/onboarding/review` |
| No bio editing shortcut | Same as above |
| No photo reordering UI | Must re-upload via onboarding |
| No tile analytics | Orbit count only, no views/dwell |
| No coaching summary preview | Toggle only, no inline preview |

---

## Future Considerations

- **Profile versioning** — Track edit history
- **Profile visibility controls** — Pause/hide from discovery
- **Export scheduled deletion** — GDPR "right to be forgotten" automation
- **Block reasons** — Optional text field when blocking
- **Tile performance metrics** — Views, dwell time, orbit rate
