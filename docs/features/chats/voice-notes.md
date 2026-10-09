# Voice Notes

**Last Updated:** 2026-08-17

## Overview

Voice notes let users send audio recordings instead of text messages. The feature uses Azure Blob Storage for audio hosting and tracks listening behavior for ECHO signals.

**Message type:** `"VOICE"`  
**Max duration:** 180 seconds (3 minutes)  
**Storage:** Azure Blob Storage (SAS token upload)

---

## Recording Flow (Frontend)

### 1. Start Recording

**User action:** Tap microphone button in chat input.

**Code reference:** `chat-thread.component.ts:803-825`

```typescript
async startRecording() {
  if (this.isRecording || typeof navigator === 'undefined') return;
  try {
    const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    this.audioChunks = [];
    this.mediaRecorder = new MediaRecorder(stream);
    this.mediaRecorder.ondataavailable = (e) => {
      if (e.data.size > 0) this.audioChunks.push(e.data);
    };
    this.mediaRecorder.start(100);
    this.isRecording = true;
    this.recordingMs = 0;
    this.recordingStartMs = Date.now();
    this.recordingTimer = window.setInterval(() => {
      this.recordingMs = Date.now() - this.recordingStartMs;
      if (this.recordingMs >= 180_000) this.stopRecording();
      this.cdr.detectChanges();
    }, 100);
    this.cdr.detectChanges();
  } catch {
    this.showToast('Microphone access denied');
  }
}
```

**Browser API:** `navigator.mediaDevices.getUserMedia({ audio: true })`

**Permissions:** Requires microphone access (browser prompt on first use).

**Timer:** Updates every 100ms, auto-stops at 3 minutes.

**UI:** Shows recording indicator + timer (`MM:SS` format).

---

### 2. Stop Recording

**User action:** Tap microphone button again (toggle off).

**Code reference:** `chat-thread.component.ts:827-846`

```typescript
stopRecording() {
  if (!this.isRecording || !this.mediaRecorder) return;
  this.mediaRecorder.stop();
  this.mediaRecorder.stream?.getTracks().forEach((t) => t.stop());
  if (this.recordingTimer) window.clearInterval(this.recordingTimer);
  this.isRecording = false;

  const durationSecs = Math.ceil(this.recordingMs / 1000);
  const mimeType = this.mediaRecorder.mimeType || 'audio/webm';
  this.mediaRecorder.onstop = async () => {
    if (durationSecs < 1) {
      this.showToast('Too short');
      this.cdr.detectChanges();
      return;
    }
    const blob = new Blob(this.audioChunks, { type: mimeType });
    await this.uploadAndSendVoice(blob, durationSecs);
  };
  this.cdr.detectChanges();
}
```

**Min duration:** 1 second (shorter recordings rejected).

**Audio format:** `audio/webm` (browser default, may vary by platform).

**Blob created:** Combines all recorded chunks into single `Blob`.

---

### 3. Upload to Blob Storage

**Service:** `MediaService.uploadVoiceNote(blob, durationSecs)`

**Code reference:** `chat-thread.component.ts:848-857`

```typescript
private async uploadAndSendVoice(blob: Blob, durationSecs: number) {
  if (!this.data) return;
  try {
    const { fileUrl } = await this.mediaService.uploadVoiceNote(blob, durationSecs);
    await firstValueFrom(this.chatApi.sendVoiceMessage(this.data.threadId, fileUrl, durationSecs));
    await this.load(this.data.threadId, { silent: true });
  } catch {
    this.showToast('Could not send voice note');
  }
}
```

**MediaService implementation (inferred):**
1. Request SAS token: `POST /media/upload-token`
2. Upload blob to Azure: `PUT <SAS URL>`
3. Confirm upload: `POST /media/confirm`
4. Return final URL

**Response:**
```json
{
  "fileUrl": "https://wovenprodblob.blob.core.windows.net/voice-notes/abc123.webm"
}
```

---

### 4. Send Voice Message

**Endpoint:** `POST /chats/{threadId}/voice-message`

**Request body:**
```json
{
  "audioUrl": "https://wovenprodblob.blob.core.windows.net/voice-notes/abc123.webm",
  "durationSecs": 42
}
```

**Code reference:** `ChatEndpoints.cs:772-858`

```csharp
var metaJson = JsonSerializer.Serialize(new
{
    audioUrl    = req.AudioUrl,
    durationSecs = req.DurationSecs
});

var msg = new ChatMessage
{
    ThreadId    = threadId,
    SenderUserId = me,
    Body        = "",
    MessageType = "VOICE",
    MetaJson    = metaJson,
    CreatedAt   = now
};

db.ChatMessages.Add(msg);
thread.UpdatedAt = now;
thread.LastMessageAt = now;
thread.MessageCount++;
await db.SaveChangesAsync(ct);
```

**Validation:**
- `audioUrl` cannot be empty
- `durationSecs` must be 1-180

**Response:**
```json
{
  "status": "SENT",
  "messageId": "abc-123-def",
  "createdAt": "2026-08-17T12:34:56Z"
}
```

---

## Playback (Recipient)

### Audio Player UI

**Template:** `chat-thread.component.html` (voice message card)

**Structure:**
```html
<audio 
  [src]="msg.meta.audioUrl" 
  controls 
  (ended)="onVoiceEnded(msg)">
</audio>
<span class="duration">{{ getVoiceDuration(msg) }}</span>
```

**Duration display:** `MM:SS` format

**Code reference:** `chat-thread.component.ts:796-801`

```typescript
getVoiceDuration(msg: any): string {
  const secs = msg?.meta?.durationSecs ?? 0;
  const mm = Math.floor(secs / 60);
  const ss = secs % 60;
  return `${mm}:${ss.toString().padStart(2, '0')}`;
}
```

---

### Listen Tracking

**Event:** User plays voice note to completion (audio `ended` event).

**Code reference:** `chat-thread.component.ts:859-865`

```typescript
onVoiceEnded(msg: any) {
  if (!this.data || !msg?.messageId) return;
  if (this.listenedMessageIds.has(msg.messageId)) return;
  if (this.isMine(msg.senderUserId)) return;
  this.listenedMessageIds.add(msg.messageId);
  this.chatApi.voiceListened(this.data.threadId, msg.messageId).subscribe();
}
```

**Deduplication:** Tracks listened messages in `Set<string>` (prevents duplicate signals).

**Self-listen guard:** Cannot track listening to own voice note.

**Endpoint:** `POST /chats/{threadId}/messages/{messageId}/voice-listened`

---

## ECHO Signals

### 1. TimeToFirstMessageMs (Voice)

**When:** User sends their first voice note in a thread.

**EventValue:** Milliseconds from match creation to first voice note.

**Code reference:** `ChatEndpoints.cs:834-847`

```csharp
var isFirstVoice = await db.ChatMessages.AsNoTracking()
    .CountAsync(m => m.ThreadId == threadId
                  && m.SenderUserId == me
                  && m.MessageType == "VOICE", ct) == 1;

if (isFirstVoice)
{
    var elapsedMs = (float)(now - match.CreatedAt).TotalMilliseconds;
    await signals.RecordAsync(me, otherUserId,
        MatchSignalEventTypes.TimeToFirstMessageMs, elapsedMs, ct: ct);
}
```

**Why track this?**  
Voice notes = higher engagement. Speed of first voice = strong interest signal.

---

### 2. VoiceNoteListenComplete

**When:** Recipient plays voice note to end.

**EventValue:** `1.0` (binary signal)

**Code reference:** `ChatEndpoints.cs:896-898`

```csharp
await signals.RecordAsync(me, senderUserId,
    MatchSignalEventTypes.VoiceNoteListenComplete, 1f, ct: ct);
```

**Current weight in ECHO formula:** `0.05` (from `ConnectionScoreBatchWorker.cs`)

**Why track this?**  
Listening to completion = attention + interest. Stronger than just opening a text message.

---

### 3. MutualVoiceExchange

**When:** Both users have sent at least one voice note in the thread.

**Check logic:** `ChatEndpoints.cs:900-912`

```csharp
var senderHasVoice = await db.ChatMessages.AsNoTracking()
    .AnyAsync(m => m.ThreadId == threadId && m.SenderUserId == senderUserId
                && m.MessageType == "VOICE", ct);
var listenerHasVoice = await db.ChatMessages.AsNoTracking()
    .AnyAsync(m => m.ThreadId == threadId && m.SenderUserId == me
                && m.MessageType == "VOICE", ct);

if (senderHasVoice && listenerHasVoice)
{
    await signals.RecordAsync(me, senderUserId,
        MatchSignalEventTypes.MutualVoiceExchange, 1f, ct: ct);
}
```

**EventValue:** `1.0` (binary signal)

**Current weight in ECHO formula:** `0.08` (from `ConnectionScoreBatchWorker.cs`)

**Why track this?**  
Mutual voice = both users comfortable with deeper engagement. High-quality connection signal.

---

## Rate Limiting

**Endpoint:** `POST /chats/{threadId}/voice-message`

**Limit:** 10 voice notes per minute per user.

**Implementation:** Redis-backed rate limiter.

**Code reference:** `ChatEndpoints.cs:784-788`

```csharp
var rateLimitKey = $"ratelimit:chat:voice:{me}";
var allowed = await cache.CheckRateLimitAsync(rateLimitKey, 10, TimeSpan.FromMinutes(1), ct);
if (!allowed)
    return Results.Json(new { error = "RATE_LIMIT_EXCEEDED", retryAfter = 60 }, statusCode: 429);
```

**Response on rate limit:**
```json
{
  "error": "RATE_LIMIT_EXCEEDED",
  "retryAfter": 60
}
```
Status: 429

---

## Database Schema

**Table:** `chat_messages`

```sql
CREATE TABLE chat_messages (
    id UUID PRIMARY KEY,
    thread_id UUID NOT NULL,
    sender_user_id INT NOT NULL,
    body TEXT NOT NULL,
    message_type VARCHAR(20) DEFAULT '',
    meta_json JSONB DEFAULT '{}',
    created_at TIMESTAMPTZ NOT NULL
);
```

**Voice message row:**
```sql
INSERT INTO chat_messages VALUES (
    'abc-123',
    'thread-456',
    789,
    '',                     -- empty body for voice
    'VOICE',
    '{"audioUrl": "https://...", "durationSecs": 42}',
    '2026-08-17 12:34:56+00'
);
```

---

## Frontend State

**Checking message type:**
```typescript
isVoiceMessage(msg: any): boolean {
  return (msg?.messageType || '').toUpperCase() === 'VOICE';
}
```

**Extracting metadata:**
```typescript
const audioUrl = msg?.meta?.audioUrl;
const durationSecs = msg?.meta?.durationSecs;
```

**Recording state:**
```typescript
isRecording: boolean = false;
recordingMs: number = 0;
recordingStartMs: number = 0;
audioChunks: Blob[] = [];
mediaRecorder?: MediaRecorder;
recordingTimer?: number;
```

---

## Error Handling

### Microphone Access Denied

**Browser prompt:** User clicks "Block" on mic permission.

**Frontend:** Shows toast "Microphone access denied"

**No server call made.**

---

### Recording Too Short

**Min duration:** 1 second

**Frontend check:** `if (durationSecs < 1) { showToast('Too short'); return; }`

**No upload attempted.**

---

### Upload Failure

**Causes:**
- Azure Blob Storage unavailable
- SAS token expired
- Network timeout

**Frontend:** Shows toast "Could not send voice note"

**User action:** Must re-record (audio blob discarded).

---

### Invalid Audio URL

**Server validation:** `ChatEndpoints.cs:790-791`

```csharp
if (string.IsNullOrWhiteSpace(req.AudioUrl))
    return Results.BadRequest(new { error = "AUDIO_URL_REQUIRED" });
```

**Response:**
```json
{
  "error": "AUDIO_URL_REQUIRED"
}
```
Status: 400

---

### Invalid Duration

**Server validation:** `ChatEndpoints.cs:793-794`

```csharp
if (req.DurationSecs <= 0 || req.DurationSecs > 180)
    return Results.BadRequest(new { error: "INVALID_DURATION" });
```

**Response:**
```json
{
  "error": "INVALID_DURATION"
}
```
Status: 400

---

## Related Documentation

- [Backend Implementation](./backend.md)
- [Frontend Implementation](./frontend.md)
- [Reactions](./reactions.md) (can ❤️ voice notes)
