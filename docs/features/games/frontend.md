# Games — Frontend Implementation

**Status:** SHIPPED  
**Last Updated:** 2026-08-17

---

## Architecture

Games use a **modal overlay** pattern:
- Game UI opens as a full-screen modal on top of chat
- Chat remains visible in background (blurred)
- Game progress persists if user exits modal (can resume)

**Key components:**
- `inline-game-player.component.ts` — game modal UI
- `game-message-card.component.ts` — chat invite cards
- `games.service.ts` — HTTP client for game API

---

## inline-game-player.component.ts

**Path:** `frontend/woven-frontend/src/app/components/inline-game-player/`

**Purpose:** Full-screen modal overlay for playing a game session.

### Inputs/Outputs

```typescript
@Input() sessionId!: string;          // Game session GUID
@Input() gameType: string = 'GAME';   // "KNOW_ME" | "RED_GREEN_FLAG"
@Output() close = new EventEmitter<void>();      // User closed modal
@Output() completed = new EventEmitter<void>();  // Game finished
```

### Component State

```typescript
loading = true;                        // Fetching round data
error = '';                            // Error message if load fails
round: GameRoundResponse | null = null;  // Current round data
answers: Record<string, string> = {};  // User's selected answers { "q1": "optionId", ... }
waitingForOther = false;               // Other user hasn't answered yet
pollInterval: any;                     // Polling timer when waiting
```

### Lifecycle

**OnInit:**
```typescript
ngOnInit() {
    this.loadRound();  // Fetch current round from API
}
```

**OnDestroy:**
```typescript
ngOnDestroy() {
    if (this.pollInterval) {
        clearInterval(this.pollInterval);  // Clean up polling
    }
}
```

### loadRound()

**Fetches current round data:**
```typescript
loadRound() {
    this.loading = true;
    this.error = '';
    this.answers = {};

    this.games.getCurrentRound(this.sessionId).subscribe({
        next: (data: GameRoundResponse) => {
            this.round = data;
            this.loading = false;
            this.cdr.markForCheck();

            if (data.hasAnswered) {
                this.startPolling();  // Other user's turn now
            }
        },
        error: (err: any) => {
            this.error = 'Could not load round';
            this.loading = false;
            this.cdr.markForCheck();
        }
    });
}
```

**API response:**
```typescript
interface GameRoundResponse {
    roundNumber: number;          // 1 or 2
    totalRounds: number;          // Always 2
    questions: QuestionData[];    // 3 questions
    timeLimit: number;            // 90 seconds (not enforced yet)
    isGuesser: boolean;           // true = predict partner's answers
    hasAnswered: boolean;         // true = already submitted
    waitingForOther: boolean;     // true = waiting for partner
}
```

---

### Question Text Conversion

**Problem:** AI generates questions in third-person ("their", "they") but the **target** user should see second-person ("your", "you").

**Solution:**
```typescript
getQuestionText(qText: string): string {
    if (!this.round) return qText;

    if (!this.round.isGuesser) {
        return this.toSecondPerson(qText);  // Target sees "you"
    }

    return qText;  // Guesser sees "they"
}

private toSecondPerson(text: string): string {
    return (text || '')
        .replace(/\bTheir\b/g, 'Your')
        .replace(/\btheir\b/g, 'your')
        .replace(/\bThey\b/g, 'You')
        .replace(/\bthey\b/g, 'you')
        .replace(/\bThem\b/g, 'You')
        .replace(/\bthem\b/g, 'you')
        .replace(/\bAre they\b/g, 'Are you')
        .replace(/\bDo they\b/g, 'Do you')
        .replace(/\bWould they\b/g, 'Would you')
        .replace(/\bHow do they\b/g, 'How do you')
        .replace(/\bWhat do they\b/g, 'What do you');
}
```

**Example transformation:**
- AI generates: "What's **their** go-to trail snack?"
- Guesser sees: "What's **their** go-to trail snack?"
- Target sees: "What's **your** go-to trail snack?"

---

### Answer Selection

**User taps an option:**
```typescript
selectAnswer(questionId: string, opt: GameOption) {
    this.answers[questionId] = this.getOptionText(opt);
}
```

**Type-safe option handling:**
```typescript
type GameOption = string | { id: string; text: string; isCorrect?: boolean };

getOptionText(opt: GameOption): string {
    return typeof opt === 'string' ? opt : opt?.text ?? '';
}

isSelected(questionId: string, opt: GameOption): boolean {
    return this.answers[questionId] === this.getOptionText(opt);
}
```

**Submit enabled when all questions answered:**
```typescript
canSubmit(): boolean {
    if (!this.round) return false;
    return this.round.questions.every((q: any) => this.answers[q.id]);
}
```

---

### Answer Submission

**Different endpoints for guesser vs target:**
```typescript
submitAnswers() {
    if (!this.canSubmit() || !this.round) return;

    this.loading = true;

    const submitCall = this.round.isGuesser
        ? this.games.submitGuesses(this.sessionId, this.answers)
        : this.games.submitTargetAnswers(this.sessionId, this.answers);

    submitCall.subscribe({
        next: (result: any) => {
            this.loading = false;
            this.cdr.markForCheck();

            if (result.status === 'WAITING_FOR_TARGET') {
                this.startPolling();  // Guesser submitted, wait for target
            } else if (result.status === 'NEXT_ROUND') {
                setTimeout(() => this.loadRound(), 1000);  // Load Round 2
            } else if (result.status === 'GAME_COMPLETE') {
                setTimeout(() => {
                    this.completed.emit();
                    this.close.emit();
                }, 1000);
            }
        },
        error: () => {
            this.error = 'Could not submit answers';
            this.loading = false;
            this.cdr.markForCheck();
        }
    });
}
```

**Response statuses:**
- `WAITING_FOR_TARGET` — Guesser submitted, target hasn't answered yet
- `NEXT_ROUND` — Both submitted, move to Round 2
- `GAME_COMPLETE` — Round 2 finished, show results

---

### Polling for Other User

**When guesser submits first:**
```typescript
startPolling() {
    this.waitingForOther = true;

    this.pollInterval = setInterval(() => {
        this.games.getCurrentRound(this.sessionId).subscribe({
            next: (data: GameRoundResponse) => {
                // Round number changed = target submitted
                if (data.roundNumber !== this.round?.roundNumber) {
                    clearInterval(this.pollInterval);
                    this.waitingForOther = false;
                    this.loadRound();  // Load next round
                }
            },
            error: (err: any) => {
                // 404 = game completed, exit modal
                if (err.status === 404 || err.status === 400) {
                    clearInterval(this.pollInterval);
                    this.waitingForOther = false;
                    this.completed.emit();
                    this.close.emit();
                }
            }
        });
    }, 2000);  // Poll every 2 seconds
}
```

**UI during polling:**
- Animated heart icon with pulse effect
- "Waiting for them" message
- "They're thinking about their answers..." subtitle

---

### UI States

**1. Loading:**
```html
<div class="content" *ngIf="loading && !waitingForOther">
    <div class="centerState">
        <div class="elegantSpinner"></div>
        <div class="stateTitle">Preparing your round</div>
    </div>
</div>
```

**2. Playing Round:**
```html
<div class="content scrollable" *ngIf="!loading && round && !waitingForOther">
    <!-- Role card -->
    <div class="roleCard" [class.guesser]="round.isGuesser">
        <div class="roleEmoji">{{ round.isGuesser ? '🎯' : '💭' }}</div>
        <div class="roleInfo">
            <div class="roleLabel">{{ round.isGuesser ? 'Your Turn to Guess' : 'Your Turn to Answer' }}</div>
            <div class="roleDesc">{{ round.isGuesser ? 'Predict their answers' : 'Be honest about yourself' }}</div>
        </div>
    </div>

    <!-- Questions -->
    <div class="questionsContainer">
        <div class="questionBlock" *ngFor="let q of round.questions; let i = index">
            <div class="qLabel">Question {{ i + 1 }}</div>
            <div class="qText">{{ getQuestionText(q.text) }}</div>
            <!-- Options... -->
        </div>
    </div>

    <!-- Submit -->
    <button class="submitButton" [disabled]="!canSubmit()" (click)="submitAnswers()">
        {{ round.isGuesser ? 'Submit Guesses' : 'Submit Answers' }}
    </button>
</div>
```

**3. Waiting for Other:**
```html
<div class="content" *ngIf="waitingForOther">
    <div class="centerState">
        <div class="waitingPulse">
            <div class="heartIcon">💝</div>
        </div>
        <div class="stateTitle">Waiting for them</div>
        <div class="stateDesc">They're thinking about their answers...</div>
    </div>
</div>
```

**4. Error:**
```html
<div class="content" *ngIf="error">
    <div class="centerState">
        <div class="errorIcon">⚠️</div>
        <div class="stateTitle">Something went wrong</div>
        <div class="stateDesc">{{ error }}</div>
        <button class="retryButton" (click)="loadRound()">Try Again</button>
    </div>
</div>
```

---

### Design System

**Colors:**
- Primary gradient: `linear-gradient(135deg, #ff6b9d 0%, #c94b8f 100%)`
- Background (guesser role): `#fff8f0` → `#ffeedd` (warm orange)
- Background (target role): `#fff5f7` → `#ffe8ee` (soft pink)
- Option hover border: `#ff6b9d`
- Selected option: full gradient background + white text

**Animations:**
- Modal slide-up on open: `translateY(40px) → translateY(0)`
- Fade-in overlay: `opacity 0 → 1`
- Waiting pulse: expanding circles (2s infinite)
- Submit button hover: `translateY(-2px)` + shadow boost

**Mobile responsive:**
- Max width: 580px
- Padding adjustments < 600px
- Font size reductions
- Smaller buttons

---

## game-message-card.component.ts

**Path:** `frontend/woven-frontend/src/app/components/game-message-card/`

**Purpose:** Chat bubble for game invite/status messages.

### Inputs/Outputs

```typescript
@Input({ required: true }) meUserId!: number;
@Input({ required: true }) message!: {
    senderUserId: number;
    body: string;
    createdAt: string;
    messageType?: string;
    meta?: any;
};

@Output() accept = new EventEmitter<string>();      // User accepted invite
@Output() reject = new EventEmitter<string>();      // User rejected invite
@Output() openResult = new EventEmitter<string>();  // User tapped "View Results"
@Output() play = new EventEmitter<{ sessionId: string; gameType: string }>();  // User tapped "Play"
```

### Message States

**Parsed from `message.meta`:**
```typescript
get sessionId(): string | null { return this.meta?.sessionId ?? null; }
get gameType(): string { return this.meta?.gameType ?? 'GAME'; }
get status(): string { return this.meta?.status ?? ''; }
get expiresAt(): Date | null { return this.meta?.expiresAt ? new Date(this.meta.expiresAt) : null; }
```

**Status checks:**
```typescript
get isPending(): boolean { return this.status.toUpperCase() === 'PENDING'; }
get isActive(): boolean { return this.status.toUpperCase() === 'ACTIVE'; }
get isCompleted(): boolean { return this.status.toUpperCase() === 'COMPLETED'; }
get isExpired(): boolean { return this.expiresAt ? Date.now() > this.expiresAt.getTime() : false; }
```

### Conditional UI

**1. Receiver (invite pending):**
```typescript
get shouldShowAcceptReject(): boolean {
    return this.isPending && !this.isExpired && !this.isInitiator && !!this.sessionId;
}
```
→ Shows Accept / Reject buttons

**2. Sender (waiting for acceptance):**
```typescript
get shouldShowWaiting(): boolean {
    return this.isPending && !this.isExpired && this.isInitiator;
}
```
→ Shows "Waiting for them to accept..."

**3. Both users (game active):**
```typescript
get shouldShowPlay(): boolean {
    return this.isActive && !!this.sessionId;
}
```
→ Shows "Play" button (opens inline-game-player modal)

**4. Both users (game completed):**
```typescript
get shouldShowResults(): boolean {
    return this.isCompleted && !!this.sessionId;
}
```
→ Shows "View Results" button

### Event Handlers

**Accept invite:**
```typescript
onAccept() {
    if (this.sessionId) {
        this.accept.emit(this.sessionId);  // Parent calls GamesService.acceptSession()
    }
}
```

**Reject invite:**
```typescript
onReject() {
    if (this.sessionId) {
        this.reject.emit(this.sessionId);  // Parent calls GamesService.rejectSession()
    }
}
```

**Play game:**
```typescript
onPlay() {
    if (!this.sessionId) return;
    this.play.emit({
        sessionId: this.sessionId,
        gameType: this.gameType,
    });  // Parent opens inline-game-player modal
}
```

**View results:**
```typescript
onOpenResult() {
    if (this.sessionId) {
        this.openResult.emit(this.sessionId);  // Parent fetches /games/sessions/{id}/result
    }
}
```

---

## games.service.ts

**Path:** `frontend/woven-frontend/src/app/services/games.service.ts`

**Purpose:** HTTP client for game API endpoints.

### Methods

**1. Check availability:**
```typescript
getAvailability(matchId: string): Observable<GameAvailabilityResponse> {
    return this.http.get<GameAvailabilityResponse>(`${baseUrl}games/matches/${matchId}/availability`);
}
```

**Response:**
```typescript
interface GameAvailabilityResponse {
    available: boolean;
    gamesRemaining: number;    // 0-2
    reason: string | null;     // "DAILY_LIMIT" | "PENDING_GAME" | null
    games: Array<{
        type: GameType;        // "KNOW_ME" | "RED_GREEN_FLAG"
        name: string;          // "Know Me"
        description: string;   // "Guess what they picked"
        duration: string;      // "2 min"
        icon: string;          // "🎯"
    }>;
}
```

**2. Create session (send invite):**
```typescript
createSession(matchId: string, gameType: GameType): Observable<GameSessionDto> {
    return this.http.post<GameSessionDto>(`${baseUrl}games/matches/${matchId}/sessions`, { gameType });
}
```

**3. Accept invite:**
```typescript
acceptSession(sessionId: string): Observable<{ status: string; message: string }> {
    return this.http.post(`${baseUrl}games/sessions/${sessionId}/accept`, {});
}
```

**4. Reject invite:**
```typescript
rejectSession(sessionId: string): Observable<{ status: string }> {
    return this.http.post(`${baseUrl}games/sessions/${sessionId}/reject`, {});
}
```

**5. Get current round:**
```typescript
getCurrentRound(sessionId: string): Observable<GameRoundResponse> {
    return this.http.get<GameRoundResponse>(`${baseUrl}games/sessions/${sessionId}/round`);
}
```

**6. Submit guesses (guesser):**
```typescript
submitGuesses(sessionId: string, answers: Record<string, string>): Observable<RoundResultResponse> {
    return this.http.post(`${baseUrl}games/sessions/${sessionId}/answers`, { answers });
}
```

**7. Submit actual answers (target):**
```typescript
submitTargetAnswers(sessionId: string, answers: Record<string, string>): Observable<RoundResultDto> {
    return this.http.post(`${baseUrl}games/sessions/${sessionId}/target-answers`, { answers });
}
```

**8. Get final result:**
```typescript
getResult(sessionId: string): Observable<FinalResultResponse> {
    return this.http.get<FinalResultResponse>(`${baseUrl}games/sessions/${sessionId}/result`);
}
```

**Response:**
```typescript
interface FinalResultResponse {
    sessionId: string;
    gameType: string;
    yourScore: number;
    theirScore: number;
    youWon: boolean;
    isTie: boolean;
    aiInsight: string;        // "You both got it — seems like you're paying attention."
    userAName?: string;
    userBName?: string;
}
```

---

## Change Detection

**All game components use `OnPush`:**
```typescript
@Component({
    changeDetection: ChangeDetectionStrategy.OnPush
})
```

**Critical:** Must call `cdr.markForCheck()` after async updates:
```typescript
this.games.getCurrentRound(sessionId).subscribe({
    next: (data) => {
        this.round = data;
        this.cdr.markForCheck();  // ← Required for OnPush
    }
});
```

---

## Future Enhancements

- [ ] **Time limit enforcement** (90-second countdown per round)
- [ ] **Animated score reveal** (counting up, confetti if perfect)
- [ ] **Sound effects** (tap, submit, win/lose)
- [ ] **Offline support** (cache round data, sync when back online)
- [ ] **Accessibility** (screen reader support, keyboard navigation)
- [ ] **Haptic feedback** (vibrate on selection, submit)
