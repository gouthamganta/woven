# Media API Reference

**Last Updated:** 2026-10-07

---

## Upload Flow

### 1. Request Upload Token

**POST /media/upload-token**

Request a SAS token for uploading a file.

**Request:**
```json
{
  "fileName": "photo.jpg",
  "contentType": "image/jpeg",
  "mediaType": "photo"
}
```

**Response (200 OK):**
```json
{
  "uploadUrl": "https://wovenprodblobs.blob.core.windows.net/photos/42/abc123.jpg?sv=2021-06-08&se=2026-10-07T12%3A45%3A00Z&sr=b&sp=w&sig=...",
  "blobName": "42/abc123.jpg",
  "expiresAt": "2026-10-07T12:45:00Z"
}
```

**Parameters:**
- `fileName`: Original file name (for extension validation)
- `contentType`: MIME type (validated against allowed types)
- `mediaType`: `"photo"` | `"voice"` | `"tile-photo"` | `"tile-voice"`

**Token Lifetime:** 15 minutes

---

### 2. Upload to Azure Blob

**PUT {uploadUrl}**

Upload the file directly to Azure Blob Storage.

**Headers:**
```
Content-Type: image/jpeg
x-ms-blob-type: BlockBlob
```

**Body:** Binary file content

**Response (201 Created):**
```
(Empty body, Azure Blob response)
```

---

### 3. Confirm Upload

**POST /media/confirm**

Confirm the upload succeeded and persist metadata.

**Request:**
```json
{
  "blobName": "42/abc123.jpg",
  "mediaType": "photo",
  "contentType": "image/jpeg",
  "sizeBytes": 245678
}
```

**Response (200 OK):**
```json
{
  "mediaUrl": "https://wovenprodblobs.blob.core.windows.net/photos/42/abc123.jpg",
  "mediaId": "abc123"
}
```

**What happens:**
- Verifies blob exists in Azure
- Saves media record to database
- Returns permanent URL

---

## Voice Note Upload

Same 3-step flow, with `mediaType: "voice"`:

**1. Request token:**
```json
{
  "fileName": "recording.webm",
  "contentType": "audio/webm",
  "mediaType": "voice"
}
```

**2. Upload:** PUT to Azure (binary webm)

**3. Confirm:**
```json
{
  "blobName": "voice-notes/thread-123/msg-456.webm",
  "mediaType": "voice",
  "contentType": "audio/webm",
  "sizeBytes": 34567,
  "durationSeconds": 12.5
}
```

**Then send message:**
```
POST /chats/{threadId}/voice-message
{
  "audioUrl": "https://wovenprodblobs.blob.core.windows.net/voice-notes/...",
  "durationSeconds": 12.5
}
```

---

## Allowed Media Types

**Photos:**
- `image/jpeg` (max 10MB)
- `image/png` (max 10MB)
- `image/webp` (max 10MB)

**Voice Notes:**
- `audio/webm` (max 5MB, max 180 seconds)
- `audio/mp4` (max 5MB, max 180 seconds)
- `audio/mpeg` (max 5MB, max 180 seconds)

**Tiles:**
- Photo: same as above
- Voice: same as above

---

## Error Responses

**400 Bad Request:**
```json
{
  "error": "Invalid content type",
  "correlationId": "a1b2c3d4"
}
```

**413 Payload Too Large:**
```json
{
  "error": "File size exceeds 10MB limit",
  "correlationId": "a1b2c3d4"
}
```

**500 Internal Server Error:**
```json
{
  "error": "Failed to generate upload token",
  "correlationId": "a1b2c3d4"
}
```

---

## Frontend Integration

**Example (TypeScript):**
```typescript
async function uploadPhoto(file: File): Promise<string> {
  // 1. Request upload token
  const tokenRes = await fetch('/media/upload-token', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      fileName: file.name,
      contentType: file.type,
      mediaType: 'photo'
    })
  });
  const { uploadUrl, blobName } = await tokenRes.json();

  // 2. Upload to Azure Blob
  await fetch(uploadUrl, {
    method: 'PUT',
    headers: {
      'Content-Type': file.type,
      'x-ms-blob-type': 'BlockBlob'
    },
    body: file
  });

  // 3. Confirm upload
  const confirmRes = await fetch('/media/confirm', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      blobName,
      mediaType: 'photo',
      contentType: file.type,
      sizeBytes: file.size
    })
  });
  const { mediaUrl } = await confirmRes.json();

  return mediaUrl;
}
```

---

## Security

**SAS Token Permissions:**
- Write-only (`sp=w`)
- 15-minute expiry
- Specific blob path (cannot overwrite others)

**Validation:**
- Content-Type header required
- File size validated client-side before upload
- MIME type validated server-side

**No Auth on Reads:**
- Blob URLs are public-read
- Obscurity as privacy (userId + GUID in path)

---

## Related Documentation

- [Photo Upload Flow](photos.md)
- [Voice Note Flow](voice-notes.md)
- [Azure Blob Storage](azure-blob.md)
- [SAS Token Generation](sas-tokens.md)
- [CDN Delivery](cdn.md)
