using WovenBackend.Data.Entities;

namespace WovenBackend.Data.Entities;

public class UserDynamicIntakeSet
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = default!;

    // e.g. "dyn48_2026010300"
    public string CycleId { get; set; } = default!;

    public DateTime CycleStartUtc { get; set; }
    public DateTime CycleEndUtc { get; set; }

    // Frozen per cycle (AI-rewritten display text)
    // Shape: [{ id, text, options:[{ key,label,subLabel }] }, ...]
    public string VariantJson { get; set; } = "[]";

    // Canonical answers only:
    // { "d1_battery":"high", "d2_tone":"calm", "d3_role":"copilot" }
    public string AnswersJson { get; set; } = "{}";

    // Derived features for matchmaking:
    // { "socialCapacity":0.6, "initiative":0.9, ... }
    public string FeaturesJson { get; set; } = "{}";

    public int MappingVersion { get; set; } = 1;

    // "base" | "ai"
    public string VariantSource { get; set; } = "base";

    // metadata: { "model": "...", "promptVersion": 1, ... }
    public string GenerationMetaJson { get; set; } = "{}";

    public DateTime? AnsweredAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
