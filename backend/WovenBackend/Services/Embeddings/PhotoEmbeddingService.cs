using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.Services.Trust;

namespace WovenBackend.Services.Embeddings;

public class PhotoEmbeddingService : IPhotoEmbeddingService
{
    private readonly WovenDbContext _db;
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PhotoEmbeddingService> _logger;

    public PhotoEmbeddingService(
        WovenDbContext db,
        HttpClient http,
        IConfiguration config,
        IServiceScopeFactory scopeFactory,
        ILogger<PhotoEmbeddingService> logger)
    {
        _db = db;
        _http = http;
        _config = config;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<int?> EmbedPhotoAsync(int userId, string photoUrl, CancellationToken ct = default)
    {
        try
        {
            // Fetch image bytes
            var imageBytes = await _http.GetByteArrayAsync(photoUrl, ct);

            // Strip EXIF — replace with raw pixel stream to remove GPS/PII metadata
            var stripped = StripExif(imageBytes);
            _logger.LogInformation("[PhotoEmbedding] EXIF stripped for user {UserId}, photo {Url}", userId, photoUrl);

            var base64 = Convert.ToBase64String(stripped);

            // Call Replicate CLIP endpoint
            var apiToken = _config["Replicate:ApiToken"]
                ?? throw new InvalidOperationException("Replicate:ApiToken not configured");

            var payload = new
            {
                version = "75b33f253f7714a281ad3e9b28f63e3232d583716ef6718f2e46641077ea040a",
                input = new { image = $"data:image/jpeg;base64,{base64}" }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.replicate.com/v1/predictions");
            req.Headers.Authorization = new AuthenticationHeaderValue("Token", apiToken);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();

            var respJson = await resp.Content.ReadAsStringAsync(ct);

            // Poll for embedding result (Replicate async API)
            var embedding = ParseReplicateEmbedding(respJson);
            if (embedding == null)
            {
                // Response doesn't have output yet - check for polling URL
                var predictionUrl = GetPredictionUrl(respJson);
                if (predictionUrl != null)
                {
                    var polledResult = await PollPredictionAsync(predictionUrl, apiToken, ct);
                    if (polledResult != null)
                    {
                        embedding = ParseReplicateEmbedding(polledResult);
                    }
                }

                if (embedding == null)
                {
                    _logger.LogWarning("[PhotoEmbedding] Failed to get embedding for user {UserId}", userId);
                    return null;
                }
            }

            // Extract visual attributes via zero-shot classification
            var (attributes, confidences) = await ExtractAttributesAsync(base64, ct);

            var row = new PhotoEmbedding
            {
                UserId = userId,
                PhotoUrl = photoUrl,
                Embedding = new Vector(embedding),
                AttributesJson = attributes != null ? JsonSerializer.Serialize(attributes) : null,
                ConfidenceScoresJson = confidences != null ? JsonSerializer.Serialize(confidences) : null,
                EmbeddedAt = DateTimeOffset.UtcNow
            };
            _db.PhotoEmbeddings.Add(row);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("[PhotoEmbedding] Stored embedding id={Id} for user {UserId}", row.Id, userId);

            var capturedUserId = userId;
            var capturedId = row.Id;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var catfish = scope.ServiceProvider.GetRequiredService<ICatfishDetectionService>();
                    await catfish.CheckPhotoAsync(capturedUserId, capturedId, CancellationToken.None);
                }
                catch { /* non-critical */ }
            });

            return row.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PhotoEmbedding] Failed for user {UserId}, url={Url}", userId, photoUrl);
            return null;
        }
    }

    private static byte[] StripExif(byte[] jpeg)
    {
        // Minimal JPEG EXIF strip: remove APP1 (0xFFE1) segments
        // Write JPEG SOI, then copy all segments except APP1
        using var input = new MemoryStream(jpeg);
        using var output = new MemoryStream();

        // Check SOI marker
        if (input.ReadByte() != 0xFF || input.ReadByte() != 0xD8)
            return jpeg; // not a valid JPEG, return as-is

        output.WriteByte(0xFF);
        output.WriteByte(0xD8);

        while (input.Position < input.Length - 1)
        {
            if (input.ReadByte() != 0xFF) break;
            int marker = input.ReadByte();

            // EOI marker - end of image
            if (marker == 0xD9)
            {
                output.WriteByte(0xFF);
                output.WriteByte(0xD9);
                break;
            }

            // SOI marker inside stream (shouldn't happen, skip)
            if (marker == 0xD8) continue;

            // SOS marker (0xDA) - start of scan data
            // After SOS, all data until EOI is compressed scan data, not markers
            if (marker == 0xDA)
            {
                // Read SOS segment length
                int hi = input.ReadByte();
                int lo = input.ReadByte();
                int segLen = (hi << 8) | lo;
                var segData = new byte[segLen - 2];
                _ = input.Read(segData, 0, segData.Length);

                // Write SOS marker and data
                output.WriteByte(0xFF);
                output.WriteByte((byte)marker);
                output.WriteByte((byte)hi);
                output.WriteByte((byte)lo);
                output.Write(segData, 0, segData.Length);

                // Copy all remaining data until EOI
                // Scan data may contain 0xFF bytes, but they're escaped as 0xFF00
                while (input.Position < input.Length)
                {
                    int b = input.ReadByte();
                    output.WriteByte((byte)b);

                    // Check for EOI marker (0xFFD9)
                    if (b == 0xFF && input.Position < input.Length)
                    {
                        int next = input.ReadByte();
                        output.WriteByte((byte)next);
                        if (next == 0xD9) break; // EOI found
                    }
                }
                break;
            }

            // Read segment length (big-endian, includes the 2 length bytes)
            int segHi = input.ReadByte();
            int segLo = input.ReadByte();
            int segLen2 = (segHi << 8) | segLo;
            var segData2 = new byte[segLen2 - 2];
            _ = input.Read(segData2, 0, segData2.Length);

            // Skip APP1 (EXIF/XMP) — marker 0xE1
            if (marker == 0xE1) continue;

            output.WriteByte(0xFF);
            output.WriteByte((byte)marker);
            output.WriteByte((byte)segHi);
            output.WriteByte((byte)segLo);
            output.Write(segData2, 0, segData2.Length);
        }

        return output.ToArray();
    }

    private static float[]? ParseReplicateEmbedding(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            // Replicate async: poll until status=succeeded, output is the embedding array
            if (doc.RootElement.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
            {
                var floats = new float[output.GetArrayLength()];
                int i = 0;
                foreach (var elem in output.EnumerateArray())
                    floats[i++] = elem.GetSingle();
                return floats;
            }
        }
        catch { }
        return null;
    }

    private async Task<(Dictionary<string, string>?, Dictionary<string, float>?)> ExtractAttributesAsync(
        string base64Image,
        CancellationToken ct)
    {
        try
        {
            // Zero-shot classification categories for visual attributes
            var categories = new Dictionary<string, string[]>
            {
                ["hair_color"] = new[] { "blonde hair", "brown hair", "black hair", "red hair", "gray hair", "bald" },
                ["style"] = new[] { "casual style", "formal style", "athletic style", "artistic style", "professional style" },
                ["skin_tone"] = new[] { "fair skin tone", "medium skin tone", "tan skin tone", "dark skin tone" },
                ["height_class"] = new[] { "short person", "average height person", "tall person" }
            };

            var attributes = new Dictionary<string, string>();
            var confidences = new Dictionary<string, float>();

            var apiToken = _config["Replicate:ApiToken"]
                ?? throw new InvalidOperationException("Replicate:ApiToken not configured");

            // Use CLIP for zero-shot classification
            // Model: openai/clip-vit-large-patch14
            foreach (var category in categories)
            {
                var payload = new
                {
                    version = "75b33f253f7714a281ad3e9b28f63e3232d583716ef6718f2e46641077ea040a",
                    input = new
                    {
                        image = $"data:image/jpeg;base64,{base64Image}",
                        text = string.Join(", ", category.Value)
                    }
                };

                using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.replicate.com/v1/predictions");
                req.Headers.Authorization = new AuthenticationHeaderValue("Token", apiToken);
                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                var resp = await _http.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[PhotoEmbedding] Zero-shot classification failed for {Category}", category.Key);
                    continue;
                }

                var respJson = await resp.Content.ReadAsStringAsync(ct);

                // Wait for prediction to complete (Replicate is async)
                var predictionUrl = GetPredictionUrl(respJson);
                if (predictionUrl != null)
                {
                    var result = await PollPredictionAsync(predictionUrl, apiToken, ct);
                    if (result != null)
                    {
                        var (bestLabel, bestScore) = ParseZeroShotResult(result, category.Value);
                        if (bestLabel != null)
                        {
                            attributes[category.Key] = bestLabel;
                            confidences[category.Key] = bestScore;
                        }
                    }
                }
            }

            _logger.LogInformation("[PhotoEmbedding] Extracted attributes: {Count}", attributes.Count);
            return (attributes, confidences);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PhotoEmbedding] Attribute extraction failed");
            return (null, null);
        }
    }

    private static string? GetPredictionUrl(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("urls", out var urls) &&
                urls.TryGetProperty("get", out var getUrl))
            {
                return getUrl.GetString();
            }
        }
        catch { }
        return null;
    }

    private async Task<string?> PollPredictionAsync(string url, string apiToken, CancellationToken ct)
    {
        // Poll up to 10 times with 1s delay
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(1000, ct);

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Token", apiToken);

            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) continue;

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("status", out var status))
            {
                var statusStr = status.GetString();
                if (statusStr == "succeeded")
                    return json;
                if (statusStr == "failed" || statusStr == "canceled")
                    return null;
            }
        }
        return null;
    }

    private static (string?, float) ParseZeroShotResult(string json, string[] labels)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("output", out var output) ||
                output.ValueKind != JsonValueKind.Array)
                return (null, 0f);

            // output is array of probabilities corresponding to labels
            var probs = new float[output.GetArrayLength()];
            int idx = 0;
            foreach (var elem in output.EnumerateArray())
                probs[idx++] = elem.GetSingle();

            // Find best match
            int bestIdx = 0;
            float bestScore = probs[0];
            for (int i = 1; i < probs.Length && i < labels.Length; i++)
            {
                if (probs[i] > bestScore)
                {
                    bestScore = probs[i];
                    bestIdx = i;
                }
            }

            // Extract base attribute name (remove " hair", " style", " skin tone" suffix)
            var label = labels[bestIdx];
            var cleanLabel = label.Split(' ')[0]; // e.g., "blonde hair" -> "blonde"
            return (cleanLabel, bestScore);
        }
        catch { }
        return (null, 0f);
    }
}
