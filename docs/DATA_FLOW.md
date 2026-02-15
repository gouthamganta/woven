# Woven Data Flow

> Complete reference for how data moves through the Woven system.
> Understanding these flows is critical for implementing features correctly.

---

## Overview Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                              FRONTEND                                   │
│  ┌─────────────┐    ┌─────────────┐    ┌─────────────┐                 │
│  │   Angular   │    │   Services  │    │    State    │                 │
│  │  Components │◀──▶│  (RxJS)     │◀──▶│ (BehaviorSubject)            │
│  └─────────────┘    └──────┬──────┘    └─────────────┘                 │
│                            │                                            │
│                     HTTP/WebSocket                                      │
└────────────────────────────┼────────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                              BACKEND                                    │
│  ┌─────────────┐    ┌─────────────┐    ┌─────────────┐                 │
│  │  Endpoints  │───▶│  Services   │───▶│    EF Core  │                 │
│  │ (Minimal API)│    │ (Business)  │    │   (ORM)     │                 │
│  └─────────────┘    └─────────────┘    └──────┬──────┘                 │
│                                               │                         │
└───────────────────────────────────────────────┼─────────────────────────┘
                                                │
                                                ▼
                                    ┌─────────────────────┐
                                    │    PostgreSQL       │
                                    │    (Database)       │
                                    └─────────────────────┘
```

---

## 1. Authentication Flow

### Login Flow

```
┌──────────┐      ┌─────────────┐      ┌─────────────┐      ┌──────────┐
│  User    │      │  Frontend   │      │   Backend   │      │    DB    │
└────┬─────┘      └──────┬──────┘      └──────┬──────┘      └────┬─────┘
     │                   │                    │                   │
     │ Enter credentials │                    │                   │
     │──────────────────▶│                    │                   │
     │                   │                    │                   │
     │                   │ POST /api/auth/login                   │
     │                   │───────────────────▶│                   │
     │                   │                    │                   │
     │                   │                    │ Validate user     │
     │                   │                    │──────────────────▶│
     │                   │                    │                   │
     │                   │                    │◀──────────────────│
     │                   │                    │                   │
     │                   │                    │ Generate JWT      │
     │                   │                    │                   │
     │                   │◀───────────────────│                   │
     │                   │ { token, user }    │                   │
     │                   │                    │                   │
     │                   │ Store token        │                   │
     │                   │ Update state       │                   │
     │                   │                    │                   │
     │◀──────────────────│                    │                   │
     │ Redirect home     │                    │                   │
```

### Token Refresh Flow

```typescript
// auth.interceptor.ts
@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    const token = this.auth.getToken();

    if (token) {
      req = req.clone({
        setHeaders: { Authorization: `Bearer ${token}` }
      });
    }

    return next.handle(req).pipe(
      catchError(err => {
        if (err.status === 401) {
          this.auth.logout();
          this.router.navigate(['/auth/login']);
        }
        return throwError(() => err);
      })
    );
  }
}
```

---

## 2. Moments Flow

### Loading Moments (Deck)

```
┌──────────┐      ┌─────────────┐      ┌─────────────┐      ┌──────────┐
│  User    │      │  Frontend   │      │   Backend   │      │    DB    │
└────┬─────┘      └──────┬──────┘      └──────┬──────┘      └────┬─────┘
     │                   │                    │                   │
     │ Open Moments      │                    │                   │
     │──────────────────▶│                    │                   │
     │                   │                    │                   │
     │                   │ GET /api/moments/deck                  │
     │                   │───────────────────▶│                   │
     │                   │                    │                   │
     │                   │                    │ Get user budget   │
     │                   │                    │──────────────────▶│
     │                   │                    │                   │
     │                   │                    │ Get active theme  │
     │                   │                    │──────────────────▶│
     │                   │                    │                   │
     │                   │                    │ Get candidates    │
     │                   │                    │ (filtered, scored)│
     │                   │                    │──────────────────▶│
     │                   │                    │                   │
     │                   │◀───────────────────│                   │
     │                   │ { theme, cards,    │                   │
     │                   │   budget }         │                   │
     │                   │                    │                   │
     │◀──────────────────│                    │                   │
     │ Display cards     │                    │                   │
```

### Making a Choice

```
┌──────────┐      ┌─────────────┐      ┌─────────────┐      ┌──────────┐
│  User    │      │  Frontend   │      │   Backend   │      │    DB    │
└────┬─────┘      └──────┬──────┘      └──────┬──────┘      └────┬─────┘
     │                   │                    │                   │
     │ Tap YES/NO        │                    │                   │
     │──────────────────▶│                    │                   │
     │                   │                    │                   │
     │                   │ POST /api/moments/choice               │
     │                   │ { cardId, choice } │                   │
     │                   │───────────────────▶│                   │
     │                   │                    │                   │
     │                   │                    │ Check budget >= 1 │
     │                   │                    │──────────────────▶│
     │                   │                    │                   │
     │                   │                    │ Deduct budget     │
     │                   │                    │──────────────────▶│
     │                   │                    │                   │
     │                   │                    │ Save choice       │
     │                   │                    │──────────────────▶│
     │                   │                    │                   │
     │                   │                    │ Check for match   │
     │                   │                    │ (did other user   │
     │                   │                    │  also choose?)    │
     │                   │                    │──────────────────▶│
     │                   │                    │                   │
     │                   │                    │ If match: create  │
     │                   │                    │ balloon           │
     │                   │                    │──────────────────▶│
     │                   │                    │                   │
     │                   │◀───────────────────│                   │
     │                   │ { success, budget, │                   │
     │                   │   matched? }       │                   │
     │                   │                    │                   │
     │◀──────────────────│                    │                   │
     │ Animate card out  │                    │                   │
     │ Update budget     │                    │                   │
     │ (Show match if    │                    │                   │
     │  occurred)        │                    │                   │
```

### Data Models

```typescript
// Frontend models
interface MomentCard {
  id: string;
  profile: {
    firstName: string;
    photos: string[];
    bio: string;
    age: number;
  };
  rating?: {
    show: boolean;
    value?: number;
    count?: number;
  };
  compatibility: number;
}

interface MomentTheme {
  id: string;
  question: string;
  optionYes: ThemeOption;
  optionNo: ThemeOption;
}

interface MomentDeckResponse {
  theme: MomentTheme;
  cards: MomentCard[];
  budget: {
    remaining: number;
    total: number;
  };
}
```

---

## 3. Balloons Flow

### Loading Balloons

```
User opens Balloons tab
         │
         ▼
GET /api/balloons
         │
         ▼
Backend queries:
  - Active balloons where user is participant
  - Include other user's profile
  - Include match type (PURE/EDGE)
  - Include timing (findLoveAt countdown)
         │
         ▼
Return balloon list to frontend
         │
         ▼
Frontend displays with countdown timers
```

### Popping a Balloon

```
┌──────────┐      ┌─────────────┐      ┌─────────────┐      ┌──────────┐
│  User    │      │  Frontend   │      │   Backend   │      │    DB    │
└────┬─────┘      └──────┬──────┘      └──────┬──────┘      └────┬─────┘
     │                   │                    │                   │
     │ Tap "Pop" on      │                    │                   │
     │ balloon           │                    │                   │
     │──────────────────▶│                    │                   │
     │                   │                    │                   │
     │                   │ POST /api/balloons/{id}/pop            │
     │                   │───────────────────▶│                   │
     │                   │                    │                   │
     │                   │                    │ Validate balloon  │
     │                   │                    │ is ACTIVE         │
     │                   │                    │──────────────────▶│
     │                   │                    │                   │
     │                   │                    │ Close balloon     │
     │                   │                    │ (reason: POP)     │
     │                   │                    │──────────────────▶│
     │                   │                    │                   │
     │                   │                    │ Create trial      │
     │                   │                    │ period            │
     │                   │                    │──────────────────▶│
     │                   │                    │                   │
     │                   │                    │ Create chat       │
     │                   │                    │ thread            │
     │                   │                    │──────────────────▶│
     │                   │                    │                   │
     │                   │◀───────────────────│                   │
     │                   │ { threadId }       │                   │
     │                   │                    │                   │
     │◀──────────────────│                    │                   │
     │ Navigate to chat  │                    │                   │
```

---

## 4. Chat Flow

### Loading Thread

```typescript
// chat.service.ts
@Injectable({ providedIn: 'root' })
export class ChatService {
  private readonly base = `${environment.apiUrl}/api/chat`;

  getThread(threadId: string): Observable<ThreadResponse> {
    return this.http.get<ThreadResponse>(`${this.base}/${threadId}`);
  }

  getMessages(threadId: string, before?: string): Observable<MessagesResponse> {
    const params = before ? { before } : {};
    return this.http.get<MessagesResponse>(
      `${this.base}/${threadId}/messages`,
      { params }
    );
  }
}
```

### Sending a Message

```
┌──────────┐      ┌─────────────┐      ┌─────────────┐      ┌──────────┐
│  User    │      │  Frontend   │      │   Backend   │      │    DB    │
└────┬─────┘      └──────┬──────┘      └──────┬──────┘      └────┬─────┘
     │                   │                    │                   │
     │ Type message      │                    │                   │
     │ Tap send          │                    │                   │
     │──────────────────▶│                    │                   │
     │                   │                    │                   │
     │                   │ Optimistic update  │                   │
     │                   │ (show message      │                   │
     │                   │  immediately)      │                   │
     │                   │                    │                   │
     │◀──────────────────│                    │                   │
     │ See message       │                    │                   │
     │ (pending state)   │                    │                   │
     │                   │                    │                   │
     │                   │ POST /api/chat/{threadId}/messages     │
     │                   │ { content }        │                   │
     │                   │───────────────────▶│                   │
     │                   │                    │                   │
     │                   │                    │ Validate thread   │
     │                   │                    │ permissions       │
     │                   │                    │──────────────────▶│
     │                   │                    │                   │
     │                   │                    │ Save message      │
     │                   │                    │──────────────────▶│
     │                   │                    │                   │
     │                   │                    │ Notify other user │
     │                   │                    │ (push/websocket)  │
     │                   │                    │                   │
     │                   │◀───────────────────│                   │
     │                   │ { message }        │                   │
     │                   │                    │                   │
     │◀──────────────────│                    │                   │
     │ Message confirmed │                    │                   │
     │ (sent state)      │                    │                   │
```

### Message Models

```typescript
interface Message {
  id: string;
  threadId: string;
  senderId: string;
  content: string;
  sentAt: string;
  status: 'sending' | 'sent' | 'delivered' | 'read';
}

interface ThreadResponse {
  id: string;
  participants: Participant[];
  status: 'trial' | 'active' | 'ended';
  trialEndsAt?: string;
  lastMessage?: Message;
}
```

---

## 5. Pulse Flow

### Loading Pulse State

```
User opens app / refreshes
         │
         ▼
GET /api/pulse/current
         │
         ▼
Backend checks:
  - Current cycle dates
  - User's answers (if any)
  - Time remaining in cycle
         │
         ▼
Return pulse state
         │
         ▼
Frontend shows:
  - Answered: Summary + countdown to edit window
  - Unanswered: Auto-open pulse sheet
```

### Submitting Pulse

```typescript
// pulse.service.ts
submit(answers: PulseAnswers): Observable<void> {
  return this.http.post<void>(`${this.base}/submit`, answers);
}

// home.ts component
async submitPulse(answers: PulseAnswers) {
  if (!this.pulse) return;
  if (!this.canEditPulse()) {
    this.closePulse();
    return;
  }

  this.savingPulse = true;
  this.pulseError = '';

  try {
    await firstValueFrom(this.pulseApi.submit(answers));
    await this.refreshPulse();
    this.closePulse();
  } catch {
    this.pulseError = 'Could not save Pulse. Try again.';
  } finally {
    this.savingPulse = false;
  }
}
```

---

## 6. Profile Flow

### Profile Data Model

```typescript
interface UserProfile {
  id: string;
  firstName: string;
  lastName?: string;  // Only visible to connections
  photos: Photo[];
  bio: string;
  birthDate: string;
  location: {
    city: string;
    state?: string;
    country: string;
  };

  // Onboarding answers
  lookingFor: string;
  interests: string[];
  lifestyle: Record<string, string>;

  // Computed
  age: number;
  lastActive?: string;  // Only visible based on relationship
}

interface Photo {
  id: string;
  url: string;
  position: number;
  isPrimary: boolean;
}
```

### Profile Update Flow

```
User edits profile
         │
         ▼
Frontend validates locally
         │
         ▼
PUT /api/profile
         │
         ▼
Backend validates:
  - Required fields present
  - Photo limits respected
  - Content moderation (if enabled)
         │
         ▼
Save to database
         │
         ▼
Return updated profile
         │
         ▼
Frontend updates local state
```

---

## 7. Error Handling Flow

### Standard Error Response

```typescript
// Backend error response format
interface ApiError {
  error: string;       // Machine-readable code
  message: string;     // Human-readable message
  details?: any;       // Additional context
}

// Example responses
{ error: 'BUDGET_EXCEEDED', message: 'Not enough budget remaining' }
{ error: 'BALLOON_EXPIRED', message: 'This balloon has expired' }
{ error: 'UNAUTHORIZED', message: 'Please log in to continue' }
```

### Frontend Error Handling Pattern

```typescript
// Component pattern
async performAction() {
  this.loading = true;
  this.error = '';

  try {
    const result = await firstValueFrom(this.service.action());
    // Success handling
  } catch (err: any) {
    // Map API errors to user-friendly messages
    this.error = this.mapError(err);
  } finally {
    this.loading = false;
  }
}

private mapError(err: any): string {
  const code = err?.error?.error;

  switch (code) {
    case 'BUDGET_EXCEEDED':
      return 'You\'ve used all your choices for today. Come back tomorrow!';
    case 'BALLOON_EXPIRED':
      return 'This balloon has already expired.';
    default:
      return 'Something went wrong. Please try again.';
  }
}
```

### Template Error Display

```html
<!-- Loading state -->
<div *ngIf="loading" class="state">
  Loading...
</div>

<!-- Error state -->
<div *ngIf="error" class="state err">
  {{ error }}
  <button (click)="retry()">Try Again</button>
</div>

<!-- Success state -->
<div *ngIf="!loading && !error">
  <!-- Content -->
</div>
```

---

## 8. State Synchronization

### Frontend State Pattern

```typescript
@Injectable({ providedIn: 'root' })
export class UserStateService {
  private readonly _user$ = new BehaviorSubject<User | null>(null);
  private readonly _budget$ = new BehaviorSubject<Budget | null>(null);

  readonly user$ = this._user$.asObservable();
  readonly budget$ = this._budget$.asObservable();

  updateUser(user: User) {
    this._user$.next(user);
  }

  updateBudget(budget: Budget) {
    this._budget$.next(budget);
  }

  // Computed observables
  readonly isLoggedIn$ = this.user$.pipe(map(u => !!u));
  readonly hasBudget$ = this.budget$.pipe(map(b => (b?.remaining ?? 0) > 0));
}
```

### Cross-Component Communication

```typescript
// Component A updates state
this.userState.updateBudget(newBudget);

// Component B reacts to changes
this.userState.budget$.pipe(
  takeUntil(this.destroy$)
).subscribe(budget => {
  this.remainingBudget = budget?.remaining ?? 0;
});
```

---

## Data Flow Checklist

When implementing new features, verify:

- [ ] API endpoint follows RESTful conventions
- [ ] Request includes proper authentication
- [ ] Response includes all required fields
- [ ] Error responses use standard format
- [ ] Frontend handles loading/error/success states
- [ ] State updates propagate to all subscribers
- [ ] Optimistic updates are used where appropriate
- [ ] Data validation happens on both frontend and backend
