# Chats Feature

**Status:** Production  
**Last Updated:** 2026-08-17

## What It Is

Chats is Woven's messaging system where matches converse after choosing each other. The feature implements a three-stage progression model: Balloon → Trial → Find Love.

Unlike traditional dating apps where messaging is immediate and unrestricted, Woven's chat system is structured around intentionality gates that encourage meaningful engagement.

---

## Three Stages

### 1. Balloon Stage (Initial State)

- **State:** `BalloonState.ACTIVE`, no `FindLoveAt` timestamp
- **What users see:** "Balloon" badge in chat header
- **What users can do:**
  - Send/receive messages
  - Send/listen to voice notes
  - React with ❤️ to messages
  - View each other's opening ChatNotes
  - Pop balloon → starts Trial
  - Unmatch gracefully

**Trigger:** Match is created (`POST /moments/respond` with same or reciprocal choice)

**Technical location:** `Match.BalloonState == ACTIVE && Match.FindLoveAt == null && !Match.IsTrial`

---

### 2. Trial Period (Testing Phase)

- **State:** `Match.IsTrial == true`, 3-minute timer
- **What users see:** "Trial Mode · MM:SS left" in chat header
- **What users can do:**
  - Same messaging as Balloon stage
  - Must make decision when timer ends: CONTINUE / END / BLOCK

**Trigger:** Either user pops the balloon (`POST /matches/{matchId}/pop`)

**Timer starts:** When **both users open the chat thread** for the first time during trial  
**Timer duration:** 3 minutes (180 seconds) from the moment both users have opened  
**Decision point:** When `Match.TrialEndsAt <= now`

**Technical location:** `Match.IsTrial == true && Match.TrialEndsAt != null`

---

### 3. Find Love Stage (Full Unlock)

- **State:** `Match.FindLoveAt <= now`
- **What users see:** "Find Love" badge, date ideas, venue recommendations
- **What users can do:**
  - All Trial features +
  - View AI-generated date ideas (revealed after 10-minute delay)
  - Express interest in date ideas
  - Get venue recommendations (when both interested)
  - Share availability signals

**Trigger (Path A):** Both users messaged → automatic unlock after 5-minute reflection window  
**Trigger (Path B):** Trial ended with both users choosing CONTINUE → immediate unlock

**Technical location:** `Match.FindLoveAt != null && Match.FindLoveAt <= now`

---

## Message Types

| Type | Value | What It Is | MetaJson Fields |
|---|---|---|---|
| **Chat** | `""` or `"CHAT"` | User text message | n/a |
| **Voice** | `"VOICE"` | Voice note | `{ audioUrl, durationSecs }` |
| **System** | `"SYSTEM"` | Platform message (nudges, tips) | varies |
| **Game** | `"GAME"` | In-chat game (Know Me, Red/Green Flag) | `{ sessionId, gameType }` |

---

## Key Mechanics

### Reflection Window (5 minutes)

When both users have messaged at least once, `Match.BothMessagedAt` is set and `Match.FindLoveAt = now + 5 minutes`.

**Code reference:**  
- `ChatEndpoints.cs:492-506` — sets `BothMessagedAt` + `FindLoveAt`
- Frontend waits for `findLoveAt` timestamp, shows countdown timer

---

### Date Idea Reveal (10 minutes after Find Love unlock)

After Find Love unlocks, date ideas remain hidden for 10 more minutes. Frontend tracks this in localStorage (`woven:dateIdeaUnlockAt:{threadId}`).

**Code reference:**  
- `chat-thread.component.ts:224-258` — `shouldRevealDateIdea()` logic
- Backend always sends `dateIdeas` array when `showFindLove == true`

---

### Ghost Refund (Spark Recovery)

If a match closes with **zero messages exchanged**, both users receive a 0.5 spark refund. This prevents ghosting penalties when neither user engaged.

**Close paths that check for ghost refund:**
- `POST /chats/{threadId}/close-gracefully` (line 586-594)
- Trial auto-close on timeout (line 261-265)
- Trial decision both END (line 742-750)

**Code reference:** `ChatEndpoints.cs:586-594`, `SparkWalletService.GhostRefundAsync(userId)`

---

## ECHO Integration

Every chat action generates behavioral signals for the ECHO matching algorithm:

| Action | Signal Type | EventValue | Notes |
|---|---|---|---|
| First message sent | `TimeToFirstMessageMs` | latency (ms) | Speed proxy for interest |
| Message sent | `MessageSent` | 1.0 | Count of messages |
| Reply latency | `MessageResponseLatencyMs` | latency (ms) | Response time tracking |
| Voice note sent | `TimeToFirstMessageMs` | latency (ms) | First voice = high engagement |
| Voice note listened (complete) | `VoiceNoteListenComplete` | 1.0 | Listener played to end |
| Mutual voice exchange | `MutualVoiceExchange` | 1.0 | Both sent voice notes |
| ❤️ on message | `MessageLove` | 1.0 | Explicit positive signal |
| ❤️ on ChatNote | `ChatNoteLove` | 1.0 | Resonance with opening note |
| Trial CONTINUE | `TrialAccepted` | 1.0 | Positive outcome |
| Trial END | `TrialRejected` | 0.0 | Negative outcome |
| Trial END reason | `TrialEndedNoSpark` / `WrongTiming` / `NotMyType` | 1.0 | Preference learning |
| Date idea selected | `DateIdeaAccepted` | 1.0 | With chosen idea metadata |

**Storage:** All signals → `match_signal_logs` table  
**Processing:** `ConnectionScoreBatchWorker` (nightly 03:50 UTC) aggregates into `ConnectionScores`  
**Weighting:** `WeightLearningBatchWorker` (Sunday 04:00 UTC) tunes signal weights based on outcomes

**Code reference:** `ChatEndpoints.cs` — every endpoint calls `IMatchSignalService.RecordAsync(...)`

---

## Frontend Components

| File | What It Does |
|---|---|
| `chats-list.component.ts` | Chat list view (all active balloons) |
| `chat-thread.component.ts` | Single chat thread (messages, voice, games) |
| `trial-decision.component.ts` | Trial decision modal (CONTINUE/END/BLOCK) |
| `game-message-card.component.ts` | Game invite card in message stream |
| `inline-game-player.component.ts` | Full-screen game player overlay |

---

## Backend Endpoints

| Endpoint | Method | What It Does |
|---|---|---|
| `/chats` | GET | List all active chat threads |
| `/chats/start` | POST | Create/get thread for a match |
| `/chats/{threadId}` | GET | Load thread (messages, trial state, date ideas) |
| `/chats/{threadId}/messages` | POST | Send text message |
| `/chats/{threadId}/voice-message` | POST | Send voice note (after blob upload) |
| `/chats/{threadId}/messages/{messageId}/voice-listened` | POST | Track voice playback completion |
| `/chats/{threadId}/messages/{messageId}/love` | POST | Love-react to message |
| `/chats/{threadId}/trial-decision` | POST | Submit trial decision (CONTINUE/END/BLOCK) |
| `/chats/{threadId}/close-gracefully` | POST | Mutual unmatch (no ghosting penalty if no messages) |
| `/chats/{threadId}/date-interest` | POST | Express interest in date idea |
| `/chats/{threadId}/venue-suggestions` | GET | Get AI venue recommendations (requires mutual interest) |

**Full API reference:** See `api.md`

---

## Related Documentation

- [Balloon State Machine](./balloon-state.md)
- [Trial Period Mechanics](./trial-period.md)
- [Trial Decision Flow](./trial-decision.md)
- [Find Love Stage](./find-love.md)
- [Voice Notes](./voice-notes.md)
- [Love Reactions](./reactions.md)
- [Close Paths](./close-paths.md)
- [Frontend Implementation](./frontend.md)
- [Backend Implementation](./backend.md)

---

## Open Questions

- **Active/Online indicator:** Designed but not built (`User.LastActiveAt` exists, UI missing)
- **"Your Turn" chat list indicator:** Designed but not built (logic exists in `isMyTurn()`)
- **ChatNote preference embedding:** Worker stub exists (`PreferenceEmbeddingWorker`), not wired to production
