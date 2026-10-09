# Azure Blob Storage Integration

**Last Updated:** 2026-10-07  
**Storage Account:** `wovenprodacr` (production)  
**Local Dev:** Azurite (Azure Storage Emulator)

---

## Overview

Azure Blob Storage is Woven's binary file storage backend. Handles all photos, voice notes, and future tile media with direct client uploads to keep the API server lightweight.

**Why Azure Blob Storage?**
- **Scalability:** Handles TBs without API server load
- **Cost:** $0.018/GB/month (hot tier), cheaper than RDS
- **CDN integration:** Native Azure CDN support
- **SAS tokens:** Secure, time-limited upload URLs
- **SDK quality:** Official .NET client with excellent docs

---

## Architecture

```
┌──────────────┐
│   Client     │
└──────┬───────┘
       │ 1. POST /media/upload-token
       │
       ▼
┌──────────────┐    2. Generate SAS token
│  API Server  ├──────────────────────────┐
│ (Woven API)  │                          │
└──────────────┘                          ▼
                                  ┌────────────────┐
       ┌──────────────────────────┤BlobServiceClient│
       │ 3. PUT (direct upload)   └────────────────┘
       │                                  │
       ▼                                  ▼
┌──────────────┐              ┌────────────────────┐
│   Client     │              │ Azure Blob Storage │
└──────┬───────┘              │  - profile-photos  │
       │                      │  - voice-notes     │
       │ 4. POST /media/confirm │  - tile-media      │
       │                      └────────────────────┘
       ▼
┌──────────────┐    5. Verify blob exists
│  API Server  ├──────────────────────────┐
└──────────────┘                          │
                                          ▼
                                  ┌────────────────┐
                                  │BlobContainerClient│
                                  └────────────────┘
```

**Key principle:** Clients upload directly to Azure Blob Storage, API server only orchestrates (tokens + verification).

---

## Storage Account Setup

### Production Configuration

**Resource Group:** `woven-prod-rg`  
**Storage Account:** `wovenprodacr`  
**Region:** East US (same as API server)  
**Tier:** Standard (General Purpose v2)  
**Replication:** LRS (Locally Redundant Storage)

**Why LRS?**
- Cost: 3× cheaper than GRS
- Media is non-critical (can be re-uploaded)
- 99.999999999% (11 nines) durability within single region

**Performance tier:** Hot (frequently accessed)  
**Phase 2:** Lifecycle policy to move to Cool after 7 days

### Local Development

**Azurite** (Azure Storage Emulator)

**Installation:**
```bash
npm install -g azurite
```

**Start Azurite:**
```bash
azurite --location c:\azurite --debug c:\azurite\debug.log
```

**Default endpoints:**
- Blob: `http://127.0.0.1:10000/devstoreaccount1`
- Queue: `http://127.0.0.1:10001/devstoreaccount1`
- Table: `http://127.0.0.1:10002/devstoreaccount1`

**Connection string:**
```
UseDevelopmentStorage=true
```

**Storage Explorer:**
- Microsoft Azure Storage Explorer (GUI)
- Connect to local Azurite via connection string
- Inspect containers, blobs, properties

---

## Containers

### Container Structure

All containers have **public read access** (anonymous blob read, no SAS needed for GET).

| Container | Purpose | Access Level | Retention | Moderation |
|---|---|---|---|---|
| `profile-photos` | User profile pictures | Public read | Permanent | ✅ Auto + manual |
| `voice-notes` | Chat audio messages | Public read | 90 days | ❌ |
| `tile-media` | Commons content (future) | Public read | Permanent | ✅ Queue for review |

### Why Public Read?

**Pros:**
- No SAS token needed for playback
- CDN caching works seamlessly
- Simpler client-side code

**Cons:**
- Blobs enumerable if path known

**Security:** Blob paths use UUIDs (not sequential IDs), making enumeration impractical.

### Container Creation

**Auto-created on first upload:**
```csharp
// MediaService.cs:38-39
var containerClient = _blobService.GetBlobContainerClient(containerName);
await containerClient.CreateIfNotExistsAsync(cancellationToken: ct);
```

**Access level:** Set in Azure Portal or via Terraform:
```hcl
resource "azurerm_storage_container" "profile_photos" {
  name                  = "profile-photos"
  storage_account_name  = azurerm_storage_account.woven.name
  container_access_type = "blob"  # Public read
}
```

---

## Connection Configuration

### appsettings.json Structure

**Production (Azure Key Vault):**
```json
{
  "Azure": {
    "Storage": {
      "ConnectionString": "@Microsoft.KeyVault(SecretUri=https://woven-kv.vault.azure.net/secrets/StorageConnectionString)"
    }
  }
}
```

**Local Development (User Secrets):**
```bash
dotnet user-secrets set "Azure:Storage:ConnectionString" "UseDevelopmentStorage=true"
```

**Connection string format (production):**
```
DefaultEndpointsProtocol=https;AccountName=wovenprodacr;AccountKey={key};EndpointSuffix=core.windows.net
```

**Never commit connection strings** to git (use User Secrets or Key Vault).

**See:** `SECRETS_SETUP.md` for full key vault integration.

### Service Registration

**Program.cs:488-492:**
```csharp
builder.Services.AddSingleton<BlobServiceClient>(sp =>
    new BlobServiceClient(builder.Configuration["Azure:Storage:ConnectionString"]
        ?? throw new InvalidOperationException("Azure:Storage:ConnectionString is required")));
builder.Services.AddScoped<IMediaService, MediaService>();
builder.Services.AddHostedService<MediaLifecycleWorker>();
```

**Why singleton?**  
`BlobServiceClient` is thread-safe and internally connection-pools. Scoping it would create unnecessary instances.

---

## Blob Operations

### Upload (Client → Blob Storage)

**Direct upload** via SAS token (no API server in the middle).

**HTTP Request:**
```http
PUT https://wovenprodacr.blob.core.windows.net/voice-notes/1042/a7b3c9d1.webm?{sasToken}
Content-Type: audio/webm
x-ms-blob-type: BlockBlob
Content-Length: 245760

[binary data]
```

**Required headers:**
- `x-ms-blob-type: BlockBlob` — Azure requirement
- `Content-Type: {mimeType}` — Stored as blob metadata, returned on GET

**Response:**
```http
HTTP/1.1 201 Created
x-ms-request-id: ...
x-ms-version: 2024-11-04
Date: Mon, 07 Oct 2024 12:00:00 GMT
```

**Error responses:**
- `401 Unauthorized` — SAS token expired or invalid
- `403 Forbidden` — SAS permissions insufficient
- `409 Conflict` — Blob already exists (overwrite=false)
- `413 Payload Too Large` — Exceeds container limit

### Verify Existence

**MediaService.cs:76-81:**
```csharp
var blobClient = _blobService
    .GetBlobContainerClient(ContainerName(container))
    .GetBlobClient(blobPath);

var exists = await blobClient.ExistsAsync(ct);
if (!exists.Value)
{
    _logger.LogWarning("[Media] ConfirmUpload: blob not found at {BlobPath}", blobPath);
    return new ConfirmUploadResult(false, null, "BLOB_NOT_FOUND");
}
```

**Why verify?**  
Client might call confirm before upload completes (race condition), or upload might fail silently. Verification prevents broken media URLs in database.

### Retrieve URL

**MediaService.cs:132-141:**
```csharp
public Task<string> GetMediaUrlAsync(string blobPath, MediaContainerType container, ...)
{
    // Dev: direct storage / Azurite URL.
    // Prod: same for now — swap hostname for CDN when Phase 2 configures it.
    var blobClient = _blobService
        .GetBlobContainerClient(ContainerName(container))
        .GetBlobClient(blobPath);

    return Task.FromResult(blobClient.Uri.ToString());
}
```

**Current behavior:** Returns direct blob storage URL  
**Phase 2:** Replace hostname with CDN endpoint

**Example URLs:**
- Local: `http://127.0.0.1:10000/devstoreaccount1/voice-notes/1042/a7b3c9d1.webm`
- Prod: `https://wovenprodacr.blob.core.windows.net/voice-notes/1042/a7b3c9d1.webm`
- CDN: `https://cdn.wooven.me/voice-notes/1042/a7b3c9d1.webm` (future)

### Delete Blob

**MediaService.cs:115-130:**
```csharp
public async Task DeleteMediaAsync(string blobPath, MediaContainerType container, ...)
{
    try
    {
        var blobClient = _blobService
            .GetBlobContainerClient(ContainerName(container))
            .GetBlobClient(blobPath);

        await blobClient.DeleteIfExistsAsync(cancellationToken: ct);
        _logger.LogInformation("[Media] Deleted {BlobPath}", blobPath);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "[Media] DeleteMediaAsync failed for {BlobPath}", blobPath);
    }
}
```

**Endpoint:** `DELETE /media/{container}/{**blobPath}`

**Ownership check** (MediaEndpoints.cs:117):
```csharp
if (!blobPath.StartsWith($"{userId}/"))
    return Results.Forbid();
```

Only the blob owner (userId in path prefix) can delete.

### Bulk Delete (User Account Deletion)

**MediaService.cs:143-171:**
```csharp
public async Task DeleteAllForUserAsync(int userId, CancellationToken ct = default)
{
    var containers = new[]
    {
        MediaContainerType.ProfilePhoto,
        MediaContainerType.TileMedia,
        MediaContainerType.VoiceNote
    };

    foreach (var container in containers)
    {
        var containerClient = _blobService.GetBlobContainerClient(ContainerName(container));
        var prefix = $"{userId}/";
        
        await foreach (var blob in containerClient.GetBlobsAsync(prefix: prefix, cancellationToken: ct))
        {
            await containerClient.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: ct);
        }
        
        _logger.LogInformation("[Media] DeleteAllForUser: deleted {Container} blobs for user {UserId}",
            container, userId);
    }
}
```

**Triggered by:** User account deletion (GDPR compliance)

**Pagination:** `GetBlobsAsync()` auto-paginates (Azure returns max 5000 per request).

---

## Blob Path Conventions

### Path Structure

**Pattern:** `{userId}/{guid}{ext}`

**Examples:**
- Profile photo: `1042/a7b3c9d1-4e2f-5a6b-7c8d-9e0f1a2b3c4d.jpg`
- Voice note: `1042/f8e7d6c5-b4a3-9281-7061-5040302010ab.webm`
- Tile media: `1042/12345678-1234-1234-1234-123456789abc.mp4`

**Why userId prefix?**
- **Ownership verification:** Quick check on confirm/delete
- **Bulk deletion:** List all blobs for user with `prefix: "{userId}/"`
- **Organized storage:** Files grouped by user (debugging, analytics)

**Why GUID?**
- **Collision-free:** No filename conflicts
- **Non-enumerable:** Can't guess other users' blob paths
- **Metadata-free:** No PII in filename

**Extension preserved:** Required for MIME type inference (mobile OS, browser).

### Container Name Mapping

**MediaService.cs:173-179:**
```csharp
private static string ContainerName(MediaContainerType type) => type switch
{
    MediaContainerType.ProfilePhoto => "profile-photos",
    MediaContainerType.TileMedia    => "tile-media",
    MediaContainerType.VoiceNote    => "voice-notes",
    _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
};
```

**Enum → container name mapping** ensures consistency across codebase.

---

## Lifecycle Management

### Retention Policies

**Current state:** No auto-deletion (all blobs permanent except manual delete)

**Phase 2 policies:**

```json
{
  "rules": [
    {
      "name": "delete-old-voice-notes",
      "enabled": true,
      "type": "Lifecycle",
      "definition": {
        "filters": {
          "blobTypes": ["blockBlob"],
          "prefixMatch": ["voice-notes/"]
        },
        "actions": {
          "baseBlob": {
            "delete": {
              "daysAfterModificationGreaterThan": 90
            }
          }
        }
      }
    },
    {
      "name": "cool-old-profile-photos",
      "enabled": true,
      "type": "Lifecycle",
      "definition": {
        "filters": {
          "blobTypes": ["blockBlob"],
          "prefixMatch": ["profile-photos/"]
        },
        "actions": {
          "baseBlob": {
            "tierToCool": {
              "daysAfterModificationGreaterThan": 30
            }
          }
        }
      }
    }
  ]
}
```

**Rules:**
1. **Voice notes:** Delete after 90 days (ephemeral chat content)
2. **Profile photos:** Move to Cool tier after 30 days (rarely accessed after initial matches)

**Cost savings:**
- Cool tier: $0.01/GB (vs. $0.018/GB hot)
- 90-day voice note deletion: Prevents unbounded storage growth

### Orphaned Blob Cleanup

**Worker:** `MediaLifecycleWorker.cs` (background service)

**Purpose:** Delete blobs that exist in storage but have no DB reference.

**Causes of orphaned blobs:**
1. Confirm step failed (blob uploaded, DB save threw)
2. User deleted account (DB row removed, blob left behind)
3. Test uploads never confirmed

**Worker logic (future implementation):**
```csharp
protected override async Task ExecuteAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        await Task.Delay(TimeSpan.FromDays(1), ct);  // Daily run
        
        // Find blobs older than 24 hours with no DB reference
        var containers = new[] { "profile-photos", "voice-notes", "tile-media" };
        
        foreach (var containerName in containers)
        {
            var container = _blobService.GetBlobContainerClient(containerName);
            await foreach (var blob in container.GetBlobsAsync(cancellationToken: ct))
            {
                if (blob.Properties.CreatedOn < DateTimeOffset.UtcNow.AddHours(-24))
                {
                    var referenced = await CheckDbReference(blob.Name, ct);
                    if (!referenced)
                    {
                        await container.DeleteBlobIfExistsAsync(blob.Name, ct);
                        _logger.LogInformation("[MediaLifecycle] Deleted orphaned blob: {BlobPath}", 
                            blob.Name);
                    }
                }
            }
        }
    }
}
```

**DB reference check:**
- Profile photos: `Users.PhotoUrl` or `UserPhotos.BlobPath`
- Voice notes: `ChatMessages.MetaJson.audioUrl`
- Tile media: `Tiles.MediaUrls` (future)

---

## Performance & Optimization

### Connection Pooling

**BlobServiceClient** internally pools HTTP connections (socket reuse).

**Configuration:**
```csharp
var options = new BlobClientOptions
{
    Transport = new HttpClientTransport(new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(30),
        // Default: 100 connections per endpoint
        DefaultRequestHeaders = { { "User-Agent", "Woven/1.0" } }
    }),
    Retry = {
        MaxRetries = 3,
        Delay = TimeSpan.FromSeconds(2),
        Mode = RetryMode.Exponential
    }
};

var blobService = new BlobServiceClient(connectionString, options);
```

**Default behavior** (no custom options):
- Max retries: 3
- Retry delay: Exponential backoff (800ms, 3s, 10s)
- Timeout: 30s per request

### Parallel Uploads

**Multiple blobs in one transaction** (e.g., onboarding 6 photos):

```csharp
var uploadTasks = photos.Select(async photo =>
{
    var blobClient = container.GetBlobClient(photo.Path);
    using var stream = new MemoryStream(photo.Bytes);
    await blobClient.UploadAsync(stream, overwrite: false, ct);
});

await Task.WhenAll(uploadTasks);
```

**Throttling:** Azure Blob supports 20,000 requests/second/account (won't hit limit).

### Caching

**Current:** No CDN, direct blob storage URLs

**Phase 2:** Azure CDN in front of blob storage
- Cache-Control headers set per container
- Profile photos: `max-age=31536000` (1 year, immutable)
- Voice notes: `max-age=604800` (7 days)

**CDN benefits:**
- Reduced blob storage egress costs
- Lower latency (edge caching)
- Burst traffic handling

**See:** [cdn.md](./cdn.md)

---

## Security

### SAS Token Permissions

**Write + Create only** (no Read, no Delete):
```csharp
var sasBuilder = new BlobSasBuilder
{
    BlobContainerName = containerName,
    BlobName = blobPath,
    Resource = "b",  // Blob (not container)
    ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(15)
};
sasBuilder.SetPermissions(BlobSasPermissions.Write | BlobSasPermissions.Create);
```

**Why no Read?**  
Containers are public — no SAS needed for GET. Including Read in SAS grants no extra access.

**Why no Delete?**  
Prevents client from deleting blob after upload. Deletion requires authenticated API call.

**See:** [sas-tokens.md](./sas-tokens.md)

### Network Security

**Production:**
- **Public endpoint:** Required for client uploads (can't be private)
- **Firewall rules:** None (would block mobile users)
- **Private link:** Not used (adds cost, no security benefit for public blobs)

**Dev:**
- **Azurite:** Runs locally, no network exposure

### Access Keys Rotation

**Azure Portal → Storage Account → Access Keys → Regenerate**

**Best practice:** Rotate every 90 days

**Impact:** Existing SAS tokens remain valid (signed with old key until expiry)

**Zero-downtime rotation:**
1. Generate new key2
2. Update Key Vault secret to key2
3. Restart API pods (picks up new key)
4. Wait 15 min (old SAS tokens expire)
5. Regenerate key1 (old key invalidated)

**Future improvement:** Use Azure Managed Identity (no connection string storage).

---

## Monitoring

### Metrics (Azure Portal)

**Storage Account → Monitoring → Metrics**

| Metric | Alert Threshold | Purpose |
|---|---|---|
| **Availability** | < 99.9% | Detect outages |
| **Transactions** | > 100k/hour | Unusual traffic spike |
| **Ingress** | > 10 GB/hour | Possible upload spam |
| **Egress** | > 50 GB/hour | CDN not working |
| **Success E2E Latency** | > 1000ms p95 | Performance degradation |

### Diagnostics Logs

**Enable:**
- Blob storage logs (read/write/delete operations)
- Metrics (hourly aggregates)

**Log retention:** 30 days (cost vs. utility)

**Example log entry:**
```json
{
  "time": "2024-10-07T12:00:00Z",
  "resourceId": "/subscriptions/.../storageAccounts/wovenprodacr",
  "operationName": "PutBlob",
  "resultType": "Success",
  "callerIpAddress": "203.0.113.45",
  "identity": "SAS",
  "uri": "https://wovenprodacr.blob.core.windows.net/voice-notes/1042/a7b3c9d1.webm"
}
```

**Querying logs:**
```kql
StorageBlobLogs
| where TimeGenerated > ago(1h)
| where OperationName == "PutBlob"
| where StatusCode != 201  // Failed uploads
| summarize count() by StatusCode, bin(TimeGenerated, 5m)
```

### Application Logs

**MediaService logs:**
```csharp
_logger.LogInformation("[Media] SAS token issued for {BlobPath} (expires {ExpiresAt})", 
    blobPath, expiresAt);
_logger.LogInformation("[Media] Confirmed upload for {BlobPath}", blobPath);
_logger.LogWarning("[Media] ConfirmUpload: blob not found at {BlobPath}", blobPath);
_logger.LogError(ex, "[Media] DeleteMediaAsync failed for {BlobPath}", blobPath);
```

**Correlation:** Every request has X-Correlation-ID, logged alongside blob operations.

---

## Cost Analysis

### Current Costs (Production)

**Assumptions:**
- 1,000 active users
- 5 voice notes per user per day
- 200 KB average voice note
- 3 profile photos per user (2 MB each)

**Storage:**
- Voice notes (90-day retention): 1000 × 5 × 200 KB × 90 = 90 GB
- Profile photos: 1000 × 3 × 2 MB = 6 GB
- **Total:** 96 GB

**Costs (Hot tier):**
- Storage: 96 GB × $0.018/GB = **$1.73/month**
- Transactions: ~150k writes/month × $0.05/10k = **$0.75/month**
- Egress: 15 GB × $0.087/GB = **$1.31/month**
- **Total:** **~$4/month**

### Phase 2 Optimizations

**Lifecycle policies:**
- Cool tier for photos > 30 days: 6 GB × $0.01/GB = $0.06/month (save $0.04)
- Voice note deletion after 90 days: No unbounded growth

**CDN caching:**
- Reduce blob egress 80%: 15 GB → 3 GB egress = save $1.04/month
- Add CDN cost: $0.08/GB × 15 GB = $1.20/month
- **Net:** $0.16/month savings (plus latency improvement)

**Managed Identity:**
- Eliminate connection string storage
- Free (no cost change)

---

## Disaster Recovery

### Backup Strategy

**Azure Blob Storage durability:** 99.999999999% (11 nines) with LRS

**Backup not needed** — media is:
- Non-critical (can be re-uploaded)
- Already replicated 3× within region (LRS)
- Source of truth is user's device (photos, recordings)

**Exception:** Tile media (future) — users can't re-upload Commons posts  
**Solution:** GRS (Geo-Redundant Storage) for `tile-media` container only

### Disaster Scenarios

| Scenario | Impact | Recovery |
|---|---|---|
| **Regional outage** | Blob storage unavailable | Wait (Azure SLA: 99.9% uptime) |
| **Account key leaked** | Unauthorized access | Rotate keys immediately |
| **Mass deletion** | User data lost | No recovery (no soft delete enabled) |
| **Corruption** | Blob unreadable | User re-uploads |

**Mitigation:** Enable soft delete (30-day retention) for production:
```hcl
resource "azurerm_storage_account" "woven" {
  blob_properties {
    delete_retention_policy {
      days = 30
    }
  }
}
```

**Cost:** Minimal (soft-deleted blobs count toward storage quota).

---

## Terraform Configuration

**Infrastructure as Code** for storage account:

```hcl
resource "azurerm_storage_account" "woven" {
  name                     = "wovenprodacr"
  resource_group_name      = azurerm_resource_group.woven.name
  location                 = azurerm_resource_group.woven.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
  account_kind             = "StorageV2"
  
  blob_properties {
    delete_retention_policy {
      days = 30
    }
    
    cors_rule {
      allowed_origins    = ["https://wooven.me", "https://*.wooven.me"]
      allowed_methods    = ["GET", "PUT", "OPTIONS"]
      allowed_headers    = ["*"]
      exposed_headers    = ["*"]
      max_age_in_seconds = 3600
    }
  }
  
  network_rules {
    default_action = "Allow"
    bypass         = ["AzureServices"]
  }
}

resource "azurerm_storage_container" "profile_photos" {
  name                  = "profile-photos"
  storage_account_name  = azurerm_storage_account.woven.name
  container_access_type = "blob"
}

resource "azurerm_storage_container" "voice_notes" {
  name                  = "voice-notes"
  storage_account_name  = azurerm_storage_account.woven.name
  container_access_type = "blob"
}

resource "azurerm_storage_container" "tile_media" {
  name                  = "tile-media"
  storage_account_name  = azurerm_storage_account.woven.name
  container_access_type = "blob"
}
```

**Deployment:**
```bash
terraform plan -out=tfplan
terraform apply tfplan
```

**State:** Stored in Azure Blob (separate storage account) for team access.

---

## Related Documentation

- [Media System Overview](./README.md)
- [SAS Token Security](./sas-tokens.md)
- [Voice Notes](./voice-notes.md) — Upload flow example
- [Photos](./photos.md) — Moderation integration
- [CDN Strategy](./cdn.md) — Phase 2 delivery
- [API Reference](./api.md)

**Implementation Files:**
- `Services/MediaService.cs` — BlobServiceClient integration
- `Services/Media/MediaLifecycleWorker.cs` — Orphaned blob cleanup
- `Program.cs` (lines 488-493) — Service registration
- `SECRETS_SETUP.md` — Connection string management
