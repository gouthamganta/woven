using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using WovenBackend.Data;

namespace WovenBackend.Services.Tiles;

// Singleton: uses IServiceScopeFactory for DB access so it stays safe when called
// from fire-and-forget Task.Run after the originating request scope has ended.
public class TileEmbeddingService
{
    private const string EmbeddingEndpoint = "https://api.openai.com/v1/embeddings";
    private const string ChatEndpoint      = "https://api.openai.com/v1/chat/completions";
    private const string EmbeddingModel    = "text-embedding-3-small";
    private const string VisionModel       = "gpt-4o-mini";
    private const int    EmbeddingDims     = 1536;
    private const int    LastNTiles        = 30;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory   _httpFactory;
    private readonly IConfiguration       _config;
    private readonly ILogger<TileEmbeddingService> _logger;

    public TileEmbeddingService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory   httpFactory,
        IConfiguration       config,
        ILogger<TileEmbeddingService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpFactory  = httpFactory;
        _config       = config;
        _logger       = logger;
    }

    public async Task EmbedTileAsync(Guid tileId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();

        var tile = await db.Tiles.FirstOrDefaultAsync(t => t.Id == tileId, ct);
        if (tile is null) return;

        var embedding = tile.ContentType switch
        {
            "photo" => await EmbedPhotoTileAsync(tile.MediaUrl, tile.ContentText, ct),
            "video" => await EmbedVideoTileAsync(tile.ContentText, ct),
            _       => await EmbedTextAsync(tile.ContentText, ct)   // "text" + fallback
        };

        if (embedding is null)
        {
            _logger.LogWarning("[TileEmbedding] No embedding produced for tile {TileId} (type={Type})", tileId, tile.ContentType);
            return;
        }

        tile.Embedding = embedding;
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("[TileEmbedding] Embedded tile {TileId} (type={Type}, user={UserId})", tileId, tile.ContentType, tile.UserId);

        await UpdateExpressionEmbeddingAsync(db, tile.UserId, ct);
    }

    // ── Photo: describe with GPT-4o-mini vision → embed description ──────────
    // This keeps all tile embeddings in the same 1536-dim text-embedding-3-small
    // space so they're directly comparable with ReceptionEmbedding, ExpressionEmbedding,
    // and PreferenceEmbedding without any projection step.
    private async Task<Vector?> EmbedPhotoTileAsync(string? mediaUrl, string? captionText, CancellationToken ct)
    {
        string? description = null;

        if (!string.IsNullOrWhiteSpace(mediaUrl))
            description = await DescribeImageAsync(mediaUrl, ct);

        // Combine description + caption if both exist; fall back to caption-only
        var textToEmbed = description is not null && captionText is not null
            ? $"{description}\n{captionText}"
            : description ?? captionText;

        return await EmbedTextAsync(textToEmbed, ct);
    }

    // ── Video: embed caption text if present, otherwise skip ─────────────────
    private async Task<Vector?> EmbedVideoTileAsync(string? captionText, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(captionText))
        {
            _logger.LogDebug("[TileEmbedding] Video tile has no caption — skipping embedding");
            return null;
        }
        return await EmbedTextAsync(captionText, ct);
    }

    // ── GPT-4o-mini vision: describe image for embedding ─────────────────────
    private async Task<string?> DescribeImageAsync(string imageUrl, CancellationToken ct)
    {
        var apiKey = _config["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        try
        {
            var body = JsonSerializer.Serialize(new
            {
                model = VisionModel,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = "Describe this image in 2-3 sentences. Focus on mood, aesthetics, and emotional tone. Be concise." },
                            new { type = "image_url", image_url = new { url = imageUrl } }
                        }
                    }
                },
                max_tokens = 150
            });

            using var http    = _httpFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, ChatEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[TileEmbedding] Vision description failed for {Url}", imageUrl);
            return null;
        }
    }

    // ── text-embedding-3-small ────────────────────────────────────────────────
    private async Task<Vector?> EmbedTextAsync(string? text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var apiKey = _config["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("[TileEmbedding] OpenAI:ApiKey not configured — skipping");
            return null;
        }

        try
        {
            var body = JsonSerializer.Serialize(new
            {
                model      = EmbeddingModel,
                input      = text,
                dimensions = EmbeddingDims
            });

            using var http    = _httpFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, EmbeddingEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var floats = doc.RootElement
                .GetProperty("data")[0]
                .GetProperty("embedding")
                .EnumerateArray()
                .Select(e => e.GetSingle())
                .ToArray();

            return new Vector(floats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TileEmbedding] OpenAI embedding call failed");
            return null;
        }
    }

    // ── ExpressionEmbedding: mean of last 30 embedded tiles ──────────────────
    private async Task UpdateExpressionEmbeddingAsync(WovenDbContext db, int userId, CancellationToken ct)
    {
        var recentEmbeddings = await db.Tiles
            .Where(t => t.UserId == userId && t.Embedding != null)
            .OrderByDescending(t => t.CreatedAt)
            .Take(LastNTiles)
            .Select(t => t.Embedding)
            .ToListAsync(ct);

        if (recentEmbeddings.Count == 0) return;

        var mean = new float[EmbeddingDims];
        foreach (var emb in recentEmbeddings)
        {
            var span = emb!.Memory.Span;
            for (var i = 0; i < EmbeddingDims; i++)
                mean[i] += span[i];
        }
        for (var i = 0; i < EmbeddingDims; i++)
            mean[i] /= recentEmbeddings.Count;

        var latestVector = await db.UserVectors
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);

        if (latestVector is null) return;

        latestVector.ExpressionEmbedding = new Vector(mean);
        latestVector.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("[TileEmbedding] Updated ExpressionEmbedding for user {UserId} ({Count} tiles)",
            userId, recentEmbeddings.Count);
    }
}
