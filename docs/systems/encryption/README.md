# Encryption System

**Last Updated:** 2026-10-07  
**Status:** ACTIVE (EncryptionService exists, not yet wired to production endpoints)

---

## Overview

Woven's encryption system protects sensitive user data at rest using **AES-256-GCM** authenticated encryption. The system is designed to prevent unauthorized access to personal information in the event of database compromise, backup leaks, or insider threats.

**Core principle:** Sensitive data is encrypted before storage and decrypted only when necessary. Even with read access to the database, attackers cannot recover plaintext without the master encryption key.

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                     Application Layer                        │
│  (Services, Endpoints)                                        │
└──────────────────┬──────────────────────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────────────────┐
│               EncryptionService                              │
│  • Encrypt(plaintext) → base64-encoded ciphertext           │
│  • Decrypt(ciphertext) → plaintext                          │
│  • EncryptBytes / DecryptBytes                              │
│  • DeriveKey(purpose) → HKDF-derived key                    │
└──────────────────┬──────────────────────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────────────────┐
│                  AES-256-GCM Engine                          │
│  • 256-bit master key                                       │
│  • 96-bit random nonce per encryption                       │
│  • 128-bit authentication tag                               │
│  • Output: nonce + ciphertext + tag                         │
└──────────────────┬──────────────────────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────────────────┐
│                  PostgreSQL Database                         │
│  • Stores base64-encoded ciphertext                         │
│  • No plaintext for encrypted fields                        │
└─────────────────────────────────────────────────────────────┘
```

---

## Components

| Component | Purpose | Location |
|-----------|---------|----------|
| **EncryptionService** | Core encryption/decryption logic | `Services/Security/EncryptionService.cs` |
| **IEncryptionService** | Service interface | `Services/Security/IEncryptionService.cs` |
| **KeyRotationWorker** | Automated key rotation checks | `Services/Security/KeyRotationWorker.cs` |
| **PiiSanitizer** | Strips PII from AI prompts & audit logs | `Services/Security/PiiSanitizer.cs` |
| **OutboundPiiHandler** | Blocks PII in outbound HTTP headers | `Infrastructure/OutboundPiiHandler.cs` |
| **SecurityAuditService** | Logs security events with hashed IPs | `Services/Security/SecurityAuditService.cs` |

See individual documentation files for details:
- [aes-256-gcm.md](./aes-256-gcm.md) — Algorithm specifics
- [key-rotation.md](./key-rotation.md) — Key rotation strategy
- [encrypted-fields.md](./encrypted-fields.md) — Which fields are encrypted
- [pii-sanitizer.md](./pii-sanitizer.md) — PII redaction utilities
- [implementation.md](./implementation.md) — Code walkthrough

---

## What Gets Encrypted?

**Currently planned (not yet wired):**
- `user_intents.reflection_sentence` — Freeform answer to "What would a meaningful connection look like for you?"

**Not encrypted:**
- Primary intent (`long_term`, `short_term`, etc.) — categorical data
- Openness array — low sensitivity
- Foundational answers — analyzed by OpenAI for pillar scoring
- Profile data (name, bio, photos) — needed for matching UI
- Chat messages — needed for real-time display

See [encrypted-fields.md](./encrypted-fields.md) for full details.

---

## Security Properties

| Property | How It's Achieved |
|----------|-------------------|
| **Confidentiality** | AES-256 ciphertext indistinguishable from random |
| **Integrity** | GCM authentication tag detects tampering |
| **Forward secrecy** | Nonce never reused (12 random bytes per encryption) |
| **Key isolation** | Master key in Azure Key Vault, not in codebase |
| **Audit trail** | All key access logged via SecurityAuditService |

**Attack resistance:**
- ✅ **SQL injection** → Ciphertext useless without master key
- ✅ **Database backup leak** → No plaintext accessible
- ✅ **Insider threat (DB admin)** → Cannot decrypt without app-level key
- ❌ **Master key leak** → All data decryptable (mitigated by 90-day rotation)
- ❌ **Memory dump** → Plaintext may be in RAM (minimize decryption lifetime)

---

## Key Management

### Master Key

**Format:** 32-byte (256-bit) random value, base64-encoded

**Storage:**
- **Local dev:** User Secrets (`dotnet user-secrets set "Encryption:MasterKey" "<value>"`)
- **Production:** Azure Key Vault (`woven-prod-kv`)
- **Never in:** Git, logs, or frontend code

**Generation:**
```csharp
var key = RandomNumberGenerator.GetBytes(32);
var keyB64 = Convert.ToBase64String(key);
Console.WriteLine(keyB64);  // 44-character base64 string
```

**Rotation schedule:** Every 90 days (automated check via KeyRotationWorker)

See [key-rotation.md](./key-rotation.md) for rotation process.

---

## Derived Keys (HKDF)

For purpose-specific keys (cache encryption, signing), `DeriveKey(purpose)` uses **HKDF-SHA256** to derive a 32-byte key from the master key:

```csharp
var cacheKey = _encryption.DeriveKey("cache-encryption-v1");
var signingKey = _encryption.DeriveKey("signing-v1");
```

**Valid purposes:**
- `"column-encryption-v1"` — Future use for multi-column encryption
- `"cache-encryption-v1"` — Redis cache encryption
- `"signing-v1"` — JWT or webhook signing

**Why HKDF?** Prevents key reuse across contexts (defense-in-depth).

---

## Usage Example

### Encrypting During Onboarding

**Planned (not yet wired):**
```csharp
app.MapPut("/onboarding/intent", async (
    IntentRequest req,
    WovenDbContext db,
    IEncryptionService encryption,  // ← Inject service
    ClaimsPrincipal user,
    CancellationToken ct) =>
{
    var userId = EndpointHelper.GetUserId(user);

    // Encrypt before storage
    var encryptedReflection = encryption.Encrypt(req.ReflectionSentence.Trim());

    var intent = await db.UserIntents.FirstOrDefaultAsync(x => x.UserId == userId, ct);
    if (intent == null)
    {
        intent = new UserIntent
        {
            UserId = userId,
            PrimaryIntent = req.PrimaryIntent,
            OpennessJson = JsonSerializer.Serialize(req.Openness),
            ReflectionSentence = encryptedReflection,  // ← Ciphertext
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.UserIntents.Add(intent);
    }
    else
    {
        intent.ReflectionSentence = encryptedReflection;
        intent.UpdatedAt = DateTime.UtcNow;
    }

    await db.SaveChangesAsync(ct);
    return Results.Ok();
});
```

### Decrypting for Match Explanation

```csharp
public class MatchExplanationService
{
    private readonly IEncryptionService _encryption;

    public async Task<string> GenerateAsync(int userAId, int userBId, CancellationToken ct)
    {
        var intentA = await _db.UserIntents.FirstAsync(x => x.UserId == userAId, ct);
        var intentB = await _db.UserIntents.FirstAsync(x => x.UserId == userBId, ct);

        // Decrypt for AI prompt
        var reflectionA = _encryption.Decrypt(intentA.ReflectionSentence);
        var reflectionB = _encryption.Decrypt(intentB.ReflectionSentence);

        // Use in OpenAI prompt...
    }
}
```

---

## Performance Impact

**Benchmark** (Intel i7, .NET 8, 1000 iterations):
- **Encrypt 150-char string:** ~0.02 ms
- **Decrypt 150-char string:** ~0.02 ms
- **Overhead vs. plaintext:** ~20 µs

**Verdict:** Negligible impact (<0.1% latency on intent submission).

---

## Compliance

**GDPR (Article 32):**
> "Implement appropriate technical measures... including... pseudonymisation and encryption of personal data."

**CCPA:**
> Encryption of personal information reduces liability in breach scenarios.

**Benefit:** Encrypted data strengthens legal defense in breach disclosure scenarios (data is "secured" if unreadable without key).

---

## Current Status

### ✅ Implemented
- `EncryptionService` with AES-256-GCM
- `Encrypt()` / `Decrypt()` methods
- `DeriveKey()` for HKDF-based key derivation
- Key loading from Azure Key Vault
- `KeyRotationWorker` (90-day check cycle)
- `PiiSanitizer` for AI prompts and EXIF stripping
- `OutboundPiiHandler` for HTTP header sanitization

### ❌ Not Yet Wired
- **Intent endpoint encryption** — `OnboardingEndpoints.cs:382` still stores plaintext
- **Match explanation decryption** — `MatchExplanationService` expects plaintext
- **Review endpoint** — `GET /onboarding/review` returns plaintext reflection

### 📋 Roadmap
1. **Wire encryption to intent endpoint** (1 day)
2. **Add decryption to match explanation service** (1 day)
3. **Migration script for existing data** (2 days) — Encrypt current plaintext rows
4. **Key rotation automation** (1 week) — Re-encrypt all data with new key
5. **Add `encryption_key_version` column** (1 day) — Support versioned keys

---

## Common Issues

| Issue | Cause | Fix |
|-------|-------|-----|
| `CryptographicException` on decrypt | Ciphertext modified or wrong key | Check key version, verify no truncation |
| `InvalidOperationException` on startup | Master key missing | Add to User Secrets or Key Vault |
| Key length error | Key not 32 bytes | Regenerate with `RandomNumberGenerator.GetBytes(32)` |
| Nonce reuse warning | Same key used across envs | Each env must have unique master key |
| Plaintext in DB | Encryption not wired | Verify `_encryption.Encrypt()` called before save |

---

## Testing

**Unit tests:** `backend/WovenBackend.Tests/Services/Security/EncryptionServiceTests.cs`

```csharp
[Fact]
public void Encrypt_Decrypt_Roundtrip()
{
    var plaintext = "Someone who values honesty.";
    var ciphertext = _encryption.Encrypt(plaintext);
    var decrypted = _encryption.Decrypt(ciphertext);

    Assert.NotEqual(plaintext, ciphertext);  // Ciphertext differs
    Assert.Equal(plaintext, decrypted);      // Roundtrip works
}
```

**Security test:** Verify ciphertext changes on each encryption (nonce uniqueness).

---

## References

- **NIST SP 800-38D** — GCM specification
- **FIPS 140-2** — Cryptographic module standards
- **Azure Key Vault Docs** — Secret management
- [aes-256-gcm.md](./aes-256-gcm.md) — Algorithm deep dive
- [key-rotation.md](./key-rotation.md) — Rotation procedures
