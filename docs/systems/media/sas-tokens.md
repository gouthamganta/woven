# SAS Token Generation & Security

**Last Updated:** 2026-10-07  
**Token Type:** Blob SAS (Shared Access Signature)  
**Expiry:** 15 minutes

---

## Overview

SAS (Shared Access Signature) tokens enable secure, time-limited, permission-scoped access to Azure Blob Storage without sharing the storage account key.

**Use case in Woven:** Client-side direct blob uploads while maintaining security and ownership boundaries.

---

## What is a SAS Token?

**Definition:** A cryptographically signed query string that grants specific permissions to a blob for a limited time.

**Example SAS token:**
```
sv=2024-11-04&se=2024-10-07T12:30:00Z&sr=b&sp=cw&sig=xJ8F7%2B...base64...
```

**Parameters:**
- `sv` — Storage version (`2024-11-04`)
- `se` — Expiry time (ISO 8601: `2024-10-07T12:30:00Z`)
- `sr` — Resource (b=blob, c=container, etc.)
- `sp` — Permissions (c=create, w=write, r=read, d=delete)
- `sig` — HMAC-SHA256 signature (prevents tampering)

**Full upload URL:**
```
https://wovenprodacr.blob.core.windows.net/voice-notes/1042/a7b3c9d1.webm?sv=2024-11-04&se=...&sig=...
```

---

## Generation Flow

### Request SAS Token

**Endpoint:** `POST /media/upload-token`

**Request:**
```json
{
  "container": "voice-note",
  "fileName": "voice-1730934120345.webm",
  "contentType": "audio/webm"
}
```

**Response:**
```json
{
  "uploadUrl": "https://...blob.core.windows.net/voice-notes/1042/a7b3c9d1.webm?sv=2024-11-04&se=...",
  "sasToken": "sv=2024-11-04&se=2024-10-07T12:30:00Z&sr=b&sp=cw&sig=...",
  "blobPath": "1042/a7b3c9d1-4e2f-5a6b-7c8d-9e0f1a2b3c4d.webm",
  "expiresAt": "2024-10-07T12:30:00Z"
}
```

**Fields:**
- `uploadUrl` — Full URL with SAS token embedded (ready to PUT)
- `sasToken` — Query string only (if client constructs URL manually)
- `blobPath` — Relative path within container (for confirm step)
- `expiresAt` — Expiry timestamp (client can check before upload)

### Backend Implementation

**MediaService.cs:27-62:**
```csharp
public async Task<UploadTokenResult> GetUploadTokenAsync(
    int userId,
    MediaContainerType container,
    string fileName,
    string contentType,
    CancellationToken ct = default)
{
    // 1. Generate blob path with userId prefix + GUID
    var ext = Path.GetExtension(fileName).ToLowerInvariant();
    var blobPath = $"{userId}/{Guid.NewGuid()}{ext}";
    var containerName = ContainerName(container);

    // 2. Get container client (creates if not exists)
    var containerClient = _blobService.GetBlobContainerClient(containerName);
    await containerClient.CreateIfNotExistsAsync(cancellationToken: ct);

    // 3. Get blob client
    var blobClient = containerClient.GetBlobClient(blobPath);
    var expiresAt = DateTimeOffset.UtcNow.AddMinutes(15);

    // 4. Build SAS token
    var sasBuilder = new BlobSasBuilder
    {
        BlobContainerName = containerName,
        BlobName = blobPath,
        Resource = "b",              // Blob-level (not container-level)
        ExpiresOn = expiresAt
    };
    sasBuilder.SetPermissions(BlobSasPermissions.Write | BlobSasPermissions.Create);

    // 5. Generate signed URI
    var sasUri = blobClient.GenerateSasUri(sasBuilder);

    _logger.LogInformation("[Media] SAS token issued for {BlobPath} (expires {ExpiresAt})", 
        blobPath, expiresAt);

    return new UploadTokenResult(
        SasToken: sasUri.Query.TrimStart('?'),
        BlobPath: blobPath,
        UploadUrl: sasUri.ToString(),
        ExpiresAt: expiresAt);
}
```

**Key steps:**
1. **Generate unique blob path** — `{userId}/{guid}.{ext}` ensures no collisions
2. **Create container if needed** — idempotent (`CreateIfNotExistsAsync`)
3. **Build SAS parameters** — permissions, expiry, resource type
4. **Sign with account key** — `GenerateSasUri()` uses storage account key to create HMAC signature
5. **Return signed URL** — client uses this for direct upload

---

## Security Model

### Permissions Granted

**SAS token permissions:** Write + Create only

```csharp
sasBuilder.SetPermissions(BlobSasPermissions.Write | BlobSasPermissions.Create);
```

**What client can do:**
- ✅ Create new blob (if doesn't exist)
- ✅ Overwrite blob (if already exists and SAS was for same path)

**What client CANNOT do:**
- ❌ Read blob (no Read permission)
- ❌ Delete blob (no Delete permission)
- ❌ List blobs (blob-level SAS, not container-level)
- ❌ Write to different blob path (SAS is path-specific)

**Why no Read?**  
All containers have public read access — no SAS needed for GET requests.

**Why no Delete?**  
Deletion requires authenticated API call to verify ownership.

### Expiry Time

**Duration:** 15 minutes (900 seconds)

**Why 15 minutes?**
- **Slow connections:** 4G upload of 5 MB file can take 10+ seconds
- **User delays:** Time between recording voice note and sending
- **Mobile backgrounding:** iOS/Android pause uploads when app backgrounded
- **Retry attempts:** Network failures require re-upload with same token

**Too short (< 5 min):** Frequent token expiry → poor UX  
**Too long (> 30 min):** Security risk if token leaked

**Current choice (15 min):** Balances usability and security.

### Signature Verification

**How Azure validates SAS token:**

1. **Extract parameters** from query string (`sv`, `se`, `sr`, `sp`, `sig`)
2. **Reconstruct string-to-sign** using:
   - Permissions (`sp`)
   - Expiry time (`se`)
   - Resource path (`/blob/wovenprodacr/voice-notes/1042/a7b3c9d1.webm`)
   - Storage version (`sv`)
3. **Compute HMAC-SHA256** using storage account key
4. **Compare signatures** — if match, grant access; else return 403

**Tampering protection:**  
Changing ANY parameter (expiry, path, permissions) invalidates the signature.

**Example attack that fails:**
```
Original:  /voice-notes/1042/a7b3c9d1.webm?se=2024-10-07T12:30:00Z&sig=ABC123
Tampered:  /voice-notes/1043/a7b3c9d1.webm?se=2024-10-07T12:30:00Z&sig=ABC123
           └─ Changed userId path, signature now invalid → 403 Forbidden
```

---

## Rate Limiting

### Upload Token Endpoint

**MediaEndpoints.cs:42-47:**
```csharp
// Rate limit: 20 upload tokens per user per day
var rlKey = $"rl:upload:{userId}:{DateOnly.FromDateTime(DateTime.UtcNow)}";
var allowed = await cache.CheckRateLimitAsync(rlKey, 20, CacheTtl.UntilMidnightUtc(), ct);
if (!allowed)
{
    http.Response.Headers["Retry-After"] = ((int)CacheTtl.UntilMidnightUtc().TotalSeconds).ToString();
    return Results.StatusCode(429);
}
```

**Limit:** 20 tokens per user per day  
**Reset:** Midnight UTC  
**Response:** `429 Too Many Requests` with `Retry-After` header

**Why 20?**
- Voice notes: 10-15 per day typical
- Profile photos: 3-6 during onboarding
- Tile media (future): 5-10 per day
- **Total:** 20 covers typical + safety margin

**Prevents:**
- Storage spam attacks
- Runaway upload loops (client bugs)
- Abuse (uploading GBs of junk data)

### Blob Upload (Azure-side)

**Azure Blob Storage limits:**
- 20,000 requests/second per storage account
- 60 MB/second ingress per storage account

**Woven usage (1000 users, peak):**
- 1000 users × 5 voice notes/day = 5000 uploads/day
- 5000 uploads ÷ 86400 seconds = 0.06 uploads/second
- **Far below limit** — no Azure-side throttling expected

---

## Error Handling

### SAS Token Errors (Client → Azure Blob)

| Error | HTTP Status | Cause | Recovery |
|---|---|---|---|
| **AuthenticationFailed** | 403 | Signature invalid | Bug in token generation, report to dev |
| **SAS token expired** | 403 | Upload took > 15 min | Request new token, re-upload |
| **Insufficient permissions** | 403 | SAS missing required permission | Bug in SasBuilder config |
| **Blob already exists** | 409 | Overwrite=false, blob exists | Bug (shouldn't happen with GUID paths) |
| **Request timeout** | 500 | Network issue or blob storage down | Retry with exponential backoff |

**Client-side handling:**

```typescript
try {
  await fetch(uploadUrl, {
    method: 'PUT',
    headers: { 'x-ms-blob-type': 'BlockBlob', 'Content-Type': contentType },
    body: blob
  });
} catch (err) {
  if (err.status === 403) {
    // SAS expired or invalid → request new token
    const newToken = await this.media.getUploadToken(container, fileName, contentType);
    await fetch(newToken.uploadUrl, ...);  // Retry with new token
  } else {
    throw err;  // Other error (500, network, etc.)
  }
}
```

### API Endpoint Errors

| Error | HTTP Status | Cause | Recovery |
|---|---|---|---|
| **Unknown container** | 400 | Invalid container name | Client validation bug |
| **Missing fileName** | 400 | Required field omitted | Client validation bug |
| **Rate limit exceeded** | 429 | > 20 tokens/day | Wait until midnight UTC |
| **Storage unavailable** | 503 | Azure Blob down | Retry after 1 minute |

---

## Token Lifecycle

### Full Upload Timeline

```
T+0s    Client: POST /media/upload-token
T+0.2s  Server: Generate SAS (expires T+900s)
T+0.3s  Client: Receive token, start blob upload
T+5s    Client: Upload completes (5 MB @ 8 Mbps)
T+5.1s  Client: POST /media/confirm
T+5.3s  Server: Verify blob exists, return URL
T+5.4s  Client: Create chat message with audio URL

Token remaining: 894.6 seconds (still valid)
```

**Token never reused** — each upload gets a fresh SAS token.

### Expiry Behavior

**What happens at expiry (T+900s)?**

**If upload in progress:**
- Azure Blob: Upload continues (permission granted when upload started)
- Result: Upload succeeds even if finishes after expiry

**If upload not started:**
- Azure Blob: Returns 403 Forbidden
- Client: Must request new token

**Confirm step:**
- No SAS token needed (uses server-side blob client)
- Can confirm hours after upload (no time limit)

### Token Storage

**Client-side:** Store SAS token in memory only (never localStorage)

**Why?**
- **Security:** Token grants write access, shouldn't persist
- **Expiry:** 15 min makes caching pointless
- **One-time use:** Each upload uses fresh token

**Server-side:** No storage — generated on-demand, stateless

---

## Advanced Topics

### Account Key vs. User Delegation SAS

**Current implementation:** Account Key SAS

**Account Key SAS:**
- ✅ Simple (no extra setup)
- ✅ Works with any authentication
- ❌ Requires storage account key access
- ❌ Key rotation invalidates all SAS tokens

**User Delegation SAS (future):**
- ✅ Uses Azure AD identity (no storage key)
- ✅ Key rotation doesn't affect SAS
- ✅ Better audit trail
- ❌ Requires Azure AD integration
- ❌ More complex setup

**Migration plan (Phase 2):**
1. Integrate Azure Managed Identity for API server
2. Grant API server Blob Contributor role
3. Switch to `BlobServiceClient(new DefaultAzureCredential())`
4. Use `blobClient.GenerateUserDelegationSasUri()`

### SAS Token Revocation

**Problem:** Once issued, SAS tokens cannot be revoked before expiry.

**Current mitigation:**
- Short expiry (15 min)
- Blob-level SAS (limits scope to single file)
- Ownership checks on confirm (prevents malicious use)

**Future solution (if needed):**
- Stored Access Policy (container-level policy, can be revoked)
- Trade-off: More complex, slower token generation

### CORS Configuration

**Required for browser uploads:**

```json
{
  "CorsRules": [
    {
      "AllowedOrigins": ["https://wooven.me", "https://*.wooven.me"],
      "AllowedMethods": ["GET", "PUT", "OPTIONS"],
      "AllowedHeaders": ["*"],
      "ExposedHeaders": ["*"],
      "MaxAgeInSeconds": 3600
    }
  ]
}
```

**Why needed?**
- Browsers enforce CORS for cross-origin requests
- Blob storage is different origin than API server
- `OPTIONS` preflight requests must succeed

**Azure Blob CORS response headers:**
```http
Access-Control-Allow-Origin: https://wooven.me
Access-Control-Allow-Methods: GET, PUT, OPTIONS
Access-Control-Allow-Headers: Content-Type, x-ms-blob-type
Access-Control-Max-Age: 3600
```

---

## Performance Considerations

### Token Generation Time

**Benchmark (local):**
- `GetUploadTokenAsync()`: ~50ms
  - GetBlobClient: ~1ms
  - CreateIfNotExistsAsync: ~30ms (first call), ~1ms (cached)
  - GenerateSasUri: ~5ms (HMAC computation)
  - Logging: ~5ms

**Production (cold start):**
- First request: ~200ms (container creation + network latency)
- Subsequent: ~80ms

**Optimization:** Container pre-creation in deployment script (eliminates 30ms).

### Concurrent Token Requests

**Scenario:** Onboarding user uploads 6 photos simultaneously.

**Sequential:**
```typescript
for (const photo of photos) {
  const token = await getUploadToken(photo);  // 80ms each
  await uploadBlob(token, photo);             // 2s each
}
// Total: (80ms + 2s) × 6 = 12.5s
```

**Parallel:**
```typescript
const tokens = await Promise.all(photos.map(p => getUploadToken(p)));  // 80ms total
await Promise.all(tokens.map((t, i) => uploadBlob(t, photos[i])));     // 2s total
// Total: 2.08s (6× faster)
```

**Backend handles parallel requests** — `BlobServiceClient` is thread-safe.

### Caching Tokens

**Bad idea:**
```typescript
// ❌ DON'T: Reuse SAS token for multiple uploads
const token = await getUploadToken(...);
await uploadBlob1(token);
await uploadBlob2(token);  // Wrong blobPath, upload fails
```

**SAS tokens are blob-specific** — each upload needs a unique token.

**No caching needed** — token generation is fast (<100ms).

---

## Monitoring

### Metrics to Track

| Metric | Threshold | Alert |
|---|---|---|
| **Token generation time** | p95 > 200ms | Slow container creation |
| **Token expiry rate** | > 1% of uploads | Slow connections or UX issue |
| **403 errors on blob PUT** | > 0.1% | SAS signature bugs |
| **Rate limit hits** | > 10/day | Possible abuse |

### Logging

**MediaService logs:**
```csharp
_logger.LogInformation("[Media] SAS token issued for {BlobPath} (expires {ExpiresAt})", 
    blobPath, expiresAt);
```

**Log aggregation query (Application Insights):**
```kql
traces
| where message contains "[Media] SAS token issued"
| summarize count() by bin(timestamp, 1h)
```

**Correlation:** Every token request includes X-Correlation-ID, logged in same entry.

---

## Testing

### Unit Tests

**Token generation:**
```csharp
[Fact]
public async Task GetUploadToken_GeneratesValidSas()
{
    var service = new MediaService(blobService, moderation, trust, logger);
    
    var result = await service.GetUploadTokenAsync(
        userId: 1042,
        container: MediaContainerType.VoiceNote,
        fileName: "test.webm",
        contentType: "audio/webm");
    
    Assert.NotNull(result.SasToken);
    Assert.Contains("voice-notes/1042/", result.BlobPath);
    Assert.True(result.ExpiresAt > DateTimeOffset.UtcNow);
    Assert.True(result.ExpiresAt < DateTimeOffset.UtcNow.AddMinutes(16));
}
```

**Ownership prefix:**
```csharp
[Fact]
public void BlobPath_ContainsUserId()
{
    var service = new MediaService(...);
    var result = await service.GetUploadTokenAsync(userId: 1042, ...);
    
    Assert.StartsWith("1042/", result.BlobPath);
}
```

### Integration Tests

**Full upload flow:**
```csharp
[Fact]
public async Task UploadFlow_EndToEnd()
{
    // 1. Request token
    var tokenResponse = await client.PostAsJsonAsync("/media/upload-token", new
    {
        container = "voice-note",
        fileName = "test.webm",
        contentType = "audio/webm"
    });
    var token = await tokenResponse.Content.ReadFromJsonAsync<UploadTokenResponse>();
    
    // 2. Upload to blob storage
    var blobResponse = await httpClient.PutAsync(token.UploadUrl, new ByteArrayContent(testBlob));
    Assert.Equal(HttpStatusCode.Created, blobResponse.StatusCode);
    
    // 3. Confirm upload
    var confirmResponse = await client.PostAsJsonAsync("/media/confirm", new
    {
        blobPath = token.BlobPath,
        container = "voice-note"
    });
    var confirmed = await confirmResponse.Content.ReadFromJsonAsync<ConfirmResponse>();
    
    Assert.True(confirmed.Success);
    Assert.NotNull(confirmed.Url);
}
```

**Expiry handling:**
```csharp
[Fact]
public async Task ExpiredToken_Returns403()
{
    var token = await service.GetUploadTokenAsync(...);
    
    // Wait for expiry (or mock time)
    await Task.Delay(TimeSpan.FromMinutes(16));
    
    var response = await httpClient.PutAsync(token.UploadUrl, new ByteArrayContent(blob));
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
}
```

---

## Security Best Practices

### ✅ Do

- **Unique blob paths** — Use GUIDs to prevent collisions
- **Minimal permissions** — Write + Create only, no Read/Delete
- **Short expiry** — 15 min balances UX and security
- **Ownership checks** — Verify userId prefix on confirm
- **Rate limiting** — Prevent token spam (20/day)
- **HTTPS only** — Never generate SAS for `http://` endpoints

### ❌ Don't

- **Don't reuse tokens** — Each upload gets fresh SAS
- **Don't cache tokens client-side** — Expiry makes caching pointless
- **Don't log SAS tokens** — Signature is secret, never log full token
- **Don't extend expiry** — Longer validity = higher leak risk
- **Don't grant container-level SAS** — Blob-level only (limits scope)

### Storage Account Key Protection

**Never commit keys to git:**
- ✅ User Secrets (local dev)
- ✅ Azure Key Vault (production)
- ❌ appsettings.json
- ❌ Environment variables in Dockerfile

**Access control:**
- Rotate keys every 90 days
- Use Managed Identity instead (future)
- Monitor access logs for anomalies

---

## Related Documentation

- [Media System Overview](./README.md)
- [Azure Blob Storage](./azure-blob.md) — Storage account setup
- [Voice Notes](./voice-notes.md) — SAS token usage example
- [Photos](./photos.md) — Alternative upload pattern (data URL)
- [API Reference](./api.md) — Full endpoint specs

**Implementation Files:**
- `Services/MediaService.cs` (lines 27-62) — Token generation
- `Endpoints/MediaEndpoints.cs` (lines 30-69) — Token endpoint
- `Services/IMediaService.cs` — Interface & DTOs
- `SECRETS_SETUP.md` — Storage account key management

**External Resources:**
- [Azure Blob SAS Documentation](https://learn.microsoft.com/en-us/azure/storage/common/storage-sas-overview)
- [SAS Best Practices](https://learn.microsoft.com/en-us/azure/storage/common/sas-best-practices)
