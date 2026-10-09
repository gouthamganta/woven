# Love Reactions

**Last Updated:** 2026-08-17

## Overview

Users can react with ❤️ to messages and ChatNotes (opening notes). Reactions are tracked as ECHO signals for preference learning.

**Icon:** ❤️ (love emoji)  
**Limit:** One love per user per message/note (idempotent)  
**Signal weight:** `0.07` in ECHO formula (from `ConnectionScoreBatchWorker.cs`)

---

## Message Love Reactions

### What It Is

Users can love-react to any message in the thread (text or voice), except their own.

**Endpoint:** `POST /chats/{threadId}/messages/{messageId}/love`

**Entity:** `MessageLoveReaction` (table: `message_love_reactions`)

---

### Request Flow

**User action:** Tap ❤️ button on a message card.

**Frontend call:**
```typescript
// Not shown in current chat-thread.component.ts — 
// likely in message card component or missing UI
chatApi.loveMessage(threadId, messageId).subscribe();
```

**Endpoint:** `POST /chats/{threadId}/messages/{messageId}/love`

**Code reference:** `ChatEndpoints.cs:978-1027`

---

### Backend Logic

```csharp
var me = GetUserId(http.User);

var thread = await db.ChatThreads.AsNoTracking()
    .FirstOrDefaultAsync(t => t.Id == threadId, ct);
if (thread == null) return Results.NotFound(new { error = "THREAD_NOT_FOUND" });

var match = await db.Matches.AsNoTracking()
    .FirstOrDefaultAsync(m => m.Id == thread.MatchId && m.BalloonState == BalloonState.ACTIVE, ct);
if (match == null) return Results.BadRequest(new { error = "MATCH_NOT_ACTIVE" });

var isParticipant = match.UserAId == me || match.UserBId == me;
if (!isParticipant) return Results.Forbid();

var message = await db.ChatMessages.AsNoTracking()
    .FirstOrDefaultAsync(m => m.Id == messageId && m.ThreadId == threadId, ct);
if (message == null) return Results.NotFound(new { error = "MESSAGE_NOT_FOUND" });

// Cannot love your own message
if (message.SenderUserId == me)
    return Results.BadRequest(new { error = "CANNOT_LOVE_OWN_MESSAGE" });

// Idempotent
var alreadyLoved = await db.MessageLoveReactions
    .AnyAsync(r => r.FromUserId == me && r.MessageId == messageId, ct);
if (alreadyLoved)
    return Results.Ok(new { status = "ALREADY_LOVED" });

db.MessageLoveReactions.Add(new MessageLoveReaction
{
    MessageId = messageId,
    ThreadId = threadId,
    FromUserId = me,
    MessageAuthorUserId = message.SenderUserId,
    CreatedAt = DateTime.UtcNow
});
await db.SaveChangesAsync(ct);

try { await signals.RecordAsync(me, message.SenderUserId, MatchSignalEventTypes.MessageLove, 1f, ct: ct); }
catch { /* non-critical */ }

return Results.Ok(new { status = "LOVED" });
```

---

### Validation

**Cannot love:**
- Your own message → `CANNOT_LOVE_OWN_MESSAGE`
- Message in closed match → `MATCH_NOT_ACTIVE`
- Message from another thread → `MESSAGE_NOT_FOUND`

**Idempotency:**  
Second love to same message returns `ALREADY_LOVED` (200 OK, no duplicate DB row).

---

### ECHO Signal

**Event type:** `MatchSignalEventTypes.MessageLove`

**EventValue:** `1.0` (binary)

**Viewer:** User who loved (reactor)

**Candidate:** Message author

**Why track this?**  
Explicit positive feedback. User chose to appreciate this specific message → strong connection signal.

**Current weight:** `0.07` in `ConnectionScoreBatchWorker.cs` (down from 0.10 in June 3 session)

---

### Database Schema

**Table:** `message_love_reactions`

```sql
CREATE TABLE message_love_reactions (
    id UUID PRIMARY KEY,
    message_id UUID NOT NULL,
    thread_id UUID NOT NULL,
    from_user_id INT NOT NULL,
    message_author_user_id INT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL
);

CREATE INDEX idx_message_love_from_message 
    ON message_love_reactions(from_user_id, message_id);
```

**Unique constraint (implicit via check):**  
Backend checks `FROM from_user_id + message_id` before inserting.

---

## ChatNote Love Reactions

### What It Is

Users can love-react to the **other user's ChatNote** (opening note written when choosing each other).

**Endpoint:** `POST /chatnotes/{noteId}/love`

**Entity:** `ChatNoteLoveReaction` (table: `chat_note_love_reactions`)

**What are ChatNotes?**  
Short notes users write when making a Moments choice (Magical/Resonant). Example: "Your travel tile reminded me of my own solo trips!"

**Where shown:** At the top of chat thread, both notes displayed side-by-side.

---

### Request Flow

**User action:** Tap ❤️ on other user's ChatNote card.

**Frontend call:**
```typescript
// Not shown in current chat-thread.component.ts —
// likely in chat-note-card component or missing UI
chatApi.loveChatNote(noteId).subscribe();
```

**Endpoint:** `POST /chatnotes/{noteId}/love`

**Code reference:** `ChatEndpoints.cs:922-974`

---

### Backend Logic

```csharp
var me = GetUserId(http.User);

var note = await db.ChatNotes.AsNoTracking()
    .FirstOrDefaultAsync(n => n.Id == noteId, ct);

if (note == null)
    return Results.NotFound(new { error = "NOTE_NOT_FOUND" });

// Cannot love your own note
if (note.FromUserId == me)
    return Results.BadRequest(new { error = "CANNOT_LOVE_OWN_NOTE" });

// Must be in an active match together
if (note.MatchId == null)
    return Results.BadRequest(new { error = "NOTE_NOT_LINKED_TO_MATCH" });

var match = await db.Matches.AsNoTracking()
    .FirstOrDefaultAsync(m => m.Id == note.MatchId && m.BalloonState == BalloonState.ACTIVE, ct);

if (match == null)
    return Results.BadRequest(new { error = "MATCH_NOT_ACTIVE" });

var isParticipant = match.UserAId == me || match.UserBId == me;
if (!isParticipant) return Results.Forbid();

// Idempotent: already loved?
var alreadyLoved = await db.ChatNoteLoveReactions
    .AnyAsync(r => r.FromUserId == me && r.NoteId == noteId, ct);

if (alreadyLoved)
    return Results.Ok(new { status: "ALREADY_LOVED" });

db.ChatNoteLoveReactions.Add(new ChatNoteLoveReaction
{
    NoteId = noteId,
    FromUserId = me,
    NoteAuthorUserId = note.FromUserId,
    CreatedAt = DateTime.UtcNow
});
await db.SaveChangesAsync(ct);

try { await signals.RecordAsync(me, note.FromUserId, MatchSignalEventTypes.ChatNoteLove, 1f, ct: ct); }
catch { /* non-critical */ }

return Results.Ok(new { status = "LOVED" });
```

---

### Validation

**Cannot love:**
- Your own note → `CANNOT_LOVE_OWN_NOTE`
- Note not linked to a match → `NOTE_NOT_LINKED_TO_MATCH`
- Note in closed match → `MATCH_NOT_ACTIVE`
- Note from a match you're not in → 403 Forbidden

**Idempotency:**  
Second love to same note returns `ALREADY_LOVED` (200 OK, no duplicate).

---

### ECHO Signal

**Event type:** `MatchSignalEventTypes.ChatNoteLove`

**EventValue:** `1.0` (binary)

**Viewer:** User who loved (reactor)

**Candidate:** Note author

**Why track this?**  
ChatNote is the user's intentional opening move. Loving it = resonance with their self-expression + choice motivation.

**Current weight:** Same as `MessageLove` in ECHO formula (both treated as explicit positive signals).

---

### Database Schema

**Table:** `chat_note_love_reactions`

```sql
CREATE TABLE chat_note_love_reactions (
    id UUID PRIMARY KEY,
    note_id UUID NOT NULL,
    from_user_id INT NOT NULL,
    note_author_user_id INT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL
);

CREATE INDEX idx_chatnote_love_from_note 
    ON chat_note_love_reactions(from_user_id, note_id);
```

---

## Frontend UI (Inferred)

### Message Love Button

**Location:** Message card (right side or long-press menu)

**States:**
1. **Unloved:** Empty ❤️ outline (gray)
2. **Loved:** Filled ❤️ (red/pink)
3. **Disabled:** Own message (no button shown)

**Animation:** Heart fill + scale bounce on tap.

---

### ChatNote Love Button

**Location:** ChatNote card (top of chat thread)

**Same states as message love.**

**Card structure (example):**
```
┌─────────────────────────────────────┐
│  ◈ Their Opening Note               │
│  "Your travel tile reminded me of   │
│   my own solo trips!"               │
│                                     │
│                          [❤️]       │
│  ✓ You loved this                   │
└─────────────────────────────────────┘
```

---

## Analytics

**Love reactions are NOT tracked via `IAnalyticsService.TrackAsync`.**

All tracking happens via `IMatchSignalService.RecordAsync` → stored in `match_signal_logs` table → processed by ECHO batch workers.

---

## ECHO Learning

### What ECHO Does With Love Signals

1. **ConnectionScoreBatchWorker** (nightly 03:50 UTC):
   - Sums `MessageLove` + `ChatNoteLove` signals per user pair
   - Multiplies by weight (0.07)
   - Adds to `ConnectionScore.Score`

2. **WeightLearningBatchWorker** (Sunday 04:00 UTC):
   - Correlates love signal presence with positive outcomes:
     - `TrialAccepted` = 1.0
     - `TimeToFirstMessageMs` < 5 minutes
     - High message count
   - Adjusts weight if correlation changes

**Current formula (from `ConnectionScoreBatchWorker.cs`):**
```csharp
LoveReactions: 0.07 weight
(down from 0.10 in June 3 session)
```

**Why the reduction?**  
Love reactions became too common (users loved every message). Weight reduced to balance with other signals.

---

## Rate Limiting

**Love endpoints are NOT rate-limited.**

Idempotency + single-love-per-message constraint provides natural spam protection.

---

## Related Documentation

- [Backend Implementation](./backend.md)
- [Frontend Implementation](./frontend.md)
- [Voice Notes](./voice-notes.md) (can love voice messages)
