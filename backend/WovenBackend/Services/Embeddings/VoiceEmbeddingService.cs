using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using WovenBackend.Data;
using WovenBackend.Data.Entities;

namespace WovenBackend.Services.Embeddings;

public class VoiceEmbeddingService : IVoiceEmbeddingService
{
    private readonly WovenDbContext _db;
    private readonly HttpClient _http;
    private readonly ILogger<VoiceEmbeddingService> _logger;

    public VoiceEmbeddingService(
        WovenDbContext db,
        HttpClient http,
        ILogger<VoiceEmbeddingService> logger)
    {
        _db = db;
        _http = http;
        _logger = logger;
    }

    public async Task EmbedVoiceAsync(Guid tileId, int userId, string audioUrl, CancellationToken ct = default)
    {
        try
        {
            // Download audio to a temp file
            var audioBytes = await _http.GetByteArrayAsync(audioUrl, ct);
            var tempPath = Path.GetTempFileName() + ".wav";
            await File.WriteAllBytesAsync(tempPath, audioBytes, ct);

            float[]? embedding;
            try
            {
                embedding = await RunSpeechBrainAsync(tempPath, ct);
            }
            finally
            {
                File.Delete(tempPath);
            }

            if (embedding == null || embedding.Length != 192)
            {
                _logger.LogWarning("[VoiceEmbedding] Invalid embedding for tile {TileId}", tileId);
                return;
            }

            // Store on tile
            var tile = await _db.Tiles.FirstOrDefaultAsync(t => t.Id == tileId, ct);
            if (tile == null) return;

            tile.VoiceEmbedding = new Vector(embedding);
            await _db.SaveChangesAsync(ct);

            // Upsert user_voice_preference via element-wise mean
            await UpdateVoicePreferenceAsync(userId, embedding, ct);

            _logger.LogInformation("[VoiceEmbedding] Stored voice embedding for tile {TileId}, user {UserId}", tileId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[VoiceEmbedding] Failed for tile {TileId}, user {UserId}", tileId, userId);
        }
    }

    private static async Task<float[]?> RunSpeechBrainAsync(string audioPath, CancellationToken ct)
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "scripts", "speechbrain_embed.py");
        var psi = new ProcessStartInfo("python3", $"\"{scriptPath}\" \"{audioPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var proc = Process.Start(psi);
        if (proc == null) return null;

        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0) return null;

        try
        {
            return JsonSerializer.Deserialize<float[]>(stdout.Trim());
        }
        catch
        {
            return null;
        }
    }

    private async Task UpdateVoicePreferenceAsync(int userId, float[] newEmbedding, CancellationToken ct)
    {
        var pref = await _db.UserVoicePreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (pref == null)
        {
            _db.UserVoicePreferences.Add(new UserVoicePreference
            {
                UserId = userId,
                PreferenceEmbedding = new Vector(newEmbedding),
                YesSampleCount = 1,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            // Incremental element-wise mean: mean = (mean * n + new) / (n + 1)
            var n = pref.YesSampleCount;
            var current = pref.PreferenceEmbedding?.Memory.ToArray() ?? new float[192];
            var updated = new float[192];
            for (int i = 0; i < 192; i++)
                updated[i] = (current[i] * n + newEmbedding[i]) / (n + 1);

            pref.PreferenceEmbedding = new Vector(updated);
            pref.YesSampleCount = n + 1;
            pref.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }
}
