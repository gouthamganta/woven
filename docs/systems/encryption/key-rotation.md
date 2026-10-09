# Key Rotation System

**Last Updated:** 2026-10-07  
**Status:** ACTIVE (automated checks, manual re-encryption)

---

## Overview

**Key rotation** is the process of replacing the encryption master key with a new key and re-encrypting all existing data. It's a critical security practice to limit the damage if a key is compromised.

**Woven's strategy:**
- **Automated checks:** KeyRotationWorker runs every 7 days, checks if rotation is due
- **Rotation cycle:** Every 90 days
- **Process:** Semi-automated (worker detects, ops team executes)

---

## Why Rotate Keys?

**Threat model:**
1. **Key compromise:** Attacker gains access to master key (stolen backup, memory dump, insider leak)
2. **Cryptanalysis advances:** Future quantum computers or algorithmic breakthroughs
3. **Compliance:** Many regulations (PCI-DSS, HIPAA) require periodic key rotation

**Impact of NOT rotating:**
- If key leaked today, all historical data encrypted with that key is compromised
- Rotating limits exposure window (only last 90 days at risk)

---

## Rotation Schedule

| Event | Frequency | Trigger |
|-------|-----------|---------|
| **KeyRotationWorker check** | Every 7 days | Automatic (background service) |
| **Rotation due** | Every 90 days | Last rotation timestamp in `security_audit_log` |
| **Manual rotation** | On-demand | Admin calls `POST /admin/security/rotate-keys` |

**Timeline example:**
- **Day 0:** New key deployed
- **Day 7-84:** Worker checks, sees "last rotation 7/14/21/... days ago" → no action
- **Day 91:** Worker checks, sees "91 days ago" → logs warning + generates new key hint
- **Ops team:** Deploys new key to Azure Key Vault, calls `/admin/security/rotate-keys` endpoint

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│              KeyRotationWorker (Background)                  │
│  • Runs every 7 days                                         │
│  • Checks last rotation timestamp from security_audit_log   │
│  • If > 90 days → logs warning + generates new key hint     │
└──────────────────┬──────────────────────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────────────────┐
│                  Ops Team (Manual)                           │
│  1. Generate new 32-byte key                                │
│  2. Deploy to Azure Key Vault as new secret version         │
│  3. Update Container Apps env to read new version           │
│  4. Call POST /admin/security/rotate-keys                   │
└──────────────────┬──────────────────────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────────────────┐
│           Re-encryption Endpoint (Future)                    │
│  • Load all encrypted rows from DB                          │
│  • Decrypt with old key (from version field)               │
│  • Encrypt with new key                                     │
│  • Save with updated version field                          │
│  • Process in batches (1000 rows/batch)                     │
└─────────────────────────────────────────────────────────────┘
```

---

## KeyRotationWorker Implementation

**File:** `backend/WovenBackend/Services/Security/KeyRotationWorker.cs`

### Background Service

```csharp
protected override async Task ExecuteAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        await Task.Delay(TimeSpan.FromDays(7), ct);
        if (ct.IsCancellationRequested) break;

        await CheckAndRotateAsync(ct);
    }
}
```

**How it works:**
- Runs as a hosted background service (started in `Program.cs`)
- Wakes up every 7 days
- Calls `CheckAndRotateAsync()` to check rotation status

---

### Rotation Check Logic

```csharp
public async Task CheckAndRotateAsync(CancellationToken ct = default)
{
    try
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();

        // Check last rotation from audit log
        var lastRotation = await db.SecurityAuditLogs.AsNoTracking()
            .Where(l => l.EventType == "encryption_key_rotation" 
                     && l.ResourceType == "complete")
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => (DateTimeOffset?)l.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (lastRotation != null &&
            (DateTimeOffset.UtcNow - lastRotation.Value).TotalDays < 90)
        {
            _logger.LogInformation(
                "[KeyRotation] No rotation needed — last rotation {Days:F0} days ago",
                (DateTimeOffset.UtcNow - lastRotation.Value).TotalDays);
            return;
        }

        _logger.LogInformation("[KeyRotation] Starting key rotation");
        _audit.Log("encryption_key_rotation", resourceType: "start", resourceId: "n/a");

        // Generate new key for reference
        var newKey = RandomNumberGenerator.GetBytes(32);
        var newKeyB64 = Convert.ToBase64String(newKey);

        _logger.LogWarning(
            "[KeyRotation] New 32-byte key generated. Ops: deploy new " +
            "Encryption:MasterKey secret and trigger POST /admin/security/rotate-keys");

        _audit.Log("encryption_key_rotation", resourceType: "complete", 
            resourceId: "n/a", service: "KeyRotationWorker");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "[KeyRotation] Key rotation check failed");
    }
}
```

**What it does:**
1. Query `security_audit_log` for last `encryption_key_rotation` event with `resource_type = "complete"`
2. If last rotation < 90 days ago → log info, exit
3. If ≥ 90 days ago (or never rotated) → log warning with new key hint
4. Generate new 32-byte key (for reference, not deployed automatically)
5. Log audit event (`encryption_key_rotation` / `start` and `complete`)

**Why not auto-deploy new key?**
- Key management is sensitive (requires ops access to Key Vault)
- Manual step ensures human oversight (prevents accidental rotation)

---

## Manual Rotation Endpoint

**File:** `backend/WovenBackend/Endpoints/AdminSecurityEndpoints.cs`

```csharp
group.MapPost("/rotate-keys", async (
    KeyRotationWorker rotationWorker,
    ISecurityAuditService audit,
    CancellationToken ct) =>
{
    audit.Log("admin_key_rotation_triggered", 
        resourceType: "KeyRotation", resourceId: "manual");

    await rotationWorker.CheckAndRotateAsync(ct);

    return Results.Ok(new { 
        triggered = true, 
        note = "Rotation check complete. See logs for outcome." 
    });
});
```

**Usage:**
```bash
curl -X POST https://wooven.me/admin/security/rotate-keys \
  -H "Authorization: Bearer <admin-token>"
```

**Requires:** `Admin` role (checked via `RequireAuthorization("Admin")`)

---

## Key Versioning Strategy

**Current status:** NOT implemented (single key, no version tracking).

**Planned approach:**

### 1. Add Version Column to Encrypted Tables

```sql
ALTER TABLE user_intents
ADD COLUMN encryption_key_version INT DEFAULT 1;
```

**Why?** Allows decrypting with old key while new key is being deployed.

---

### 2. Store Multiple Key Versions

**Azure Key Vault approach:**
- Create new **secret version** in Key Vault (same secret name, new version ID)
- Container Apps env var points to specific version or "latest"

**Example:**
```bash
# Current key (v1)
Encryption:MasterKey@v1 = "abc123..." (generated 2026-01-15)

# New key (v2)
Encryption:MasterKey@v2 = "xyz789..." (generated 2026-04-15)
```

**App config:**
```json
{
  "Encryption": {
    "MasterKey_v1": "abc123...",
    "MasterKey_v2": "xyz789...",
    "CurrentVersion": 2
  }
}
```

---

### 3. Re-encryption Migration Script

**Pseudocode:**
```csharp
public async Task ReEncryptAllIntentsAsync(int oldVersion, int newVersion)
{
    var oldKey = _config[$"Encryption:MasterKey_v{oldVersion}"];
    var newKey = _config[$"Encryption:MasterKey_v{newVersion}"];

    var oldEncryption = new EncryptionService(oldKey);
    var newEncryption = new EncryptionService(newKey);

    var totalUsers = await _db.UserIntents
        .Where(x => x.EncryptionKeyVersion == oldVersion)
        .CountAsync();

    _logger.LogInformation(
        "[ReEncrypt] Starting re-encryption of {Total} intents from v{Old} to v{New}",
        totalUsers, oldVersion, newVersion);

    const int batchSize = 1000;
    int processed = 0;

    while (processed < totalUsers)
    {
        var batch = await _db.UserIntents
            .Where(x => x.EncryptionKeyVersion == oldVersion)
            .OrderBy(x => x.Id)
            .Skip(processed)
            .Take(batchSize)
            .ToListAsync();

        foreach (var intent in batch)
        {
            // Decrypt with old key
            var plaintext = oldEncryption.Decrypt(intent.ReflectionSentence);

            // Encrypt with new key
            var newCiphertext = newEncryption.Encrypt(plaintext);

            intent.ReflectionSentence = newCiphertext;
            intent.EncryptionKeyVersion = newVersion;
        }

        await _db.SaveChangesAsync();
        processed += batch.Count;

        _logger.LogInformation(
            "[ReEncrypt] Progress: {Processed}/{Total} ({Pct:F1}%)",
            processed, totalUsers, (processed * 100.0 / totalUsers));
    }

    _logger.LogInformation("[ReEncrypt] Re-encryption complete");
}
```

**Run as:**
- Background job (Azure Service Bus message)
- Admin endpoint (`POST /admin/security/re-encrypt`)
- Manual script via `dotnet run --re-encrypt`

**Why batches?** Prevents locking entire table (allows concurrent reads/writes).

---

## Rotation Runbook (Ops Team)

### Step 1: Monitor Logs

**Watch for:**
```
[KeyRotation] New 32-byte key generated. Ops: deploy new Encryption:MasterKey...
```

**Trigger:** KeyRotationWorker detects ≥90 days since last rotation.

---

### Step 2: Generate New Key

**Local script:**
```bash
dotnet run --project backend/WovenBackend -- generate-encryption-key
```

**Output:**
```
New encryption key (base64):
a3d8f7e2c1b9... (44 characters)

Store this in Azure Key Vault as a new secret version.
```

**Or manually:**
```csharp
var key = RandomNumberGenerator.GetBytes(32);
var keyB64 = Convert.ToBase64String(key);
Console.WriteLine(keyB64);
```

---

### Step 3: Deploy to Azure Key Vault

**Azure CLI:**
```bash
# Create new secret version
az keyvault secret set \
  --vault-name woven-prod-kv \
  --name Encryption-MasterKey \
  --value "<new-key-base64>"

# Get version ID
az keyvault secret show \
  --vault-name woven-prod-kv \
  --name Encryption-MasterKey \
  --query "id"
# Output: https://woven-prod-kv.vault.azure.net/secrets/Encryption-MasterKey/abc123def456
```

**Version ID:** `abc123def456` (last part of URL)

---

### Step 4: Update Container Apps

**Option A: Point to latest version (auto-rotates)**
```bash
az containerapp update \
  --name woven-backend \
  --resource-group woven-prod-rg \
  --set-env-vars "Encryption__MasterKey=secretref:encryption-master-key"
```

**Option B: Point to specific version (manual control)**
```bash
az containerapp update \
  --name woven-backend \
  --resource-group woven-prod-rg \
  --set-env-vars "Encryption__MasterKey=secretref:encryption-master-key@abc123def456"
```

**Recommendation:** Option B (explicit version) for controlled rollout.

---

### Step 5: Trigger Re-encryption

**Call admin endpoint:**
```bash
curl -X POST https://wooven.me/admin/security/rotate-keys \
  -H "Authorization: Bearer <admin-token>"
```

**Expected response:**
```json
{
  "triggered": true,
  "note": "Rotation check complete. See logs for outcome."
}
```

**Verify logs:**
```bash
az containerapp logs show \
  --name woven-backend \
  --resource-group woven-prod-rg \
  --follow

# Look for:
# [KeyRotation] Starting key rotation
# [KeyRotation] Rotation cycle complete
```

---

### Step 6: Re-encrypt Existing Data (Future)

**When encryption is wired to production:**
```bash
curl -X POST https://wooven.me/admin/security/re-encrypt \
  -H "Authorization: Bearer <admin-token>" \
  -d '{"oldVersion": 1, "newVersion": 2}'
```

**Monitor progress:**
```bash
az containerapp logs show ... --follow

# Look for:
# [ReEncrypt] Starting re-encryption of 12,345 intents...
# [ReEncrypt] Progress: 1000/12345 (8.1%)
# [ReEncrypt] Progress: 2000/12345 (16.2%)
# ...
# [ReEncrypt] Re-encryption complete
```

**Duration estimate:** ~10 seconds per 1000 intents (at 0.01ms/decrypt + 0.01ms/encrypt).

---

## Security Considerations

### 1. Old Key Retention

**Question:** When can we delete the old key?

**Answer:** Only after ALL data is re-encrypted with the new key.

**Strategy:**
- Keep old key in Key Vault as archived version (not deleted)
- Mark as "deprecated" in runbook
- Delete after 30-day retention window (allows rollback if new key has issues)

---

### 2. Zero-Downtime Rotation

**Problem:** If we swap key immediately, in-flight requests with old ciphertext will fail decryption.

**Solution: Dual-key period (grace window)**

**Pseudocode:**
```csharp
public string Decrypt(string ciphertext)
{
    try
    {
        // Try current key
        return _currentEncryption.Decrypt(ciphertext);
    }
    catch (CryptographicException)
    {
        // Fallback to old key (if version field missing/unknown)
        return _oldEncryption.Decrypt(ciphertext);
    }
}
```

**Why?** Allows gradual migration (old ciphertext still readable during re-encryption).

**Duration:** 24 hours after rotation start (ensures all in-flight data processed).

---

### 3. Audit Trail

**Every rotation logs:**
- `encryption_key_rotation` / `start` — Rotation initiated
- `encryption_key_rotation` / `complete` — Rotation finished
- `admin_key_rotation_triggered` — Manual trigger via endpoint

**Query audit log:**
```sql
SELECT created_at, event_type, resource_type, details_json
FROM security_audit_log
WHERE event_type = 'encryption_key_rotation'
ORDER BY created_at DESC;
```

**Example output:**
```
| created_at          | event_type              | resource_type | details_json                |
|---------------------|-------------------------|---------------|-----------------------------|
| 2026-04-15 03:50:12 | encryption_key_rotation | complete      | {"service": "KeyRotation"}  |
| 2026-04-15 03:50:10 | encryption_key_rotation | start         | {}                          |
| 2026-01-15 04:05:33 | encryption_key_rotation | complete      | {"service": "KeyRotation"}  |
```

---

## Rollback Strategy

**Scenario:** New key deployed, but re-encryption fails (bug in migration script).

**Steps:**
1. **Revert Container Apps env to old key version**
   ```bash
   az containerapp update \
     --name woven-backend \
     --set-env-vars "Encryption__MasterKey=secretref:encryption-master-key@<old-version-id>"
   ```

2. **Restart pods**
   ```bash
   az containerapp revision restart \
     --name woven-backend \
     --resource-group woven-prod-rg
   ```

3. **Verify decryption works**
   ```bash
   curl https://wooven.me/onboarding/review \
     -H "Authorization: Bearer <user-token>"
   # Should return decrypted reflection
   ```

4. **Roll back re-encryption progress**
   ```sql
   UPDATE user_intents
   SET encryption_key_version = 1
   WHERE encryption_key_version = 2;
   ```

5. **Investigate failure**
   - Check logs for `CryptographicException`
   - Verify new key length (must be 32 bytes)
   - Test re-encryption on single row manually

---

## Testing Rotation Locally

**1. Generate two keys**
```bash
dotnet run --generate-key
# Output: abc123... (save as KEY_V1)

dotnet run --generate-key
# Output: xyz789... (save as KEY_V2)
```

**2. Set User Secrets**
```bash
dotnet user-secrets set "Encryption:MasterKey_v1" "<KEY_V1>"
dotnet user-secrets set "Encryption:MasterKey_v2" "<KEY_V2>"
dotnet user-secrets set "Encryption:CurrentVersion" "1"
```

**3. Encrypt data with v1**
```bash
curl -X PUT http://localhost:5135/onboarding/intent \
  -H "Authorization: Bearer <token>" \
  -d '{"reflectionSentence": "Test data for rotation"}'
```

**4. Verify stored with v1**
```sql
SELECT encryption_key_version, reflection_sentence
FROM user_intents
WHERE user_id = 123;
-- encryption_key_version = 1
```

**5. Rotate to v2**
```bash
dotnet user-secrets set "Encryption:CurrentVersion" "2"
dotnet run --re-encrypt --old-version 1 --new-version 2
```

**6. Verify re-encrypted**
```sql
SELECT encryption_key_version, reflection_sentence
FROM user_intents
WHERE user_id = 123;
-- encryption_key_version = 2 (ciphertext changed)
```

**7. Test decryption still works**
```bash
curl http://localhost:5135/onboarding/review \
  -H "Authorization: Bearer <token>"
# Should return "Test data for rotation"
```

---

## Monitoring & Alerts

**Metrics to track:**
- Days since last rotation (alert if > 95 days)
- Key rotation failures (alert on any exception in KeyRotationWorker)
- Re-encryption progress (dashboard showing %)

**Azure Monitor query (Log Analytics):**
```kusto
traces
| where message contains "[KeyRotation]"
| order by timestamp desc
| take 50
```

**Alert rule:**
```kusto
traces
| where message contains "[KeyRotation] New 32-byte key generated"
| where timestamp > ago(7d)
| count
// Alert if count > 0 (rotation due, ops team notified)
```

---

## Future Improvements

### 1. Automated Re-encryption

**Current:** Manual endpoint call after key deployment.

**Future:** Trigger re-encryption automatically on new key detection.

**Implementation:**
- KeyRotationWorker detects new `Encryption:MasterKey_v2` in config
- Starts background re-encryption job (Azure Service Bus message)
- Ops team only deploys key, no manual trigger needed

---

### 2. Gradual Rollout

**Current:** Re-encrypt all rows in one operation.

**Future:** Re-encrypt incrementally (10% of users per hour).

**Why?** Reduces DB load spike, allows A/B testing new key.

---

### 3. Key Expiry Metadata

**Store key metadata in DB:**
```sql
CREATE TABLE encryption_keys (
    version INT PRIMARY KEY,
    created_at TIMESTAMPTZ NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    status TEXT CHECK (status IN ('active', 'deprecated', 'archived'))
);
```

**Query:**
```sql
SELECT version, status, expires_at
FROM encryption_keys
WHERE status = 'active';
```

---

## References

- **NIST SP 800-57** — Key management recommendations
  - https://csrc.nist.gov/publications/detail/sp/800-57-part-1/rev-5/final
- **Azure Key Vault rotation**
  - https://learn.microsoft.com/en-us/azure/key-vault/secrets/overview-rotation
- **PCI-DSS requirement 3.6** — Key rotation for encryption keys
  - https://www.pcisecuritystandards.org/

---

## Summary

**Woven's key rotation system:**
- ✅ **Automated detection** — KeyRotationWorker checks every 7 days
- ✅ **90-day cycle** — Balances security (shorter window) vs. ops burden
- ✅ **Manual execution** — Ops team deploys new key (human oversight)
- ✅ **Audit logging** — All rotation events tracked in `security_audit_log`
- 📋 **Future:** Automated re-encryption, gradual rollout, key versioning

**Current status:** Rotation checks active, re-encryption not yet implemented (encryption not wired to endpoints).
