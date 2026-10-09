# Media CDN & Delivery

**Last Updated:** 2026-10-07

---

## Overview

Woven serves all media (photos, voice notes, TTS narrations) through Azure Blob Storage with CDN caching.

**Storage:** Azure Blob Storage (wovenprodblobs)  
**CDN:** Azure CDN (planned, not yet configured)  
**Delivery:** Direct blob URLs with SAS tokens

---

## Current Architecture (No CDN)

**Photo URLs:**
```
https://wovenprodblobs.blob.core.windows.net/photos/{userId}/{photoId}.jpg
```

**Voice Note URLs:**
```
https://wovenprodblobs.blob.core.windows.net/voice-notes/{threadId}/{messageId}.webm
```

**TTS Narration URLs:**
```
https://wovenprodblobs.blob.core.windows.net/tts/{candidateId}/{yyyyMMdd}.mp3
```

**Access:** Public read access (no SAS tokens required for reads)

---

## Planned CDN Architecture

**CDN Endpoint:** `https://cdn.wooven.me/`

**Benefits:**
- Edge caching (reduced latency globally)
- Bandwidth cost reduction
- HTTPS by default
- Custom domain

**Configuration:**
```json
{
  "CDN": {
    "Enabled": true,
    "Endpoint": "https://cdn.wooven.me",
    "CacheControl": {
      "Photos": "public, max-age=31536000, immutable",
      "VoiceNotes": "public, max-age=86400",
      "TTS": "public, max-age=172800"
    }
  }
}
```

---

## Caching Strategy

**Photos (immutable):**
- Cache-Control: `public, max-age=31536000, immutable`
- Once uploaded, never changes
- Safe to cache forever
- Photo updates = new photoId

**Voice Notes:**
- Cache-Control: `public, max-age=86400` (24 hours)
- Rarely re-listened after 24h
- Message deletion = tombstone (404)

**TTS Narrations:**
- Cache-Control: `public, max-age=172800` (48 hours)
- Regenerated daily if missing
- Cache key: `tts/{candidateId}/{yyyyMMdd}.mp3`

**API Responses (metadata):**
- Cache-Control: `no-cache` (always revalidate)
- URLs may change (SAS token rotation)

---

## Image Transformations (Future)

**Thumbnail Generation:**
- On-demand via CDN edge workers
- Query param: `?width=300&quality=80`
- Cached at edge after first request

**Formats:**
- Original: JPEG/PNG (user uploaded)
- WebP: Modern browsers (80% smaller)
- AVIF: Chrome/Edge (90% smaller)

**Responsive Images:**
```html
<img 
  srcset="
    https://cdn.wooven.me/photos/123/abc.jpg?w=400 400w,
    https://cdn.wooven.me/photos/123/abc.jpg?w=800 800w,
    https://cdn.wooven.me/photos/123/abc.jpg?w=1200 1200w
  "
  sizes="(max-width: 600px) 400px, (max-width: 1200px) 800px, 1200px"
  src="https://cdn.wooven.me/photos/123/abc.jpg"
/>
```

---

## Security

**Photo Privacy:**
- Public read URLs (no auth required)
- Obscure blob names (userId + GUID)
- No directory listing

**Voice Note Privacy:**
- Same as photos
- threadId in path (only parties know it)

**SAS Token Uploads:**
- Write-only, 15-minute expiry
- One token per file
- Validates MIME type

---

## Performance

**Current (No CDN):**
- Latency: 200-500ms (Asia to Azure East US)
- Bandwidth: Charged at blob storage egress rates

**With CDN (Planned):**
- Latency: 20-100ms (edge cache hit)
- Bandwidth: CDN egress rates (cheaper)
- Cache hit rate: 85-95% expected

---

## Monitoring

**Metrics to Track:**
- CDN hit rate
- Origin requests (cache misses)
- Edge latency (p50, p95, p99)
- Bandwidth usage (CDN vs origin)
- 4xx/5xx error rates

**Alerts:**
- CDN hit rate < 80%
- Origin requests > 10% of total
- p95 latency > 200ms

---

## Related Documentation

- [Photo Upload](photos.md)
- [Voice Notes](voice-notes.md)
- [Azure Blob Storage](azure-blob.md)
- [SAS Tokens](sas-tokens.md)
- [Media API](api.md)
