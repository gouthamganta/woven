# Assistant Frontend Architecture

**Last Updated:** 2026-08-17

---

## Overview

This document covers the frontend implementation of the Unified Woven Assistant:
- Component structure and lifecycle
- Drag-and-drop mechanics
- Badge rendering and glow states
- Modal orchestration
- Pending task service

---

## Component Structure

### File Location

`frontend/woven-frontend/src/app/components/woven-assistant/woven-assistant.component.ts`

**Component Type:** Standalone component (Angular 21)  
**Change Detection:** `OnPush` (manual change detection via `ChangeDetectorRef`)

### Template Architecture

The component uses an **inline template** (no separate `.html` file) with 4 main sections:

1. **Floating orb trigger** — Draggable button with badge
2. **Pending task modals** — Pulse sheet and coaching card
3. **Chat sheet** — Bottom drawer with message history
4. **Backdrop** — Dark overlay when open

### Component Definition

```typescript
@Component({
  selector: 'woven-assistant',
  standalone: true,
  imports: [CommonModule, FormsModule, PulseSheetComponent, CoachingCardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `...`,
  styles: [`...`]
})
export class WovenAssistantComponent implements OnInit, AfterViewChecked
```

**Location:** Lines 13-461

**Key imports:**
- `PulseSheetComponent` — Daily pulse modal
- `CoachingCardComponent` — Weekly coaching card
- `CommonModule` — Angular directives (`*ngIf`, `*ngFor`)
- `FormsModule` — Two-way binding (`[(ngModel)]`)

---

## State Management

### Core State Properties

```typescript
// UI state
isOpen  = false;
draft   = '';
thinking = false;
history: SupportMessage[] = [];

// Pending tasks
pendingTasks: PendingTask[] = [];
badgeCount = 0;
glowState: 'none' | 'red' | 'white' | 'gold' = 'none';

// Modal states
showingPulse = false;
showingCoaching = false;
pulseState: any = null;
coachingSummary: any = null;

// Drag state
isDragging = false;
orbX = 18;  // Initial right position (px from left edge)
orbY = 88;  // Initial bottom position (px from bottom edge)
private startX = 0;
private startY = 0;
private dragMoved = false;

// Lifecycle flags
private shouldScroll = false;
private isBrowser: boolean;
```

**Location:** Lines 467-492

### State Initialization

```typescript
constructor(
  private support: SupportService,
  private pendingTask: PendingTaskService,
  private cdr: ChangeDetectorRef,
  @Inject(PLATFORM_ID) platformId: object,
) {
  this.isBrowser = isPlatformBrowser(platformId);
}

async ngOnInit() {
  if (this.isBrowser) {
    await this.checkPendingTasks();
  }
}
```

**Location:** Lines 494-507

**SSR Safety:** `isBrowser` check prevents DOM operations during server-side rendering (Angular Universal compatibility).

---

## Drag-and-Drop Mechanics

### Drag Lifecycle

#### 1. Start Drag

```typescript
startDrag(event: MouseEvent | TouchEvent) {
  if (!this.isBrowser) return;
  event.preventDefault();

  this.isDragging = true;
  this.dragMoved = false;

  const clientX = 'touches' in event ? event.touches[0].clientX : event.clientX;
  const clientY = 'touches' in event ? event.touches[0].clientY : event.clientY;

  this.startX = clientX - this.orbX;
  this.startY = window.innerHeight - clientY - this.orbY;

  const onMove = (e: MouseEvent | TouchEvent) => this.onDrag(e);
  const onEnd = () => this.endDrag();

  document.addEventListener('mousemove', onMove);
  document.addEventListener('mouseup', onEnd);
  document.addEventListener('touchmove', onMove);
  document.addEventListener('touchend', onEnd);

  this.cdr.markForCheck();
}
```

**Location:** Lines 516-538

**Touch Support:** Handles both mouse and touch events. `'touches' in event` type guard distinguishes touch from mouse.

**Coordinate System:**
- `orbX` = pixels from **left edge** (stored)
- `orbY` = pixels from **bottom edge** (stored)
- Mouse `clientY` = pixels from **top edge** (event system)
- Conversion: `startY = window.innerHeight - clientY - this.orbY`

**Event Delegation:** Registers global `document` listeners (not just button) so drag continues even when cursor leaves button bounds.

#### 2. Drag Move

```typescript
onDrag(event: MouseEvent | TouchEvent) {
  if (!this.isDragging || !this.isBrowser) return;

  this.dragMoved = true;

  const clientX = 'touches' in event ? event.touches[0].clientX : event.clientX;
  const clientY = 'touches' in event ? event.touches[0].clientY : event.clientY;

  this.orbX = Math.max(0, Math.min(window.innerWidth - 56, clientX - this.startX));
  this.orbY = Math.max(0, Math.min(window.innerHeight - 56, window.innerHeight - clientY - this.startY));

  this.cdr.markForCheck();
}
```

**Location:** Lines 540-552

**Boundary Clamping:**
- `Math.max(0, ...)` — Prevents negative X/Y (off left/bottom edges)
- `Math.min(window.innerWidth - 56, ...)` — Prevents overflow right edge (56px = orb width)
- `Math.min(window.innerHeight - 56, ...)` — Prevents overflow top edge

**Change Detection:** Manual `markForCheck()` required because `OnPush` change detection doesn't auto-detect property updates from event handlers.

#### 3. End Drag

```typescript
endDrag() {
  if (!this.isDragging) return;

  this.isDragging = false;

  document.removeEventListener('mousemove', this.onDrag.bind(this));
  document.removeEventListener('mouseup', this.endDrag.bind(this));
  document.removeEventListener('touchmove', this.onDrag.bind(this));
  document.removeEventListener('touchend', this.endDrag.bind(this));

  this.cdr.markForCheck();
}
```

**Location:** Lines 554-565

**Cleanup:** Removes global event listeners to prevent memory leaks.

### Click vs Drag Discrimination

```typescript
handleClick(event: MouseEvent) {
  if (this.dragMoved) {
    event.stopPropagation();
    event.preventDefault();
    return;
  }
  this.toggle();
}
```

**Location:** Lines 567-574

**Problem:** Drag gesture ends with `mouseup` event, which would also trigger `click` event → unwanted modal open.

**Solution:** `dragMoved` flag tracks if drag occurred. If true, click is suppressed. If false (user tapped in place), toggle is called.

### CSS Drag Feedback

```css
.orb {
  cursor: grab;
  transition: transform 0.2s ease;
}

.orb.dragging {
  cursor: grabbing;
  transform: scale(1.1);
  transition: none;  /* Disable transition during drag for responsiveness */
}
```

**Location:** Lines 127-149 (styles)

---

## Badge & Glow System

### Badge Count Rendering

```html
<span class="orbBadge" *ngIf="badgeCount > 0">{{ badgeCount }}</span>
```

**Location:** Line 41 (template)

**Visibility:** Only shown when `badgeCount > 0` (via `*ngIf`).

**Styling:**

```css
.orbBadge {
  position: absolute;
  top: -4px;
  right: -4px;
  min-width: 20px;
  height: 20px;
  padding: 0 6px;
  background: #E74C3C;  /* Red */
  border-radius: 10px;
  font-size: 11px;
  font-weight: 700;
  color: white;
  border: 2px solid var(--bg-base);  /* Creates "cut out" effect */
}
```

**Location:** Lines 217-234

**Badge Number Source:**

```typescript
async checkPendingTasks() {
  try {
    const res = await firstValueFrom(this.pendingTask.getPendingTasks());
    this.pendingTasks = res.tasks;
    this.badgeCount = this.pendingTasks.length;  // ← Badge count
    this.updateGlowState();
    this.cdr.markForCheck();
  } catch {
    // Silent fail - just no badge/glow
  }
}
```

**Location:** Lines 653-663

### Glow State Calculation

```typescript
private updateGlowState() {
  if (this.pendingTasks.length === 0) {
    this.glowState = 'none';
    return;
  }

  const highest = this.pendingTasks[0];  // Tasks are pre-sorted by backend
  switch (highest.type) {
    case 'date_feedback':
      this.glowState = 'red';
      break;
    case 'weekly_coaching':
      this.glowState = 'gold';
      break;
    case 'daily_pulse':
      this.glowState = 'white';
      break;
    default:
      this.glowState = 'none';
  }
}
```

**Location:** Lines 665-685

**Logic:** Glow color determined by **highest priority task** (first in array, since backend pre-sorts by priority).

### Glow CSS Binding

```html
<button
  class="orb"
  [class.glow-red]="glowState === 'red'"
  [class.glow-white]="glowState === 'white'"
  [class.glow-gold]="glowState === 'gold'"
>
```

**Location:** Lines 20-27 (template)

**Glow Animations:**

```css
/* Red glow (Date Feedback) */
.orb.glow-red .orbCore {
  box-shadow: 0 0 24px #E74C3C, 0 4px 20px rgba(231,76,60,0.5);
  animation: pulse-red 2s ease-in-out infinite;
}
@keyframes pulse-red {
  0%, 100% { box-shadow: 0 0 24px #E74C3C, 0 4px 20px rgba(231,76,60,0.5); }
  50% { box-shadow: 0 0 32px #E74C3C, 0 6px 28px rgba(231,76,60,0.7); }
}

/* White glow (Daily Pulse) */
.orb.glow-white .orbCore {
  box-shadow: 0 0 24px #E8E4F3, 0 4px 20px rgba(232,228,243,0.4);
  animation: pulse-white 2s ease-in-out infinite;
}
@keyframes pulse-white {
  0%, 100% { box-shadow: 0 0 24px #E8E4F3, 0 4px 20px rgba(232,228,243,0.4); }
  50% { box-shadow: 0 0 32px #E8E4F3, 0 6px 28px rgba(232,228,243,0.6); }
}

/* Gold glow (Weekly Coaching) */
.orb.glow-gold .orbCore {
  box-shadow: 0 0 28px var(--gold-400), 0 4px 20px rgba(212,160,23,0.6);
  animation: pulse-gold 2s ease-in-out infinite;
}
@keyframes pulse-gold {
  0%, 100% { box-shadow: 0 0 28px var(--gold-400), 0 4px 20px rgba(212,160,23,0.6); }
  50% { box-shadow: 0 0 36px var(--gold-400), 0 6px 28px rgba(212,160,23,0.8); }
}
```

**Location:** Lines 191-215

**Animation Timing:** 2-second loop, infinite repeat, ease-in-out timing function (smooth pulse).

---

## Modal Orchestration

### Open Flow

```typescript
async open() {
  this.isOpen = true;

  // Check if there are pending tasks to show first
  if (this.pendingTasks.length > 0) {
    const highest = this.pendingTasks[0];
    await this.showPendingTask(highest);
  }

  this.cdr.markForCheck();
  if (this.isBrowser && !this.showingPulse && !this.showingCoaching) {
    setTimeout(() => this.inputEl?.nativeElement.focus(), 350);
    // Log assistant opened
    this.pendingTask.logInteraction('AssistantChatOpened', {
      hasPendingTasks: this.pendingTasks.length > 0
    }).subscribe();
  }
}
```

**Location:** Lines 584-601

**Decision Tree:**
1. If `pendingTasks.length > 0` → Show first pending task modal
2. Else → Open chat sheet + focus input field after 350ms (animation duration)

**Logging:** `AssistantChatOpened` event logged **only when chat opens** (not when pending task modal shows).

### Showing Pending Tasks

```typescript
async showPendingTask(task: PendingTask) {
  if (task.type === 'daily_pulse') {
    this.showingPulse = true;
    this.pulseState = { cycleId: task.data.cycleId };
  } else if (task.type === 'weekly_coaching') {
    this.showingCoaching = true;
    this.coachingSummary = {
      id: task.data.summaryId,
      summaryText: task.data.summaryText
    };
  }
}
```

**Location:** Lines 603-616

**Note:** Date Feedback case not implemented yet (future work).

### Modal Conditional Rendering

```html
<!-- Pulse modal -->
<app-pulse-sheet
  *ngIf="showingPulse"
  [state]="pulseState"
  (saved)="onPulseSaved($event)"
  (skipped)="onPulseSkipped()"
  (closed)="onPulseClosed()"
></app-pulse-sheet>

<!-- Coaching modal -->
<woven-coaching-card
  *ngIf="showingCoaching"
  [summary]="coachingSummary"
  (dismissed)="onCoachingDismissed()"
  (skipped)="onCoachingSkipped()"
></woven-coaching-card>

<!-- Chat sheet (shown only when no modals open) -->
<div class="sheet" [class.visible]="isOpen && !showingPulse && !showingCoaching">
```

**Location:** Lines 45-61 (template)

**Visibility Logic:**
- Pulse modal: `showingPulse === true`
- Coaching modal: `showingCoaching === true`
- Chat sheet: `isOpen === true` AND no modals showing

**Mutual Exclusion:** Only one UI element visible at a time.

### Event Handlers

#### Pulse Events

```typescript
async onPulseSaved(answers: any) {
  this.showingPulse = false;
  await this.pendingTask.logInteraction('DailyPulseCompleted', {
    answers
  }).toPromise();
  await this.advanceToNextPendingOrChat();
}

async onPulseSkipped() {
  this.showingPulse = false;
  await this.pendingTask.logInteraction('DailyPulseSkipped', {
    nextAction: this.pendingTasks.length > 1 ? 'opened_next_pending' : 'opened_assistant'
  }).toPromise();
  await this.advanceToNextPendingOrChat();
}

onPulseClosed() {
  this.showingPulse = false;
  this.close();  // Close entire assistant (user dismissed with X button)
}
```

**Location:** Lines 688-707

**Difference:**
- `onPulseSaved()` / `onPulseSkipped()` → Auto-advance to next task
- `onPulseClosed()` → Hard close entire assistant

#### Coaching Events

```typescript
async onCoachingDismissed() {
  this.showingCoaching = false;
  await this.pendingTask.logInteraction('WeeklyCoachingDismissed', {
    summaryId: this.coachingSummary?.id
  }).toPromise();
  await this.advanceToNextPendingOrChat();
}

async onCoachingSkipped() {
  this.showingCoaching = false;
  await this.pendingTask.logInteraction('WeeklyCoachingSkipped', {
    summaryId: this.coachingSummary?.id,
    nextAction: this.pendingTasks.length > 1 ? 'opened_next_pending' : 'opened_assistant'
  }).toPromise();
  await this.advanceToNextPendingOrChat();
}
```

**Location:** Lines 710-725

### Auto-Advance Logic

```typescript
async advanceToNextPendingOrChat() {
  // Remove the completed/skipped task
  this.pendingTasks.shift();  // Pop first element from array
  this.badgeCount = this.pendingTasks.length;
  this.updateGlowState();

  if (this.pendingTasks.length > 0) {
    // Show next pending task
    const next = this.pendingTasks[0];
    await this.showPendingTask(next);
  } else {
    // No more pending - show chat
    if (this.isBrowser) {
      setTimeout(() => this.inputEl?.nativeElement.focus(), 350);
    }
  }
  this.cdr.markForCheck();
}
```

**Location:** Lines 728-745

**Flow:**
1. Remove first task from `pendingTasks` array (`shift()`)
2. Update badge count
3. Recalculate glow state
4. **If more tasks** → Show next modal
5. **If no more tasks** → Open chat sheet + focus input

**No Refetch:** Tasks consumed from in-memory array (fetched once on init).

---

## Chat Interface

### Message History

```typescript
history: SupportMessage[] = [];
```

**Type Definition (from `SupportService`):**

```typescript
export interface SupportMessage {
  role: 'user' | 'assistant';
  content: string;
}
```

### Message Rendering

```html
<div class="messages" #messageList>
  <!-- Welcome message (shown when history is empty) -->
  <div class="msg assistant" *ngIf="history.length === 0">
    <div class="bubble">
      Hey — I'm Woven. Ask me anything about the app, how things work, or just tell me what's on your mind.
    </div>
  </div>

  <!-- Message history -->
  <ng-container *ngFor="let m of history">
    <div class="msg" [class.user]="m.role === 'user'" [class.assistant]="m.role === 'assistant'">
      <div class="bubble">{{ m.content }}</div>
    </div>
  </ng-container>

  <!-- Typing indicator -->
  <div class="msg assistant" *ngIf="thinking">
    <div class="bubble typing">
      <span></span><span></span><span></span>
    </div>
  </div>
</div>
```

**Location:** Lines 79-99 (template)

**Conditional Rendering:**
- Welcome message: `history.length === 0`
- Typing indicator: `thinking === true`

### Send Message

```typescript
async send() {
  const text = this.draft.trim();
  if (!text || this.thinking) return;

  this.history = [...this.history, { role: 'user', content: text }];
  this.draft = '';
  this.thinking = true;
  this.shouldScroll = true;
  this.cdr.markForCheck();

  try {
    const res = await firstValueFrom(this.support.chat(this.history));
    this.history = [...this.history, { role: 'assistant', content: res.reply }];
  } catch {
    this.history = [...this.history, {
      role: 'assistant',
      content: 'Something went wrong on my end — try again in a moment.',
    }];
  } finally {
    this.thinking = false;
    this.shouldScroll = true;
    this.cdr.markForCheck();
  }
}
```

**Location:** Lines 623-646

**Optimistic Update:**
1. User message added to history immediately
2. Input cleared
3. Thinking state set to `true` (shows typing indicator)
4. HTTP request sent
5. Assistant response appended to history
6. Thinking state set to `false`

**Immutability:** `this.history = [...this.history, newMessage]` creates new array reference (required for `OnPush` change detection).

### Auto-Scroll

```typescript
private shouldScroll = false;

ngAfterViewChecked() {
  if (this.shouldScroll) {
    this.scrollToBottom();
    this.shouldScroll = false;
  }
}

private scrollToBottom() {
  const el = this.messageList?.nativeElement;
  if (el) el.scrollTop = el.scrollHeight;
}
```

**Location:** Lines 491, 509-514, 648-651

**Pattern:**
1. Set `shouldScroll = true` when message added
2. Angular runs `ngAfterViewChecked()` after DOM update
3. Scroll to bottom
4. Reset flag to prevent infinite loop

**Why not `setTimeout()`?** `ngAfterViewChecked` is more reliable — guaranteed to run after DOM updates, even when change detection batches multiple updates.

---

## Pending Task Service

### File Location

`frontend/woven-frontend/src/app/services/pending-task.service.ts`

### Type Definitions

```typescript
export type PendingTaskType = 'date_feedback' | 'weekly_coaching' | 'daily_pulse';

export interface PendingTask {
  type: PendingTaskType;
  priority: number;
  data: {
    matchId?: string;
    partnerName?: string;
    summaryId?: number;
    summaryText?: string;
    cycleId?: string;
  };
}

export interface PendingTasksResponse {
  tasks: PendingTask[];
}
```

**Location:** Lines 6-22

**Data Shape:** Matches backend JSON response exactly (no transformation needed).

### Service Methods

```typescript
@Injectable({ providedIn: 'root' })
export class PendingTaskService {
  private api = environment.apiUrl;

  constructor(private http: HttpClient) {}

  getPendingTasks(): Observable<PendingTasksResponse> {
    return this.http.get<PendingTasksResponse>(`${this.api}/me/pending-tasks`);
  }

  logInteraction(eventType: string, context?: Record<string, any>): Observable<{logged: boolean}> {
    return this.http.post<{logged: boolean}>(`${this.api}/me/interaction-log`, {
      eventType,
      context
    });
  }
}
```

**Location:** Lines 24-40

**Endpoints:**
- `GET /me/pending-tasks` — Fetch pending tasks
- `POST /me/interaction-log` — Log user interaction

**Usage Pattern:**

```typescript
// Fetch tasks (component init)
const res = await firstValueFrom(this.pendingTask.getPendingTasks());
this.pendingTasks = res.tasks;

// Log interaction (after user action)
await this.pendingTask.logInteraction('DailyPulseCompleted', {
  answers: { energy: 'high' }
}).toPromise();
```

**Note:** Service uses `Observable` (RxJS). Component converts to `Promise` via `firstValueFrom()` or `.toPromise()`.

---

## Styling System

### CSS Variables Used

All colors reference CSS custom properties (design token system):

```css
var(--bg-base)       /* Background color (behind orb core icon) */
var(--bg-surface)    /* Sheet background */
var(--bg-elevated)   /* Input + button backgrounds */
var(--text-primary)  /* Primary text color */
var(--text-dim)      /* Dimmed text (placeholder, subtitle) */
var(--text-muted)    /* Muted text (hover states) */
var(--border-soft)   /* Soft border (handle, input) */
var(--border-subtle) /* Subtle border (sheet header) */
var(--gold-400)      /* Gold accent (light) */
var(--gold-500)      /* Gold accent (dark) */
var(--font-display)  /* Display font (header) */
var(--font-ui)       /* UI font (body text) */
```

**Theme Support:** Variables defined in `styles.scss`, automatically switch between light/dark themes.

### Orb Core Gradient

```css
background: linear-gradient(135deg, var(--gold-500), var(--gold-400));
```

**Angle:** 135° (top-left to bottom-right), darker gold → lighter gold.

### Breathing Animation

```css
.orbCore {
  animation: orbBreath 3s ease-in-out infinite;
}

@keyframes orbBreath {
  0%, 100% { box-shadow: 0 4px 20px rgba(212,160,23,0.35), 0 0 16px rgba(212,160,23,0.25); }
  50%       { box-shadow: 0 6px 28px rgba(212,160,23,0.45), 0 0 22px rgba(212,160,23,0.35); }
}
```

**Location:** Lines 163, 185-188

**Effect:** Subtle pulsing shadow (3-second cycle), paused when orb is open or dragging.

### Sheet Slide-Up Animation

```css
.sheet {
  transform: translateY(100%);  /* Hidden below viewport */
  transition: transform 0.3s cubic-bezier(0.32, 0.72, 0, 1);
}

.sheet.visible {
  transform: translateY(0);  /* Slide to position */
}
```

**Location:** Lines 252-274

**Timing:** 300ms, custom cubic-bezier easing (smooth deceleration).

---

## ViewChild References

```typescript
@ViewChild('messageList') messageList?: ElementRef<HTMLDivElement>;
@ViewChild('inputEl') inputEl?: ElementRef<HTMLInputElement>;
@ViewChild('orbBtn') orbBtn?: ElementRef<HTMLButtonElement>;
```

**Location:** Lines 463-465

**Usage:**
- `messageList` — Scroll container (for auto-scroll to bottom)
- `inputEl` — Input field (for auto-focus after modal close)
- `orbBtn` — Orb button (currently unused, reserved for future features)

**Optional (`?`) chaining:** Required because elements may not exist during SSR or before `ngAfterViewInit()`.

---

## Change Detection Strategy

**Mode:** `OnPush`

**Requirements:**
- Call `this.cdr.markForCheck()` after every state update
- Use immutable array updates: `this.history = [...this.history, newMsg]`
- Manual detection required for:
  - Drag position updates
  - HTTP response handlers
  - Modal state changes

**Locations of `markForCheck()` calls:**
- Line 538 (start drag)
- Line 552 (drag move)
- Line 565 (end drag)
- Line 594 (open assistant)
- Line 621 (close assistant)
- Line 632 (send message — before HTTP)
- Line 645 (send message — after HTTP)
- Line 660 (check pending tasks)
- Line 745 (auto-advance)

**Performance Benefit:** Prevents change detection from running on every browser event (scroll, mouse move, etc.). Only runs when explicitly triggered.

---

## SSR Compatibility

### Platform Check

```typescript
constructor(
  @Inject(PLATFORM_ID) platformId: object,
) {
  this.isBrowser = isPlatformBrowser(platformId);
}
```

**Location:** Lines 494-501

### Guarded Operations

All DOM-dependent operations wrapped in `if (this.isBrowser)`:

```typescript
async ngOnInit() {
  if (this.isBrowser) {
    await this.checkPendingTasks();
  }
}

startDrag(event: MouseEvent | TouchEvent) {
  if (!this.isBrowser) return;
  // ...drag logic
}

if (this.isBrowser && !this.showingPulse && !this.showingCoaching) {
  setTimeout(() => this.inputEl?.nativeElement.focus(), 350);
}
```

**Location:** Lines 503-507, 517, 594-596

**Why:** During server-side rendering:
- `window` / `document` don't exist
- `ViewChild` refs are undefined
- HTTP calls should be deferred to client-side hydration

---

## Timing & Delays

### Focus Delay

```typescript
setTimeout(() => this.inputEl?.nativeElement.focus(), 350);
```

**Delay:** 350ms (matches sheet slide-up animation duration: 300ms + 50ms buffer)

**Reason:** Focus call must wait until input element is visible and interactive (after CSS transition completes).

### No Polling

Component does **not** poll for new pending tasks. Tasks fetched once on init, consumed from in-memory array.

**Future Enhancement:** WebSocket listener to refresh tasks when new coaching summary delivered.

---

## Error Handling

### Silent Failures

```typescript
async checkPendingTasks() {
  try {
    const res = await firstValueFrom(this.pendingTask.getPendingTasks());
    // ...
  } catch {
    // Silent fail - just no badge/glow
  }
}
```

**Location:** Lines 653-663

**Philosophy:** Task fetch failure should not block UI. Orb shows with no badge, user can still open chat.

### HTTP Logging Errors

```typescript
await this.pendingTask.logInteraction('DailyPulseCompleted', {
  answers
}).toPromise();
```

**No try/catch.** If logging fails:
- Unhandled promise rejection logged to console
- User flow continues uninterrupted

**Trade-off:** Silent data loss vs blocked UX. UX prioritized.

---

## Future Improvements

### Position Persistence

**Current:** Orb position resets on page refresh (`orbX = 18`, `orbY = 88` in component init).

**Proposed:**

```typescript
ngOnInit() {
  const saved = localStorage.getItem('woven-assistant-pos');
  if (saved) {
    const { x, y } = JSON.parse(saved);
    this.orbX = x;
    this.orbY = y;
  }
}

endDrag() {
  // ...existing cleanup
  localStorage.setItem('woven-assistant-pos', JSON.stringify({
    x: this.orbX,
    y: this.orbY
  }));
}
```

**Status:** Deliberately not implemented. Design choice: stateless resets keep UI predictable.

### WebSocket Updates

**Current:** Pending tasks fetched once on init.

**Proposed:** Listen for `CoachingSummaryDelivered` event via SignalR → refresh tasks → pulse orb.

```typescript
this.signalR.on('CoachingSummaryDelivered', async () => {
  await this.checkPendingTasks();
  // Trigger attention animation (e.g., bounce orb)
});
```

### Chat History Persistence

**Current:** Chat history cleared on close (`history: []` never persisted).

**Proposed:** Store in `localStorage` or backend table, restore on reopen.

**Design Decision:** Ephemeral by design. Each session is fresh conversation (support chat, not messaging).

---

## Testing Checklist

### Unit Tests

- [ ] Badge count updates when `pendingTasks` changes
- [ ] Glow state matches first pending task type
- [ ] Drag updates `orbX` and `orbY` within viewport bounds
- [ ] Click after drag is suppressed (`dragMoved === true`)
- [ ] Click without drag opens modal (`dragMoved === false`)
- [ ] Auto-advance removes first task from array
- [ ] Chat history appends messages in order
- [ ] Typing indicator shows when `thinking === true`

### Integration Tests

- [ ] Fetch pending tasks on init (mocked API)
- [ ] Send message posts to support API
- [ ] Log interaction posts to backend
- [ ] Open orb with pending tasks → shows first modal
- [ ] Complete pulse → logs event → shows next task
- [ ] Skip coaching → logs event → opens chat
- [ ] Auto-scroll triggers after message sent

### Manual Testing

- [ ] Drag orb to all 4 corners → stays within bounds
- [ ] Drag on mobile (touch events) works
- [ ] Click orb without dragging opens assistant
- [ ] Badge shows correct count (1, 2, 3)
- [ ] Glow color matches highest priority task
- [ ] Red glow for date feedback
- [ ] Gold glow for coaching
- [ ] White glow for pulse
- [ ] Chat input auto-focuses after modal close
- [ ] Typing indicator animates smoothly
- [ ] Sheet slide-up animation smooth (300ms)
- [ ] Backdrop click closes assistant

---

## Related Docs

- [README.md](./README.md) — Feature overview
- [pending-tasks.md](./pending-tasks.md) — Task detection logic
- [badge-glow.md](./badge-glow.md) — Glow state details
- [auto-advance.md](./auto-advance.md) — Auto-advance flow
- [signal-logging.md](./signal-logging.md) — Logged events
- [backend.md](./backend.md) — Backend endpoints
