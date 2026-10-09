# PII Handling

**Consolidated from:** `docs/technical/SECURITY.md`, `docs/security/README.md`

---

## Overview

Woven handles personally identifiable information (PII) with encryption at rest, sanitization in logs/AI prompts, and auto-anonymization after retention periods.

---

## What Is PII

**PII in Woven:**
- Email addresses
- Full names
- Location (city, state, lat/lng)
- Profile photos
- Chat messages
- Voice notes
- Intent reflections
- Foundational Q&A answers

---

## Encryption at Rest

**AES-256-GCM encrypted fields:**
- `users.email`
- `users.full_name`
- `user_profiles.city`
- `user_profiles.state`
- `user_intents.reflection_sentence`
- `user_optional_fields.value`

**See [encryption.md](encryption.md) for full details.**

---

## PII Sanitization

### In Logs

**PiiSanitizer removes:**
- Email addresses (regex pattern)
- Phone numbers (regex pattern)
- IP addresses (hashed before logging)

**Implementation:**
```csharp
public static class PiiSanitizer
{
    public static string SanitizeForLogging(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        
        // Remove emails
        text = Regex.Replace(text, @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", "[EMAIL]");
        
        // Remove phone numbers
        text = Regex.Replace(text, @"\b\d{3}[-.]?\d{3}[-.]?\d{4}\b", "[PHONE]");
        
        return text;
    }
    
    public static string HashForAudit(string value, string salt)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(value + salt);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }
}
```

**Source:** `backend/WovenBackend/Services/Security/PiiSanitizer.cs`

---

### In AI Prompts

**Before every OpenAI call:**
1. Strip email patterns
2. Strip phone patterns
3. Truncate to 200 characters
4. Apply prompt injection detection

**Example:**
```csharp
var sanitized = PiiSanitizer.SanitizeForAi(userBio);
// "Hi! Email me at john@example.com or call 555-1234"
// → "Hi! Email me at [EMAIL] or call [PHONE]"
```

**Applied in:**
- `AiProfileService` (profile scoring)
- `MatchExplanationService` (match summaries)
- `KnowMeAgent` (game questions)
- `RedGreenFlagAgent` (game scenarios)

**Source:** `backend/WovenBackend/Services/Security/PiiSanitizer.cs`

---

## Data Retention

### Analytics Auto-Anonymization

**Policy:** 12-month retention for user-identifiable analytics

**Implementation:**
```csharp
// AnalyticsRetentionWorker runs monthly
var cutoff = DateTime.UtcNow.AddMonths(-12);

await db.AnalyticsEvents
    .Where(e => e.CreatedAt < cutoff)
    .ExecuteUpdateAsync(s => s
        .SetProperty(e => e.UserIdHash, (string?)null)
        .SetProperty(e => e.SessionId, (string?)null));
```

**What is anonymized:**
- `user_id_hash` → NULL
- `session_id` → NULL
- Event metadata retained (for aggregate analytics)

**Source:** `backend/WovenBackend/Services/Analytics/AnalyticsRetentionWorker.cs`

---

### Chat Message Retention

**Current policy:** Indefinite retention

**Planned:** 
- 90-day retention for inactive matches
- Export option before deletion

**Status:** Not yet implemented

---

### Account Deletion

**User-initiated deletion:**
```
DELETE /me/account
```

**What is deleted:**
- User account
- Profile data
- Chat messages (from user's perspective)
- Photos
- Tiles
- Matches

**What is retained (anonymized):**
- Analytics events (user_id_hash set to NULL)
- Aggregate match statistics
- System logs (correlation IDs only)

**Implementation:** Soft delete with 30-day grace period (planned).

**Source:** `backend/WovenBackend/Endpoints/UserDataEndpoints.cs` (planned)

---

## PII Access Controls

### Database Access

**Production:**
- Azure PostgreSQL with VNet isolation
- No public IP
- Access restricted to Container Apps subnet
- Admin access via Azure portal only (MFA required)

**Local development:**
- Docker container (localhost:5433)
- No sensitive data in dev environment

---

### Application Access

**All PII queries scoped by user ID:**
```csharp
// ✅ Good: user-scoped
var profile = await db.UserProfiles
    .Where(p => p.UserId == userId)
    .FirstOrDefaultAsync();

// ❌ Bad: unrestricted query
var profiles = await db.UserProfiles.ToListAsync();
```

**Enforced via:**
- `EndpointHelper.GetUserId()` extracts user from JWT
- Every endpoint filters by authenticated user
- No admin endpoints in production yet

---

## Data Export

### User Data Export

**Endpoint:** `GET /me/export`

**Returns:**
```json
{
  "user": {
    "email": "user@example.com",
    "fullName": "Jane Doe",
    "createdAt": "2026-01-15T10:00:00Z"
  },
  "profile": {
    "age": 28,
    "gender": "WOMAN",
    "location": {"city": "Seattle", "state": "WA"}
  },
  "matches": [...],
  "messages": [...],
  "tiles": [...]
}
```

**Format:** JSON (GDPR-compliant)

**Source:** `backend/WovenBackend/Endpoints/UserDataEndpoints.cs`

---

## PII in Logs

### What Gets Logged

**Safe to log:**
- User ID (integer, not PII)
- Correlation IDs
- Timestamps
- Event types
- Aggregate counts

**Never logged:**
- Email addresses
- Full names
- Chat message content
- Location (city/state)
- Reflection sentences
- JWT tokens

---

### Log Sanitization

**Automatic:**
```csharp
_logger.LogInformation(
    "[MatchService] Pool built | UserId={UserId} CorrelationId={Cid}",
    userId, correlationId);
// No PII in log line
```

**If PII must be logged (rare):**
```csharp
var sanitized = PiiSanitizer.SanitizeForLogging(userInput);
_logger.LogWarning(
    "[Security] Suspicious input | UserId={UserId} Input={Input}",
    userId, sanitized);
```

---

## Compliance

### GDPR

**Implemented:**
- ✅ Right to access (data export)
- ✅ Right to erasure (account deletion)
- ✅ Right to rectification (profile edit)
- ✅ Data minimization (only collect what's needed)
- ✅ Storage limitation (12-month analytics retention)

**Planned:**
- ⏳ Right to portability (standard format export)
- ⏳ Right to object (opt-out of processing)
- ⏳ Consent management

---

### CCPA

**Implemented:**
- ✅ Right to know (data export)
- ✅ Right to delete (account deletion)

**Planned:**
- ⏳ Right to opt-out of sale (N/A, we don't sell data)
- ⏳ Privacy notice
- ⏳ Do Not Sell link

---

## Best Practices

1. **Encrypt PII at rest** — Use AES-256-GCM
2. **Sanitize PII in logs** — Use PiiSanitizer
3. **Limit retention** — Auto-anonymize after 12 months
4. **User-scoped queries** — Filter by authenticated user ID
5. **Minimal collection** — Only collect necessary data
6. **Secure transmission** — HTTPS everywhere
7. **Access controls** — VNet isolation, MFA for admin
8. **Audit logging** — Log access to sensitive data

---

**Last Updated:** 2026-10-07  
**Source Files:**
- `backend/WovenBackend/Services/Security/PiiSanitizer.cs`
- `backend/WovenBackend/Services/Analytics/AnalyticsRetentionWorker.cs`
- `backend/WovenBackend/Endpoints/UserDataEndpoints.cs`
