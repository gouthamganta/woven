# Match Narration

**Voice narration on Moments cards** — TTS audio that reads a curated quote from the candidate's profile, creating a richer intro experience. Paired with Ken Burns photo animation.

---

## What is match narration?

A cinematic intro layer added to Moments cards (daily Deck tab). When a user views a match, they see:
1. **Ken Burns photo animation** — up to 3 profile photos with slow pan/zoom
2. **Curated quote** — shortest meaningful text from bio or foundational answers
3. **TTS narration** — OpenAI voice reading the quote (optional, can fail gracefully)

**Service:** `backend/WovenBackend/Services/Matchmaking/MatchNarratorService.cs`

**Table:** Data is embedded in `daily_deck_items` via JSON fields:
- `ken_burns_photo_urls` — array of photo URLs
- `curated_quote` — text shown visually
- `narration_url` — Azure Blob URL to MP3 file
- `narration_exposed` — always false (baseline period for A/B testing)

---

## Generation flow

**When:** During daily deck build (`DailyDeckOrchestrator.BuildDeckAsync()`)

**For each candidate in deck:**
1. Load up to 3 profile photos in sort order
2. Extract curated quote from bio/foundational answers
3. Generate TTS audio via OpenAI (if quote exists and API key configured)
4. Cache audio in Azure Blob (`tts/{candidateId}/{yyyyMMdd}.mp3`)
5. Return `MatchNarratorResult` object
6. Embed result in `DailyDeckItems` JSON

**Pre-generation at scale:**
- TTS calls run in parallel via `Task.WhenAll` (5 candidates at a time)
- Total deck build time: ~2-3 seconds for narration layer

---

## Ken Burns photos

**What:** Profile photos shown with subtle pan/zoom animation (Ken Burns effect).

**Selection logic:**
```csharp
var photos = await db.UserPhotos
    .Where(p => p.UserId == candidateId)
    .OrderBy(p => p.SortOrder)
    .Take(3)
    .Select(p => p.Url)
    .ToListAsync();

// Returns null if no photos exist
string[]? kenBurnsPhotoUrls = photos.Count >= 1 ? photos.ToArray() : null;
```

**Frontend behavior:**
- If `kenBurnsPhotoUrls` is null → show placeholder or skip animation
- If 1 photo → static display
- If 2-3 photos → crossfade + Ken Burns effect

---

## Curated quote

**What:** A single representative quote from the candidate's profile.

**Selection criteria:**
1. Collect all candidate text sources:
   - `bio` from `user_optional_fields`
   - All answers from latest `user_foundational_question_sets.answers_json`
2. Filter to texts ≥20 characters (meaningful length)
3. Pick the SHORTEST qualifying text (concise = punchier)
4. Trim to 120 characters max, append ellipsis if truncated

**Code:**
```csharp
var candidates = new List<string>();
if (!string.IsNullOrWhiteSpace(bio)) candidates.Add(bio.Trim());
candidates.AddRange(answerTexts.Where(a => !string.IsNullOrWhiteSpace(a)));

var qualifying = candidates
    .Where(c => c.Length >= MinQuoteChars)  // 20
    .OrderBy(c => c.Length)
    .FirstOrDefault();

string? curatedQuote = qualifying != null
    ? qualifying.Length > MaxQuoteChars  // 120
        ? qualifying[..MaxQuoteChars].TrimEnd() + "…"
        : qualifying
    : null;
```

**Why shortest?** Long answers tend to be verbose. Short answers are often punchy insights — better for first impression.

---

## TTS generation

**Model:** OpenAI `tts-1`  
**Voice:** `nova` (neutral, warm)  
**Format:** MP3  
**Cache:** Azure Blob Storage, 48h TTL

**Generation logic:**
```csharp
private async Task<string?> GetOrGenerateTtsAsync(
    int candidateId, DateOnly date, string text, string apiKey, CancellationToken ct)
{
    var blobPath = $"tts/{candidateId}/{date:yyyyMMdd}.mp3";
    var containerClient = _blob.GetBlobContainerClient("tts-narration");
    var blobClient = containerClient.GetBlobClient(blobPath);

    // Return cached audio if it exists
    if (await blobClient.ExistsAsync(ct))
        return blobClient.Uri.ToString();

    // Generate via OpenAI
    var body = JsonSerializer.Serialize(new
    {
        model = "tts-1",
        input = text,
        voice = "nova",
        response_format = "mp3"
    });

    using var http = _httpFactory.CreateClient();
    using var request = new HttpRequestMessage(HttpMethod.Post, 
        "https://api.openai.com/v1/audio/speech");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    request.Content = new StringContent(body, Encoding.UTF8, "application/json");

    using var response = await http.SendAsync(request, ct);
    response.EnsureSuccessStatusCode();

    var audioBytes = await response.Content.ReadAsByteArrayAsync(ct);

    await blobClient.UploadAsync(
        new BinaryData(audioBytes),
        new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "audio/mpeg" } },
        ct);

    return blobClient.Uri.ToString();
}
```

**Error handling:**
- TTS failure → `narrationUrl = null` (graceful degradation)
- Frontend checks: if `narrationUrl` is null, skip audio and show quote visually only
- Logged as warning, does NOT block deck build

---

## Cache strategy

**Path structure:**
```
tts/{candidateId}/{yyyyMMdd}.mp3
```

**TTL:** 48 hours (configured in Azure Blob lifecycle policy)

**Why per-candidate, per-date?**
- Quote text can change if user edits bio/answers
- Daily cache ensures freshness
- Avoids stale narration after profile updates

**Re-use logic:**
- If blob exists at path → return URL immediately (no OpenAI call)
- If deck is rebuilt on same day → cached audio is reused
- If user's profile hasn't changed → same audio may exist for multiple days (but path changes, so re-generated)

**Future optimization:** Hash quote text and use hash-based cache key (quote-level caching vs. date-level).

---

## Baseline period (A/B testing prep)

**`NarrationExposed` field:**
- Always set to `false` during baseline period
- Backend generates narration but does NOT expose it to users
- WeightLearning service filters to `NarrationExposed = false` for first 30 days

**Why?**
1. Establish baseline outcome metrics WITHOUT narration influence
2. After 30 days, enable narration for 50% of users (`NarrationExposed = true`)
3. Compare outcomes: TimeToFirstMessage, ChoiceRate, MessageDepth
4. If narration improves outcomes → roll out to 100%
5. If neutral/negative → keep off by default

**Current status:** All users see `NarrationExposed = false` (feature built but not launched).

---

## Answer extraction logic

**Foundational answers are stored as JSON:**
```json
[
  { "q": "What does a good weekend look like?", "a": "Hiking, cooking, reading" },
  { "q": "What are you looking for?", "a": "Someone who values growth" }
]
```

**Extraction:**
```csharp
private static List<string> ExtractAnswerTexts(string? answersJson)
{
    if (string.IsNullOrWhiteSpace(answersJson)) return new();
    try
    {
        using var doc = JsonDocument.Parse(answersJson);
        var result = new List<string>();
        foreach (var elem in doc.RootElement.EnumerateArray())
        {
            if (elem.TryGetProperty("a", out var aEl))
            {
                var text = aEl.GetString();
                if (!string.IsNullOrWhiteSpace(text)) result.Add(text.Trim());
            }
        }
        return result;
    }
    catch { return new(); }
}
```

**Result:** List of all answer texts, used as quote candidates.

---

## Integration with deck build

**Called from:** `DailyDeckOrchestrator.BuildDeckAsync()`

**For each candidate:**
```csharp
var narratorResult = await _matchNarrator.BuildNarratorFieldsAsync(
    candidateId, dateUtc, ct);

// Add to DeckItem
deckItem.KenBurnsPhotoUrls = narratorResult.KenBurnsPhotoUrls;
deckItem.CuratedQuote = narratorResult.CuratedQuote;
deckItem.NarrationUrl = narratorResult.NarrationUrl;
deckItem.NarrationExposed = narratorResult.NarrationExposed;
```

**Parallel generation:**
```csharp
var narratorTasks = selectedCandidates.Select(c =>
    _matchNarrator.BuildNarratorFieldsAsync(c.CandidateId, dateUtc, ct));

var narratorResults = await Task.WhenAll(narratorTasks);
```

**Result:** All TTS calls run in parallel (5-15 calls), total time ~2-3 seconds.

---

## API response format

**GET /moments/deck**

```json
{
  "items": [
    {
      "candidateId": 123,
      "fullName": "Alex",
      "profilePhoto": "https://...",
      "kenBurnsPhotoUrls": [
        "https://blob.../photo1.jpg",
        "https://blob.../photo2.jpg",
        "https://blob.../photo3.jpg"
      ],
      "curatedQuote": "I believe in finding joy in the small things — a good conversation, a sunrise, homemade pasta.",
      "narrationUrl": "https://blob.../tts/123/20260604.mp3",
      "narrationExposed": false
    }
  ]
}
```

**If TTS fails:**
```json
{
  "curatedQuote": "I believe in finding joy in the small things...",
  "narrationUrl": null,
  "narrationExposed": false
}
```

Frontend shows quote visually, skips audio.

---

## Frontend behavior (not yet implemented)

**Expected flow:**
1. User views Moments card
2. Ken Burns animation starts (photo pan/zoom)
3. If `narrationUrl` exists and `narrationExposed = true`:
   - Preload audio
   - Play TTS narration in background
   - Show subtle audio icon (playing indicator)
4. If `narrationUrl` is null OR `narrationExposed = false`:
   - Show `curatedQuote` text only
   - No audio playback
5. User can tap to pause/resume narration

**Not built yet:** Frontend components for narration playback. Data is generated and ready.

---

## Performance metrics

**TTS generation time:**
- Single call: ~1-2 seconds
- Parallel batch (5 candidates): ~2-3 seconds total
- Cached call: <100ms (blob exists check)

**Cache hit rate:**
- Day 1: 0% (all new)
- Day 2+: 60-80% (deck rebuilds reuse same candidates)

**Storage costs:**
- Average MP3 size: 50-100 KB
- 1000 users × 10 candidates/day = 10,000 files/day
- Storage: ~500 MB/day
- 48h TTL → ~1 GB total storage
- Azure cost: ~$0.02/month (negligible)

---

## Error scenarios

| Scenario | Behavior |
|---|---|
| No profile photos | `kenBurnsPhotoUrls = null`, frontend skips animation |
| No bio or answers | `curatedQuote = null`, no narration generated |
| Bio/answers all <20 chars | `curatedQuote = null`, no narration generated |
| TTS API call fails | `narrationUrl = null`, logged as warning, deck build continues |
| OpenAI API key missing | `narrationUrl = null`, no TTS attempted |
| Azure Blob upload fails | `narrationUrl = null`, logged as warning |

**All failures are non-blocking.** Deck build always succeeds, narration is optional enhancement.

---

## Configuration

**Required environment variables:**
- `OpenAI:ApiKey` — for TTS generation
- `Azure:BlobConnectionString` — for audio storage

**Optional settings:**
- `TTS:MinQuoteChars` — default 20
- `TTS:MaxQuoteChars` — default 120
- `TTS:Voice` — default "nova"
- `TTS:Model` — default "tts-1"

**Container name:** `tts-narration` (auto-created if missing)

---

## Logging

**Key log lines:**
```csharp
_logger.LogInformation(
    "[MatchNarrator] Generated TTS for candidate {CandidateId}, URL: {Url}",
    candidateId, narrationUrl);

_logger.LogWarning(
    "[MatchNarrator] TTS generation failed for candidate {CandidateId}",
    candidateId);
```

**Correlation ID:** All OpenAI calls include `X-Correlation-ID` header for tracing.

---

## Future enhancements

**Pending:**
1. Frontend playback UI (audio player, pause/resume, waveform visualization)
2. A/B test launch (flip `NarrationExposed` to true for 50% of users)
3. Voice selection (let users choose nova vs. other voices)
4. Speed control (1.0x, 1.25x, 1.5x playback)

**Explored but deferred:**
- Piper TTS (local, free) — quality not good enough for production
- ElevenLabs — too expensive at scale ($0.30 per 1K chars)
- Google Cloud TTS — similar quality to OpenAI, no cost advantage

**Current choice:** OpenAI `tts-1` is best balance of quality, speed, and cost.

---

## Related files

**Backend:**
- `backend/WovenBackend/Services/Matchmaking/MatchNarratorService.cs` — Generation logic
- `backend/WovenBackend/Services/Matchmaking/DailyDeckOrchestrator.cs` — Calls narrator service
- `backend/WovenBackend/Data/Entities/DailyDeckItem.cs` — Stores narration fields in JSON

**Database:**
- Table: `daily_deck_items` (JSON fields: `ken_burns_photo_urls`, `curated_quote`, `narration_url`, `narration_exposed`)
- Table: `user_photos` (source for Ken Burns photos)
- Table: `user_optional_fields` (bio source)
- Table: `user_foundational_question_sets` (answers source)

**Azure:**
- Blob container: `tts-narration`
- Lifecycle policy: 48h TTL on all blobs

---

## Testing checklist

**Quote selection:**
- [ ] Shortest text ≥20 chars is chosen
- [ ] Bio is included as candidate
- [ ] All foundational answers are included
- [ ] Text >120 chars is trimmed with ellipsis
- [ ] No quote selected if all texts <20 chars

**TTS generation:**
- [ ] OpenAI API called with correct model/voice
- [ ] Audio saved to Azure Blob at correct path
- [ ] Blob has `audio/mpeg` content type
- [ ] Cached blob is reused on subsequent calls
- [ ] TTS failure returns null gracefully

**Integration:**
- [ ] Narrator service called for each deck candidate
- [ ] Results embedded in DailyDeckItems JSON
- [ ] Deck build succeeds even when all TTS calls fail
- [ ] Parallel generation completes in <5 seconds
