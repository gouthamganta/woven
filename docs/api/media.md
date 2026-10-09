# Media API Reference

**Base URL:** `https://api.wooven.me` (prod) / `http://localhost:5135` (dev)

---

## Overview

Media upload uses Azure Blob Storage with SAS tokens for direct client-to-storage uploads (keeps binary traffic off API server).

**Upload Flow:**
1. Request SAS token from API (`/media/upload-token`)
2. Upload file directly to Azure Blob Storage
3. Confirm upload with API (`/media/confirm`)

**Containers:**
- `profile-photo` — User profile photos
- `tile-media` — Commons content photos/videos
- `voice-note` — Voice message audio files

**Source:** [`backend/WovenBackend/Endpoints/MediaEndpoints.cs`](../../backend/WovenBackend/Endpoints/MediaEndpoints.cs)

---

## Endpoints

### POST /media/upload-token

**Description:** Get short-lived SAS token for direct blob upload.

**Authentication:** Required (JWT)

**Rate Limit:** 20 tokens per user per calendar day

**Request:**
```json
{
  "container": "profile-photo",
  "fileName": "photo1.jpg",
  "contentType": "image/jpeg"
}
```

**Containers:**
- `profile-photo`
- `tile-media`
- `voice-note`

**Response (200 OK):**
```json
{
  "sasToken": "?sv=2021-06-08&se=2026-10-07T16:00:00Z&sr=b&sp=w&sig=...",
  "blobPath": "123/profile-photo/uuid.jpg",
  "uploadUrl": "https://storage.blob.core.windows.net/media/123/profile-photo/uuid.jpg?sv=...",
  "expiresAt": "2026-10-07T16:00:00Z"
}
```

**Fields:**
- `sasToken` — SAS query string (append to `blobPath`)
- `blobPath` — Blob path within container
- `uploadUrl` — Full URL for PUT request (includes SAS)
- `expiresAt` — Token expiration (typically 1 hour)

**Errors:**
- `400` — Unknown container
- `400` — Missing fileName or contentType
- `429` — Rate limit exceeded (20 tokens per day)

**Source:** [`MediaEndpoints.cs:30-68`](../../backend/WovenBackend/Endpoints/MediaEndpoints.cs)

---

### POST /media/confirm

**Description:** Confirm upload completed and get public URL.

**Authentication:** Required (JWT)

**Request:**
```json
{
  "container": "profile-photo",
  "blobPath": "123/profile-photo/uuid.jpg"
}
```

**Ownership Check:**
- `blobPath` must start with `{userId}/` (prevents users confirming others' uploads)

**Response (200 OK):**
```json
{
  "success": true,
  "url": "https://storage.blob.core.windows.net/media/123/profile-photo/uuid.jpg"
}
```

**Response (422 Unprocessable Entity):**
```json
{
  "success": false,
  "error": "BLOB_NOT_FOUND"
}
```

**Errors:**
- `400` — Unknown container or missing blobPath
- `403 Forbidden` — Blob path does not belong to user
- `422 BLOB_NOT_FOUND` — Blob does not exist (upload failed)

**Source:** [`MediaEndpoints.cs:74-99`](../../backend/WovenBackend/Endpoints/MediaEndpoints.cs)

---

## Upload Flow

### 1. Client Requests Token

```typescript
const tokenResponse = await fetch('/media/upload-token', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${accessToken}`,
    'Content-Type': 'application/json'
  },
  body: JSON.stringify({
    container: 'profile-photo',
    fileName: 'photo1.jpg',
    contentType: 'image/jpeg'
  })
});

const { uploadUrl, blobPath } = await tokenResponse.json();
```

---

### 2. Upload to Azure Blob Storage

**HTTP PUT with blob content:**
```typescript
const file = document.querySelector('input[type="file"]').files[0];

await fetch(uploadUrl, {
  method: 'PUT',
  headers: {
    'x-ms-blob-type': 'BlockBlob',
    'Content-Type': 'image/jpeg'
  },
  body: file
});
```

**Note:** No `Authorization` header needed (SAS token in URL handles auth).

---

### 3. Confirm Upload

```typescript
const confirmResponse = await fetch('/media/confirm', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${accessToken}`,
    'Content-Type': 'application/json'
  },
  body: JSON.stringify({
    container: 'profile-photo',
    blobPath: blobPath
  })
});

const { url } = await confirmResponse.json();
// url is the public CDN URL for the uploaded file
```

---

## Blob Path Format

**Pattern:** `{userId}/{container}/{uuid}.{ext}`

**Examples:**
- `123/profile-photo/a1b2c3d4.jpg`
- `456/tile-media/e5f6g7h8.mp4`
- `789/voice-note/i9j0k1l2.webm`

**Ownership Enforcement:**
```csharp
// Confirms blob path starts with userId
if (!req.BlobPath.StartsWith($"{userId}/"))
    return Results.Forbid();
```

---

## SAS Token Security

**Permissions:** Write-only (`sp=w`)

**Expiration:** 1 hour from issue

**Scope:** Single blob (`sr=b`)

**Generation:**
```csharp
var sasBuilder = new BlobSasBuilder
{
    BlobContainerName = containerName,
    BlobName = blobPath,
    Resource = "b",
    StartsOn = DateTimeOffset.UtcNow,
    ExpiresOn = DateTimeOffset.UtcNow.AddHours(1)
};
sasBuilder.SetPermissions(BlobSasPermissions.Write);
```

**Source:** [`MediaService.cs`](../../backend/WovenBackend/Services/MediaService.cs)

---

## Rate Limiting

**Limit:** 20 upload tokens per user per calendar day

**Resets:** Midnight UTC

**429 Response:**
```json
{
  "error": "Rate limit exceeded",
  "retryAfter": 43200,
  "correlationId": "..."
}
```

**Headers:**
```http
Retry-After: 43200
```

---

## Supported File Types

**Profile Photos:**
- `image/jpeg`
- `image/png`
- `image/webp`

**Tile Media:**
- Images: `image/jpeg`, `image/png`, `image/webp`
- Videos: `video/mp4`, `video/webm`

**Voice Notes:**
- `audio/webm`
- `audio/ogg`
- `audio/mp4`

---

**Last Updated:** 2026-10-07  
**Source Files:**
- `backend/WovenBackend/Endpoints/MediaEndpoints.cs`
- `backend/WovenBackend/Services/MediaService.cs`
