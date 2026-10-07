# Frontend Architecture

**Framework:** Angular 21  
**Language:** TypeScript 5.9  
**Port:** 4202 (development)  
**SSR:** Enabled (Angular Universal)  
**UI Library:** PrimeNG 21

---

## Overview

Woven's frontend is a modern Angular 21 application with **Server-Side Rendering** (SSR), **standalone components**, and **OnPush change detection** for optimal performance.

**Design principles:**
- **Standalone components** — no NgModules, simpler DI
- **OnPush change detection** — manual `cdr.markForCheck()` after async updates
- **Services for data** — components never call HTTP directly
- **Optimistic UI** — instant feedback, silent error correction
- **Flat route structure** — no nested outlets, simple navigation

---

## Project Structure

```
frontend/woven-frontend/src/app/
├── app.component.ts          ← Root component (shell)
├── app.routes.ts             ← Main route definitions
├── app.config.ts             ← App providers (DI, HTTP, SSR)
├── pages/                    ← Feature pages (routed components)
│   ├── landing-simple/       ← Public landing page
│   ├── login/                ← Login/OAuth page
│   ├── onboarding/           ← Multi-step onboarding
│   ├── home/                 ← Tab container (Moments/Commons/Chats/You)
│   ├── moments/              ← Daily deck (Deck + Drawn tabs)
│   ├── chats/                ← Chat list + thread view
│   ├── balloons/             ← Balloon (match connection window)
│   ├── matches/              ← Match profile viewer
│   ├── commons/              ← Content feed (Tiles + Orbit)
│   ├── my-tiles/             ← User's own tiles
│   ├── profile/              ← User profile
│   ├── settings/             ← Settings page
│   └── legal/                ← Privacy policy, terms
├── components/               ← Reusable components
│   ├── coaching-card/        ← Weekly coaching summary card
│   └── ...                   ← Other shared components
├── services/                 ← Data services (HTTP, state, real-time)
│   ├── chat.service.ts       ← Chat API + SignalR
│   ├── moments.service.ts    ← Moments API (deck, swipes)
│   ├── matches.service.ts    ← Match API
│   ├── games.service.ts      ← KnowMe, RedGreenFlag games
│   ├── push-notification.service.ts ← Web Push subscriptions
│   ├── realtime.service.ts   ← SignalR connection manager
│   └── ...                   ← Other services
├── core/                     ← Core services (auth, guards, interceptors)
│   ├── guards/               ← Route guards (AuthGuard, GuestGuard)
│   ├── interceptors/         ← HTTP interceptors (auth, correlation)
│   └── ...                   ← Other core services
├── onboarding/               ← Onboarding flow components
│   ├── foundational.component.ts  ← Foundational questions
│   ├── details.component.ts       ← Profile details
│   └── ...                        ← Other onboarding steps
└── styles.scss               ← Global styles (CSS variables, tokens)
```

---

## Routing

**Flat structure** — no nested outlets, all routes at top level.

**Route definitions:** [app.routes.ts](../../frontend/woven-frontend/src/app/app.routes.ts)

| Path | Component | Purpose |
|------|-----------|---------|
| `/` | `LandingSimpleComponent` | Public landing page (3D intro video) |
| `/login` | `LoginComponent` | Google OAuth login |
| `/onboarding/*` | Various | Multi-step onboarding (photos, foundational, details, tiles) |
| `/home` | `HomeComponent` | Tab container (Moments/Commons/Chats/You) |
| `/moments` | `MomentsPage` | Daily deck (Deck + Drawn tabs) |
| `/chats` | `ChatsListComponent` | Chat list |
| `/chats/:threadId` | `ChatsListComponent` | Chat thread view |
| `/matches/:matchId/profile` | `MatchProfileComponent` | Match profile viewer |
| `/commons` | `CommonsComponent` | Content feed (Tiles + Orbit) |
| `/you` | `ProfileComponent` | User profile |
| `/you/settings` | `SettingsComponent` | Settings page |
| `/you/tiles` | `MyTilesComponent` | User's own tiles |
| `/legal/privacy` | `PrivacyPolicyComponent` | Privacy policy |
| `/legal/terms` | `TermsComponent` | Terms of service |

**Guards:**
- `AuthGuard` — requires authenticated user (redirects to `/login` if not)
- `GuestGuard` — requires guest (redirects to `/home` if authenticated)

---

## Standalone Components

**All components are standalone** — no `NgModule` declarations.

**Example:**
```typescript
@Component({
  selector: 'app-moments-page',
  standalone: true,
  imports: [CommonModule, MomentsCardComponent, TabViewModule],
  templateUrl: './moments.page.html',
  styleUrls: ['./moments.page.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class MomentsPage { }
```

**Benefits:**
- Simpler DI — no need to track NgModule hierarchies
- Smaller bundles — tree-shaking at component level
- Easier testing — no module setup

---

## Change Detection

**All page components use `OnPush` strategy.**

**Rule:** After async state updates, call `cdr.markForCheck()` to trigger change detection.

**Example:**
```typescript
export class MomentsPage {
  private cdr = inject(ChangeDetectorRef);
  private momentsService = inject(MomentsService);

  deck: DailyDeck | null = null;

  async ngOnInit() {
    // After async data fetch, manually trigger change detection
    this.deck = await firstValueFrom(this.momentsService.getDeck());
    this.cdr.markForCheck();
  }
}
```

**Why OnPush:**
- **Performance** — Angular only checks components when inputs change or manual trigger
- **Explicit** — async state updates are visible in code (`cdr.markForCheck()`)
- **Prevents bugs** — no silent failures from forgotten subscriptions

---

## Services Architecture

**Data services handle all HTTP calls.** Components never call `HttpClient` directly.

**Pattern:**
1. Service injects `HttpClient`
2. Service methods return `Observable<T>` (or `Promise<T>` via `firstValueFrom`)
3. Components call service methods, subscribe/await results
4. Components call `cdr.markForCheck()` after async updates

**Example service:**
```typescript
@Injectable({ providedIn: 'root' })
export class MomentsService {
  private http = inject(HttpClient);
  private baseUrl = environment.apiUrl;

  getDeck(): Observable<DailyDeck> {
    return this.http.get<DailyDeck>(`${this.baseUrl}/moments/deck`);
  }

  respond(candidateId: number, choice: 'MAGICAL' | 'LOGICAL' | 'SKIP'): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/moments/respond`, { candidateId, choice });
  }
}
```

**Key services:**

| Service | Purpose | File |
|---------|---------|------|
| **ChatService** | Chat API + SignalR hub | [services/chat.service.ts](../../frontend/woven-frontend/src/app/services/chat.service.ts) |
| **MomentsService** | Moments API (deck, swipes) | [services/moments.service.ts](../../frontend/woven-frontend/src/app/services/moments.service.ts) |
| **MatchesService** | Match API (balloon pop, profiles) | [services/matches.service.ts](../../frontend/woven-frontend/src/app/services/matches.service.ts) |
| **GamesService** | KnowMe, RedGreenFlag games | [services/games.service.ts](../../frontend/woven-frontend/src/app/services/games.service.ts) |
| **CoachingService** | Weekly coaching summaries | [services/coaching.service.ts](../../frontend/woven-frontend/src/app/services/coaching.service.ts) |
| **PushNotificationService** | Web Push subscriptions | [services/push-notification.service.ts](../../frontend/woven-frontend/src/app/services/push-notification.service.ts) |
| **RealtimeService** | SignalR connection manager | [services/realtime.service.ts](../../frontend/woven-frontend/src/app/services/realtime.service.ts) |
| **MediaService** | Azure Blob upload (SAS tokens) | [services/media.service.ts](../../frontend/woven-frontend/src/app/services/media.service.ts) |
| **CommonsService** | Tile feed, Orbit interactions | [services/commons.service.ts](../../frontend/woven-frontend/src/app/services/commons.service.ts) |

---

## HTTP Interceptors

**Interceptors add headers, handle errors, retry requests.**

**Registered interceptors:**

| Interceptor | Purpose | File |
|-------------|---------|------|
| **AuthInterceptor** | Adds `Authorization: Bearer <token>` header to all API requests | [core/interceptors/auth.interceptor.ts](../../frontend/woven-frontend/src/app/core/interceptors/auth.interceptor.ts) |
| **CorrelationIdInterceptor** | Generates `X-Correlation-ID` header (16-char hex) per request | [core/interceptors/correlation-id.interceptor.ts](../../frontend/woven-frontend/src/app/core/interceptors/correlation-id.interceptor.ts) |

**Example:**
```typescript
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = localStorage.getItem('jwt');
  if (token) {
    req = req.clone({
      setHeaders: { Authorization: `Bearer ${token}` }
    });
  }
  return next(req);
};
```

**Registration:** [app.config.ts](../../frontend/woven-frontend/src/app/app.config.ts)

---

## Real-Time Communication (SignalR)

**Library:** `@microsoft/signalr` 8.0.7

**Hub:** `ChatHub` (backend SignalR hub)

**Service:** [RealtimeService](../../frontend/woven-frontend/src/app/services/realtime.service.ts)

**Flow:**
1. User logs in → `RealtimeService.start()` connects to SignalR hub
2. Backend sends events: `ReceiveMessage`, `TypingStatus`, `MessageRead`, etc.
3. Frontend components subscribe to `RealtimeService` observables
4. On event → update local state + call `cdr.markForCheck()`

**Example:**
```typescript
export class ChatsListComponent {
  private realtimeService = inject(RealtimeService);
  private cdr = inject(ChangeDetectorRef);

  ngOnInit() {
    this.realtimeService.onMessageReceived$.subscribe(msg => {
      this.messages.push(msg);
      this.cdr.markForCheck();
    });
  }
}
```

**Connection lifecycle:**
- Login → `start()` (auto-reconnect enabled)
- Logout → `stop()`
- Network error → auto-reconnect (exponential backoff)

---

## Web Push Notifications

**Service:** [PushNotificationService](../../frontend/woven-frontend/src/app/services/push-notification.service.ts)

**Service Worker:** [public/service-worker.js](../../frontend/woven-frontend/public/service-worker.js)

**Flow:**
1. User grants notification permission → request VAPID public key from backend
2. Service worker registers push subscription
3. Frontend sends subscription to backend (`POST /push-notifications/subscribe`)
4. Backend sends Web Push notifications via VAPID

**Events handled by service worker:**
- `push` — displays notification
- `notificationclick` — opens chat thread or redirects to app

**Permissions:**
- Browser asks for permission on first visit (after login)
- User can revoke in browser settings

---

## Optimistic UI

**Pattern:** Show instant feedback, silently correct errors.

**Example: Sending a message**
```typescript
async sendMessage(text: string) {
  const tempId = crypto.randomUUID();
  const tempMessage = { id: tempId, text, sentAt: new Date(), status: 'sending' };

  // 1. Optimistic add
  this.messages.push(tempMessage);
  this.cdr.markForCheck();

  try {
    // 2. Send to backend
    const confirmed = await firstValueFrom(this.chatService.sendMessage(this.threadId, text));

    // 3. Replace temp with confirmed
    const idx = this.messages.findIndex(m => m.id === tempId);
    this.messages[idx] = confirmed;
  } catch (err) {
    // 4. Silent rollback (or show error toast)
    this.messages = this.messages.filter(m => m.id !== tempId);
  }

  this.cdr.markForCheck();
}
```

**Used for:**
- Sending messages
- Swiping on Moments cards
- Popping balloons
- Submitting game answers

---

## Styling

**Global styles:** [styles.scss](../../frontend/woven-frontend/src/styles.scss)

**Design system:**
- **CSS variables** — all colors, spacing, typography defined as tokens
- **No raw hex or px** — everything references variables
- **Theme-aware** — light/dark mode via `data-theme` attribute
- **PrimeNG theme** — custom theme built on `@primeng/themes`

**Example variables:**
```scss
:root {
  --color-primary: #6C5CE7;
  --color-background: #FFFFFF;
  --spacing-sm: 8px;
  --spacing-md: 16px;
  --font-family-base: 'Inter', sans-serif;
  --border-radius-md: 12px;
}
```

**Component styles:**
- **Scoped SCSS** — every component has `.scss` file
- **BEM naming** — `.moments-card__header`, `.moments-card__actions`
- **No global classes in components** — components are self-contained

**Design rules (from CLAUDE.md):**
- **No hover translateY lifts** — hover = glow/shadow/color only
- **No age on Moments cards** — name + badge + explanation + actions only
- **Background drift stays on** — don't touch `woven-bg` unless asked

---

## SSR (Server-Side Rendering)

**Enabled:** Yes (Angular Universal)

**Entry points:**
- **Browser:** [main.ts](../../frontend/woven-frontend/src/main.ts)
- **Server:** [main.server.ts](../../frontend/woven-frontend/src/main.server.ts)

**Benefits:**
- **SEO** — landing page fully rendered for crawlers
- **Faster initial load** — HTML sent before JS hydration
- **Better UX** — content visible before Angular boots

**SSR-safe patterns:**
```typescript
export class SomeComponent {
  private platformId = inject(PLATFORM_ID);

  ngOnInit() {
    if (isPlatformBrowser(this.platformId)) {
      // Browser-only code (localStorage, window, etc.)
      const token = localStorage.getItem('jwt');
    }
  }
}
```

**SSR routes:** [app.routes.server.ts](../../frontend/woven-frontend/src/app/app.routes.server.ts)

---

## State Management

**No global state library** (no NgRx, no Akita).

**Patterns:**
1. **Services hold state** — `BehaviorSubject` for reactive state
2. **Components subscribe** — `async` pipe or manual subscription
3. **OnPush + markForCheck()** — manual change detection

**Example:**
```typescript
@Injectable({ providedIn: 'root' })
export class ChatService {
  private threadsSubject = new BehaviorSubject<ChatThread[]>([]);
  public threads$ = this.threadsSubject.asObservable();

  async loadThreads() {
    const threads = await firstValueFrom(this.http.get<ChatThread[]>('/chats'));
    this.threadsSubject.next(threads);
  }
}
```

**Why no NgRx:**
- App is small enough for service-based state
- Fewer abstractions → simpler mental model
- Easier to reason about data flow

---

## Performance Optimizations

### 1. OnPush Change Detection
All page components use `ChangeDetectionStrategy.OnPush` → Angular skips change detection unless:
- Input changes (rare, most pages have no inputs)
- Manual `cdr.markForCheck()` call

### 2. Lazy Loading
Routes lazy-load components via dynamic imports:
```typescript
{
  path: 'moments',
  loadComponent: () => import('./pages/moments/moments.page').then(m => m.MomentsPage),
  canActivate: [AuthGuard]
}
```

### 3. Image Optimization
- **Lazy loading** — `loading="lazy"` on all images
- **Responsive images** — `srcset` for multiple resolutions
- **Azure CDN** — images served from Azure Blob + CDN

### 4. Bundle Splitting
- **Main bundle** — app code
- **Vendor bundle** — Angular + PrimeNG
- **Lazy chunks** — per-route components

**Build output (production):**
```
main.js         — 200 KB
vendor.js       — 800 KB
moments.js      — 50 KB  (lazy)
chats.js        — 80 KB  (lazy)
```

---

## Environment Configuration

**Files:**
- [environment.ts](../../frontend/woven-frontend/src/environments/environment.ts) — development
- [environment.prod.ts](../../frontend/woven-frontend/src/environments/environment.prod.ts) — production

**Variables:**
```typescript
export const environment = {
  production: false,
  apiUrl: 'http://localhost:5135',
  signalRUrl: 'http://localhost:5135/hubs/chat'
};
```

**File replacement:**  
Angular CLI replaces `environment.ts` with `environment.prod.ts` on production build (configured in [angular.json](../../frontend/woven-frontend/angular.json)).

---

## Build & Deployment

### Development

```bash
cd frontend/woven-frontend
npx ng serve --port 4202
```

**Dev server:** http://localhost:4202  
**API base:** http://localhost:5135 (absolute URL, no proxy)

### Production

```bash
npx ng build --configuration production
```

**Output:** `dist/woven-frontend/browser/` (static files for CDN/nginx)  
**SSR output:** `dist/woven-frontend/server/` (Node.js server for SSR)

**Serving SSR:**
```bash
node dist/woven-frontend/server/server.mjs
```

---

## Testing

**Framework:** Vitest 4.0.8 (replacing Karma)

**Running tests:**
```bash
cd frontend/woven-frontend
npm test
```

**Test files:** `*.spec.ts` next to component files

**Example:**
```typescript
import { describe, it, expect } from 'vitest';
import { MomentsPage } from './moments.page';

describe('MomentsPage', () => {
  it('should create', () => {
    const component = new MomentsPage();
    expect(component).toBeTruthy();
  });
});
```

**Current coverage:** Minimal (bootstrap phase)

---

## Key Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| **@angular/core** | 21.0.0 | Angular framework |
| **@angular/ssr** | 21.0.4 | Server-side rendering |
| **primeng** | 21.0.2 | UI component library |
| **@microsoft/signalr** | 8.0.7 | Real-time communication |
| **gsap** | 3.15.0 | Animations (landing page) |
| **lenis** | 1.3.25 | Smooth scroll (landing page) |
| **three** | 0.184.0 | 3D intro video (landing page) |
| **lottie-web** | 5.13.0 | Lottie animations |
| **motion** | 12.39.0 | Motion library (animations) |

---

## Known Issues

**Issue:** Some components still use default change detection (not OnPush)  
**Impact:** Slight performance hit  
**Fix:** Migrate all components to OnPush + manual `cdr.markForCheck()`

**Issue:** No E2E tests yet  
**Impact:** Manual testing required for critical flows  
**Fix:** Add Playwright E2E tests

---

## Next Steps

1. **Migrate all components to OnPush** — performance win
2. **Add E2E tests** — Playwright for critical flows (login, swipe, chat, trial)
3. **Bundle size optimization** — analyze with `webpack-bundle-analyzer`
4. **Service Worker for offline** — cache API responses, queue writes
5. **Accessibility audit** — WCAG 2.1 AA compliance

---

**Last Updated:** 2026-10-07  
**Evidence:** [app.routes.ts](../../frontend/woven-frontend/src/app/app.routes.ts), [services/](../../frontend/woven-frontend/src/app/services/), [pages/](../../frontend/woven-frontend/src/app/pages/)
