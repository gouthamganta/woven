# Encrypted Fields

**Last Updated:** 2026-10-07  
**Status:** Planned (EncryptionService exists, not yet wired to production)

---

## Overview

This document catalogs all database fields that are (or will be) encrypted at rest using AES-256-GCM. Encryption is reserved for **highly sensitive personal data** where database compromise would reveal private user information.

**Key principle:** Only encrypt fields that meet BOTH criteria:
1. **High sensitivity** — Leaking this data would harm user privacy or safety
2. **Low query frequency** — Field is not used in WHERE clauses or joins (encryption breaks indexing)

---

## Encrypted Fields

### 1. Intent Reflection Sentence

**Table:** `user_intents`  
**Column:** `reflection_sentence`  
**Type:** TEXT  
**Status:** ⚠️ **PLANNED (not yet encrypted)**

**Why encrypt?**
- Most personal freeform text in the app
- Reveals vulnerabilities, relationship history, values
- Examples:
  - "Someone who understands my anxiety and doesn't judge me."
  - "A partner who wants kids and shares my faith."
  - "Just someone to laugh with — I'm tired of serious relationships."

**Usage:**
- **Write:** Onboarding flow (`PUT /onboarding/intent`)
- **Read:** Match explanation generation (MatchExplanationService)
- **Query:** Never used in WHERE clause (no need for indexing)

**Implementation status:**
```csharp
// Current (PLAINTEXT):
intent.ReflectionSentence = req.ReflectionSentence.Trim();

// Planned (ENCRYPTED):
intent.ReflectionSentence = _encryption.Encrypt(req.ReflectionSentence.Trim());
```

**Migration required:** Yes (encrypt existing plaintext rows).

---

## Fields NOT Encrypted

### Why These Fields Are Plaintext

#### 1. Primary Intent (`user_intents.primary_intent`)

**Why not encrypted?**
- Categorical data (4 values: `long_term`, `short_term`, `casual`, `exploring`)
- Low sensitivity (public-facing profile information)
- Used in matchmaking queries (`WHERE primary_intent IN (...)`)
- Encrypting would break SQL indexing

**Example:**
```sql
SELECT user_id FROM user_intents
WHERE primary_intent = 'long_term'
AND age BETWEEN 25 AND 35;
-- Needs index on primary_intent (impossible if encrypted)
```

---

#### 2. Openness Array (`user_intents.openness_json`)

**Why not encrypted?**
- Multi-select from predefined list ("Open to slow burn", "Open to casual", etc.)
- Low sensitivity (optional, not deeply personal)
- Used for matchmaking filters

**Example value:**
```json
["Open to slow burn", "Open to casual"]
```

**Risk assessment:** Low (attacker learns user is open to casual dating — not a privacy breach).

---

#### 3. Foundational Answers (`user_foundational_question_sets.answers_json`)

**Why not encrypted?**
- Already analyzed by OpenAI for pillar scoring (AI sees plaintext)
- Used for vector similarity search (embeddings computed from answers)
- High query frequency (matchmaking pipeline reads for every candidate)

**Data example:**
```json
{
  "q1": "I'm most alive when I'm exploring new places or ideas.",
  "q2": "I value deep conversations over small talk."
}
```

**Risk mitigation:** Answers are not shown to other users (only AI pillar scores are used).

---

#### 4. Chat Messages (`chat_messages.content`)

**Why not encrypted?**
- Needs real-time display (decrypting every message on chat load = slow)
- High query frequency (paginated reads, search)
- Messages are between matched users (both consented to share)

**Workaround for privacy:** Messages auto-delete after 90 days (ephemeral, not permanent record).

---

#### 5. Profile Data (Name, Bio, Photos)

**Why not encrypted?**
- Shown to other users in Moments cards (public-facing)
- Used in search/filtering (e.g., "users in Bangalore")
- Encrypting would break app functionality

**Risk assessment:** Low (users expect profile data to be visible to matches).

---

## Future Candidates for Encryption

### 1. Phone Number (Verification)

**Table:** `user_phone_verifications`  
**Column:** `phone_number`  
**Current:** Plaintext (hashed with salt)  
**Future:** Encrypt before storage, hash only for uniqueness check

**Why encrypt?**
- Phone numbers are PII (GDPR/CCPA protected)
- Leaking phone numbers enables harassment, doxxing

**Challenge:** Uniqueness constraint (can't index encrypted field).

**Solution:** Store both:
- `phone_number_hash` (SHA-256, indexed for uniqueness)
- `phone_number_encrypted` (AES-256-GCM, for admin recovery)

**Implementation:**
```csharp
var phoneHash = PiiSanitizer.HashForAudit(phoneNumber, salt);
var phoneEncrypted = _encryption.Encrypt(phoneNumber);

var verification = new UserPhoneVerification
{
    PhoneNumberHash = phoneHash,      // For uniqueness check
    PhoneNumberEncrypted = phoneEncrypted,  // For admin support
    // ...
};
```

---

### 2. Email Address

**Table:** `users`  
**Column:** `email`  
**Current:** Plaintext  
**Future:** Encrypted (same dual-storage strategy as phone)

**Why encrypt?**
- Email is PII
- Leaking emails enables phishing, spam

**Challenge:** Login requires looking up user by email.

**Solution:**
```csharp
var emailHash = PiiSanitizer.HashForAudit(email, salt);
var emailEncrypted = _encryption.Encrypt(email);

// Lookup by hash
var user = await _db.Users.FirstOrDefaultAsync(u => u.EmailHash == emailHash);

// Decrypt for display in admin panel
var plainEmail = _encryption.Decrypt(user.EmailEncrypted);
```

---

### 3. Location Coordinates (Precise GPS)

**Table:** `user_locations`  
**Column:** `lat`, `lng`  
**Current:** Decimal (plaintext)  
**Future:** Coarse-grained only (city-level), precise coords encrypted

**Why?**
- Precise GPS reveals home/work address (stalking risk)
- Matchmaking only needs city-level ("within 10 km")

**Implementation:**
- Store `city_id` (integer, indexed) for matchmaking
- Store encrypted `(lat, lng)` for venue recommendations
- Never expose precise coords to frontend

---

### 4. Date Venue Choices

**Table:** `match_date_ideas`  
**Column:** `venue_id`, `custom_location`  
**Current:** Plaintext  
**Future:** Encrypt custom locations (user-entered addresses)

**Why?**
- Custom venue text may reveal home address ("my apartment", "123 Main St")
- Pre-selected `venue_id` is safe (references public venue DB)

**Implementation:**
```csharp
if (req.VenueId != null)
{
    // Safe: public venue
    dateIdea.VenueId = req.VenueId;
}
else if (!string.IsNullOrEmpty(req.CustomLocation))
{
    // Encrypt user-entered address
    dateIdea.CustomLocationEncrypted = _encryption.Encrypt(req.CustomLocation);
}
```

---

## Encryption Decision Matrix

| Field | Sensitivity | Query Freq | Encrypt? | Reason |
|-------|-------------|------------|----------|--------|
| Intent reflection | **High** | Low | ✅ **Yes** | Deeply personal, no WHERE clause |
| Phone number | **High** | Low | 📋 **Future** | PII, dual-storage (hash + encrypt) |
| Email | **Medium** | Medium | 📋 **Future** | PII, dual-storage |
| Precise GPS | **High** | Low | 📋 **Future** | Stalking risk, use city-level |
| Custom venue text | **Medium** | Low | 📋 **Future** | May contain home address |
| Primary intent | Low | High | ❌ No | Categorical, needed for queries |
| Openness array | Low | High | ❌ No | Multi-select, low sensitivity |
| Foundational answers | Medium | High | ❌ No | Analyzed by AI, vector search |
| Chat messages | Medium | High | ❌ No | Real-time display, pagination |
| Profile bio | Low | High | ❌ No | Public-facing, search |

**Legend:**
- ✅ **Yes** — Currently planned/implemented
- 📋 **Future** — Under consideration
- ❌ **No** — Excluded (low sensitivity or high query frequency)

---

## Query Impact Analysis

### Problem: Encrypted Fields Can't Be Indexed

**SQL limitation:**
```sql
-- This WORKS (plaintext):
CREATE INDEX idx_primary_intent ON user_intents(primary_intent);
SELECT * FROM user_intents WHERE primary_intent = 'long_term';

-- This FAILS (encrypted):
SELECT * FROM user_intents WHERE reflection_sentence = 'looking for love';
-- Can't index encrypted field (every row is unique ciphertext)
```

**Why?** Encryption output is random (different ciphertext for same plaintext due to random nonce).

---

### Solution: Dual Storage (Hash + Encrypt)

**For fields needing both searchability and encryption:**

**Example: Phone number**
```sql
CREATE TABLE user_phone_verifications (
    phone_number_hash TEXT NOT NULL UNIQUE,  -- For uniqueness check
    phone_number_encrypted TEXT NOT NULL,    -- For admin recovery
    -- ...
);

CREATE INDEX idx_phone_hash ON user_phone_verifications(phone_number_hash);
```

**Lookup:**
```csharp
var hash = PiiSanitizer.HashForAudit(phoneNumber, salt);
var existing = await _db.PhoneVerifications
    .FirstOrDefaultAsync(p => p.PhoneNumberHash == hash);
```

**Recovery (admin only):**
```csharp
var plaintext = _encryption.Decrypt(existing.PhoneNumberEncrypted);
```

**Why this works:**
- Hash is deterministic (same input → same hash) → can index
- Encrypt is for storage security (leak reveals hash, not plaintext)
- Hash + salt prevents rainbow table attacks

---

### When NOT to Use Dual Storage

**If field needs:**
- **Prefix search** (`LIKE 'John%'`) → Encryption impossible (use tokenization)
- **Range queries** (`WHERE age BETWEEN 25 AND 35`) → Store age in plaintext (low sensitivity)
- **Full-text search** → Use separate search index (Elasticsearch) with encrypted source

---

## Migration Strategy

**When encryption is enabled for an existing field:**

### Step 1: Add Encrypted Column

```sql
ALTER TABLE user_intents
ADD COLUMN reflection_sentence_encrypted TEXT;
```

**Why separate column?** Allows gradual migration (old plaintext + new encrypted coexist).

---

### Step 2: Backfill Encrypted Values

```csharp
var intents = await _db.UserIntents
    .Where(x => x.ReflectionSentenceEncrypted == null)
    .ToListAsync();

foreach (var intent in intents)
{
    if (!string.IsNullOrEmpty(intent.ReflectionSentence))
    {
        intent.ReflectionSentenceEncrypted = _encryption.Encrypt(intent.ReflectionSentence);
    }
}

await _db.SaveChangesAsync();
```

**Run as:** Background job (process 1000 rows per batch).

---

### Step 3: Switch Application Code

```csharp
// Old code (read plaintext):
var reflection = intent.ReflectionSentence;

// New code (read encrypted):
var reflection = _encryption.Decrypt(intent.ReflectionSentenceEncrypted);
```

**Deploy:** Blue-green deployment (new pods read encrypted, old pods read plaintext).

---

### Step 4: Drop Plaintext Column

```sql
ALTER TABLE user_intents DROP COLUMN reflection_sentence;
ALTER TABLE user_intents RENAME COLUMN reflection_sentence_encrypted TO reflection_sentence;
```

**When?** After 7-day observation period (ensure no decryption errors).

---

## PII Handling in Other Layers

### Outbound HTTP (External APIs)

**File:** `Infrastructure/OutboundPiiHandler.cs`

**Problem:** Prevent accidentally leaking user IDs/emails in HTTP headers to OpenAI, analytics, etc.

**Solution:** Strip PII headers before outbound calls.

```csharp
private static readonly string[] _blockedHeaders = ["X-User-Id", "X-UserId", "User-Id"];

protected override async Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request, CancellationToken ct)
{
    foreach (var header in _blockedHeaders)
    {
        if (request.Headers.Contains(header))
        {
            _logger.LogWarning("[OutboundPii] Stripped header {Header}", header);
            request.Headers.Remove(header);
        }
    }

    // Add anonymous correlation token instead
    request.Headers.TryAddWithoutValidation("X-Anonymous-Token", GenerateToken());

    return await base.SendAsync(request, ct);
}
```

**Result:** External services see `X-Anonymous-Token: a3f9b2c1...` (random, unlinkable to user).

---

### AI Prompts (OpenAI)

**File:** `Services/Security/PiiSanitizer.cs`

**Problem:** User might paste email/phone in foundational answers or chat messages.

**Solution:** Regex-based redaction before sending to OpenAI.

```csharp
public static string SanitizeForAi(string input)
{
    var result = EmailPattern().Replace(input, "[email]");
    result = PhonePattern().Replace(result, "[phone]");
    result = HandlePattern().Replace(result, "[handle]");
    result = AddressPattern().Replace(result, "their area");
    return result;
}
```

**Before:**
```
"Contact me at john@example.com or call 555-1234. I live at 123 Main St."
```

**After:**
```
"Contact me at [email] or call [phone]. I live in their area."
```

**Trade-off:** Loses semantic info, but prevents PII leak to OpenAI logs.

---

### Audit Logs (IP Addresses)

**File:** `Services/Security/SecurityAuditService.cs`

**Problem:** Storing raw IP addresses in audit logs is PII (GDPR issue).

**Solution:** Hash IP before storage.

```csharp
var ipHash = ipAddress != null
    ? PiiSanitizer.HashForAudit(ipAddress, salt)
    : null;

db.SecurityAuditLogs.Add(new SecurityAuditLog
{
    IpHash = ipHash,  // SHA-256(ip + salt)
    // ...
});
```

**Why hash?** Can still detect suspicious patterns (same IP hash → same user) without storing PII.

---

### Photo EXIF Data

**File:** `Services/Security/PiiSanitizer.cs`

**Problem:** Photo EXIF contains GPS coords, device info, timestamps.

**Solution:** Strip EXIF before upload to Azure Blob.

```csharp
public static byte[] StripExif(byte[] jpeg)
{
    // Parse JPEG segments
    // Skip APP1 (0xE1 = EXIF/XMP) and APP2 (0xE2 = ICC profile)
    // Keep image data, quantization tables, Huffman tables
    // Return stripped JPEG
}
```

**What's removed:**
- GPS coordinates (lat/lng)
- Device make/model (iPhone 14 Pro Max)
- Timestamp (2026-04-15 14:32:01)
- Software version (iOS 17.4)

**What's kept:**
- Image pixels (visual content)
- Orientation flag (rotate correctly)
- Color profile (sRGB)

---

## Compliance Mapping

### GDPR Article 32 (Security of Processing)

**Requirement:**
> "Implement appropriate technical measures... including... pseudonymisation and encryption of personal data."

**Woven's implementation:**
- ✅ Encryption: AES-256-GCM for intent reflections
- ✅ Pseudonymisation: Hashed IPs, phone numbers
- ✅ Access control: Master key in Azure Key Vault (RBAC)

---

### CCPA § 1798.81.5 (Reasonable Security)

**Requirement:**
> "Businesses that own, license, or maintain personal information about a California resident shall implement reasonable security procedures."

**Woven's implementation:**
- ✅ Encryption at rest (database)
- ✅ Encryption in transit (HTTPS, TLS 1.3)
- ✅ PII sanitization (outbound HTTP, AI prompts)
- ✅ Audit logging (all key access events)

---

### HIPAA (Not Applicable, But Best Practice)

**Why mentioned?** Woven is not a healthcare app, but HIPAA's encryption standards are industry best practice.

**HIPAA Technical Safeguards:**
- ✅ **§164.312(a)(2)(iv)** — Encryption and decryption (AES-256)
- ✅ **§164.312(e)(2)(ii)** — Encryption in transit (TLS 1.3)
- ✅ **§164.308(a)(1)(ii)(D)** — Audit controls (SecurityAuditService)

---

## Testing Encrypted Fields

### Unit Test: Roundtrip Encryption

```csharp
[Fact]
public void ReflectionSentence_EncryptDecrypt_Roundtrip()
{
    var plaintext = "Someone who values honesty.";
    var ciphertext = _encryption.Encrypt(plaintext);
    var decrypted = _encryption.Decrypt(ciphertext);

    Assert.NotEqual(plaintext, ciphertext);  // Ciphertext differs
    Assert.Equal(plaintext, decrypted);      // Roundtrip works
}
```

---

### Integration Test: Store & Retrieve

```csharp
[Fact]
public async Task Intent_StoreEncrypted_RetrieveDecrypted()
{
    var userId = 123;
    var reflection = "Looking for a life partner.";

    // Store encrypted
    var intent = new UserIntent
    {
        UserId = userId,
        ReflectionSentence = _encryption.Encrypt(reflection)
    };
    _db.UserIntents.Add(intent);
    await _db.SaveChangesAsync();

    // Retrieve and decrypt
    var stored = await _db.UserIntents.FirstAsync(x => x.UserId == userId);
    var decrypted = _encryption.Decrypt(stored.ReflectionSentence);

    Assert.Equal(reflection, decrypted);
}
```

---

### Security Test: Verify Ciphertext Changes

```csharp
[Fact]
public void Encrypt_SamePlaintext_DifferentCiphertext()
{
    var plaintext = "test";
    var ciphertext1 = _encryption.Encrypt(plaintext);
    var ciphertext2 = _encryption.Encrypt(plaintext);

    // Nonce randomness ensures different ciphertext
    Assert.NotEqual(ciphertext1, ciphertext2);
}
```

---

## Summary

**Encrypted fields (current + planned):**
- ✅ Intent reflection sentence (planned, not wired)
- 📋 Phone number (future: dual hash + encrypt)
- 📋 Email (future: dual hash + encrypt)
- 📋 Precise GPS (future: coarse city + encrypted coords)
- 📋 Custom venue text (future: encrypt user-entered addresses)

**Excluded fields (plaintext):**
- Primary intent, openness array (categorical, low sensitivity)
- Foundational answers (AI-analyzed, vector search)
- Chat messages (real-time display, pagination)
- Profile data (public-facing)

**Decision criteria:**
1. **High sensitivity?** → Encrypt
2. **Needs indexing/search?** → Dual storage (hash + encrypt) or exclude
3. **Real-time display?** → Exclude (decrypt on every read = slow)

**Compliance:** GDPR Article 32, CCPA § 1798.81.5 compliance via encryption + pseudonymisation.
