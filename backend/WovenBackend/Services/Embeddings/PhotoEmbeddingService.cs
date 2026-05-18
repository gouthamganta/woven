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
            var embedding = ParseReplicateEmbedding(respJson);
            if (embedding == null)
            {
                _logger.LogWarning("[PhotoEmbedding] Failed to parse Replicate response for user {UserId}", userId);
                return null;
            }

            var row = new PhotoEmbedding
            {
                UserId = userId,
                PhotoUrl = photoUrl,
                Embedding = new Vector(embedding),
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
            if (marker == 0xD9) { output.WriteByte(0xFF); output.WriteByte(0xD9); break; }
            if (marker == 0xD8) continue;

            // Read segment length (big-endian, includes the 2 length bytes)
            int hi = input.ReadByte();
            int lo = input.ReadByte();
            int segLen = (hi << 8) | lo;
            var segData = new byte[segLen - 2];
            _ = input.Read(segData, 0, segData.Length);

            // Skip APP1 (EXIF/XMP) — marker 0xE1
            if (marker == 0xE1) continue;

            output.WriteByte(0xFF);
            output.WriteByte((byte)marker);
            output.WriteByte((byte)hi);
            output.WriteByte((byte)lo);
            output.Write(segData, 0, segData.Length);
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
}
