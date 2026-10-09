# AI Moderation (OpenAI Moderation API)

**Last Updated:** 2026-10-07  
**Service:** `Services/Moderation/ModerationService.cs`

---

## Overview

Woven uses OpenAI's **Moderation API** (`omni-moderation-latest`) for automated content safety checks:

- **Text moderation** — Tiles, chat messages, bio text
- **Image moderation** — Profile photos, Commons media tiles

All checks run server-side. Users never see "AI moderation" branding.

---

## OpenAI Moderation API

### Model
`omni-moderation-latest` — multi-modal model supporting text + images.

### Endpoint
`POST https://api.openai.com/v1/moderations`

### Rate Limits
- **Free tier:** 60 requests/min
- **Paid tier:** 3,500 requests/min (default for prod)

### Categories Flagged
- `sexual` — Sexual content
- `hate` — Hate speech, identity attacks
- `harassment` — Bullying, threats
- `self-harm` — Self-harm promotion
- `sexual/minors` — CSAM (child safety)
- `hate/threatening` — Hate + violence
- `violence/graphic` — Gore, graphic violence

---

## Text Moderation

### Implementation

**File:** `ModerationService.cs:246-265`

```csharp
private async Task<bool> CheckOpenAiModerationAsync(string? text, CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(text)) return false;

    var body = JsonSerializer.Serialize(new { 
        input = text, 
        model = "omni-moderation-latest" 
    });
    using var content = new StringContent(body, Encoding.UTF8, "application/json");

    var response = await _http.PostAsync("https://api.openai.com/v1/moderations", content, ct);
    if (!response.IsSuccessStatusCode)
    {
        _logger.LogWarning("[Moderation] OpenAI moderation API returned {Status}", response.StatusCode);
        return false;  // Fail open on API errors
    }

    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    return doc.RootElement
        .GetProperty("results")[0]
        .GetProperty("flagged")
        .GetBoolean();
}
```

### Usage

**Tiles (text content):**
```csharp
var flagged = await CheckOpenAiModerationAsync(item.Tile.ContentText, ct);
if (flagged)
{
    item.Decision = "rejected";
    item.RejectReason = "openai_moderation_flagged";
    item.Tile.IsExpired = true;
}
else
{
    item.Decision = "approved";
    item.Tile.IsModerated = true;
}
```

**Behavior:**
- **Flagged → tile expired** (soft delete, hidden from feed)
- **Clean → approved** (`IsModerated = true`)
- **API error → fail open** (approve by default, log warning)

---

## Image Moderation

### Implementation

**File:** `ModerationService.cs:203-243`

```csharp
public async Task<ModerationImageResult> ModerateImageAsync(int userId, string imageUrl, CancellationToken ct = default)
{
    if (string.IsNullOrWhiteSpace(imageUrl)) return ModerationImageResult.APPROVED;

    try
    {
        var body = JsonSerializer.Serialize(new
        {
            model = "omni-moderation-latest",
            input = new[] { new { type = "image_url", image_url = new { url = imageUrl } } }
        });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync("https://api.openai.com/v1/moderations", content, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("[Moderation] Image moderation API returned {Status} for user {UserId}",
                response.StatusCode, userId);
            return ModerationImageResult.ESCALATED;  // Fail safe on errors
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var flagged = doc.RootElement
            .GetProperty("results")[0]
            .GetProperty("flagged")
            .GetBoolean();

        if (flagged)
        {
            _logger.LogWarning("[Moderation] Image auto-rejected for user {UserId}: {Url}", userId, imageUrl);
            return ModerationImageResult.AUTO_REJECTED;
        }

        return ModerationImageResult.APPROVED;
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "[Moderation] Image moderation failed for user {UserId} — escalating", userId);
        return ModerationImageResult.ESCALATED;
    }
}
```

### Return Values

```csharp
public enum ModerationImageResult 
{ 
    APPROVED,       // Safe — allow upload
    ESCALATED,      // API error — allow but flag for review
    AUTO_REJECTED   // Flagged — block upload
}
```

### Usage (Profile Photos)

**Onboarding flow:**
```csharp
var result = await _moderation.ModerateImageAsync(userId, photoUrl, ct);

switch (result)
{
    case ModerationImageResult.AUTO_REJECTED:
        return Results.BadRequest(new { error = "PHOTO_INAPPROPRIATE" });
    
    case ModerationImageResult.ESCALATED:
        // Allow upload but queue for manual review
        await EnqueueForReviewAsync(userId, photoUrl, ct);
        break;
    
    case ModerationImageResult.APPROVED:
        // Proceed normally
        break;
}
```

---

## Request/Response Examples

### Text Moderation Request

```json
POST https://api.openai.com/v1/moderations
Authorization: Bearer sk-...
Content-Type: application/json

{
  "input": "I love this app, it's amazing!",
  "model": "omni-moderation-latest"
}
```

**Response (clean):**
```json
{
  "id": "modr-...",
  "model": "omni-moderation-latest",
  "results": [
    {
      "flagged": false,
      "categories": {
        "sexual": false,
        "hate": false,
        "harassment": false,
        "self-harm": false,
        "sexual/minors": false,
        "hate/threatening": false,
        "violence/graphic": false
      },
      "category_scores": {
        "sexual": 0.000012,
        "hate": 0.000003,
        "harassment": 0.000007,
        ...
      }
    }
  ]
}
```

**Response (flagged):**
```json
{
  "results": [
    {
      "flagged": true,
      "categories": {
        "harassment": true,
        ...
      }
    }
  ]
}
```

### Image Moderation Request

```json
POST https://api.openai.com/v1/moderations
Authorization: Bearer sk-...
Content-Type: application/json

{
  "model": "omni-moderation-latest",
  "input": [
    {
      "type": "image_url",
      "image_url": {
        "url": "https://wovenprodblob.blob.core.windows.net/photos/abc123.jpg"
      }
    }
  ]
}
```

**Response format:** Same as text (flagged: true/false + categories).

---

## Error Handling

### API Failures

**Principle:** Fail open (approve by default), log warning, escalate to manual review.

```csharp
if (!response.IsSuccessStatusCode)
{
    _logger.LogWarning("[Moderation] OpenAI API returned {Status}", response.StatusCode);
    return false;  // Text: approve by default
    return ModerationImageResult.ESCALATED;  // Images: flag for review
}
```

### Exceptions

**Text moderation:**
```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex, "[Moderation] OpenAI check failed for tile {TileId} — skipping", item.TileId);
    // Tile stays pending, will be retried on next worker pass
}
```

**Image moderation:**
```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex, "[Moderation] Image moderation failed for user {UserId} — escalating", userId);
    return ModerationImageResult.ESCALATED;
}
```

---

## Configuration

### OpenAI API Key

**Local dev:** User Secrets
```bash
cd backend/WovenBackend
dotnet user-secrets set "OpenAI:ApiKey" "sk-..."
```

**Production:** Azure Key Vault
```json
{
  "OpenAI": {
    "ApiKey": "@Microsoft.KeyVault(SecretUri=https://woven-prod-kv.vault.azure.net/secrets/OpenAI-ApiKey/)"
  }
}
```

### Authorization Header

**Setup in constructor:**
```csharp
public ModerationService(WovenDbContext db, IConfiguration config, HttpClient http, ILogger<ModerationService> logger)
{
    _http = http;
    var apiKey = _config["OpenAI:ApiKey"];
    if (!string.IsNullOrWhiteSpace(apiKey))
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
}
```

---

## Performance

### Latency
- **Text moderation:** ~200-500ms avg
- **Image moderation:** ~800-1200ms avg

### Throughput
- **Text:** Sync check (user waits during tile creation)
- **Media:** Async via `ModerationWorker` (5-min batches)

### Cost
- **Text:** $0.002 per 1K tokens (~$0.000002 per tile)
- **Images:** $0.003 per image
- **Monthly estimate (1K users):** ~$50-$100

---

## Monitoring

### Metrics to Track

1. **Flagged content rate** — % of content auto-rejected
2. **API latency** — p50, p95, p99 response times
3. **API errors** — 429s (rate limit), 500s (OpenAI downtime)
4. **Escalation rate** — % escalated to manual review

### Logs

**Approved:**
```
[Moderation] Tile abc123 approved by OpenAI moderation
```

**Rejected:**
```
[Moderation] Tile abc123 flagged by OpenAI moderation
```

**API error:**
```
[Moderation] OpenAI moderation API returned 429
```

---

## Production Checklist

- [x] OpenAI API key in Key Vault
- [ ] Monitor 429 rate limit errors
- [ ] Set up alerting for spike in flagged content
- [ ] A/B test sync vs async text moderation (latency impact)
- [ ] Add category-specific logging (sexual vs hate vs harassment)
- [ ] Test fail-open behavior during OpenAI outage
- [ ] Document escalation SLA for manual review

---

## Future Improvements

1. **Category-specific actions**  
   - Instant ban for `sexual/minors`
   - Soft warning for `harassment` (first offense)

2. **Confidence thresholds**  
   - Use `category_scores` for nuanced decisions (e.g., only reject if `harassment > 0.8`)

3. **Multi-language support**  
   - OpenAI supports 100+ languages — test non-English content

4. **Appeal flow**  
   - Users can contest auto-rejections → human review

---

## Related Documentation

- [Moderation Overview](./README.md)
- [Review Queue](./review-queue.md)
- [Trust System](../trust/README.md)
- [API Reference](./api.md)
