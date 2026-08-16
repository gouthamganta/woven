using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;

namespace WovenBackend.Services.Matchmaking;

public class MatchNarratorResult
{
    public string[]? KenBurnsPhotoUrls { get; set; }
    public string? CuratedQuote { get; set; }
    public string? NarrationUrl { get; set; }
    public bool NarrationExposed { get; set; } = false;
}

public interface IMatchNarratorService
{
    Task<MatchNarratorResult> BuildNarratorFieldsAsync(
        int candidateId, DateOnly dateUtc, CancellationToken ct = default);
}

/// <summary>
/// Populates cinematic intro fields on DeckItems:
///   KenBurnsPhotoUrls — up to 3 profile photos in sort order
///   CuratedQuote      — shortest qualifying text ≥20 chars from bio/answers, trimmed to 120
///   NarrationUrl      — TTS audio URL (nullable; null = skip audio, Ken Burns still plays)
///   NarrationExposed  — always false; WeightLearning filters to establish 30-day baseline
///
/// TTS is pre-generated at deck build time via Task.WhenAll (5 parallel calls).
/// Audio is cached in Azure Blob at tts/{candidateId}/{yyyyMMdd}.mp3 with 48h TTL.
/// If TTS fails, NarrationUrl is null — frontend silently skips audio.
/// </summary>
public class MatchNarratorService : IMatchNarratorService
{
    private const string TtsEndpoint     = "https://api.openai.com/v1/audio/speech";
    private const string TtsModel        = "tts-1";
    private const string TtsVoice        = "nova";
    private const string TtsContainer    = "tts-narration";
    private const int MinQuoteChars      = 20;
    private const int MaxQuoteChars      = 120;

    private readonly WovenDbContext _db;
    private readonly BlobServiceClient _blob;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<MatchNarratorService> _logger;

    public MatchNarratorService(
        WovenDbContext db,
        BlobServiceClient blob,
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<MatchNarratorService> logger)
    {
        _db          = db;
        _blob        = blob;
        _httpFactory = httpFactory;
        _config      = config;
        _logger      = logger;
    }

    public async Task<MatchNarratorResult> BuildNarratorFieldsAsync(
        int candidateId, DateOnly dateUtc, CancellationToken ct = default)
    {
        // 1. Load up to 3 profile photos in sort order
        var photos = await _db.UserPhotos
            .Where(p => p.UserId == candidateId)
            .OrderBy(p => p.SortOrder)
            .Take(3)
            .Select(p => p.Url)
            .ToListAsync(ct);

        string[]? kenBurnsPhotoUrls = photos.Count >= 1 ? photos.ToArray() : null;

        // 2. Pick curated quote: shortest text ≥20 chars from bio or foundational answers
        var bio = await _db.UserOptionalFields
            .Where(f => f.UserId == candidateId && f.Key == "bio")
            .Select(f => f.Value)
            .FirstOrDefaultAsync(ct);

        var latestAnswered = await _db.UserFoundationalQuestionSets
            .Where(s => s.UserId == candidateId && s.AnsweredAt != null)
            .OrderByDescending(s => s.AnsweredAt)
            .Select(s => s.AnswersJson)
            .FirstOrDefaultAsync(ct);

        var answerTexts = ExtractAnswerTexts(latestAnswered);

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(bio)) candidates.Add(bio.Trim());
        candidates.AddRange(answerTexts.Where(a => !string.IsNullOrWhiteSpace(a)));

        var qualifying = candidates
            .Where(c => c.Length >= MinQuoteChars)
            .OrderBy(c => c.Length)
            .FirstOrDefault();

        string? curatedQuote = qualifying != null
            ? qualifying.Length > MaxQuoteChars
                ? qualifying[..MaxQuoteChars].TrimEnd() + "…"
                : qualifying
            : null;

        // 3. Generate TTS only if there's a quote and an API key configured
        string? narrationUrl = null;
        if (curatedQuote != null)
        {
            var apiKey = _config["OpenAI:ApiKey"];
            if (!string.IsNullOrWhiteSpace(apiKey))
                narrationUrl = await GetOrGenerateTtsAsync(candidateId, dateUtc, curatedQuote, apiKey, ct);
        }

        return new MatchNarratorResult
        {
            KenBurnsPhotoUrls = kenBurnsPhotoUrls,
            CuratedQuote      = curatedQuote,
            NarrationUrl      = narrationUrl,
            NarrationExposed  = false  // always false until voice launch; WeightLearning filters this
        };
    }

    // ── TTS generation ───────────────────────────────────────────────────────────

    private async Task<string?> GetOrGenerateTtsAsync(
        int candidateId, DateOnly date, string text, string apiKey, CancellationToken ct)
    {
        var blobPath = $"tts/{candidateId}/{date:yyyyMMdd}.mp3";
        try
        {
            var containerClient = _blob.GetBlobContainerClient(TtsContainer);
            await containerClient.CreateIfNotExistsAsync(cancellationToken: ct);
            var blobClient = containerClient.GetBlobClient(blobPath);

            // Return cached audio if it exists
            if (await blobClient.ExistsAsync(ct))
                return blobClient.Uri.ToString();

            // Generate via OpenAI
            var body = JsonSerializer.Serialize(new
            {
                model           = TtsModel,
                input           = text,
                voice           = TtsVoice,
                response_format = "mp3"
            });

            using var http    = _httpFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, TtsEndpoint);
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MatchNarrator] TTS generation failed for candidate {CandidateId}", candidateId);
            return null;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

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
}
