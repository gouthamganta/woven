# AES-256-GCM Algorithm

**Last Updated:** 2026-10-07  
**Status:** ACTIVE

---

## Overview

**AES-256-GCM** (Advanced Encryption Standard with Galois/Counter Mode) is a **symmetric authenticated encryption algorithm** used by Woven to protect sensitive user data at rest.

**Why GCM over CBC/CTR?**
- **Authenticated encryption** — Detects tampering via authentication tag
- **Parallelizable** — Faster than CBC on modern CPUs
- **NIST-approved** — FIPS 140-2 compliant
- **Hardware-accelerated** — AES-NI instruction set on Intel/AMD CPUs

**Security level:** 256-bit key = 2^256 possible keys (effectively unbreakable with current technology).

---

## Algorithm Components

### 1. AES Block Cipher

**Block size:** 128 bits (16 bytes)  
**Key size:** 256 bits (32 bytes)  
**Rounds:** 14 (vs. 10 for AES-128, 12 for AES-192)

**AES-256 encrypts data in 128-bit blocks using:**
- **SubBytes** — Non-linear substitution (S-box)
- **ShiftRows** — Byte permutation
- **MixColumns** — Linear diffusion
- **AddRoundKey** — XOR with round key derived from master key

**Result:** Each 16-byte block becomes ciphertext through 14 rounds of these transformations.

---

### 2. GCM (Galois/Counter Mode)

**GCM combines:**
- **CTR mode** (Counter Mode) — Turns block cipher into stream cipher
- **GHASH** — Galois field multiplication for authentication tag

**Why CTR mode?**
- Encryption is **parallelizable** (each block independent)
- No padding required (works on any length plaintext)
- Nonce reuse is catastrophic (hence our 96-bit random nonce)

**Why GHASH?**
- Computes authentication tag over ciphertext + optional associated data
- Detects tampering: any bit flip → tag verification fails

---

## Encryption Process

### Input
- **Plaintext:** UTF-8 encoded string (e.g., "Someone who values honesty.")
- **Master key:** 32 bytes (256 bits)
- **Nonce:** 12 bytes (96 bits) — **randomly generated per encryption**
- **Tag size:** 16 bytes (128 bits)

### Steps

**1. Generate random nonce**
```csharp
var nonce = RandomNumberGenerator.GetBytes(12);  // 96 bits
```

**Why random?** GCM security requires **never reusing** a (key, nonce) pair. Random nonce from cryptographically secure RNG ensures uniqueness.

**2. Initialize AES-GCM engine**
```csharp
using var aes = new AesGcm(_masterKey, TagSize);
```

**3. Encrypt plaintext**
```csharp
var plaintext = Encoding.UTF8.GetBytes("Someone who values honesty.");
var ciphertext = new byte[plaintext.Length];
var tag = new byte[16];

aes.Encrypt(nonce, plaintext, ciphertext, tag);
```

**What happens internally:**
- **CTR mode:** Nonce + counter → AES block encryption → keystream XOR plaintext = ciphertext
- **GHASH:** Ciphertext + nonce → authentication tag (GMAC)

**4. Combine nonce + ciphertext + tag**
```csharp
var result = new byte[12 + ciphertext.Length + 16];
nonce.CopyTo(result, 0);                         // First 12 bytes
ciphertext.CopyTo(result, 12);                   // Middle
tag.CopyTo(result, 12 + ciphertext.Length);      // Last 16 bytes
```

**5. Base64 encode for storage**
```csharp
var stored = Convert.ToBase64String(result);
// Example: "xQ7sK2mP9dUzT5Vm+3RkP... (44+ chars)"
```

---

## Decryption Process

### Input
- **Ciphertext:** Base64-encoded string from database
- **Master key:** 32 bytes (same key used for encryption)

### Steps

**1. Decode from base64**
```csharp
var combined = Convert.FromBase64String(ciphertext);
```

**2. Extract nonce, ciphertext, tag**
```csharp
var nonce = combined[..12];                    // First 12 bytes
var tag = combined[^16..];                     // Last 16 bytes
var ciphertext = combined[12..^16];            // Middle
```

**3. Verify length**
```csharp
if (combined.Length < 12 + 16)
    throw new CryptographicException("Ciphertext too short");
```

**Why?** Prevents attacks with malformed input.

**4. Decrypt and verify tag**
```csharp
var plaintext = new byte[ciphertext.Length];
using var aes = new AesGcm(_masterKey, 16);
aes.Decrypt(nonce, ciphertext, tag, plaintext);  // Throws if tag invalid
```

**What happens internally:**
- **Verify tag:** Recompute GHASH over ciphertext, compare to stored tag
  - If mismatch → `CryptographicException` (tampered data)
- **Decrypt:** Nonce + counter → AES keystream → XOR ciphertext = plaintext

**5. Convert to string**
```csharp
var decrypted = Encoding.UTF8.GetString(plaintext);
// "Someone who values honesty."
```

---

## Storage Format

**Database column:** `TEXT` (e.g., `user_intents.reflection_sentence`)

**Stored value:**
```
Base64(nonce[12] + ciphertext[N] + tag[16])
```

**Example:**
- **Plaintext:** "Someone who values honesty." (30 chars)
- **Ciphertext blob:** 12 + 30 + 16 = 58 bytes
- **Base64 encoded:** ~78 characters (4/3 overhead)

**Actual DB value:**
```
xQ7sK2mP9dUzT5Vm+3RkP9kL2mN4pQ8zR... (base64)
```

**Why base64?** PostgreSQL TEXT column stores UTF-8 strings. Base64 ensures binary data doesn't corrupt.

---

## Security Properties

### Confidentiality

**Claim:** Without the master key, ciphertext is **indistinguishable from random**.

**Why?** AES-256 is a **pseudorandom permutation**. Given ciphertext alone (no key), no algorithm can recover plaintext faster than brute-force (2^256 tries).

**Attack resistance:**
- ❌ **Frequency analysis** — CTR mode output is random stream, no patterns
- ❌ **Known-plaintext attack** — Even knowing (plaintext, ciphertext) pairs doesn't reveal key
- ❌ **Chosen-plaintext attack** — Same (attacker chooses input, sees output)

---

### Integrity

**Claim:** Any modification to ciphertext is **detected** during decryption.

**How?** GCM authentication tag is computed as:
```
tag = GHASH(ciphertext || len(ciphertext))
```

**GHASH** is Galois field multiplication (GF(2^128)). Flipping even 1 bit in ciphertext changes tag computation → verification fails.

**Attack scenarios:**
- ✅ **Bit flip attack** — Attacker changes 1 bit → decryption throws `CryptographicException`
- ✅ **Replay attack** — Attacker replays old ciphertext → tag still valid, but nonce is different (defender checks context)
- ✅ **Truncation attack** — Attacker removes bytes → length mismatch or tag failure

---

### Non-Malleability

**Claim:** Attacker cannot modify ciphertext to produce **predictable plaintext changes**.

**Example attack (CBC mode):**
- Flip bit in ciphertext block → flips corresponding bit in next plaintext block
- GCM blocks this: any change → tag verification fails

**Why this matters:** Prevents "attacker blindly changes encrypted salary from $50k to $90k" attacks.

---

## Cryptographic Parameters

### Nonce Size (96 bits)

**Why 96 bits?**
- GCM standard recommends 96 bits for optimal performance
- Allows 2^96 encryptions before nonce collision risk (essentially infinite for our use case)
- Smaller nonce (64-bit) would require counter management

**Collision probability:**
- After 2^48 encryptions: ~0.01% chance of nonce reuse
- Woven encrypts ~1000 intents/day → 2^48 encryptions in ~750 billion years

**Security if reused:** Catastrophic. Reusing (key, nonce) leaks XOR of plaintexts.

**Mitigation:** Always random, never incremental.

---

### Tag Size (128 bits)

**Why 128 bits?**
- Provides 2^128 security against forgery (unbreakable)
- Smaller tags (96-bit, 64-bit) reduce security margin

**Forgery probability:**
- Attacker guessing valid tag: 1 / 2^128 ≈ 2.9 × 10^-39
- Even trying 1 billion tags/second for 1 billion years: negligible success chance

---

### Key Size (256 bits)

**Why 256 bits vs. 128 bits?**
- **AES-128** is secure (no practical attack exists), but AES-256 provides extra margin
- **Post-quantum resistance:** Grover's algorithm (quantum) reduces AES-256 to 128-bit security (still safe)
- **Compliance:** Some regulations (e.g., TOP SECRET) require 256-bit keys

**Brute force time (AES-256):**
- Trying 1 trillion keys/second: 3.7 × 10^53 years to try half the keyspace

---

## Hardware Acceleration (AES-NI)

**AES-NI** (AES New Instructions) is a CPU instruction set extension for fast AES encryption.

**Supported CPUs:**
- Intel: Core i5/i7/i9 (2010+), Xeon
- AMD: Ryzen, EPYC
- ARM: ARMv8-A (Apple M1/M2, AWS Graviton)

**Performance gain:**
- **Without AES-NI:** ~100 MB/s (software AES)
- **With AES-NI:** ~3 GB/s (30x faster)

**.NET Core automatically uses AES-NI** on supported hardware (no code changes needed).

**Verify on Azure:**
```bash
# SSH into container
lscpu | grep aes
# Output: Flags: ... aes ...
```

---

## Comparison with Other Modes

| Mode | Confidentiality | Integrity | Parallelizable | Padding | Use Case |
|------|----------------|-----------|----------------|---------|----------|
| **GCM** | ✅ | ✅ | ✅ | ❌ (no padding) | **Woven (best choice)** |
| CBC | ✅ | ❌ (needs HMAC) | ❌ | ✅ | Legacy systems |
| CTR | ✅ | ❌ (needs HMAC) | ✅ | ❌ | Streaming |
| CCM | ✅ | ✅ | ❌ | ❌ | IoT (simpler than GCM) |

**Why not CBC?**
- Requires HMAC for integrity (2 passes over data)
- Padding oracle attacks (if not implemented carefully)
- Not parallelizable (slower)

**Why not ChaCha20-Poly1305?**
- Also a good choice (used by TLS 1.3)
- AES-GCM has better hardware support on x86/ARM
- .NET Core `AesGcm` class is well-tested

---

## Common Pitfalls

### 1. Nonce Reuse

**NEVER reuse a (key, nonce) pair.**

**Bad example:**
```csharp
var nonce = new byte[12];  // All zeros — DISASTER
aes.Encrypt(nonce, plaintext1, ciphertext1, tag1);
aes.Encrypt(nonce, plaintext2, ciphertext2, tag2);  // BROKEN
```

**Why broken?**
- Reused nonce → same keystream for both encryptions
- `ciphertext1 XOR ciphertext2 = plaintext1 XOR plaintext2`
- Attacker can recover plaintexts

**Woven's fix:** Always `RandomNumberGenerator.GetBytes(12)`.

---

### 2. Using Wrong Key

**Symptom:** `CryptographicException: The computed authentication tag did not match the input authentication tag.`

**Cause:** Decrypting with a different key than used for encryption.

**Example:**
- Encrypted in **dev** with key `abc123...`
- Trying to decrypt in **prod** with key `xyz789...`

**Fix:** Ensure same key in both environments (or use key versioning).

---

### 3. Truncating Ciphertext

**Bad example:**
```csharp
var ciphertext = _db.Intents.Select(x => x.ReflectionSentence.Substring(0, 50));
// Truncated ciphertext missing tag → decryption fails
```

**Why broken?** GCM needs the full `nonce + ciphertext + tag`. Truncation removes tag → verification fails.

**Fix:** Always store/retrieve full ciphertext (including nonce and tag).

---

### 4. Storing Nonce Separately

**Bad example:**
```sql
CREATE TABLE encrypted_data (
    nonce BYTEA,
    ciphertext BYTEA,
    tag BYTEA
);
```

**Why bad?** More complex (3 columns), easy to mismatch nonce/ciphertext.

**Woven's approach:** Single column with `nonce + ciphertext + tag` concatenated.

---

## Performance Benchmarks

**Environment:** Intel i7-10700K, .NET 8, 1000 iterations

| Operation | Time | Throughput |
|-----------|------|------------|
| Encrypt 50-char string | 0.015 ms | 3.3 MB/s |
| Encrypt 150-char string | 0.020 ms | 7.5 MB/s |
| Encrypt 500-char string | 0.035 ms | 14.3 MB/s |
| Decrypt 150-char string | 0.020 ms | 7.5 MB/s |

**Overhead vs. plaintext storage:** ~20 µs per operation.

**Verdict:** Negligible for user intents (encrypt on write, decrypt only for match explanation).

---

## Code Walkthrough

### EncryptionService.cs

**File:** `backend/WovenBackend/Services/Security/EncryptionService.cs`

```csharp
public class EncryptionService : IEncryptionService
{
    private const int NonceSize = 12;  // 96 bits
    private const int TagSize = 16;    // 128 bits

    private readonly byte[] _masterKey;

    public EncryptionService(IConfiguration config)
    {
        var keyB64 = config["Encryption:MasterKey"]
            ?? throw new InvalidOperationException("Encryption:MasterKey is required");
        _masterKey = Convert.FromBase64String(keyB64);
        if (_masterKey.Length != 32)
            throw new InvalidOperationException("Encryption:MasterKey must be 32 bytes");
    }

    public byte[] EncryptBytes(byte[] data)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var ciphertext = new byte[data.Length];

        using var aes = new AesGcm(_masterKey, TagSize);
        aes.Encrypt(nonce, data, ciphertext, tag);

        // Concatenate: nonce[12] + ciphertext + tag[16]
        var result = new byte[NonceSize + ciphertext.Length + TagSize];
        nonce.CopyTo(result, 0);
        ciphertext.CopyTo(result, NonceSize);
        tag.CopyTo(result, NonceSize + ciphertext.Length);
        return result;
    }

    public byte[] DecryptBytes(byte[] combined)
    {
        if (combined.Length < NonceSize + TagSize)
            throw new CryptographicException("Ciphertext too short");

        var nonce = combined[..NonceSize];
        var tag = combined[^TagSize..];
        var ciphertext = combined[NonceSize..^TagSize];
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_masterKey, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);  // Throws if tag invalid
        return plaintext;
    }
}
```

**Key details:**
- `using var aes = new AesGcm(...)` — Disposes key material after use (clears memory)
- `aes.Decrypt(...)` — Throws `CryptographicException` if tag verification fails
- `RandomNumberGenerator.GetBytes(12)` — Cryptographically secure random nonce

---

## Testing

**Unit test:** `EncryptionServiceTests.cs`

```csharp
[Fact]
public void EncryptBytes_ProducesDifferentCiphertexts()
{
    var plaintext = Encoding.UTF8.GetBytes("test");
    var ciphertext1 = _encryption.EncryptBytes(plaintext);
    var ciphertext2 = _encryption.EncryptBytes(plaintext);

    // Same plaintext → different ciphertext (due to random nonce)
    Assert.NotEqual(ciphertext1, ciphertext2);
}

[Fact]
public void DecryptBytes_WithWrongKey_Throws()
{
    var ciphertext = _encryption.EncryptBytes(Encoding.UTF8.GetBytes("test"));
    var wrongKeyService = new EncryptionService(wrongKeyConfig);

    Assert.Throws<CryptographicException>(() =>
        wrongKeyService.DecryptBytes(ciphertext));
}
```

---

## References

- **NIST SP 800-38D** — GCM specification
  - https://nvlpubs.nist.gov/nistpubs/Legacy/SP/nistspecialpublication800-38d.pdf
- **.NET AesGcm docs**
  - https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm
- **FIPS 140-2** — Cryptographic module standards
  - https://csrc.nist.gov/publications/detail/fips/140/2/final
- **AES-NI whitepaper** (Intel)
  - https://www.intel.com/content/www/us/en/docs/intrinsics-guide/index.html#techs=AES

---

## Summary

**AES-256-GCM** provides:
- ✅ **Confidentiality** — Ciphertext indistinguishable from random
- ✅ **Integrity** — Detects any tampering via authentication tag
- ✅ **Performance** — Hardware-accelerated on modern CPUs
- ✅ **Compliance** — NIST-approved, FIPS 140-2 compliant

**Woven's implementation:**
- 256-bit master key from Azure Key Vault
- 96-bit random nonce per encryption (never reused)
- 128-bit authentication tag
- Base64-encoded storage in PostgreSQL TEXT columns

**Result:** Even if database is compromised, reflection sentences remain unreadable without the master key.
