# EmailHash Backfill Strategy

## Problem
Encrypted email fields use AES-GCM with random nonces, making them unsearchable via equality checks. The `Email` column cannot be used for duplicate detection during Google OAuth login.

## Solution
Added `EmailHash` column (SHA-256 hash of lowercase, trimmed email) for searchable lookups while keeping `Email` encrypted.

**Migration:** `20261010023150_AddEmailHashToUsers`

## Production Backfill Required

### Current State After Migration
- New users: `EmailHash` populated automatically
- Existing users: `EmailHash = ""` (default value)
- Impact: Existing users signing in via Google OAuth will create duplicate accounts

### Backfill Approach

**Option 1: Application-Level Backfill (Recommended)**

Create a one-time admin endpoint or background job:

```csharp
// Backfill existing users with EmailHash
var usersWithoutHash = await db.Users
    .Where(u => u.EmailHash == "")
    .ToListAsync(ct);

foreach (var user in usersWithoutHash)
{
    // Email is decrypted automatically by EF converter
    user.EmailHash = encryption.ComputeEmailHash(user.Email);
}

await db.SaveChangesAsync(ct);
```

**Option 2: SQL-Level Backfill**

⚠️ **Not possible** - Email is encrypted in DB, requires application-level decryption.

### Deployment Plan

1. **Pre-deployment:**
   - Review this strategy with team
   - Decide on backfill timing (immediate vs. scheduled)
   
2. **Deploy:**
   - Apply migration (adds column + index)
   - All new users get EmailHash populated
   
3. **Post-deployment:**
   - Run backfill job/endpoint (decrypts emails, computes hashes)
   - Verify: `SELECT COUNT(*) FROM "Users" WHERE "EmailHash" = ''` should be 0
   - Monitor for duplicate account creation

### Testing

**Local QA:**
```sql
-- Check users without hash
SELECT "Id", "Email", "EmailHash" 
FROM "Users" 
WHERE "EmailHash" = '' 
LIMIT 10;

-- Verify hash format (should be base64, ~44 chars)
SELECT "EmailHash", LENGTH("EmailHash")
FROM "Users"
WHERE "EmailHash" != ''
LIMIT 5;
```

### Rollback

If issues arise:
```sql
-- Revert migration
dotnet ef migrations remove

-- Or manually
ALTER TABLE "Users" DROP COLUMN "EmailHash";
```

## Files Changed

- `User.cs` - Added `EmailHash` property
- `EncryptionService.cs` - Added `ComputeEmailHash()` method
- `IEncryptionService.cs` - Added interface method
- `AuthEndpoints.cs` - Updated to use `EmailHash` for lookups
- `DevSeedEndpoints.cs` - Updated seed data to include `EmailHash`
- Migration: `20261010023150_AddEmailHashToUsers.cs`

## References

- GitHub Issue: #166 (AUTH-QA-001)
- Codex Evidence: qa/evidence/2026-10-08 (PostgreSQL probe showing null results on encrypted email equality)
