# Voice Notes — Recording, Upload & Playback

**Last Updated:** 2026-10-07  
**Container:** `voice-notes`  
**Message Type:** `VOICE`

---

## Overview

Voice notes are audio messages sent in chat threads. They bypass the cold-start problem of text chat by letting users hear each other's voice, tone, and energy before committing to a match.

**Key differentiator:** Voice-first chat creates higher-intent connections.

---

## Complete Flow

```
┌────────┐  1. Record audio              ┌──────────────┐
│Frontend│ ←─────────────────────────────│MediaRecorder │
└────┬───┘                                └──────────────┘
     │
     │  2. POST /media/upload-token      ┌─────────────┐
     ├──────────────────────────────────>│MediaService │
     │    { container: "voice-note",     └──────┬──────┘
     │      fileName: "voice-{ts}.webm",        │
     │      contentType: "audio/webm" }         │
     │                                           │
     │  3. { uploadUrl, sasToken, blobPath, expiresAt }
     │ ←─────────────────────────────────────────┘
     │
     │  4. PUT {uploadUrl}                 ┌────────────┐
     ├────────────────────────────────────>│Azure Blob  │
     │    Headers:                         │ Storage    │
     │      x-ms-blob-type: BlockBlob      └────────────┘
     │      Content-Type: audio/webm
     │    Body: [audio blob]
     │
     │  5. POST /media/confirm             ┌─────────────┐
     ├────────────────────────────────────>│MediaService │
     │    { blobPath, container }          └──────┬──────┘
     │                                            │
     │  6. Verify blob exists                     │
     │  7. { success: true, url }                 │
     │ ←──────────────────────────────────────────┘
     │
     │  8. POST /chats/{threadId}/voice-message
     ├────────────────────────────────────>┌─────────────┐
     │    { audioUrl, durationSecs }      │ChatEndpoints│
     │                                     └──────┬──────┘
     │  9. Create ChatMessage                    │
     │     MessageType = "VOICE"                 │
     │     MetaJson = { audioUrl, durationSecs } │
     │ 10. Send push notification                │
     │ 11. { status: "SENT", messageId }         │
     │ ←─────────────────────────────────────────┘
```

**Recipient's flow:**
```
12. Open thread → See "🎤 Voice note"
13. Tap play → Audio loads from Azure Blob
14. Listen to completion
15. POST /chats/{threadId}/messages/{messageId}/voice-listened
    → Record VoiceNoteListenComplete signal (ECHO)
    → Check for MutualVoiceExchange (both users sent voice)
```

---

## Step-by-Step Breakdown

### 1. Recording Audio

**Frontend:** MediaRecorder API (Web Audio)

```typescript
// chat-thread.component.ts (recording logic)
let mediaRecorder: MediaRecorder;
let audioChunks: Blob[] = [];

// Request microphone permission
const stream = await navigator.mediaDevices.getUserMedia({ audio: true });

// Start recording
mediaRecorder = new MediaRecorder(stream, {
  mimeType: 'audio/webm;codecs=opus'  // Most efficient
});

mediaRecorder.ondataavailable = (e) => {
  audioChunks.push(e.data);
};

mediaRecorder.start();

// Stop after 60s max (or user tap)
setTimeout(() => mediaRecorder.stop(), 60000);

mediaRecorder.onstop = async () => {
  const audioBlob = new Blob(audioChunks, { type: 'audio/webm' });
  const durationSecs = calculateDuration();  // From recording timestamps
  
  // Proceed to upload
  await uploadVoiceNote(audioBlob, durationSecs);
};
```

**Format:** WebM with Opus codec (best browser support + compression)

**Fallback formats:**
- Safari: `audio/mp4` (AAC codec)
- Legacy: `audio/ogg` (Vorbis codec)

**Duration limit:** 60 seconds (enforced client-side, validated server-side)

---

### 2. Request Upload Token

**Endpoint:** `POST /media/upload-token`

**Request:**
```json
{
  "container": "voice-note",
  "fileName": "voice-1730934120345.webm",
  "contentType": "audio/webm"
}
```

**Response:**
```json
{
  "uploadUrl": "https://...blob.core.windows.net/voice-notes/1042/a7b3c9d1.webm?sv=2024-11-04&se=...",
  "sasToken": "sv=2024-11-04&se=2024-10-07T12:30:00Z&sr=b&sp=cw&sig=...",
  "blobPath": "1042/a7b3c9d1-4e2f-5a6b-7c8d-9e0f1a2b3c4d.webm",
  "expiresAt": "2024-10-07T12:30:00Z"
}
```

**SAS token permissions:** Write + Create (no Read — public container)  
**Expiry:** 15 minutes (enough for slow connections)

**Rate limit:** 20 upload tokens per user per day

**See:** [sas-tokens.md](./sas-tokens.md) for security details.

---

### 3. Upload to Blob Storage

**Direct PUT** from browser to Azure Blob Storage (no API server involved).

**HTTP Request:**
```http
PUT https://...blob.core.windows.net/voice-notes/1042/a7b3c9d1.webm?{sasToken}
Content-Type: audio/webm
x-ms-blob-type: BlockBlob
Content-Length: 245760

[binary audio data]
```

**Headers:**
- `x-ms-blob-type: BlockBlob` — required by Azure Blob API
- `Content-Type: audio/webm` — MIME type for correct playback
- `Content-Length` — browser adds automatically

**Response:**
```http
201 Created
```

**Error handling:**
- **401 Unauthorized:** SAS token expired (15 min limit) → request new token
- **403 Forbidden:** SAS signature invalid → bug in token generation
- **413 Payload Too Large:** File exceeds 5 MB → compress or shorten recording
- **500 Server Error:** Azure Blob down → retry with exponential backoff

**Frontend implementation:**
```typescript
// media.service.ts
async uploadVoiceNote(blob: Blob, durationSecs: number) {
  const ext = blob.type.includes('ogg') ? 'ogg' 
            : blob.type.includes('mp4') ? 'mp4' 
            : 'webm';
  const fileName = `voice-${Date.now()}.${ext}`;

  // Step 1: Get SAS token
  const token = await firstValueFrom(
    this.http.post<UploadTokenResponse>(`${apiUrl}/media/upload-token`, {
      containerType: 'voice-note',
      fileName,
      contentType: blob.type || 'audio/webm',
    })
  );

  // Step 2: PUT to blob storage
  await fetch(token.uploadUrl, {
    method: 'PUT',
    headers: { 
      'x-ms-blob-type': 'BlockBlob', 
      'Content-Type': blob.type || 'audio/webm' 
    },
    body: blob,
  });

  // Step 3: Confirm upload
  const confirmed = await firstValueFrom(
    this.http.post<ConfirmResponse>(`${apiUrl}/media/confirm`, {
      mediaId: token.mediaId,
    })
  );

  return { fileUrl: confirmed.fileUrl, durationSecs };
}
```

**Note:** Frontend service uses outdated field names (`containerType`, `mediaId`) — should be `container`, `blobPath` to match backend.

---

### 4. Confirm Upload

**Endpoint:** `POST /media/confirm`

**Purpose:** Verify blob exists before creating chat message (prevent broken audio URLs).

**Request:**
```json
{
  "blobPath": "1042/a7b3c9d1-4e2f-5a6b-7c8d-9e0f1a2b3c4d.webm",
  "container": "voice-note"
}
```

**Backend logic** (MediaService.cs:64-113):
```csharp
public async Task<ConfirmUploadResult> ConfirmUploadAsync(
    int userId, string blobPath, MediaContainerType container, ...)
{
    var blobClient = _blobService
        .GetBlobContainerClient(ContainerName(container))
        .GetBlobClient(blobPath);

    var exists = await blobClient.ExistsAsync(ct);
    if (!exists.Value)
    {
        _logger.LogWarning("[Media] ConfirmUpload: blob not found at {BlobPath}", blobPath);
        return new ConfirmUploadResult(false, null, "BLOB_NOT_FOUND");
    }

    var url = blobClient.Uri.ToString();

    // Voice notes: no moderation (text-based trust signals instead)
    // Processing stub — FFmpeg transcoding queue in Phase 2
    if (container == MediaContainerType.VoiceNote)
        _logger.LogInformation("[Media] PROCESSING_QUEUED:{BlobPath}", blobPath);

    _logger.LogInformation("[Media] Confirmed upload for {BlobPath}", blobPath);
    return new ConfirmUploadResult(true, url, null);
}
```

**Response (success):**
```json
{
  "success": true,
  "url": "https://...blob.core.windows.net/voice-notes/1042/a7b3c9d1.webm"
}
```

**Response (failure):**
```json
{
  "success": false,
  "error": "BLOB_NOT_FOUND"
}
```

**Ownership check** (MediaEndpoints.cs:90):
```csharp
// Every blob path is prefixed with {userId}/
if (!req.BlobPath.StartsWith($"{userId}/"))
    return Results.Forbid();
```

**No moderation** for voice notes (unlike profile photos). Trust signals handle bad actors:
- Excessive voice spam → rate limit
- Recipient blocks → match closed + trust penalty
- Harassment reports → manual review

---

### 5. Create Chat Message

**Endpoint:** `POST /chats/{threadId}/voice-message`

**Request:**
```json
{
  "audioUrl": "https://...blob.core.windows.net/voice-notes/1042/a7b3c9d1.webm",
  "durationSecs": 37.5
}
```

**Backend logic** (ChatEndpoints.cs:770-858):
```csharp
group.MapPost("/{threadId:guid}/voice-message", async (
    Guid threadId,
    VoiceMessageRequest req,
    WovenDbContext db,
    INotificationService notify,
    IMatchSignalService signals,
    ...) =>
{
    var me = GetUserId(http.User);

    // Rate limit: 10 voice messages per minute
    var rateLimitKey = $"ratelimit:chat:voice:{me}";
    var allowed = await cache.CheckRateLimitAsync(rateLimitKey, 10, TimeSpan.FromMinutes(1), ct);
    if (!allowed) {
        http.Response.Headers["Retry-After"] = "60";
        return Results.StatusCode(429);
    }

    // Validate duration (60s max)
    if (req.DurationSecs < 1 || req.DurationSecs > 60)
        return Results.BadRequest(new { error = "INVALID_DURATION" });

    // Verify thread ownership
    var thread = await db.ChatThreads
        .Include(t => t.Match)
        .FirstOrDefaultAsync(t => t.Id == threadId, ct);
    if (thread == null) return Results.NotFound();

    var match = thread.Match;
    var isParticipant = match.UserAId == me || match.UserBId == me;
    if (!isParticipant) return Results.Forbid();

    var otherUserId = match.UserAId == me ? match.UserBId : match.UserAId;

    // Create message
    var metaJson = JsonSerializer.Serialize(new
    {
        audioUrl = req.AudioUrl,
        durationSecs = req.DurationSecs
    });

    var msg = new ChatMessage
    {
        ThreadId = threadId,
        SenderUserId = me,
        Body = "",
        MessageType = "VOICE",
        MetaJson = metaJson,
        CreatedAt = now
    };

    db.ChatMessages.Add(msg);
    thread.UpdatedAt = now;
    thread.LastMessageAt = now;
    thread.MessageCount++;
    await db.SaveChangesAsync(ct);

    // Record TimeToFirstVoiceNote signal (once per thread per sender)
    var isFirstVoice = await db.ChatMessages
        .CountAsync(m => m.ThreadId == threadId 
                      && m.SenderUserId == me 
                      && m.MessageType == "VOICE", ct) == 1;

    if (isFirstVoice)
    {
        var elapsedMs = (float)(now - match.CreatedAt).TotalMilliseconds;
        await signals.RecordAsync(me, otherUserId,
            MatchSignalEventTypes.TimeToFirstMessageMs, elapsedMs, ct: ct);
    }

    // Push notification
    _ = notify.NewChatMessageAsync(otherUserId, threadId, msg.Id, 
        "🎤 Voice note", me, now, ct);

    return Results.Ok(new { status = "SENT", messageId = msg.Id, createdAt = msg.CreatedAt });
});
```

**MetaJson structure:**
```json
{
  "audioUrl": "https://...blob.core.windows.net/voice-notes/1042/a7b3c9d1.webm",
  "durationSecs": 37.5
}
```

**Message rendering:**
- **Chat list:** "🎤 Voice note"
- **Thread:** Audio player with duration (e.g., "0:37")

**Rate limiting:**
- **Voice messages:** 10 per minute (prevents spam)
- **Upload tokens:** 20 per day (prevents storage abuse)

---

### 6. Playback & Listen Tracking

**Frontend audio player:**

```typescript
// Render in chat thread
<div *ngIf="msg.messageType === 'VOICE'" class="voice-message">
  <button (click)="playVoice(msg)">▶️</button>
  <span>{{ formatDuration(msg.meta.durationSecs) }}</span>
</div>

async playVoice(msg: ChatMessage) {
  const audio = new Audio(msg.meta.audioUrl);
  audio.play();

  // Track listen completion
  audio.onended = async () => {
    if (msg.senderUserId !== this.currentUserId) {
      await this.trackListenComplete(msg.id);
    }
  };
}
```

**Listen tracking endpoint:** `POST /chats/{threadId}/messages/{messageId}/voice-listened`

**Backend logic** (ChatEndpoints.cs:860-917):
```csharp
group.MapPost("/{threadId:guid}/messages/{messageId:guid}/voice-listened", async (...) =>
{
    var me = GetUserId(http.User);

    // Verify thread ownership
    var thread = await db.ChatThreads.AsNoTracking()
        .FirstOrDefaultAsync(t => t.Id == threadId, ct);
    if (thread == null) return Results.NotFound(new { error = "THREAD_NOT_FOUND" });

    var match = await db.Matches.AsNoTracking()
        .FirstOrDefaultAsync(m => m.Id == thread.MatchId, ct);
    var isParticipant = match.UserAId == me || match.UserBId == me;
    if (!isParticipant) return Results.Forbid();

    var message = await db.ChatMessages.AsNoTracking()
        .FirstOrDefaultAsync(m => m.Id == messageId 
                               && m.ThreadId == threadId 
                               && m.MessageType == "VOICE", ct);
    if (message == null) return Results.NotFound(new { error = "MESSAGE_NOT_FOUND" });

    // Cannot credit listening to your own note
    if (message.SenderUserId == me)
        return Results.BadRequest(new { error = "CANNOT_LISTEN_OWN_NOTE" });

    var senderUserId = message.SenderUserId;

    // Record listen completion signal
    await signals.RecordAsync(me, senderUserId,
        MatchSignalEventTypes.VoiceNoteListenComplete, 1f, ct: ct);

    // Check for mutual voice exchange
    var senderHasVoice = await db.ChatMessages.AsNoTracking()
        .AnyAsync(m => m.ThreadId == threadId 
                    && m.SenderUserId == senderUserId 
                    && m.MessageType == "VOICE", ct);
    var listenerHasVoice = await db.ChatMessages.AsNoTracking()
        .AnyAsync(m => m.ThreadId == threadId 
                    && m.SenderUserId == me 
                    && m.MessageType == "VOICE", ct);

    if (senderHasVoice && listenerHasVoice)
    {
        await signals.RecordAsync(me, senderUserId,
            MatchSignalEventTypes.MutualVoiceExchange, 1f, ct: ct);
    }

    return Results.Ok(new { status = "RECORDED" });
});
```

**ECHO Signals:**
- `VoiceNoteListenComplete` — 0.05 connection score boost
- `MutualVoiceExchange` — 0.08 connection score boost (both users sent voice)

**See:** ECHO signals documentation for full scoring model.

---

## Storage & Delivery

### Blob Path Pattern

**Format:** `{userId}/{guid}.{ext}`  
**Example:** `1042/a7b3c9d1-4e2f-5a6b-7c8d-9e0f1a2b3c4d.webm`

**Ownership:** Blob path prefix ensures only creator can confirm/delete.

### Container Configuration

**Name:** `voice-notes`  
**Access level:** Public (anonymous read, authenticated write via SAS)  
**Retention:** 90 days (lifecycle policy deletes old blobs)

**Why public read?**
- No SAS token needed for playback
- CDN caching works seamlessly
- Simplifies client-side audio player

**Security:** Blob paths are UUIDs (not enumerable), access requires knowing the exact URL.

### File Formats & Sizes

**Accepted formats:**
- `audio/webm` (Opus codec) — preferred, best compression
- `audio/mp4` (AAC codec) — Safari fallback
- `audio/ogg` (Vorbis codec) — legacy support

**Size limits:**
- Max file size: 5 MB
- Max duration: 60 seconds
- Typical 30s message: ~200 KB (webm/opus)

**Compression:**
- Opus: 32 kbps (phone-quality speech)
- AAC: 64 kbps (compatibility)

---

## Processing Pipeline (Phase 2)

### Planned Enhancements

**1. Format normalization:**
- Convert all uploads to `audio/webm` (Opus codec)
- Generate `audio/mp4` (AAC) fallback for Safari
- Delete original upload after processing

**2. Audio optimization:**
- Normalize volume (loudness equalization)
- Noise reduction (background hum, traffic)
- Silence trimming (start/end)

**3. Transcription:**
- Whisper API (OpenAI) for speech-to-text
- Store transcript in `MetaJson.transcript`
- Accessibility feature (deaf users, silent environments)
- Trust signal (detect harassment via text moderation)

**4. Speaker verification:**
- Voice fingerprinting across messages
- Detect voice swapping (catfish indicator)
- Flag mismatched gender (voice vs. profile)

**Implementation:**
```csharp
// MediaService.cs:102-103 (current stub)
if (container == MediaContainerType.VoiceNote)
    _logger.LogInformation("[Media] PROCESSING_QUEUED:{BlobPath}", blobPath);
```

**Worker:** `MediaProcessingWorker` (future background job)

---

## Frontend Implementation

### Recording UI States

**States:**
1. **Idle** — Microphone button visible
2. **Requesting permission** — "Allow microphone access" prompt
3. **Recording** — Timer counting up, red dot pulsing
4. **Uploading** — Progress spinner, "Sending voice note..."
5. **Sent** — Checkmark, message appears in thread
6. **Error** — Red banner with retry option

### Recording Component

**File:** `pages/chats/chat-thread.component.ts`

**State management:**
```typescript
isRecording = false;
recordingDuration = 0;
uploadingVoice = false;
voiceError: string | null = null;

private mediaRecorder: MediaRecorder | null = null;
private audioChunks: Blob[] = [];
private recordingStartTime: number = 0;
```

**Methods:**
```typescript
async startRecording() {
  try {
    const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    
    this.mediaRecorder = new MediaRecorder(stream, {
      mimeType: 'audio/webm;codecs=opus'
    });
    
    this.mediaRecorder.ondataavailable = (e) => {
      this.audioChunks.push(e.data);
    };
    
    this.mediaRecorder.onstop = async () => {
      await this.handleRecordingComplete();
      stream.getTracks().forEach(t => t.stop());  // Release microphone
    };
    
    this.isRecording = true;
    this.recordingStartTime = Date.now();
    this.mediaRecorder.start();
    
    // Update duration every 100ms
    this.updateDurationTimer();
    
    // Auto-stop at 60s
    setTimeout(() => {
      if (this.isRecording) this.stopRecording();
    }, 60000);
    
  } catch (err) {
    this.voiceError = 'Microphone access denied';
  }
}

stopRecording() {
  if (this.mediaRecorder && this.isRecording) {
    this.mediaRecorder.stop();
    this.isRecording = false;
  }
}

async handleRecordingComplete() {
  const audioBlob = new Blob(this.audioChunks, { type: 'audio/webm' });
  const durationSecs = (Date.now() - this.recordingStartTime) / 1000;
  
  this.audioChunks = [];
  this.uploadingVoice = true;
  
  try {
    const { fileUrl } = await this.media.uploadVoiceNote(audioBlob, durationSecs);
    await this.chat.sendVoiceMessage(this.threadId, fileUrl, durationSecs);
    this.voiceError = null;
  } catch (err) {
    this.voiceError = 'Failed to send voice note. Try again?';
  } finally {
    this.uploadingVoice = false;
  }
}
```

### Service Methods

**File:** `services/chat.service.ts`

```typescript
sendVoiceMessage(threadId: string, audioUrl: string, durationSecs: number) {
  return this.http.post(`${apiUrl}/chats/${threadId}/voice-message`, {
    audioUrl,
    durationSecs
  });
}

trackVoiceListened(threadId: string, messageId: string) {
  return this.http.post(
    `${apiUrl}/chats/${threadId}/messages/${messageId}/voice-listened`,
    {}
  );
}
```

---

## Error Handling

### Client-Side Errors

| Error | Cause | Recovery |
|---|---|---|
| Microphone access denied | User declined permission | Show instructions to enable in browser settings |
| MediaRecorder not supported | Old browser | Show "Voice notes require a modern browser" |
| Upload timeout | Slow connection | Retry with exponential backoff |
| SAS token expired | Upload took > 15 min | Request new token, re-upload |
| Blob confirm failed | Network error during upload | Re-upload from scratch |

### Server-Side Errors

| Error Code | HTTP Status | Cause | Recovery |
|---|---|---|---|
| `INVALID_DURATION` | 400 | Duration < 1s or > 60s | Client validation bug |
| `THREAD_NOT_FOUND` | 404 | Invalid threadId | Refresh thread list |
| `BLOB_NOT_FOUND` | 422 | Confirm before upload complete | Wait, retry confirm |
| Rate limit | 429 | > 10 messages/min | Show "Slow down" message, retry after 60s |

---

## Performance Considerations

### Upload Time

**Typical 30s message:**
- Recording: 30s
- Blob size: 200 KB
- Upload (10 Mbps): < 1s
- Confirm: < 200ms
- Create message: < 100ms
- **Total:** ~31s user time

**Optimization:** Show "uploading" state immediately, send message optimistically.

### Storage Growth

**Assumptions:**
- 1000 active users
- 5 voice messages per user per day
- 200 KB average size
- 90-day retention

**Storage:** 1000 × 5 × 200 KB × 90 = 90 GB  
**Cost (Azure Blob):** ~$2/month (hot tier)

**Phase 2:** Move to cool tier after 7 days (10× cheaper).

### Bandwidth

**Download:** Every voice note played = 1 blob fetch  
**CDN caching:** Minimal (different recipients, different URLs)

**Monthly bandwidth (1000 users, 5 msgs/day, 50% listen rate):**
- 1000 × 5 × 0.5 × 200 KB × 30 = 15 GB
- Cost: ~$1/month

---

## Testing

### Manual Test Flow

**Happy path:**
1. Open chat thread
2. Tap microphone button
3. Allow microphone access
4. Record 10s message
5. Tap stop
6. Verify "Uploading..." spinner
7. Verify message appears in thread with 🎤 icon
8. Other user: tap play, listen to completion
9. Check DB: `MatchSignalLogs` has `VoiceNoteListenComplete` entry

**Error cases:**
1. Deny microphone → expect error message
2. Record 61s message → auto-stopped at 60s
3. Upload while offline → expect retry prompt
4. SAS token expires → expect new token request
5. Send 11 messages in 1 min → expect 429 rate limit

### Automated Tests

**Unit tests:**
- MediaRecorder event handling
- Duration calculation accuracy
- Audio blob creation
- Upload retry logic

**Integration tests:**
- Full upload flow (mock Azure Blob)
- Listen tracking (mock signal service)
- Mutual voice exchange detection
- Rate limit enforcement

---

## Monitoring & Metrics

### Key Metrics

- **Voice message volume:** Messages per day (overall + per user)
- **Upload success rate:** % that complete all 5 steps
- **Listen rate:** % of received voice notes played to completion
- **Mutual voice rate:** % of matches with both users sending voice
- **Average duration:** Typical message length (target: 20-40s)

### Logging

**MediaService:**
```
[Media] SAS token issued for {BlobPath}
[Media] Confirmed upload for {BlobPath}
[Media] PROCESSING_QUEUED:{BlobPath}
```

**ChatEndpoints:**
```
[Chat] Voice message sent | ThreadId={ThreadId} SenderId={UserId} Duration={Duration}s
[Chat] Voice listened | MessageId={MessageId} ListenerId={UserId}
[Chat] Mutual voice exchange | ThreadId={ThreadId}
```

### Alerts

- **High upload failure rate** (> 5%): Azure Blob performance issue
- **Low listen rate** (< 30%): UX problem, users not engaging
- **Voice spam detected** (user sends > 20/day): Trust review
- **SAS token expiries** (> 1%): Slow connections, increase timeout

---

## ECHO Integration

### Signals Recorded

| Signal | Event | Value | Weight (Connection Score) |
|---|---|---|---|
| `TimeToFirstMessageMs` | First voice note sent | Milliseconds since match | Variable (faster = higher) |
| `VoiceNoteListenComplete` | Recipient played to end | 1.0 | 0.05 |
| `MutualVoiceExchange` | Both users sent voice | 1.0 | 0.08 |

**Why these matter:**

**TimeToFirstMessageMs:**  
Fast response = high interest. 10-minute reply beats 24-hour reply.

**VoiceNoteListenComplete:**  
Listening shows respect + engagement. Ignoring voice = low intent.

**MutualVoiceExchange:**  
Voice reciprocation = comfort + trust. Strongest positive signal.

**See:** ECHO documentation for full matching algorithm.

---

## Product Philosophy

### Why Voice Notes?

**Cold-start problem:** Text chat is low-energy, easy to ghost.  
**Voice advantage:** Hearing someone's voice creates instant connection.  
**Intentionality filter:** Voice notes take effort → filters low-intent users.

### Voice vs. Video Calls

**Voice wins for:**
- No appearance pressure
- Async (time zone friendly)
- Low bandwidth
- Private (can record anywhere)

**Video for:** Later stages (post-trial, date planning)

### Privacy Considerations

**No transcription storage** (Phase 2 will create, not store)  
**90-day auto-delete** (ephemeral, not archived)  
**No voice fingerprint database** (verification runs per-match only)

---

## Related Documentation

- [Media System Overview](./README.md)
- [SAS Token Security](./sas-tokens.md)
- [Azure Blob Storage](./azure-blob.md)
- [API Reference](./api.md)
- [Photos Upload](./photos.md) — Different upload pattern (data URL)
- [CDN Strategy](./cdn.md) — Delivery optimization

**Implementation Files:**
- `Services/MediaService.cs` — Upload & confirm
- `Endpoints/ChatEndpoints.cs` — Voice message endpoints
- `Services/MatchSignalService.cs` — ECHO integration
- `frontend/services/media.service.ts` — Upload client
- `frontend/pages/chats/chat-thread.component.ts` — Recording UI
