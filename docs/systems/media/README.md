# Media System

**Last Updated:** 2026-10-07  
**Status:** ACTIVE (CDN pending Phase 2)

---

## Overview

The Media system handles all file uploads, storage, and delivery for Woven. Built on Azure Blob Storage with direct-upload architecture to keep large binaries off the API server.

### What It Handles

- **Profile Photos** (moderated)
- **Voice Notes** (chat audio messages)
- **Tile Media** (future — Commons content)

### Architecture Pattern

```
Client → API (SAS token) → Client → Azure Blob (direct PUT) → Client → API (confirm) → Done
```

This keeps the API server lightweight and scales storage independently.

---

## Core Components

| Component | Purpose | Location |
|---|---|---|
| **MediaService** | SAS token generation, upload confirmation, moderation | `Services/MediaService.cs` |
| **MediaEndpoints** | HTTP API for token/confirm/delete | `Endpoints/MediaEndpoints.cs` |
| **Azure Blob Storage** | Binary storage (3 containers) | Azure resource |
| **ModerationService** | Image content moderation | `Services/Moderation/` |
| **MediaLifecycleWorker** | Cleanup orphaned blobs | `Services/Media/MediaLifecycleWorker.cs` |

---

## Storage Containers

All blob paths follow pattern: `{userId}/{guid}{ext}`

| Container | Purpose | Moderation | Max Size |
|---|---|---|---|
| `profile-photos` | User profile pictures | ✅ Auto-moderation | 10 MB |
| `voice-notes` | Chat audio messages | ❌ | 5 MB |
| `tile-media` | Commons posts (future) | ⚠️ Queue for review | 20 MB |

---

## Upload Flows

### 1. Direct Blob Upload (Recommended)

**Used by:** Voice notes, future photo uploads

```
┌──────┐  1. POST /media/upload-token        ┌─────┐
│Client├────────────────────────────────────>│ API │
└──┬───┘  { container, fileName, contentType }└──┬──┘
   │                                            │
   │  2. { uploadUrl, sasToken, blobPath, expiresAt }
   │<───────────────────────────────────────────┘
   │
   │  3. PUT {uploadUrl}                     ┌──────────┐
   ├─────────────────────────────────────────>│Azure Blob│
   │    Headers: x-ms-blob-type: BlockBlob   └──────────┘
   │    Body: [binary]
   │
   │  4. POST /media/confirm                  ┌─────┐
   ├─────────────────────────────────────────>│ API │
   │    { blobPath, container }               └──┬──┘
   │                                            │
   │  5. { success: true, url }                 │
   │<───────────────────────────────────────────┘
```

**Benefits:**
- No binary data through API server
- Scales storage independently
- Client-side upload progress
- 15-minute upload window

**See:** [voice-notes.md](./voice-notes.md), [sas-tokens.md](./sas-tokens.md)

### 2. Data URL Upload (Legacy)

**Used by:** Onboarding photo flow (transitioning to direct upload)

```
Client → API (POST /onboarding/photos) → Backend converts data URL → Blob Storage
```

Backend receives base64-encoded data URLs and handles blob upload internally.

**See:** [photos.md](./photos.md)

---

## Security

### Ownership Checks

Every blob path starts with `{userId}/` — ownership verified on:
- Confirm (line 90, MediaEndpoints.cs)
- Delete (line 117, MediaEndpoints.cs)

Only the creating user can confirm or delete their blobs.

### Rate Limiting

**Upload tokens:** 20 per user per day  
**Voice messages:** 10 per minute per user (chat endpoint)

### Moderation

**Profile photos** get auto-moderated on confirm:
- `AUTO_REJECTED` → blob deleted, confirmation fails
- `ESCALATED` → catfish flag, admin review queued
- `APPROVED` → confirmation succeeds

**See:** [photos.md](./photos.md#moderation)

---

## Configuration

### Azure Blob Storage Setup

**Connection String** (User Secrets / Azure Key Vault):
```json
{
  "Azure": {
    "Storage": {
      "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=..."
    }
  }
}
```

**Local Dev:** Uses Azurite (Azure Storage Emulator)  
**Production:** Azure Storage account in `woven-prod-rg`

### Service Registration

**Program.cs:489-493**
```csharp
builder.Services.AddSingleton<BlobServiceClient>(sp =>
    new BlobServiceClient(builder.Configuration["Azure:Storage:ConnectionString"]));
builder.Services.AddScoped<IMediaService, MediaService>();
builder.Services.AddHostedService<MediaLifecycleWorker>();
```

`BlobServiceClient` is thread-safe — singleton is correct.

---

## API Reference

**Full specs:** [api.md](./api.md)

| Endpoint | Method | Purpose |
|---|---|---|
| `/media/upload-token` | POST | Get SAS token for direct upload |
| `/media/confirm` | POST | Verify blob exists after upload |
| `/media/{container}/{**blobPath}` | DELETE | Delete owned blob |

---

## Integration Points

### Voice Notes

**Flow:** MediaRecorder → SAS token → blob upload → confirm → chat message  
**Tracking:** Listen completion signals for ECHO  
**See:** [voice-notes.md](./voice-notes.md)

### Profile Photos

**Flow:** Onboarding upload → moderation → User entity `PhotoUrl` field  
**Validation:** Image format, size limits, NSFW detection  
**See:** [photos.md](./photos.md)

### Commons Tiles (Future)

**Flow:** Similar to voice notes — direct blob upload with processing queue  
**Processing:** FFmpeg transcoding, image resizing, thumbnail generation  
**See:** CLAUDE.md (line 102) — "Processing stub — Phase 2"

---

## CDN Strategy (Phase 2)

**Current:** Direct blob storage URLs  
**Planned:** Azure CDN endpoint swap in `GetMediaUrlAsync()`

**MediaService.cs:133-140** (current implementation):
```csharp
public Task<string> GetMediaUrlAsync(string blobPath, MediaContainerType container, ...)
{
    // Dev: direct storage / Azurite URL.
    // Prod: same for now — swap hostname for CDN when Phase 2 configures it.
    var blobClient = _blobService
        .GetBlobContainerClient(ContainerName(container))
        .GetBlobClient(blobPath);
    return Task.FromResult(blobClient.Uri.ToString());
}
```

**Phase 2 changes:**
- Create Azure CDN profile + endpoint
- Update `GetMediaUrlAsync()` to return CDN URL
- Configure cache rules (photos: 1 year, voice: 7 days)
- Purge API for content updates

**See:** [cdn.md](./cdn.md)

---

## Monitoring

### Log Patterns

All logs prefixed with `[Media]`:

```csharp
_logger.LogInformation("[Media] SAS token issued for {BlobPath} (expires {ExpiresAt})", ...);
_logger.LogInformation("[Media] Confirmed upload for {BlobPath}", blobPath);
_logger.LogWarning("[Media] ProfilePhoto auto-rejected for user {UserId}: {BlobPath}", ...);
_logger.LogError(ex, "[Media] ConfirmUpload failed for {BlobPath}", blobPath);
```

### Key Metrics

- Upload token issuance rate
- Confirm success rate
- Moderation rejection rate
- Blob storage growth
- SAS token expiry vs. confirm timing

---

## Error Handling

### Confirm Upload Errors

| Error Code | Meaning | HTTP Status |
|---|---|---|
| `BLOB_NOT_FOUND` | Upload failed or SAS expired | 422 |
| `PHOTO_REJECTED` | Moderation auto-rejected | 422 |
| `INTERNAL_ERROR` | Service exception | 422 |

**Client handling:** Retry upload flow from step 1 (new SAS token).

---

## Development

### Testing Locally

1. **Start Azurite:**
   ```bash
   azurite --location c:\azurite --debug c:\azurite\debug.log
   ```

2. **Connection string** (appsettings.Development.json):
   ```
   UseDevelopmentStorage=true
   ```

3. **Storage Explorer** to inspect containers

### Manual Upload Test

```bash
# 1. Get token
curl -X POST http://localhost:5135/media/upload-token \
  -H "Authorization: Bearer {jwt}" \
  -H "Content-Type: application/json" \
  -d '{"container":"voice-note","fileName":"test.webm","contentType":"audio/webm"}'

# 2. Upload to returned uploadUrl
curl -X PUT "{uploadUrl}" \
  -H "x-ms-blob-type: BlockBlob" \
  -H "Content-Type: audio/webm" \
  --data-binary @test.webm

# 3. Confirm
curl -X POST http://localhost:5135/media/confirm \
  -H "Authorization: Bearer {jwt}" \
  -H "Content-Type: application/json" \
  -d '{"blobPath":"{returned-path}","container":"voice-note"}'
```

---

## Migration Notes

### Transitioning Photo Uploads

**Current state:** Onboarding uses data URL → backend upload  
**Target state:** All photos use direct blob upload (consistent with voice notes)

**Migration path:**
1. Add photo upload UI using media service pattern
2. Update onboarding to use new flow
3. Deprecate data URL endpoint (breaking change — coordinate with mobile)

### Blob Path Migration

Old pattern: `{guid}.{ext}` (no userId prefix)  
New pattern: `{userId}/{guid}.{ext}` (ownership embedded)

**No migration needed** — old blobs grandfathered, new uploads use new pattern.

---

## Related Documentation

- [Voice Notes Flow](./voice-notes.md) — Complete voice message lifecycle
- [Photo Upload](./photos.md) — Profile picture validation & moderation
- [Azure Blob Integration](./azure-blob.md) — Storage account setup
- [SAS Token Generation](./sas-tokens.md) — Security model & expiry
- [CDN Strategy](./cdn.md) — Phase 2 delivery optimization
- [API Reference](./api.md) — Full endpoint specs

**Backend Implementation:**
- `Services/MediaService.cs` — Core service
- `Services/IMediaService.cs` — Interface & DTOs
- `Endpoints/MediaEndpoints.cs` — HTTP API
- `Services/Moderation/ModerationService.cs` — Image moderation

**Frontend Integration:**
- `services/media.service.ts` — Upload client
- `pages/chats/chat-thread.component.ts` — Voice recording
- `pages/onboarding/photos/photos.page.ts` — Photo onboarding
