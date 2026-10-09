# Trust Score Algorithm

**Last Updated:** 2026-10-07  
**Component:** Behavioral Trust Scoring  
**Status:** Production

---

## Overview

**TrustScore** is a 0.0–1.0 float that measures account authenticity and behavioral quality. It is **NOT** a match compatibility score — it is a **safety filter** that removes low-quality accounts from candidate pools.

**Critical threshold:** `0.25`  
Users below this score are **filtered out of all candidate pools** — they cannot appear in anyone's deck, cannot receive moments, and cannot be matched.

**Default:** `0.5` (new user baseline)

---

## Scoring Formula

### Starting State (New User)
```
TrustScore = 0.50 (default)
```

### Bot Detection (One-Time, At Onboarding Exit)
```
score = DefaultTrust (0.50)

if (has UserProfile)                    → score += 0.15
if (has FoundationalQuestionAnswers)    → score += 0.10
if (has uploaded Photo)                 → score += 0.05

TrustScore = clamp(score, 0.0, 1.0)
```

**Example:**
```
New user completes onboarding:
- UserProfile created      → +0.15
- Foundational answers     → +0.10
- 1 photo uploaded         → +0.05
───────────────────────────────────
Final TrustScore: 0.50 + 0.30 = 0.80
```

---

### Penalties (Continuous, Post-Onboarding)

#### 1. Multi-Account Detection
**Trigger:** Same device fingerprint used by 2+ different users  
**Penalty:** `-0.20`

**Implementation:**
```csharp
var hash = PiiSanitizer.HashForAudit(fingerprint, "fp-v1");
var cacheKey = $"fp:{hash}";

var recorded = await _cache.GetAsync<string>(cacheKey, ct);
if (recorded != null && int.TryParse(recorded, out var existingUserId) && existingUserId != userId)
{
    user.TrustScore = Math.Max(0.0f, user.TrustScore - 0.20f);
    user.TrustUpdatedAt = DateTime.UtcNow;
}

// Record this user's claim on the fingerprint (30-day TTL)
await _cache.SetAsync(cacheKey, userId.ToString(), TimeSpan.FromDays(30), ct);
```

**Cache Storage:**
- Key: `fp:{sha256_hash}` (fingerprint hashed via `PiiSanitizer`, never raw)
- Value: `userId` (integer as string)
- TTL: 30 days (renews on each login)

**Edge Cases:**
- **Shared device (family, cafe):** Legitimate users may share fingerprints. Penalty only applies once per collision.
- **Fingerprint rotation:** After 30 days, fingerprint expires from cache → no penalty on next login.

---

#### 2. Velocity Abuse
**Trigger:** User posts >10 tiles in 1 hour  
**Penalty:** `-0.10`

**Implementation:**
```csharp
var cutoff = DateTimeOffset.UtcNow.AddHours(-1);
var recentTiles = await _db.Tiles
    .CountAsync(t => t.UserId == userId && t.CreatedAt >= cutoff, ct);

if (recentTiles > 10)
{
    user.TrustScore = Math.Max(0.0f, user.TrustScore - 0.10f);
    user.TrustUpdatedAt = DateTime.UtcNow;
}
```

**Rationale:**
- 10 tiles/hour = 1 tile every 6 minutes (reasonable)
- >10 tiles/hour = bot or spam behavior

**Frequency:** Checked on every tile post (POST `/tiles`)

---

#### 3. Manual Flag (Admin Action)
**Trigger:** Admin flags user via `/admin/flag-user`  
**Penalty:** Custom (configurable per flag reason)

**Implementation:**
```csharp
await _trust.FlagAsync(userId, reason, penaltyScore: 0.15f, ct);
// Sets TrustScore = min(current, penaltyScore)
```

**Example Flag Reasons:**
- `spam_content` → `0.15` (keeps user at/below threshold)
- `harassment` → `0.10` (hard filter)
- `catfish_suspected` → `0.20` (soft filter, requires investigation)

**Effect:**
```csharp
user.TrustScore = Math.Min(user.TrustScore, penaltyScore);
```
(Never raises TrustScore — always a penalty or no-op)

---

## Threshold Logic

### Candidate Pool Filtering (SQL-Level)
```sql
-- CandidatePoolService.cs (GetCandidatesAsync)
SELECT u.id, u.full_name, u.profile_photo
FROM users u
WHERE u.id != @userId
  AND u.trust_score >= 0.25         -- HARD FILTER
  AND u.gender = @reciprocalGender
  -- ... other filters
ORDER BY u.last_active_at DESC
LIMIT 1000;
```

**Effect:**
- TrustScore `0.24` → **NEVER** appears in candidate pools (no matches, no moments sent/received)
- TrustScore `0.25` → Eligible (minimum acceptable quality)
- TrustScore `0.80` → No benefit over `0.25` (trust is binary pass/fail)

---

## State Transitions

### Scenario 1: Legitimate New User
```
Day 1 (Onboarding Exit):
  TrustScore: 0.50 + 0.30 (bot detection bonuses) = 0.80
  Status: ✓ Eligible for matches (0.80 > 0.25)

Day 7 (First Login from New Device):
  TrustScore: 0.80 (no fingerprint collision)
  Status: ✓ Eligible

Day 30 (Posts 12 tiles in 1 hour):
  TrustScore: 0.80 - 0.10 (velocity penalty) = 0.70
  Status: ✓ Eligible (0.70 > 0.25)
```

---

### Scenario 2: Multi-Account Abuser
```
Day 1 (Onboarding Exit, Account A):
  TrustScore: 0.80
  Fingerprint: abc123 → Redis: fp:hash(abc123) = userA
  Status: ✓ Eligible

Day 2 (Onboarding Exit, Account B, Same Device):
  TrustScore: 0.80 (initial)
  Fingerprint: abc123 → Redis collision detected (fp:hash(abc123) = userA)
  Penalty Applied: 0.80 - 0.20 = 0.60
  Status: ✓ Eligible (0.60 > 0.25)

Day 3 (Account C, Same Device):
  TrustScore: 0.80 (initial)
  Fingerprint: abc123 → Redis collision detected (fp:hash(abc123) = userB, last write)
  Penalty Applied: 0.80 - 0.20 = 0.60
  Status: ✓ Eligible (0.60 > 0.25)
```

**Issue:** Multi-account penalty alone does NOT filter users below threshold.  
**Mitigation:** Combine with manual flag if pattern detected.

---

### Scenario 3: Bot / Spam Account
```
Day 1 (Onboarding):
  - No profile created
  - No foundational answers
  - No photo uploaded
  TrustScore: 0.50 (default, no bonuses)
  Status: ✓ Eligible (0.50 > 0.25)

Day 1 (Posts 50 tiles in 1 hour):
  TrustScore: 0.50 - 0.10 (velocity penalty) = 0.40
  Status: ✓ Eligible (0.40 > 0.25)

Day 1 (Admin flags "spam_content"):
  TrustScore: min(0.40, 0.15) = 0.15
  Status: ✗ FILTERED (0.15 < 0.25)
```

---

### Scenario 4: Flagged User Recovery
```
Day 1: TrustScore = 0.15 (flagged for spam)
  Status: ✗ FILTERED (0.15 < 0.25)

Day 7: Admin manual review → "false positive, legitimate user"
  Admin action: SET TrustScore = 0.80
  Status: ✓ Eligible (0.80 > 0.25)
```

**Admin Endpoint (Not Yet Built):**
```http
POST /admin/set-trust-score
{
  "userId": 123,
  "newScore": 0.80,
  "reason": "False positive spam flag — user verified"
}
```

---

## Database Schema

### Users Table (Trust Fields)
```sql
ALTER TABLE users ADD COLUMN trust_score FLOAT DEFAULT 0.5;
ALTER TABLE users ADD COLUMN trust_updated_at TIMESTAMPTZ;

CREATE INDEX idx_users_trust_score ON users(trust_score);
CREATE INDEX idx_users_trust_updated ON users(trust_updated_at);
```

**Query Patterns:**
```sql
-- Candidate pool filtering (production query)
SELECT * FROM users
WHERE trust_score >= 0.25
  AND gender = 'F'
ORDER BY last_active_at DESC
LIMIT 1000;

-- Admin dashboard: Low-trust users
SELECT id, email, full_name, trust_score, trust_updated_at
FROM users
WHERE trust_score < 0.30
ORDER BY trust_score ASC
LIMIT 100;
```

---

## API Methods (TrustService)

### 1. CheckDeviceFingerprintAsync
**Trigger:** User login (POST `/auth/login`)  
**Purpose:** Detect multi-account abuse

```csharp
public async Task CheckDeviceFingerprintAsync(int userId, string? fingerprint, CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(fingerprint)) return;

    var hash = PiiSanitizer.HashForAudit(fingerprint, "fp-v1");
    var cacheKey = $"fp:{hash}";

    var recorded = await _cache.GetAsync<string>(cacheKey, ct);
    if (recorded != null && int.TryParse(recorded, out var existingUserId) && existingUserId != userId)
    {
        // Same fingerprint, different user → penalty
        user.TrustScore = Math.Max(0.0f, user.TrustScore - 0.20f);
        user.TrustUpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _audit.Log("suspicious_pattern", userId: userId, 
            resourceType: "DeviceFingerprint", resourceId: hash[..8]);
    }

    await _cache.SetAsync(cacheKey, userId.ToString(), TimeSpan.FromDays(30), ct);
}
```

---

### 2. CheckVelocityAsync
**Trigger:** Tile post (POST `/tiles`)  
**Purpose:** Detect spam / bot posting

```csharp
public async Task CheckVelocityAsync(int userId, CancellationToken ct)
{
    var cutoff = DateTimeOffset.UtcNow.AddHours(-1);
    var recentTiles = await _db.Tiles
        .CountAsync(t => t.UserId == userId && t.CreatedAt >= cutoff, ct);

    if (recentTiles <= 10) return;

    var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
    if (user is null) return;

    user.TrustScore = Math.Max(0.0f, user.TrustScore - 0.10f);
    user.TrustUpdatedAt = DateTime.UtcNow;
    await _db.SaveChangesAsync(ct);
}
```

---

### 3. RunBotDetectionAsync
**Trigger:** Onboarding exit (POST `/onboarding/complete`)  
**Purpose:** Reward profile completion

```csharp
public async Task RunBotDetectionAsync(int userId, CancellationToken ct)
{
    var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
    if (user is null) return;

    float score = 0.50f; // DefaultTrust

    var hasProfile = await _db.UserProfiles.AnyAsync(p => p.UserId == userId, ct);
    if (hasProfile) score += 0.15f;

    var hasAnswers = await _db.UserFoundationalQuestionSets
        .AnyAsync(q => q.UserId == userId && q.AnsweredAt != null, ct);
    if (hasAnswers) score += 0.10f;

    var hasPhoto = await _db.UserPhotos.AnyAsync(p => p.UserId == userId, ct);
    if (hasPhoto) score += 0.05f;

    user.TrustScore = Math.Clamp(score, 0.0f, 1.0f);
    user.TrustUpdatedAt = DateTime.UtcNow;
    await _db.SaveChangesAsync(ct);
}
```

---

### 4. FlagAsync (Admin Action)
**Trigger:** Admin dashboard (POST `/admin/flag-user`)  
**Purpose:** Manual penalty for policy violations

```csharp
public async Task FlagAsync(int userId, string reason, float penaltyScore, CancellationToken ct)
{
    var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
    if (user is null) return;

    user.TrustScore = Math.Min(user.TrustScore, penaltyScore);
    user.TrustUpdatedAt = DateTime.UtcNow;
    await _db.SaveChangesAsync(ct);

    _audit.Log("trust_flag", userId: userId, 
        resourceType: "TrustScore", resourceId: reason);
}
```

---

### 5. GetTrustScoreAsync
**Trigger:** Admin dashboard queries, candidate pool filtering  
**Purpose:** Read-only score retrieval

```csharp
public async Task<float> GetTrustScoreAsync(int userId, CancellationToken ct)
{
    return await _db.Users.AsNoTracking()
        .Where(u => u.Id == userId)
        .Select(u => u.TrustScore)
        .FirstOrDefaultAsync(ct);
}
```

---

### 6. IsTrustedEnoughAsync
**Trigger:** Pre-flight checks (e.g., before allowing tile post, match action)  
**Purpose:** Binary pass/fail check

```csharp
public async Task<bool> IsTrustedEnoughAsync(int userId, CancellationToken ct)
{
    var score = await GetTrustScoreAsync(userId, ct);
    return score >= 0.25f;
}
```

---

## Monitoring

### Key Metrics
- `trust_score_distribution` — histogram (0.0–0.25, 0.25–0.50, 0.50–0.75, 0.75–1.0)
- `low_trust_filtered_count` — users with TrustScore < 0.25 (daily snapshot)
- `multi_account_detections_per_day` — fingerprint collisions
- `velocity_penalties_per_day` — tile spam detections
- `manual_flags_per_day` — admin actions
- `avg_trust_score_new_users` — avg TrustScore at onboarding exit (target: 0.70–0.85)

### Alerts
- `low_trust_filtered_count > 10% active users` → investigate penalty calibration
- `avg_trust_score_new_users < 0.50` → investigate bot detection logic
- `multi_account_detections_per_day > 100` → investigate device fingerprint collisions (shared devices)

---

## Calibration History

### Phase 2B (Initial Tuning, 2026-05-17)
```
TrustThreshold: 0.25
DefaultTrust: 0.50
ProfileCompletionBonus: 0.15
AnsweredQuestionsBonus: 0.10
HasPhotoBonus: 0.05
MultiAccountPenalty: 0.20
VelocityPenalty: 0.10
```

**Observed Behavior:**
- 95% of legitimate users: 0.70–0.85 (onboarding exit)
- 98% pass threshold (0.25) on first day
- 2% flagged for multi-account or velocity abuse

**No changes made** — thresholds working as intended.

---

## Edge Cases

### Case 1: User logs in from 2 devices (phone + laptop)
**Effect:** Different fingerprints → no penalty  
**Rationale:** Device fingerprints are device-specific, not user-specific.

### Case 2: User borrows friend's phone to log in
**Effect:** Friend's fingerprint already in Redis → multi-account penalty (-0.20)  
**Mitigation:** Manual admin review if user reports false flag.

### Case 3: User completes onboarding, but TrustScore = 0.50 (no bonuses)
**Root Cause:** No profile, no answers, no photo → bot detection bonuses not applied  
**Effect:** Still eligible (0.50 > 0.25), but lower quality signal  
**Future:** Require profile completion before onboarding exit (Phase 6B)

### Case 4: Admin flags user, TrustScore drops to 0.15, then admin reverses flag
**Current Behavior:** No automatic recovery — TrustScore stays at 0.15  
**Mitigation:** Admin must manually SET TrustScore to 0.80 via admin endpoint  
**Future:** Add "reverse flag" action (Phase 6C)

---

## Security Considerations

### 1. Device Fingerprint Hashing
**Why:** Fingerprints are PII (browser config, screen resolution, installed fonts).  
**Implementation:** `PiiSanitizer.HashForAudit(fingerprint, "fp-v1")` before Redis storage.  
**Storage:** Only SHA256 hash stored, never raw fingerprint.

### 2. PII Audit Logging
**Event:** `suspicious_pattern` logged on every multi-account detection  
**Fields:** `userId`, `resourceType: "DeviceFingerprint"`, `resourceId: hash[..8]` (first 8 chars of hash)

### 3. Fingerprint TTL (30 Days)
**Why:** Shared devices (family, cafe) should not permanently block legitimate users.  
**Effect:** After 30 days, fingerprint expires → no penalty on next login.

---

## Related Documentation

- [README.md](./README.md) — Trust & Verification system overview
- [verification.md](./verification.md) — Verified badge system (separate from TrustScore)
- [anti-ghosting.md](./anti-ghosting.md) — GhostScore (separate from TrustScore)
- [../matchmaking/candidate-pool.md](../matchmaking/candidate-pool.md) — SQL filtering by TrustScore
- [../security/pii-sanitization.md](../security/pii-sanitization.md) — Fingerprint hashing

---

## Future Enhancements

### Phase 6A: Admin Dashboard
- View low-trust users (TrustScore < 0.30)
- Manual flag/unflag actions
- Trust score history timeline

### Phase 6B: Stricter Onboarding
- Require profile completion before onboarding exit
- Raise default bonuses to push avg TrustScore → 0.85

### Phase 6C: Automatic Recovery
- Reverse flag action (admin)
- Decay penalties over time (e.g., -0.10 penalty decays by +0.02/week)
