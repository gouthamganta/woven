# Trust & Verification System

**Last Updated:** 2026-10-07  
**Status:** Production  
**Owner:** Platform Safety Team

---

## Overview

The Trust & Verification system is Woven's multi-layered defense against fraud, catfishing, and low-quality user behavior. It combines **identity verification**, **behavioral scoring**, and **anti-ghosting mechanisms** to maintain a high-trust community.

Unlike traditional dating apps that rely solely on photo verification, Woven's approach is **behavioral-first**. Trust is earned through authentic engagement, not just a selfie.

---

## System Architecture

```
┌─────────────────────────────────────────────────────────┐
│                   Trust & Verification                   │
├─────────────────────────────────────────────────────────┤
│                                                          │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  │
│  │   Identity   │  │ Behavioral   │  │     Anti-    │  │
│  │ Verification │  │Trust Scoring │  │   Ghosting   │  │
│  └──────────────┘  └──────────────┘  └──────────────┘  │
│         │                  │                  │         │
│         ├─────────────────┬┴──────────────────┤         │
│         │                 ▼                   │         │
│         │         ┌──────────────┐            │         │
│         │         │ User.        │            │         │
│         │         │ - TrustScore │            │         │
│         │         │ - GhostScore │            │         │
│         │         │ - IsVerified │            │         │
│         │         └──────────────┘            │         │
│         │                                     │         │
│         ▼                                     ▼         │
│  ┌──────────────────────────────────────────────────┐  │
│  │          Candidate Pool Filtering                │  │
│  │  - TrustScore ≥ 0.25 → eligible for matches      │  │
│  │  - GhostScore → delivery boost penalty           │  │
│  │  - IsVerified → badge in UI, no filter          │  │
│  └──────────────────────────────────────────────────┘  │
│                                                          │
└─────────────────────────────────────────────────────────┘
```

---

## Components

### 1. Identity Verification

**What:** Photo selfie verification using CLIP embeddings  
**Badge:** ✓ (shown on Moments cards, profiles)  
**Effect:** Social proof only — verified users are NOT boosted in ECHO  

**Process:**
- User submits live selfie
- CLIP embeddings compare selfie to existing profile photos
- Cosine similarity ≥ 0.70 → verified
- 5 attempts per day rate limit

**File:** `verification.md`, `photo-verification.md`

---

### 2. Trust Scoring

**What:** Behavioral trust score (0.0–1.0, default 0.5)  
**Threshold:** 0.25 — users below this are **filtered from candidate pools**  
**Purpose:** Bot detection, multi-account detection, velocity abuse

**Signals:**
- **Bot detection** (profile completion, foundational answers, photos) → +0.30
- **Multi-account** (same device fingerprint, different user) → -0.20
- **Velocity abuse** (>10 tiles/hour) → -0.10
- **Manual flag** (admin action) → custom penalty

**File:** `trust-score.md`

---

### 3. Anti-Ghosting

**What:** Detects one-sided conversations, refunds sparks, calculates GhostScore  
**Purpose:** Protect users from wasted effort, surface responsiveness in ECHO

**Mechanisms:**
- **Silent thread detection** — 24h no reply → 0.5 spark refund + push notification
- **Balloon expiry nudges** — 24h before balloon expires → push to both users
- **GhostScore calculation** — (replied + 1) / (received + 2), 90-day lookback, nightly update

**Effect:**
- GhostScore < 0.3 → delivery penalty in `DeliveryBoostService`
- Low GhostScore users see fewer of their moments reach their recipients
- 0.5 spark refund on all 3 unmatch close paths (BLOCK, END, EXPIRE + no messages)

**File:** `anti-ghosting.md`

---

## User Journey

### New User (Day 1)
```
TrustScore: 0.50 (default)
GhostScore: 0.50 (default)
IsVerified: false

Action → Effect
- Complete profile + foundational questions + upload photo → TrustScore: 0.80
- Still eligible for matches (0.80 > 0.25)
- No verification badge yet
```

### Verified User (Day 7)
```
TrustScore: 0.80
GhostScore: 0.50 (no match history yet)
IsVerified: false

Action → Effect
- Submit selfie → cosine similarity 0.75 → IsVerified: true
- ✓ badge appears on all Moments cards
- No ECHO boost (verification is social proof, not a ranking signal)
```

### Power User (Day 30)
```
TrustScore: 0.80
GhostScore: 0.90 (replies to 9/10 first messages)
IsVerified: true

Effect:
- High delivery rate (GhostScore 0.90 → no penalty)
- Verified badge on all cards
- Eligible for all candidate pools
```

### Flagged User (Day 2)
```
TrustScore: 0.50 → 0.20 (multi-account detection)
GhostScore: 0.50
IsVerified: false

Effect:
- TrustScore 0.20 < 0.25 → FILTERED OUT OF ALL CANDIDATE POOLS
- Cannot appear in anyone's deck
- No matches until TrustScore recovers
```

---

## Database Schema

### Users Table (Trust Fields)
```sql
ALTER TABLE users ADD COLUMN trust_score FLOAT DEFAULT 0.5;
ALTER TABLE users ADD COLUMN trust_updated_at TIMESTAMPTZ;
ALTER TABLE users ADD COLUMN ghost_score FLOAT DEFAULT 0.5;
ALTER TABLE users ADD COLUMN last_active_at TIMESTAMPTZ;
ALTER TABLE users ADD COLUMN is_verified BOOLEAN DEFAULT FALSE;
ALTER TABLE users ADD COLUMN verified_at TIMESTAMPTZ;
ALTER TABLE users ADD COLUMN verification_type VARCHAR(20);
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

### PhotoEmbeddings Table (used for verification)
```sql
CREATE TABLE photo_embeddings (
    id SERIAL PRIMARY KEY,
    user_id INT NOT NULL,
    photo_url TEXT NOT NULL,
    embedding VECTOR(512),              -- CLIP ViT-L/14 embeddings
    embedded_at TIMESTAMPTZ NOT NULL
);
CREATE INDEX idx_pe_user ON photo_embeddings(user_id);
```

---

## API Endpoints

### Verification
```http
POST /verification/selfie
Authorization: Bearer <jwt>
Content-Type: application/json

{
  "blobPath": "123/selfie_20261007.jpg"
}

Response 200 OK:
{
  "success": true,
  "verified": true,
  "error": null
}

Response 429 Too Many Requests:
Retry-After: 86400
```

```http
GET /verification/status
Authorization: Bearer <jwt>

Response 200 OK:
{
  "isVerified": true,
  "verifiedAt": "2026-10-07T14:32:00Z",
  "verificationType": "selfie",
  "latestAttempt": {
    "id": "uuid",
    "type": "selfie",
    "status": "verified",
    "submittedAt": "2026-10-07T14:32:00Z",
    "verifiedAt": "2026-10-07T14:32:10Z",
    "failureReason": null
  }
}
```

---

## Background Workers

### GhostDetectionWorker
- **Frequency:** Every 6 hours
- **Tasks:**
  - `ProcessSilentThreadsAsync()` — refund sparks for 24h+ silent threads
  - `ProcessExpiringBalloonsAsync()` — nudge users 24h before balloon expires
  - `UpdateGhostScoresAsync()` — nightly at 03:30 UTC (90-day lookback)

**Run Mode:**
- Disabled via `WOVEN_DISABLE_BATCH_WORKERS=true` on web pods
- Runs on dedicated worker pods

---

## Configuration

### TrustService Constants
```csharp
TrustThreshold = 0.25f;          // Minimum trust to appear in candidate pools
DefaultTrust = 0.50f;            // New user starting score
VelocityTileLimit = 10;          // Max tiles/hour before penalty
VelocityPenalty = 0.10f;         // Penalty for velocity abuse
MultiAccountPenalty = 0.20f;     // Penalty for device fingerprint collision
ProfileCompletionBonus = 0.15f;  // Reward for complete profile
AnsweredQuestionsBonus = 0.10f;  // Reward for foundational answers
HasPhotoBonus = 0.05f;           // Reward for uploaded photo
```

### VerificationService Constants
```csharp
SimilarityThreshold = 0.70;      // Cosine similarity threshold for selfie verification
DailyAttemptLimit = 5;           // Max verification attempts per day
```

### GhostDetectionService Constants
```csharp
SilentThresholdHours = 24;       // Hours before spark refund
RefundKeyTtlDays = 3;            // Cache TTL for refund deduplication
```

---

## Key Design Decisions

### 1. Verification is NOT a ranking signal
**Why:** We don't want unverified users to be invisible. Verification is social proof, not a filter.  
**Effect:** IsVerified only affects badge display, never ECHO scoring.

### 2. TrustScore is a HARD filter at 0.25
**Why:** Low-trust users (bots, multi-accounts, velocity abusers) must be removed from pools entirely.  
**Effect:** No half-measures — below threshold = no matches.

### 3. GhostScore is a DELIVERY penalty, not a pool filter
**Why:** Ghosting is about responsiveness, not account validity. Low GhostScore users can still receive moments, but their sent moments have lower delivery rates.  
**Effect:** Soft penalty on delivery, not hard filter on eligibility.

### 4. 0.5 spark refund on ALL unmatch paths with zero messages
**Why:** Users should never lose a full spark on a match that went nowhere.  
**Paths:** BLOCK, END (no_spark), balloon EXPIRE (no messages exchanged)

### 5. Fingerprint hashing for multi-account detection
**Why:** Device fingerprints are PII. Never store raw.  
**Implementation:** `PiiSanitizer.HashForAudit(fingerprint, "fp-v1")` before Redis storage.

---

## Monitoring

### Key Metrics
- `trust_score_distribution` — histogram of TrustScore across active users
- `ghost_score_distribution` — histogram of GhostScore across active users
- `verification_attempt_rate` — daily selfie submission count
- `verification_success_rate` — % of selfies that pass similarity threshold
- `spark_refund_count` — total refunds issued in last 24h
- `low_trust_filtered_count` — users filtered from candidate pools (TrustScore < 0.25)

### Alerts
- `verification_success_rate < 0.40` → investigate CLIP endpoint or similarity threshold
- `spark_refund_count > 500/day` → investigate ghosting patterns or balloon TTL
- `low_trust_filtered_count > 10% active users` → investigate TrustScore penalties

---

## Related Documentation

- [verification.md](./verification.md) — Verified badge system
- [trust-score.md](./trust-score.md) — Trust scoring algorithm
- [photo-verification.md](./photo-verification.md) — Photo verification flow
- [anti-ghosting.md](./anti-ghosting.md) — Ghost detection & spark refunds
- [implementation.md](./implementation.md) — Service implementation details
- [../matchmaking/candidate-pool.md](../matchmaking/candidate-pool.md) — Trust filtering in ECHO
- [../../features/sparks/refunds.md](../../features/sparks/refunds.md) — Spark refund logic

---

## Migration Notes

### Phase 2B (Trust Scoring) — 2026-05-17
- Added `trust_score`, `trust_updated_at` to `users` table
- Wired `TrustService` to `CandidatePoolService` (SQL filtering)
- Default 0.5, threshold 0.25

### Phase 4A (Anti-Ghosting) — 2026-05-17
- Added `ghost_score`, `last_active_at` to `users` table
- Created `GhostDetectionWorker` (6h interval + nightly score update)
- Wired spark refunds to 3 unmatch close paths

### Phase 5A (Identity Verification) — 2026-05-24
- Created `user_verifications` table
- Added `is_verified`, `verified_at`, `verification_type` to `users` table
- Wired `VerificationService` with CLIP embeddings (cosine similarity ≥ 0.70)
- 5 attempts/day rate limit

---

## Production Checklist

- [ ] `Replicate:ApiToken` configured in Key Vault
- [ ] `GhostDetectionWorker` disabled on web pods (`WOVEN_DISABLE_BATCH_WORKERS=true`)
- [ ] `GhostDetectionWorker` enabled on worker pods (env flag unset)
- [ ] `TrustService` wired to `CandidatePoolService` (SQL: `trust_score >= 0.25`)
- [ ] `DeliveryBoostService` penalizes `ghost_score < 0.3`
- [ ] Verification badge (✓) wired to Moments card UI
- [ ] Spark refund wired to all 3 unmatch close paths
- [ ] Redis fingerprint hashing (never raw PII)
- [ ] Photo EXIF stripping before embedding (`PhotoEmbeddingService.StripExif()`)

---

**Next Steps:**
1. Add horoscope to verification flow (Phase 5B — not yet built)
2. Add government ID verification (Phase 5C — not yet built)
3. Add Trust Score to admin dashboard (Phase 6A — not yet built)
