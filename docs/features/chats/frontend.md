# Frontend Implementation

**Last Updated:** 2026-08-17

## Overview

The Chats feature frontend is built with Angular 21, using standalone components with OnPush change detection.

**Key files:**
- `chat-thread.component.ts` — Single chat thread view
- `chats-list.component.ts` — List of active balloons
- `chat.service.ts` — HTTP client for chat endpoints
- `trial-decision.component.ts` — Trial decision modal
- `game-message-card.component.ts` — Game invite cards

---

## Architecture

### Change Detection Strategy

**All components use `ChangeDetectionStrategy.OnPush`.**

**Why?**  
Performance optimization for message lists (can have 50+ rows). Change detection only runs when:
1. Input property changes (reference change)
2. Event fires from template
3. Manual trigger via `cdr.markForCheck()`

**Required pattern:**
```typescript
async someAsyncMethod() {
  const data = await firstValueFrom(this.api.call());
  this.data = data;
  this.cdr.markForCheck();  // ← Required for OnPush
}
```

**Code reference:** Every component declares `ChangeDetectionStrategy.OnPush` in decorator.

---

### State Management

**No global state store (no NgRx, no signals).**

All state lives in component properties:
- `data: ChatThreadResponse | null` (thread state)
- `messages: ChatMessage[]` (message list)
- `loading: boolean` (loading indicator)
- `isRecording: boolean` (voice recording state)

**Updates:** Replace entire object reference to trigger change detection.

```typescript
this.data = {
  ...this.data,
  messages: [...(this.data.messages ?? []), optimisticMsg]
};
this.cdr.markForCheck();
```

---

## Components

### 1. ChatsListComponent

**File:** `chats-list.component.ts`

**Route:** `/chats`

**What it shows:**
- List of active chat threads (ACTIVE balloons only)
- Each card: other user photo, name, last message, trial/Find Love status
- Pop balloon button (if not trial, not Find Love)

**Data loading:**
```typescript
async load() {
  this.loading = true;
  this.error = '';
  try {
    const res = await firstValueFrom(this.chatApi.list());
    this.chats = res?.chats ?? [];
    this.meUserId = res?.meUserId ?? 0;
  } catch {
    this.error = 'Could not load balloons.';
  } finally {
    this.loading = false;
    this.cdr.markForCheck();
  }
}
```

**Realtime updates:**  
Subscribes to `realtime.newChatMessage$` → moves chat to top of list on new message.

**Code reference:** Line 44-55

---

### 2. ChatThreadComponent

**File:** `chat-thread.component.ts`

**Route:** `/chats/{threadId}`

**What it shows:**
- Chat header (other user name, status badge)
- Message list (text, voice, system, game messages)
- Chat input (text + voice recording)
- Trial decision modal (when trial ends)
- Game picker (Know Me, Red/Green Flag)
- Date idea cards (Find Love stage)

**Data loading:**
```typescript
async load(threadId: string, opts?: { silent?: boolean }) {
  const silent = !!opts?.silent;

  if (!silent) {
    this.loading = true;
    this.error = '';
    this.cdr.detectChanges();
  }

  try {
    const fresh = await firstValueFrom(this.chatApi.thread(threadId));

    this.data = {
      ...fresh,
      messages: [...(fresh.messages ?? [])],
    };

    // Check trial status
    this.checkTrialStatus();

    this.shouldAutoScroll = true;
    this.cdr.detectChanges();
  } catch (err: any) {
    console.error('❌ Chat load error:', err);
    this.error = 'Could not load chat.';
    this.cdr.detectChanges();
  } finally {
    this.loading = false;
    this.cdr.detectChanges();
  }
}
```

**Code reference:** Line 280-330

---

#### Timers

**Three timers run every second:**

1. **Global timer** (`ngOnInit`, line 122-139):
   - Updates `this.now = Date.now()`
   - Checks if Find Love just unlocked → triggers transition animation
   - Checks if date ideas just unlocked (10-min delay) → triggers unlock animation

2. **Recording timer** (`startRecording`, line 816-820):
   - Updates `recordingMs` every 100ms
   - Auto-stops at 180 seconds (3 minutes)

3. **Trial timer** (from backend `trialSecondsLeft`):
   - Server calculates, frontend displays
   - No client-side timer needed

---

#### Auto-Scroll

**When to scroll:** New message added (optimistic or real).

**How:**
```typescript
this.shouldAutoScroll = true;
this.cdr.detectChanges();

// In ngAfterViewChecked:
if (this.shouldAutoScroll) {
  this.shouldAutoScroll = false;
  setTimeout(() => this.scrollToBottom(), 0);
}
```

**Code reference:** Line 168-173, 876-881

**Why `setTimeout(0)`?**  
Waits for Angular to render new message DOM before scrolling.

---

#### Optimistic UI

**Send message flow:**
1. User types message, clicks send
2. Add temporary message to `messages` array (ID = `tmp_{timestamp}`)
3. Clear input, scroll to bottom
4. `POST /chats/{threadId}/messages`
5. On success: silent reload (replaces temp with real message)
6. On error: remove temp message, show toast

**Code reference:** Line 534-582

```typescript
const tempId = `tmp_${Date.now()}`;
const optimisticMsg = {
  messageId: tempId,
  senderUserId: myId,
  body: text,
  messageType: 'CHAT' as const,
  createdAt: new Date().toISOString(),
};

this.data = {
  ...this.data,
  messages: [...(this.data.messages ?? []), optimisticMsg as any],
};

try {
  await firstValueFrom(this.chatApi.send(threadId, text));
  await this.load(threadId, { silent: true });
} catch (err) {
  this.data = {
    ...this.data,
    messages: (this.data.messages ?? []).filter((m) => m.messageId !== tempId),
  };
  this.showToast('Could not send.');
}
```

---

### 3. TrialDecisionComponent

**File:** `trial-decision.component.ts`

**What it is:** Modal overlay shown when trial ends.

**Input:** None (controlled by parent)

**Output:** `decision: { decision: 'CONTINUE' | 'END' | 'BLOCK', endReason?: string }`

**UI structure:**
```
┌─────────────────────────────────────┐
│  Trial Period Ended                 │
│                                     │
│  [◈] Continue                       │
│  [ ] End Match                      │
│  [ ] Block                          │
│                                     │
│  (if END selected)                  │
│  Why?                               │
│  ( ) No spark                       │
│  ( ) Wrong timing                   │
│  ( ) Not my type                    │
│                                     │
│            [Submit]                 │
└─────────────────────────────────────┘
```

**Parent integration:**
```typescript
// In chat-thread.component.ts
showTrialDecision = false;

checkTrialStatus() {
  if (this.data.canMakeDecision && !this.showTrialDecision) {
    const myDecision = this.data.isUserA ? this.data.userADecision : this.data.userBDecision;
    if (!myDecision) {
      this.showTrialDecision = true;
    }
  }
}

async onTrialDecision(event: { decision: 'CONTINUE' | 'END' | 'BLOCK'; endReason?: string }) {
  const result = await firstValueFrom(
    this.chatApi.trialDecision(this.data.threadId, event.decision, event.endReason)
  );
  this.showTrialDecision = false;
  // Handle result...
}
```

---

### 4. GameMessageCardComponent

**File:** `game-message-card.component.ts`

**What it is:** Card shown in message stream when a game is sent.

**Inputs:**
- `sessionId: string`
- `gameType: string`
- `senderUserId: number`

**Outputs:**
- `acceptGame: CustomEvent<sessionId>`
- `rejectGame: CustomEvent<sessionId>`
- `openGame: CustomEvent<sessionId>`

**States:**
1. **Pending (received):** "Accept" / "Decline" buttons
2. **Accepted:** "Play" button
3. **Completed:** "View Results" button

**Parent integration:**
```typescript
async onAcceptGame(sessionId: string) {
  await firstValueFrom(this.games.acceptSession(sessionId));
  this.showToast('Game accepted!');
  setTimeout(() => this.refreshSilent(), 500);
}
```

**Code reference:** `chat-thread.component.ts:505-532`

---

### 5. InlineGamePlayerComponent

**File:** `inline-game-player.component.ts`

**What it is:** Full-screen overlay for playing Know Me / Red-Green Flag games.

**Inputs:**
- `sessionId: string`
- `gameType: string`

**Outputs:**
- `completed: CustomEvent`
- `close: CustomEvent`

**UI:** Renders game rounds, tracks answers, submits on completion.

**Parent integration:**
```typescript
showGamePlayer = false;
currentGameSessionId: string | null = null;

openGamePlayer(event: any) {
  this.currentGameSessionId = event.sessionId;
  this.currentGameType = event.gameType;
  this.showGamePlayer = true;
}

closeGamePlayer() {
  this.showGamePlayer = false;
  this.currentGameSessionId = null;
}

onGameCompleted() {
  this.showToast('Game completed!');
  this.refreshSilent();
}
```

**Code reference:** `chat-thread.component.ts:706-735`

---

## Services

### ChatService

**File:** `chat.service.ts`

**Methods:**

| Method | Endpoint | Returns |
|---|---|---|
| `list()` | `GET /chats` | `ChatsListResponse` |
| `start(matchId)` | `POST /chats/start` | `StartChatResponse` |
| `thread(threadId)` | `GET /chats/{threadId}` | `ChatThreadResponse` |
| `send(threadId, body)` | `POST /chats/{threadId}/messages` | `SendMessageResponse` |
| `trialDecision(threadId, decision, endReason?)` | `POST /chats/{threadId}/trial-decision` | `any` |
| `expressDateInterest(threadId, ideaIndex, ideaText)` | `POST /chats/{threadId}/date-interest` | `{ mutualInterest: boolean }` |
| `sendVoiceMessage(threadId, audioUrl, durationSecs)` | `POST /chats/{threadId}/voice-message` | `{ messageId, createdAt }` |
| `voiceListened(threadId, messageId)` | `POST /chats/{threadId}/messages/{messageId}/voice-listened` | `any` |

**Validation (client-side):**
```typescript
export const CHAT_MESSAGE_MAX_LENGTH = 1000;
export const CHAT_MESSAGE_MIN_LENGTH = 1;

send(threadId: string, body: string): Observable<SendMessageResponse> {
  const trimmedBody = body?.trim() ?? '';
  if (trimmedBody.length < CHAT_MESSAGE_MIN_LENGTH) {
    throw new Error('Message cannot be empty');
  }
  if (trimmedBody.length > CHAT_MESSAGE_MAX_LENGTH) {
    throw new Error(`Message cannot exceed ${CHAT_MESSAGE_MAX_LENGTH} characters`);
  }
  return this.http.post<SendMessageResponse>(...);
}
```

**Code reference:** Line 137-149

---

### RealtimeService

**File:** `realtime.service.ts`

**What it does:** SignalR WebSocket connection for live updates.

**Events:**

| Event | Payload | When |
|---|---|---|
| `newChatMessage$` | `{ threadId, messageId, body, senderUserId, createdAt }` | Someone sends message |
| `matchExpired$` | `{ matchId }` | Balloon expires |
| `momentReceived$` | `{ momentId, senderId }` | New match created |

**Subscription:**
```typescript
this.rtSub = this.realtime.newChatMessage$.subscribe((e: NewChatMessageEvent) => {
  if (e.threadId !== this.currentThreadId || !this.data) return;
  if (this.data.messages?.some((m) => m.messageId === e.messageId)) return;
  this.data = {
    ...this.data,
    messages: [...(this.data.messages ?? []), {
      messageId: e.messageId,
      senderUserId: e.senderUserId,
      body: e.body,
      messageType: 'CHAT' as const,
      createdAt: e.createdAt,
    } as any],
  };
  this.shouldAutoScroll = true;
  this.cdr.markForCheck();
});
```

**Code reference:** `chat-thread.component.ts:142-157`

---

## Voice Recording Flow

### MediaRecorder API

**Browser support:** Chrome, Edge, Safari 14.1+

**Start recording:**
```typescript
const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
this.mediaRecorder = new MediaRecorder(stream);
this.mediaRecorder.ondataavailable = (e) => {
  if (e.data.size > 0) this.audioChunks.push(e.data);
};
this.mediaRecorder.start(100);  // Record in 100ms chunks
```

**Stop recording:**
```typescript
this.mediaRecorder.stop();
this.mediaRecorder.stream?.getTracks().forEach((t) => t.stop());

this.mediaRecorder.onstop = async () => {
  const blob = new Blob(this.audioChunks, { type: 'audio/webm' });
  await this.uploadAndSendVoice(blob, durationSecs);
};
```

**Upload:**
```typescript
const { fileUrl } = await this.mediaService.uploadVoiceNote(blob, durationSecs);
await firstValueFrom(this.chatApi.sendVoiceMessage(threadId, fileUrl, durationSecs));
```

**Code reference:** `chat-thread.component.ts:803-857`

---

## Date Idea Reveal Logic

**Two-stage unlock:**
1. **Find Love unlocks:** `findLoveAt <= now`
2. **Ideas revealed:** 10 minutes after stage 1

**Why client-side delay?**  
Backend always sends ideas when Find Love unlocked. Frontend hides them for 10 minutes to encourage natural conversation first.

**localStorage tracking:**
```typescript
private storageKeyForIdea(threadId: string) {
  return `woven:dateIdeaUnlockAt:${threadId}`;
}

private markDateIdeaUnlockStart(threadId: string) {
  const key = this.storageKeyForIdea(threadId);
  const existing = window.localStorage.getItem(key);
  if (existing) return;
  window.localStorage.setItem(key, String(this.now));
}

shouldRevealDateIdea(): boolean {
  if (!this.isFindLoveReady()) return false;

  this.markDateIdeaUnlockStart(this.data.threadId);
  const startedAt = this.getIdeaUnlockStart(this.data.threadId);
  if (!startedAt) return false;

  return this.now - startedAt >= this.dateIdeaDelayMs;
}
```

**Code reference:** Line 224-258

---

## Error Handling

### HTTP Errors

**Pattern:**
```typescript
try {
  const result = await firstValueFrom(this.api.call());
  // Success path
} catch (err) {
  console.error('Error:', err);
  this.showToast('Could not complete action.');
}
```

**No global error interceptor** — each component handles errors locally.

---

### Toast Messages

**Helper method:**
```typescript
private showToast(msg: string) {
  this.toast = msg;
  this.cdr.detectChanges();
  window.setTimeout(() => {
    this.toast = '';
    this.cdr.detectChanges();
  }, 1400);
}
```

**Display:** Bottom-center toast, auto-dismiss after 1.4 seconds.

---

## Routing

**Chats routes:**
```
/chats                  → ChatsListComponent
/chats/:threadId        → ChatThreadComponent
```

**Navigation:**
```typescript
// From list to thread
open(c: ChatListItem) {
  this.router.navigateByUrl(`/chats/${c.threadId}`);
}

// Back to list
back() {
  this.router.navigateByUrl('/chats');
}
```

**Code reference:** `chats-list.component.ts:127-130`, `chat-thread.component.ts:360-362`

---

## Styling

**Theme:** Dark plum theme (migrated June 3-4 session)

**CSS variables used:**
- `--plum-bg-primary` — chat background
- `--plum-surface` — message bubbles
- `--plum-accent` — Find Love badge
- `--plum-text-primary` — message text
- `--plum-text-secondary` — timestamps

**No hover translateY lifts** (design rule).

**Message bubbles:**
- Mine: Right-aligned, `--plum-accent` background
- Theirs: Left-aligned, `--plum-surface` background

---

## Related Documentation

- [Backend Implementation](./backend.md)
- [Voice Notes](./voice-notes.md)
- [Trial Period Mechanics](./trial-period.md)
- [Find Love Stage](./find-love.md)
