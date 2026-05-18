using Microsoft.EntityFrameworkCore;
using Pgvector;
using WovenBackend.Data;

namespace WovenBackend.Services.Embeddings;

public class AttachmentProxyService : IAttachmentProxyService
{
    private readonly WovenDbContext _db;
    private readonly ILogger<AttachmentProxyService> _logger;

    public AttachmentProxyService(WovenDbContext db, ILogger<AttachmentProxyService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ComputeAttachmentProxyAsync(int userId, CancellationToken ct = default)
    {
        // Fetch threads where user participated
        var threads = await _db.ChatThreads.AsNoTracking()
            .Where(t => _db.ChatMessages.Any(m => m.ThreadId == t.Id && m.SenderUserId == userId))
            .Select(t => t.Id)
            .ToListAsync(ct);

        if (threads.Count < 5)
        {
            _logger.LogInformation("[AttachmentProxy] Skipping user {UserId} — only {N} threads", userId, threads.Count);
            return;
        }

        // Load messages for qualifying threads (>=10 messages each)
        var threadMessages = await _db.ChatMessages.AsNoTracking()
            .Where(m => threads.Contains(m.ThreadId))
            .Select(m => new
            {
                m.ThreadId,
                m.SenderUserId,
                m.CreatedAt,
                BodyLen = m.Body.Length
            })
            .ToListAsync(ct);

        var qualifyingThreadIds = threadMessages
            .GroupBy(m => m.ThreadId)
            .Where(g => g.Count() >= 10)
            .Select(g => g.Key)
            .ToList();

        if (qualifyingThreadIds.Count < 5)
        {
            _logger.LogInformation("[AttachmentProxy] Skipping user {UserId} — only {N} qualifying threads", userId, qualifyingThreadIds.Count);
            return;
        }

        var features = new float[4];
        var responseTimes = new List<double>();
        var lengthConsistencies = new List<double>();
        int totalInitiated = 0;
        int totalFollowUps = 0;
        int totalMessages = 0;

        foreach (var threadId in qualifyingThreadIds)
        {
            var msgs = threadMessages
                .Where(m => m.ThreadId == threadId)
                .OrderBy(m => m.CreatedAt)
                .ToList();

            var userMsgs = msgs.Where(m => m.SenderUserId == userId).ToList();
            var otherMsgs = msgs.Where(m => m.SenderUserId != userId).ToList();

            // Response time: time from last other-user message to user's next reply
            for (int i = 0; i < msgs.Count - 1; i++)
            {
                if (msgs[i].SenderUserId != userId && msgs[i + 1].SenderUserId == userId)
                {
                    var rt = (msgs[i + 1].CreatedAt - msgs[i].CreatedAt).TotalHours;
                    if (rt >= 0 && rt < 72) responseTimes.Add(rt);
                }
            }

            // Message length consistency (std dev of user message lengths)
            if (userMsgs.Count > 1)
            {
                var avgLen = userMsgs.Average(m => (double)m.BodyLen);
                var stdDev = Math.Sqrt(userMsgs.Average(m => (m.BodyLen - avgLen) * (m.BodyLen - avgLen)));
                lengthConsistencies.Add(stdDev);
            }

            // Initiation rate: did user send the first message?
            if (msgs.Count > 0 && msgs[0].SenderUserId == userId) totalInitiated++;

            // Follow-up rate: user messages that follow another user message (double-texting)
            for (int i = 1; i < msgs.Count; i++)
            {
                if (msgs[i].SenderUserId == userId && msgs[i - 1].SenderUserId == userId)
                    totalFollowUps++;
            }
            totalMessages += userMsgs.Count;
        }

        // Feature 0: avg response time hours, normalized (0h = 1.0, 24h = 0.0)
        features[0] = responseTimes.Count > 0
            ? Math.Clamp(1f - (float)(responseTimes.Average() / 24.0), 0f, 1f)
            : 0.5f;

        // Feature 1: message length consistency (lower stddev = higher consistency)
        features[1] = lengthConsistencies.Count > 0
            ? Math.Clamp(1f - (float)(lengthConsistencies.Average() / 200.0), 0f, 1f)
            : 0.5f;

        // Feature 2: initiation rate (threads started by user / qualifying threads)
        features[2] = (float)totalInitiated / qualifyingThreadIds.Count;

        // Feature 3: follow-up rate (double-texts / total user messages)
        features[3] = totalMessages > 0
            ? Math.Clamp((float)totalFollowUps / totalMessages, 0f, 1f)
            : 0f;

        var vector = await _db.UserVectors
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);

        if (vector == null) return;

        vector.AttachmentProxyEmbedding = new Vector(features);
        vector.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("[AttachmentProxy] Computed for user {UserId}", userId);
    }
}
