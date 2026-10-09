# Encryption

**Consolidated from:** `docs/technical/ENCRYPTION_SECURITY_DESIGN.md`

---

## Overview

Woven uses **AES-256-GCM** for field-level encryption of PII. Encryption and decryption are handled exclusively by `EncryptionService` via EF Core value converters.

---

## Encryption Algorithm

**Algorithm:** AES-256-GCM (Galois/Counter Mode)  
**Key Size:** 256 bits  
**Nonce:** 96 bits (fresh random per encryption)  
**Tag:** 128 bits (authentication tag)

**Why GCM:**
- Authenticated encryption (prevents tampering)
- Fast (hardware-accelerated on modern CPUs)
- NIST-approved
- Parallelizable

---

## Encrypted Fields

| Table | Column | Notes |
|---|---|---|
| `users` | `email` | Encrypted on registration and every update |
| `users` | `full_name` | Encrypted on registration and every update |
| `user_profiles` | `city` | Encrypted at profile creation/update |
| `user_profiles` | `state` | Encrypted at profile creation/update |
| `user_intents` | `reflection_sentence` | Encrypted at write |
| `user_optional_fields` | `value` | Encrypted at write (covers all optional freetext fields) |

**Source:** `backend/WovenBackend/Data/WovenDbContext.cs` (EncryptedStringConverter registration)

---

## Fields NOT Encrypted

| Table | Column | Reason |
|---|---|---|
| `chat_messages` | `body` | `CHECK` constraint enforces 1-1000 character length. AES-256-GCM ciphertext is longer than plaintext, violating constraint. |
| pgvector columns | All vector columns | `EncryptedStringConverter` is string→string; pgvector stores float[] (type incompatibility) |
| `user_photos` | `url` | Public Azure Blob URLs (no sensitive data) |

**Documented gap:** `chat_messages.body` encryption requires migration to widen/drop CHECK constraint first.

**Source:** `docs/technical/ENCRYPTION_SECURITY_DESIGN.md`

---

## Implementation

### EncryptionService

**Interface:**
```csharp
public interface IEncryptionService
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
}
```

**Usage:**
```csharp
// Encrypt
var encrypted = _encryption.Encrypt(user.Email);

// Decrypt
var plaintext = _encryption.Decrypt(user.Email);
```

**Source:** `backend/WovenBackend/Services/EncryptionService.cs`

---

### EF Core Value Converter

**Registration:**
```csharp
// WovenDbContext.OnModelCreating
builder.Entity<User>()
    .Property(u => u.Email)
    .HasConversion(new EncryptedStringConverter(_encryptionService))
    .HasColumnName("email");
```

**Transparent encryption:**
- **Write:** Plaintext → Encrypt → Database
- **Read:** Database → Decrypt → Plaintext

**No code changes required** in endpoints or services (encryption is transparent).

---

### Ciphertext Format

**Storage format:**
```
<nonce>:<ciphertext>:<tag>
```

**Example:**
```
a1b2c3d4e5f6g7h8i9j0k1l2:4m5n6o7p8q9r0s...:1a2b3c4d5e6f7g8h
```

**Base64-encoded** for database storage (compatible with PostgreSQL `text` columns).

---

## Key Management

### Master Key

**Storage:**
- **Production:** Azure Key Vault
- **Local dev:** .NET User Secrets

**Configuration:**
```json
{
  "Encryption": {
    "MasterKey": "<base64-encoded-256-bit-key>"
  }
}
```

**Never:**
- Hardcoded in source
- Checked into Git
- Stored in appsettings.json
- Logged or exposed in errors

---

### Key Rotation

**Worker:** `KeyRotationWorker` (background service)

**Process:**
1. Load new key from config
2. Query all encrypted fields
3. Decrypt with old key
4. Encrypt with new key
5. Update database
6. Mark rotation complete

**Schedule:** Configurable (recommended: quarterly)

**Source:** `backend/WovenBackend/Services/KeyRotationWorker.cs`

**Status:** Implemented but not yet scheduled in production.

---

## Security Properties

### Confidentiality

**AES-256-GCM provides:**
- Strong confidentiality (256-bit key space)
- Semantic security (same plaintext → different ciphertext due to random nonce)
- No patterns in ciphertext

---

### Authenticity

**GCM mode provides:**
- Authentication tag (128-bit)
- Tamper detection (modified ciphertext fails decryption)
- Prevents ciphertext manipulation

**Verification:**
```csharp
// AesGcm.Decrypt throws CryptographicException if tag invalid
try
{
    aes.Decrypt(nonce, ciphertext, tag, plaintext);
}
catch (CryptographicException)
{
    throw new Exception("Ciphertext has been tampered with");
}
```

---

## Threat Model

### Protected Against

✅ **Database breach** — Attacker gets ciphertext but no key  
✅ **SQL injection** — Even if attacker exfiltrates data, it's encrypted  
✅ **Insider threat** — DB admins see ciphertext only  
✅ **Backup theft** — Backups contain encrypted data

### NOT Protected Against

❌ **Application compromise** — Attacker with code execution can decrypt  
❌ **Memory dumps** — Plaintext exists in RAM after decryption  
❌ **Key compromise** — If master key leaked, all data decryptable

---

## Best Practices

1. **Encrypt at application layer** — Not at database layer (transparent to app)
2. **Fresh nonce per encryption** — Never reuse nonces
3. **Store tag with ciphertext** — Required for decryption
4. **Rotate keys periodically** — Limit exposure window
5. **Never log ciphertext** — Prevents correlation attacks
6. **Minimal plaintext exposure** — Decrypt only when needed

---

## Performance Impact

**Overhead:**
- **Encryption:** ~10μs per field (negligible)
- **Decryption:** ~10μs per field (negligible)
- **Hardware-accelerated** on modern CPUs (AES-NI)

**Query impact:**
- **Cannot index encrypted fields** (ciphertext is random)
- **Cannot query by encrypted fields** (e.g., `WHERE email = 'x@y.com'` won't work)
- **Use separate hash columns for lookups** (e.g., `email_hash`)

---

## Migration Strategy

**Adding encryption to existing field:**

1. Add new encrypted column
2. Backfill: read plaintext → encrypt → write
3. Dual-write period (write to both columns)
4. Switch reads to encrypted column
5. Drop plaintext column

**Example migration:**
```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<string>(
        name: "email_encrypted",
        table: "users",
        nullable: true);
    
    // Backfill via application code (not SQL)
}
```

---

## Compliance

**Supports:**
- GDPR Article 32 (Security of processing)
- CCPA § 1798.81.5 (Reasonable security)
- HIPAA § 164.312(a)(2)(iv) (Encryption and decryption)

**Note:** Encryption alone is not sufficient for compliance. Must be combined with access controls, audit logging, and breach notification procedures.

---

**Last Updated:** 2026-10-07  
**Source Files:**
- `backend/WovenBackend/Services/EncryptionService.cs`
- `backend/WovenBackend/Services/KeyRotationWorker.cs`
- `backend/WovenBackend/Data/WovenDbContext.cs`
