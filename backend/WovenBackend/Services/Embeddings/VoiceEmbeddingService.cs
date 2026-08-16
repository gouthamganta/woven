using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using WovenBackend.Data;
using WovenBackend.Data.Entities;

namespace WovenBackend.Services.Embeddings;

public class VoiceEmbeddingService : IVoiceEmbeddingService
{
    private readonly WovenDbContext _db;
    private readonly HttpClient     _http;
    private readonly IConfiguration _config;
    private readonly ILogger<VoiceEmbeddingService> _logger;

    public VoiceEmbeddingService(
        WovenDbContext db,
        HttpClient     http,
        IConfiguration config,
        ILogger<VoiceEmbeddingService> logger)
    {
        _db     = db;
        _http   = http;
        _config = config;
        _logger = logger;
    }

    public async Task EmbedVoiceAsync(Guid tileId, int userId, string audioUrl, CancellationToken ct = default)
    {
        try
        {
            var audioBytes = await _http.GetByteArrayAsync(audioUrl, ct);

            var embedding = await GetEmbeddingAsync(audioBytes, ct);
            if (embedding == null || embedding.Length != 192)
            {
                _logger.LogWarning("[VoiceEmbedding] Invalid or missing embedding for tile {TileId}", tileId);
                return;
            }

            var tile = await _db.Tiles.FirstOrDefaultAsync(t => t.Id == tileId, ct);
            if (tile == null) return;

            tile.VoiceEmbedding = new Vector(embedding);
            await _db.SaveChangesAsync(ct);

            await UpdateUserVoiceEmbeddingAsync(userId, embedding, ct);

            _logger.LogInformation("[VoiceEmbedding] Stored embedding for tile {TileId}, user {UserId}", tileId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[VoiceEmbedding] Failed for tile {TileId}, user {UserId}", tileId, userId);
        }
    }

    // ── HTTP endpoint (production) ────────────────────────────────────────────
    // Expects: POST SpeechBrain:EndpointUrl with audio/wav body.
    // Returns: JSON float array of length 192 (ECAPA-TDNN speaker embedding).
    private async Task<float[]?> GetEmbeddingAsync(byte[] audioBytes, CancellationToken ct)
    {
        var endpointUrl = _config["SpeechBrain:EndpointUrl"];
        if (!string.IsNullOrWhiteSpace(endpointUrl))
            return await CallHttpEndpointAsync(endpointUrl, audioBytes, ct);

        // Dev fallback: spawn Python subprocess when no HTTP endpoint configured.
        return await RunSubprocessAsync(audioBytes, ct);
    }

    private async Task<float[]?> CallHttpEndpointAsync(string endpointUrl, byte[] audioBytes, CancellationToken ct)
    {
        try
        {
            using var content = new ByteArrayContent(audioBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

            using var response = await _http.PostAsync(endpointUrl, content, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[VoiceEmbedding] HTTP endpoint returned {Status}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<float[]>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[VoiceEmbedding] HTTP endpoint call failed for {Url}", endpointUrl);
            return null;
        }
    }

    // ── Subprocess fallback (dev only — not available in production containers) ─
    private static async Task<float[]?> RunSubprocessAsync(byte[] audioBytes, CancellationToken ct)
    {
        var tempPath = Path.GetTempFileName() + ".wav";
        try
        {
            await File.WriteAllBytesAsync(tempPath, audioBytes, ct);

            var scriptPath = Path.Combine(AppContext.BaseDirectory, "scripts", "speechbrain_embed.py");
            var psi = new ProcessStartInfo("python3", $"\"{scriptPath}\" \"{tempPath}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false
            };

            using var proc = Process.Start(psi);
            if (proc == null) return null;

            var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            if (proc.ExitCode != 0) return null;

            return JsonSerializer.Deserialize<float[]>(stdout.Trim());
        }
        catch
        {
            return null;
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    // Updates UserVector.VoiceEmbedding with a running mean of the user's own voice tiles.
    // UserVoicePreference (what voices attract this user) is updated separately by
    // TileViewProcessorWorker when the user dwells on someone else's voice tile.
    private async Task UpdateUserVoiceEmbeddingAsync(int userId, float[] newEmbedding, CancellationToken ct)
    {
        var userVector = await _db.UserVectors
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);

        if (userVector == null) return;

        if (userVector.VoiceEmbedding == null)
        {
            userVector.VoiceEmbedding = new Vector(newEmbedding);
        }
        else
        {
            // Running mean: (current * (n-1) + new) / n
            // n = total voice tiles with embeddings for this user (includes the one just saved)
            var n = await _db.Tiles
                .CountAsync(t => t.UserId == userId && t.VoiceEmbedding != null, ct);
            n = Math.Max(n, 2); // guard: at least 2 so denominator > previous count

            var current = userVector.VoiceEmbedding.Memory.ToArray();
            var updated = new float[192];
            for (int i = 0; i < 192; i++)
                updated[i] = (current[i] * (n - 1) + newEmbedding[i]) / n;

            userVector.VoiceEmbedding = new Vector(updated);
        }

        userVector.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}
