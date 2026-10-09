# Verified Badge System

**Last Updated:** 2026-10-07  
**Component:** Identity Verification  
**Status:** Production

---

## Overview

The **Verified Badge** (✓) is a social proof signal that confirms a user's photos match their live selfie. It appears on:
- Moments cards (next to name)
- Match profiles
- Chat headers

**Critical constraint:** Verification is **NOT** a ranking signal in ECHO. Verified users do NOT get boosted in matching, delivery, or candidate pool selection. The badge is purely **social proof** for other users to see.

---

## User Flow

### 1. Trigger
**Where:** Settings page → "Get Verified" button  
**Precondition:** User must have at least 1 profile photo uploaded

### 2. Selfie Submission
**UI:**
- Camera capture (mobile: native camera, web: MediaRecorder)
- "Take a selfie matching your profile photos" instruction
- Live preview + retake button

**Validation:**
- Photo must be uploaded to user's Azure Blob folder (`{userId}/selfie_{timestamp}.jpg`)
- Blob path ownership check: `blobPath.StartsWith($"{userId}/")`
- 5 attempts per day rate limit (429 response with `Retry-After` header)

**API:**
```http
POST /verification/selfie
Authorization: Bearer <jwt>
Content-Type: application/json

{
  "blobPath": "123/selfie_20261007_143200.jpg"
}
```

### 3. Backend Processing (VerificationService)

**Step 1:** Check existing profile photo embeddings
```csharp
var existingEmbeddingIds = await _db.PhotoEmbeddings
    .Where(e => e.UserId == userId)
    .Select(e => e.Id)
    .ToListAsync(ct);

if (existingEmbeddingIds.Count == 0)
    return new VerificationResult(false, null, "Upload profile photos first");
```

**Step 2:** Embed the selfie via CLIP (Replicate API)
```csharp
var selfieEmbeddingId = await _photoEmbedding.EmbedPhotoAsync(userId, blobPath, ct);
// PhotoEmbeddingService:
// - Downloads image bytes from blob URL
// - Strips EXIF metadata (removes GPS, PII)
// - Encodes as base64
// - Calls Replicate CLIP (ViT-L/14, 512-dim vector)
// - Stores embedding in photo_embeddings table
```

**Step 3:** Create pending verification row
```csharp
var verification = new UserVerification
{
    UserId = userId,
    Type = "selfie",
    Status = "pending",
    SubmittedAt = DateTimeOffset.UtcNow
};
_db.UserVerifications.Add(verification);
await _db.SaveChangesAsync(ct);
```

**Step 4:** Compute best cosine similarity
```csharp
var bestSimilarity = await _db.PhotoEmbeddings
    .Where(e => e.UserId == userId && existingEmbeddingIds.Contains(e.Id))
    .Select(e => 1.0 - (double)e.Embedding!.CosineDistance(selfieEmb))
    .OrderByDescending(s => s)
    .FirstOrDefaultAsync(ct);
```

**Step 5:** Apply threshold (0.70)
```csharp
if (bestSimilarity >= 0.70)
{
    user.IsVerified = true;
    user.VerifiedAt = DateTimeOffset.UtcNow;
    user.VerificationType = "selfie";

    verification.Status = "verified";
    verification.VerifiedAt = DateTimeOffset.UtcNow;
    
    _analytics.Track(userId, null, "VerificationCompleted", new { type = "selfie" });
}
else
{
    verification.Status = "failed";
    verification.FailureReason = "Selfie did not match profile photos";
    
    _analytics.Track(userId, null, "VerificationFailed", 
        new { type = "selfie", failureReason = "no_match" });
}
```

### 4. Response
**Success (similarity ≥ 0.70):**
```json
{
  "success": true,
  "verified": true,
  "error": null
}
```

**Failure (similarity < 0.70):**
```json
{
  "success": false,
  "verified": false,
  "error": "We couldn't match your selfie to your profile photos. Try better lighting or update your profile photos."
}
```

**Rate Limited (>5 attempts/day):**
```http
HTTP 429 Too Many Requests
Retry-After: 86400

{
  "error": "Too many verification attempts. Try again tomorrow."
}
```

---

## Database Schema

### Users Table (Verification Fields)
```sql
ALTER TABLE users ADD COLUMN is_verified BOOLEAN DEFAULT FALSE;
ALTER TABLE users ADD COLUMN verified_at TIMESTAMPTZ;
ALTER TABLE users ADD COLUMN verification_type VARCHAR(20);  -- 'selfie', 'govt_id' (future)
```

### UserVerifications Table
```sql
CREATE TABLE user_verifications (
    id UUID PRIMARY KEY,
    user_id INT NOT NULL,
    type VARCHAR(20) NOT NULL,          -- 'selfie'
    status VARCHAR(20) NOT NULL,        -- 'pending', 'verified', 'failed'
    submitted_at TIMESTAMPTZ NOT NULL,
    verified_at TIMESTAMPTZ,
    failure_reason VARCHAR(200)
);

CREATE INDEX idx_uv_user_submitted ON user_verifications(user_id, submitted_at DESC);
```

**Query pattern:**
```sql
-- Latest verification attempt for user 123
SELECT * FROM user_verifications
WHERE user_id = 123
ORDER BY submitted_at DESC
LIMIT 1;
```

---

## Badge Display Rules

### Moments Card
```html
<div class="moment-card">
  <div class="moment-header">
    <span class="name">Alex</span>
    <span class="verified-badge" *ngIf="moment.isVerified">✓</span>
  </div>
  <!-- ... -->
</div>
```

**CSS:**
```css
.verified-badge {
  color: var(--color-primary);
  font-weight: 600;
  margin-left: 4px;
  font-size: 18px;
}
```

### Match Profile
```html
<div class="profile-header">
  <h2>{{ profile.name }}
    <span class="verified-icon" *ngIf="profile.isVerified">✓</span>
  </h2>
</div>
```

### Chat Header
```html
<ion-toolbar>
  <ion-title>
    {{ match.partnerName }}
    <ion-icon name="checkmark-circle" *ngIf="match.partnerIsVerified"></ion-icon>
  </ion-title>
</ion-toolbar>
```

---

## Edge Cases

### Case 1: User deletes all profile photos after verification
**Effect:** Badge remains (IsVerified = true)  
**Rationale:** Verification is a point-in-time check. Re-verification is manual only.

### Case 2: User uploads new photos after verification
**Effect:** Badge remains  
**Rationale:** We trust the original verification. No automatic re-checks.

### Case 3: User attempts verification with no profile photos
**Response:**
```json
{
  "success": false,
  "verified": null,
  "error": "Upload profile photos first"
}
```

### Case 4: EXIF metadata contains GPS coordinates
**Mitigation:** `PhotoEmbeddingService.StripExif()` removes all APP1 (EXIF/XMP) segments before embedding.

### Case 5: Replicate API timeout or rate limit
**Response:**
```json
{
  "success": false,
  "verified": null,
  "error": "Could not process selfie"
}
```
**Retry:** User can retry immediately (counts against daily 5-attempt limit).

---

## API Reference

### POST /verification/selfie

**Headers:**
- `Authorization: Bearer <jwt>`
- `Content-Type: application/json`

**Request Body:**
```json
{
  "blobPath": "123/selfie_20261007_143200.jpg"
}
```

**Validation:**
- `blobPath` must start with `"{userId}/"` (ownership check)
- User must have at least 1 existing profile photo embedding
- Rate limit: 5 attempts per user per UTC day

**Response 200 OK (verified):**
```json
{
  "success": true,
  "verified": true,
  "error": null
}
```

**Response 200 OK (failed):**
```json
{
  "success": false,
  "verified": false,
  "error": "We couldn't match your selfie to your profile photos. Try better lighting or update your profile photos."
}
```

**Response 400 Bad Request:**
```json
{
  "error": "Invalid blob path"
}
```

**Response 429 Too Many Requests:**
```
Retry-After: 86400
```

**Response 500 Internal Server Error:**
```json
{
  "success": false,
  "verified": null,
  "error": "Could not process selfie"
}
```

---

### GET /verification/status

**Headers:**
- `Authorization: Bearer <jwt>`

**Response 200 OK:**
```json
{
  "isVerified": true,
  "verifiedAt": "2026-10-07T14:32:10Z",
  "verificationType": "selfie",
  "latestAttempt": {
    "id": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
    "type": "selfie",
    "status": "verified",
    "submittedAt": "2026-10-07T14:32:00Z",
    "verifiedAt": "2026-10-07T14:32:10Z",
    "failureReason": null
  }
}
```

**Response 200 OK (not verified, previous failure):**
```json
{
  "isVerified": false,
  "verifiedAt": null,
  "verificationType": null,
  "latestAttempt": {
    "id": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
    "type": "selfie",
    "status": "failed",
    "submittedAt": "2026-10-07T14:20:00Z",
    "verifiedAt": null,
    "failureReason": "Selfie did not match profile photos"
  }
}
```

**Response 200 OK (never attempted):**
```json
{
  "isVerified": false,
  "verifiedAt": null,
  "verificationType": null,
  "latestAttempt": null
}
```

---

## Monitoring

### Key Metrics
- `verification_attempts_per_day` — daily submission count
- `verification_success_rate` — % of selfies passing threshold (target: 60-80%)
- `verification_failure_reasons` — distribution: `no_match` vs `no_photos` vs `processing_error`
- `avg_similarity_score_verified` — average cosine similarity for verified users
- `avg_similarity_score_failed` — average cosine similarity for failed attempts

### Alerts
- `verification_success_rate < 40%` → investigate CLIP endpoint, lighting guidance, or threshold calibration
- `verification_success_rate > 95%` → investigate bot submissions (threshold may be too low)
- `processing_error_rate > 5%` → investigate Replicate API stability

---

## Known Issues

### Issue 1: Cosine similarity varies by lighting
**Impact:** Same person, different lighting → similarity 0.65 (below threshold)  
**Mitigation:** UI guidance: "Use natural lighting, face the camera directly"  
**Future:** Add lighting quality check before submission (pre-flight via browser-side face detection)

### Issue 2: CLIP embeddings are race-sensitive
**Impact:** CLIP ViT-L/14 encodes race/skin tone. Multiracial users may see lower similarity scores.  
**Mitigation:** Threshold tuned to 0.70 (aggressive enough to block catfish, permissive enough for lighting/angle variance)  
**Future:** Fine-tune CLIP on diverse selfie dataset (India-focused)

### Issue 3: Profile photo updated after verification
**Impact:** User verifies with photo A, then replaces with photo B (different person). Badge remains.  
**Mitigation:** Manual review only (admin flag system). No automatic re-verification.  
**Future:** Re-verify on profile photo change (Phase 5B)

---

## Rate Limiting

**Limit:** 5 attempts per user per UTC day  
**Cache Key:** `rl:verify:{userId}:{DateOnly}`  
**TTL:** Until midnight UTC (`CacheTtl.UntilMidnightUtc()`)  
**Reset:** Automatic at 00:00 UTC

**Implementation:**
```csharp
var rlKey = $"rl:verify:{userId}:{DateOnly.FromDateTime(DateTime.UtcNow)}";
var allowed = await cache.CheckRateLimitAsync(rlKey, 5, CacheTtl.UntilMidnightUtc(), ct);
if (!allowed)
{
    http.Response.Headers["Retry-After"] = ((int)CacheTtl.UntilMidnightUtc().TotalSeconds).ToString();
    return Results.StatusCode(429);
}
```

---

## Security Considerations

### 1. EXIF Stripping
**Why:** EXIF metadata can contain GPS coordinates, camera serial numbers, timestamps.  
**Implementation:** `PhotoEmbeddingService.StripExif()` removes all APP1 (0xFFE1) JPEG segments before embedding.

### 2. Blob Path Ownership Validation
**Why:** Prevent user A from submitting user B's photo as their selfie.  
**Implementation:**
```csharp
if (!req.BlobPath.StartsWith($"{userId}/", StringComparison.Ordinal))
    return Results.BadRequest(new { error = "Invalid blob path" });
```

### 3. PII Audit Logging
**Event:** `pii_access` logged on every successful verification  
**Fields:** `userId`, `service: "VerificationService"`, `resourceType: "selfie_verified"`, `piiStripped: true`

### 4. Embedding Storage
**Table:** `photo_embeddings` (CLIP vectors, 512-dim)  
**Retention:** Indefinite (required for future re-verification)  
**Access:** Read-only for VerificationService, CatfishDetectionService

---

## Future Enhancements

### Phase 5B (Horoscope Verification)
- Add horoscope field to onboarding (designed, not built)
- Cross-check horoscope with birthdate (detect lies)
- No separate badge — horoscope accuracy contributes to TrustScore

### Phase 5C (Government ID Verification)
- Aadhaar / PAN card upload (India-specific)
- OCR + face match via external KYC provider (e.g., IDfy, Onfido)
- "Government Verified" badge (separate from selfie badge)
- Premium tier only (not required for basic matching)

### Phase 5D (Re-verification on Photo Change)
- Trigger re-verification when user replaces profile photo
- Badge removed until new verification passes
- Grace period: 7 days to re-verify before badge removal

---

## Related Documentation

- [README.md](./README.md) — Trust & Verification system overview
- [photo-verification.md](./photo-verification.md) — Photo verification technical deep dive
- [trust-score.md](./trust-score.md) — Trust scoring algorithm
- [../security/pii-sanitization.md](../security/pii-sanitization.md) — EXIF stripping, PII handling

---

## Migration History

**Phase 5A (2026-05-24):**
- Created `user_verifications` table
- Added `is_verified`, `verified_at`, `verification_type` to `users` table
- Implemented `VerificationService` with CLIP embeddings
- Set threshold: cosine similarity ≥ 0.70
- Wired to frontend Settings page
