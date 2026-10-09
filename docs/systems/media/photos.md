# Photo Upload, Validation & Storage

**Last Updated:** 2026-10-07  
**Container:** `profile-photos`

---

## Overview

Profile photos are the primary visual identity in Woven. The system handles upload, validation, moderation, and storage with a focus on safety and quality.

**Current flow:** Data URL upload (onboarding)  
**Future flow:** Direct blob upload (consistency with voice notes)

---

## Upload Patterns

### 1. Onboarding Flow (Current)

**Used during:** Initial account setup at `/onboarding/photos`

```
┌──────┐  1. File picker                 ┌──────────┐
│Client│────────────────────────────────>│FileReader│
└──┬───┘                                  └────┬─────┘
   │                                          │
   │  2. readAsDataURL(file)                  │
   │<─────────────────────────────────────────┘
   │
   │  3. PUT /onboarding/photos           ┌─────┐
   ├─────────────────────────────────────>│ API │
   │    { photos: [{url, caption, sortOrder}] }  │
   │                                       └──┬──┘
   │                                          │
   │                                      4. Parse data URLs
   │                                      5. Upload to blob storage
   │                                      6. Run moderation
   │                                      7. Save User.PhotoUrl
   │                                          │
   │  8. { message, count }                   │
   │<─────────────────────────────────────────┘
```

**Frontend:** `pages/onboarding/photos/photos.page.ts`

**Key characteristics:**
- Base64-encoded data URLs sent in JSON payload
- Backend handles blob upload internally
- Multiple photos uploaded in single request (up to 6)
- First photo becomes primary `User.PhotoUrl`

### 2. Direct Blob Upload (Planned)

**Will use:** Same pattern as voice notes (SAS token → blob → confirm)

**Benefits over data URL:**
- No base64 encoding overhead (33% size reduction)
- Progress feedback for large images
- Consistent with voice note UX
- No request payload size limits

**Migration:** Breaking change — requires mobile app update.

---

## Photo Slots & Requirements

### Onboarding Photo Steps

| Slot | Required | Purpose | Hint |
|---|---|---|---|
| 1 | ✅ | Hello shot | "Clear eyes, easy smile, great light" |
| 2 | ✅ | Full-body vibe | "Show how you show up" |
| 3 | ✅ | In the zone | "Building, exploring, geeking out" |
| 4 | ❌ | Captured in wild | "A moment you didn't plan" |
| 5 | ❌ | Conversation starter | "Give them a reason to ask" |
| 6 | ❌ | Wildcard | "Mystery + personality" |

**Minimum:** 3 photos to proceed  
**Maximum:** 6 total  
**Caption limit:** 40 characters per photo

### Frontend Validation

**Component:** `PhotosPageComponent`

```typescript
// File type check
if (!file.type.startsWith('image/')) {
  this.error = 'That doesn't look like an image. Try a photo instead 🙂';
  return;
}

// Minimum requirement
if (this.filledCount < this.minRequired) {
  this.error = `Add at least ${this.minRequired} photos to continue.`;
  return;
}
```

**No frontend size limit** — browser FileReader loads full image into memory.

---

## Backend Validation

### Size Limits

**Max file size:** 10 MB per photo (enforced at blob confirm)  
**Max total payload:** 50 MB (ASP.NET request limit)

**6 photos × 10 MB base64-encoded:**
- Original: 60 MB
- Base64 overhead: 60 MB × 1.33 = 80 MB
- **Exceeds limit** — current implementation bug

**Solution:** Direct blob upload (no base64 encoding).

### Format Validation

**Accepted formats:**
- JPEG (.jpg, .jpeg)
- PNG (.png)
- WebP (.webp)
- HEIC (.heic) — iOS native format

**Rejected:**
- GIF (no animations)
- BMP (too large)
- SVG (XSS risk)
- TIFF (uncommon)

**Implementation:** File extension check + MIME type validation.

### Dimension Requirements

**Minimum:** 400×400 px (prevents low-quality uploads)  
**Maximum:** 4000×6000 px (reasonable high-quality limit)  
**Aspect ratio:** Free (no constraints)

**Implementation:** Image header parsing in backend.

---

## Moderation

### Auto-Moderation Pipeline

**Trigger:** Profile photo confirm  
**Service:** `IModerationService.ModerateImageAsync(userId, url, ct)`  
**Model:** Azure Content Safety API (NSFW detection)

**MediaService.cs:86-99** (confirm flow):
```csharp
if (container == MediaContainerType.ProfilePhoto)
{
    var modResult = await _moderation.ModerateImageAsync(userId, url, ct);
    if (modResult == ModerationImageResult.AUTO_REJECTED)
    {
        await blobClient.DeleteIfExistsAsync(cancellationToken: ct);
        _logger.LogWarning("[Media] ProfilePhoto auto-rejected for user {UserId}: {BlobPath}", 
            userId, blobPath);
        return new ConfirmUploadResult(false, null, "PHOTO_REJECTED");
    }
    if (modResult == ModerationImageResult.ESCALATED)
    {
        await _trust.FlagAsync(userId, "CATFISH_SUSPECTED", 0.5f, ct);
        _logger.LogWarning("[Media] ProfilePhoto escalated (catfish suspected) for user {UserId}: {BlobPath}", 
            userId, blobPath);
    }
}
```

### Moderation Results

| Result | Action | User Impact | Trust Score |
|---|---|---|---|
| `APPROVED` | Photo saved, confirm succeeds | None | No change |
| `AUTO_REJECTED` | Blob deleted, confirm fails | "Photo rejected" error, must re-upload | No change |
| `ESCALATED` | Photo saved, queued for review | Can proceed, photo visible | -0.5 catfish flag |

**Rejection reasons:**
- NSFW content detected
- Multiple faces (primary photo must be solo)
- No face detected
- Violent or graphic imagery
- Known celebrity face (impersonation risk)

**Escalation triggers:**
- Suspicious editing artifacts
- Stock photo fingerprint match
- Inconsistent photo quality across slots
- Face mismatch between slots

### Manual Review Queue

**Escalated photos** go to moderation queue for human review.

**Admin actions:**
1. **Approve** — clears flag, photo stays
2. **Reject** — deletes photo, user notified, must re-upload
3. **Ban user** — account suspended, all photos deleted

**Queue:** `ModerationQueue` entity (future Commons moderation shares this)

---

## Storage & Delivery

### Blob Path Pattern

**Current (onboarding):** Backend generates path during data URL processing  
**Future (direct upload):** `{userId}/{guid}.{ext}` from SAS token

**Example:** `1042/a7b3c9d1-4e2f-5a6b-7c8d-9e0f1a2b3c4d.jpg`

### Primary Photo Selection

**User.PhotoUrl** stores the primary (first uploaded) photo URL.

**Onboarding endpoint** sets:
```csharp
user.PhotoUrl = photos[0].url; // First photo becomes primary
```

**Other slots** stored in `UserPhotos` table:
```sql
CREATE TABLE user_photos (
  id UUID PRIMARY KEY,
  user_id INT NOT NULL REFERENCES users(id),
  blob_path TEXT NOT NULL,
  caption TEXT,
  sort_order INT NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
```

### Delivery

**Current:** Direct blob storage URL  
**Phase 2:** Azure CDN with 1-year cache

**Example URLs:**
- Dev: `http://127.0.0.1:10000/devstoreaccount1/profile-photos/1042/...jpg`
- Prod: `https://wovenprodacr.blob.core.windows.net/profile-photos/1042/...jpg`
- CDN: `https://cdn.wooven.me/profile-photos/1042/...jpg` (future)

**Security:** All blobs publicly readable (no SAS token needed for GET).

---

## Processing Pipeline (Future)

### Image Optimization

**Phase 2 enhancements:**

1. **Resize variants:**
   - Thumbnail: 200×200 (card view)
   - Medium: 800×800 (profile view)
   - Original: preserved for full-screen

2. **Format conversion:**
   - JPEG → WebP (30% smaller, modern browsers)
   - Fallback: Keep JPEG for compatibility

3. **Compression:**
   - Lossy: 85% quality (imperceptible, major savings)
   - Strip EXIF (privacy — location, device, timestamps removed)

4. **Async processing:**
   - Upload confirms immediately with original
   - Worker generates variants in background
   - CDN serves optimized versions when ready

**Implementation:** `MediaProcessingWorker` (stub exists at line 102, MediaService.cs)

---

## Frontend Implementation

### Photo Upload Component

**File:** `pages/onboarding/photos/photos.page.ts`

**State management:**
```typescript
slots: PhotoSlot[] = Array.from({ length: 6 }, () => ({ 
  dataUrl: null, 
  caption: '' 
}));

stepIndex = 0;  // Current photo step (0-5) or summary (6)
saving = false;
error = '';
```

**Key methods:**

```typescript
// File selection
onFileChange(ev: Event) {
  const file = input.files[0];
  if (!file.type.startsWith('image/')) {
    this.error = 'Not an image';
    return;
  }
  
  const reader = new FileReader();
  reader.onload = () => {
    this.slotAt(this.stepIndex).dataUrl = String(reader.result);
  };
  reader.readAsDataURL(file);
}

// Upload to backend
async saveAndContinue() {
  const payload = {
    photos: this.slots
      .filter(s => s.dataUrl)
      .map((s, i) => ({
        url: s.dataUrl,           // Base64 data URL
        caption: s.caption.slice(0, 40),
        sortOrder: i + 1
      }))
  };
  
  await firstValueFrom(this.onboarding.savePhotos(payload));
  await this.router.navigateByUrl('/onboarding/intent');
}
```

### Service Layer

**File:** `onboarding/onboarding.service.ts`

```typescript
savePhotos(payload: SavePhotosPayload) {
  return this.http.put<{ message: string; count: number }>(
    `${environment.apiUrl}/onboarding/photos`,
    payload
  );
}
```

**No MediaService used** — onboarding bypasses direct blob upload flow.

---

## Backend Implementation

### Onboarding Endpoint

**File:** `Endpoints/OnboardingEndpoints.cs`

**Simplified flow:**
```csharp
app.MapPut("/onboarding/photos", async (SavePhotosRequest req, ...) =>
{
    var userId = GetUserId(user);
    
    // Parse data URLs, validate, upload to blob storage
    foreach (var photo in req.Photos)
    {
        var blobPath = await UploadDataUrlToBlob(photo.Url, userId);
        var modResult = await _moderation.ModerateImageAsync(userId, blobPath, ct);
        
        if (modResult == ModerationImageResult.AUTO_REJECTED)
            return Results.UnprocessableEntity(new { error = "PHOTO_REJECTED" });
        
        // Save to UserPhotos table
    }
    
    // Set primary photo
    user.PhotoUrl = firstBlobUrl;
    await db.SaveChangesAsync(ct);
});
```

**Data URL parsing:**
```csharp
// data:image/jpeg;base64,/9j/4AAQSkZJRg...
var match = Regex.Match(dataUrl, @"^data:image/(\w+);base64,(.+)$");
var format = match.Groups[1].Value;  // "jpeg"
var base64 = match.Groups[2].Value;  // raw base64
var bytes = Convert.FromBase64String(base64);
```

### Blob Upload Helper

```csharp
private async Task<string> UploadDataUrlToBlob(
    string dataUrl, 
    int userId, 
    CancellationToken ct)
{
    var bytes = ParseDataUrl(dataUrl, out var ext);
    var blobPath = $"{userId}/{Guid.NewGuid()}.{ext}";
    
    var container = _blobService.GetBlobContainerClient("profile-photos");
    await container.CreateIfNotExistsAsync(ct);
    
    var blob = container.GetBlobClient(blobPath);
    using var stream = new MemoryStream(bytes);
    await blob.UploadAsync(stream, overwrite: false, ct);
    
    return blobPath;
}
```

---

## Error Handling

### Client-Side Errors

| Error | Cause | Recovery |
|---|---|---|
| "Not an image" | Wrong file type selected | Re-select valid image file |
| "Couldn't read photo" | FileReader failure | Try different file |
| "Add at least 3 photos" | Minimum not met | Upload more photos |
| Network error | API unreachable | Retry after connection restored |

### Server-Side Errors

| Error Code | Cause | HTTP Status | Recovery |
|---|---|---|---|
| `PHOTO_REJECTED` | Moderation auto-reject | 422 | Choose different photo |
| `INVALID_DATA_URL` | Malformed base64 | 400 | Report bug (shouldn't happen) |
| `UPLOAD_FAILED` | Blob storage error | 500 | Retry entire upload |
| `MODERATION_TIMEOUT` | Content Safety API down | 503 | Retry in 1 minute |

---

## Design Philosophy

### Why Moderation Matters

**Catfishing protection:** Stock photos and fake identities destroy trust.  
**Safety:** NSFW content has no place in a dating app's public profiles.  
**Quality bar:** Low-effort profiles indicate low intent.

### Moderation vs. Censorship

**We moderate FOR:**
- Safety (NSFW, violent content)
- Authenticity (stock photos, celebrity pics)
- Quality (blurry, group shots for primary photo)

**We DON'T moderate FOR:**
- Attractiveness (subjective, problematic)
- Body type (discriminatory)
- Style choices (tattoos, piercings, clothing)

**Philosophy:** The platform protects users from harm, not from each other's preferences.

---

## Performance Considerations

### Base64 Overhead

**Data URL encoding:**
- Original: 2 MB photo
- Base64: 2 MB × 1.33 = 2.67 MB
- JSON payload: ~2.7 MB

**6 photos:** 16 MB → 21 MB encoded

**Impact:**
- Longer upload time
- Higher bandwidth cost
- Larger request payload
- Client memory pressure (FileReader loads all into RAM)

**Solution:** Direct blob upload eliminates encoding step.

### Moderation Latency

**Azure Content Safety API:** ~500ms average, 2s p99

**During onboarding:**
- User uploads 3-6 photos
- Moderation runs sequentially (could parallelize)
- Total: 1.5s - 12s added to onboarding

**Not blocking:** User sees "Uploading photos..." spinner.

**Optimization:** Pre-upload moderation (check data URL before blob upload) to fail faster.

---

## Migration to Direct Upload

### Implementation Plan

**Phase 1: Add new endpoint**
```
POST /media/upload-token (container: profile-photo)
POST /media/confirm
```
Already exists, just needs frontend integration.

**Phase 2: Update onboarding UI**
- Replace FileReader → data URL with MediaRecorder pattern
- Call upload-token → blob PUT → confirm
- Maintain same UX (6-step flow, captions, preview)

**Phase 3: Deprecate old endpoint**
- Mark `PUT /onboarding/photos` as deprecated
- Support both flows for 1 release cycle
- Remove old endpoint

**Breaking change:** Mobile apps must update before old endpoint removal.

---

## Testing

### Manual Test Flow

**Onboarding:**
1. Navigate to `/onboarding/photos`
2. Upload 3 valid photos (JPEG, < 10 MB)
3. Add captions
4. Verify preview shows correctly
5. Click "Save and Continue"
6. Check `User.PhotoUrl` in DB
7. Verify blob storage has 3 files

**Moderation rejection:**
1. Upload NSFW image (test mode flag)
2. Expect "PHOTO_REJECTED" error
3. Verify blob was deleted
4. Verify `User.PhotoUrl` unchanged

**Moderation escalation:**
1. Upload stock photo
2. Expect upload succeeds
3. Check trust score decreased
4. Verify `ModerationQueue` has entry

### Automated Tests

**Unit tests needed:**
- Data URL parsing (valid/invalid formats)
- Blob path generation (userId prefix)
- Moderation result handling
- Photo count validation (3-6 range)

**Integration tests needed:**
- Full upload flow (FileReader → API → blob)
- Moderation pipeline (mock Azure Content Safety)
- Primary photo selection logic
- Error recovery (retry on 500)

---

## Monitoring & Metrics

### Key Metrics

- **Upload success rate:** % of uploads that complete moderation
- **Rejection rate:** % auto-rejected (target: < 5%)
- **Escalation rate:** % flagged for review (target: < 2%)
- **Average upload time:** FileReader + API + moderation (target: < 8s for 3 photos)
- **Data URL payload size:** Monitor for oversized requests

### Logging

**MediaService logs:**
```
[Media] SAS token issued for {BlobPath}
[Media] Confirmed upload for {BlobPath}
[Media] ProfilePhoto auto-rejected for user {UserId}: {BlobPath}
[Media] ProfilePhoto escalated (catfish suspected) for user {UserId}: {BlobPath}
```

**Onboarding logs:**
```
[Onboarding] Photos saved | UserId={UserId} Count={Count}
```

### Alerts

- **High rejection rate** (> 10%): Possible moderation model drift
- **High escalation rate** (> 5%): Possible spam attack
- **Slow uploads** (p95 > 15s): Azure Blob performance issue
- **Failed moderations** (> 1%): Content Safety API down

---

## Related Documentation

- [Media System Overview](./README.md)
- [Voice Notes](./voice-notes.md) — Direct blob upload pattern (future for photos)
- [Azure Blob Storage](./azure-blob.md) — Storage configuration
- [SAS Tokens](./sas-tokens.md) — Security model
- [API Reference](./api.md) — All media endpoints

**Implementation Files:**
- `Services/MediaService.cs` — Blob upload & moderation
- `Services/Moderation/ModerationService.cs` — Image validation
- `Endpoints/OnboardingEndpoints.cs` — Photo upload endpoint
- `frontend/pages/onboarding/photos/` — Upload UI
