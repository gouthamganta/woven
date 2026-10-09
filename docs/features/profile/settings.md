# Settings Page (`/you/settings`)

**Component:** `SettingsPageComponent`  
**Route:** `/you/settings`  
**Status:** ACTIVE  
**Last Updated:** 2026-08-17

---

## Purpose

Central hub for account management, privacy controls, notification preferences, and app settings.

---

## Layout Structure

```
┌─────────────────────────────────────┐
│  ← Settings                         │
├─────────────────────────────────────┤
│  ACCOUNT                            │
│  ┌─────────────────────────────┐    │
│  │ Log out                  →  │    │
│  ├─────────────────────────────┤    │
│  │ Delete account           →  │    │
│  └─────────────────────────────┘    │
├─────────────────────────────────────┤
│  PRIVACY                            │
│  ┌─────────────────────────────┐    │
│  │ Blocked users        [3] ↓  │    │
│  │ ┌─────────────────────────┐ │    │
│  │ │ Avatar | Name | Unblock │ │    │
│  │ │ Avatar | Name | Unblock │ │    │
│  │ └─────────────────────────┘ │    │
│  └─────────────────────────────┘    │
├─────────────────────────────────────┤
│  NOTIFICATIONS                      │
│  ┌─────────────────────────────┐    │
│  │ Push notifications   [ ON ] │    │
│  └─────────────────────────────┘    │
├─────────────────────────────────────┤
│  FOR YOU                            │
│  ┌─────────────────────────────┐    │
│  │ Weekly reflection    [ ON ] │    │
│  └─────────────────────────────┘    │
├─────────────────────────────────────┤
│  ABOUT                              │
│  ┌─────────────────────────────┐    │
│  │ Privacy Policy           →  │    │
│  ├─────────────────────────────┤    │
│  │ Terms of Service         →  │    │
│  └─────────────────────────────┘    │
│                                     │
│  Woven v0.1                         │
└─────────────────────────────────────┘
```

---

## Sections

### 1. ACCOUNT

#### Log out

**Action:**
```typescript
logout() {
  localStorage.removeItem('accessToken');
  localStorage.removeItem('user');
  this.router.navigateByUrl('/login');
}
```

**Behavior:**
- Clears JWT token from localStorage
- Clears cached user object
- Redirects to `/login`
- No server-side session invalidation (stateless JWT)

---

#### Delete account

**Flow:**
1. Click "Delete account" → `deleteStep = 1`
2. **First sheet:** "Delete your account?"
   - Body: "This permanently removes your profile, photos, tiles, and chat history. Matches will be anonymized. This cannot be undone."
   - Actions: "Cancel" / "Yes, delete"
3. Click "Yes, delete" → `deleteStep = 2`
4. **Second sheet:** "Are you absolutely sure?"
   - Body: "Once deleted, your account is gone forever. There is no recovery."
   - Actions: "Cancel" / "Delete forever"
5. Click "Delete forever" → calls `confirmDelete()`

**Code:**
```typescript
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

**Backend:** `DELETE /me/account`
- Deletes all blobs (photos, voice notes) via `IMediaService.DeleteAllForUserAsync`
- Anonymizes matches: sets `UserAId` or `UserBId` to `0`
- Bulk-deletes: tiles, chat messages, embeddings, preferences, weights
- Deletes user row
- **Note:** Response includes AI retention notice:
  > "AI processors (OpenAI, Replicate) may retain embeddings per their own retention policies. Contact support to submit deletion requests to those providers."

**Sheet overlay:**
- Fixed position, full-screen backdrop (`rgba(0,0,0,0.72)`)
- Bottom sheet: rounded top corners (`--radius-xl`)
- Click backdrop → calls `cancelDelete()`
- Click inside sheet → `$event.stopPropagation()`

---

### 2. PRIVACY

#### Blocked users

**Toggle behavior:**
```typescript
toggleBlocks() {
  this.showBlocks = !this.showBlocks;
  if (this.showBlocks && this.blockedUsers.length === 0 && !this.loadingBlocks) {
    this.fetchBlocks();
  }
  this.cdr.markForCheck();
}
```

**Lazy loading:**
- Fetches on first expand only
- Endpoint: `GET /me/blocks`
- Response:
  ```json
  [
    {
      "userId": 123,
      "name": "Jane Doe",
      "photo": "https://...",
      "blockedAt": "2026-08-15T10:30:00Z"
    }
  ]
  ```

**UI states:**
- Closed: Shows count badge (e.g., `[3]`) + down arrow
- Open: Shows list or "No blocked users" empty state
- Loading: Shows "Loading…" text

**Unblock action:**
```typescript
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

**Optimistic UI:**
- Button shows `"…"` while request pending
- On success: removes row from list
- On error: button returns to "Unblock" state

**Block row:**
- Avatar: 36px circle, fallback = first letter
- Name: `--text-primary`, 14px
- Unblock button: Plum theme, 12px, disabled while working

---

### 3. NOTIFICATIONS

#### Push notifications toggle

**Supported check:**
```typescript
async ngOnInit() {
  this.notifSupported = await this.push.isSupported();
  this.notifEnabled = this.notifSupported && await this.push.isSubscribed();
  this.cdr.markForCheck();
}
```

**Toggle logic:**
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

**PushService methods:**
- `isSupported()` — Checks for `navigator.serviceWorker` + `PushManager`
- `isSubscribed()` — Checks for active push subscription
- `register()` — Requests permission, subscribes, sends to backend
- `unregister()` — Unsubscribes, deletes from backend

**Backend endpoints:**
- `GET /me/vapid-public-key` — Returns public key for subscription
- `POST /me/push-subscription` — Registers browser subscription
- `DELETE /me/push-subscription` — Unregisters

**Toggle styling:**
- Off: `--bg-elevated`, `--border-soft`
- On: `--rose-400` background + border
- Thumb: 18px white circle, translates 18px when on
- Disabled: Opacity 0.5 if browser unsupported

---

### 4. FOR YOU

#### Weekly reflection toggle

**What it does:**
- Enables/disables weekly coaching summaries
- Delivered via email (future: in-app inbox)
- Default: enabled (`User.CoachingOptedOut = false`)

**Toggle logic:**
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

**CoachingService methods:**
- `optOut()` — `POST /coaching/opt-out` → sets `User.CoachingOptedOut = true`
- `optIn()` — `DELETE /coaching/opt-out` → sets `User.CoachingOptedOut = false`

**Backend:**
- `CoachingSummaryWorker` checks `CoachingOptedOut` before generating summaries
- Summaries stored in `coaching_summaries` table
- Sent weekly (schedule TBD)

---

### 5. ABOUT

#### Privacy Policy / Terms of Service

**Links:**
- Privacy Policy → `routerLink="/privacy"`
- Terms of Service → `routerLink="/terms"`

**Styling:**
- Row with right arrow `→`
- `router-link-active` class applies on match

---

## Component State

```typescript
export class SettingsPageComponent implements OnInit {
  blockedUsers: BlockedUser[] = [];
  showBlocks = false;
  loadingBlocks = false;
  unblocking: number | null = null;

  notifEnabled = false;
  notifPending = false;
  notifSupported = true;

  coachingEnabled = true;  // Default ON
  coachingPending = false;

  deleteStep = 0;  // 0 = closed, 1 = first confirm, 2 = second confirm
  deleting = false;
}
```

---

## Design Tokens

### Card styling
```css
.card {
  background: var(--bg-surface);
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-lg);
  overflow: hidden;
}
```

### Row styling
```css
.row {
  padding: 16px 18px;
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.row:active {
  background: rgba(255,255,255,0.04);
}
```

### Toggle styling
```css
.toggle {
  width: 44px;
  height: 26px;
  border-radius: var(--radius-full);
  background: var(--bg-elevated);
  border: 1px solid var(--border-soft);
}

.toggle.on {
  background: var(--rose-400);
  border-color: var(--rose-400);
}

.thumb {
  width: 18px;
  height: 18px;
  border-radius: 50%;
  background: #fff;
  transform: translateX(0);
  transition: transform 200ms var(--ease-out);
}

.toggle.on .thumb {
  transform: translateX(18px);
}
```

---

## Accessibility

### Keyboard Navigation
- All rows focusable
- Toggle buttons: space/enter to activate

### Screen Readers
- Section labels: `ACCOUNT`, `PRIVACY`, etc. (uppercase, 10px, 700 weight)
- Row labels: descriptive text
- Toggle state: announced via ARIA (implicit)

### High Contrast
- Stored in `UserPreferences.HighContrast`
- Applied via `PUT /me/accessibility`
- Not yet wired to UI (future work)

---

## Error Handling

| Scenario | Handling |
|---|---|
| Fetch blocks fails | `loadingBlocks = false`, list remains empty |
| Unblock fails | Button returns to "Unblock", no toast |
| Delete fails | Closes sheet, `deleting = false` |
| Push toggle fails | Non-critical `catch`, state reverts |
| Coaching toggle fails | Non-critical `catch`, no user feedback |

**No toast notifications** — Silent failures (matches design system)

---

## Security

### CSRF Protection
- JWT in localStorage (not cookies)
- All requests include `Authorization: Bearer <token>` header
- No CSRF tokens needed (stateless)

### Delete safeguards
- Two-step confirmation
- No accidental deletes
- Irreversible action clearly stated

---

## Related Endpoints

| Endpoint | Method | Purpose |
|---|---|---|
| `/me/blocks` | GET | List blocked users |
| `/me/blocks/{userId}` | DELETE | Unblock user |
| `/me/account` | DELETE | Hard delete account |
| `/me/vapid-public-key` | GET | Web Push public key |
| `/me/push-subscription` | POST | Register push subscription |
| `/me/push-subscription` | DELETE | Unregister push subscription |
| `/coaching/opt-out` | POST | Disable coaching summaries |
| `/coaching/opt-out` | DELETE | Enable coaching summaries |
| `/me/accessibility` | GET | Get accessibility settings |
| `/me/accessibility` | PUT | Update accessibility settings |

---

## Future Enhancements

| Feature | Notes |
|---|---|
| In-app block management | Currently blocked via chat only |
| Block with reason | Optional text field |
| Export data trigger | One-click GDPR export |
| Notification granularity | Per-event toggles (messages, moments, etc.) |
| Theme switcher | Dark/light/auto |
| Language selector | I18n support |
| High contrast mode UI | Accessibility setting exists, not wired |
| Reduce motion UI | Accessibility setting exists, not wired |
