namespace WovenBackend.Data;

public class UserPreference
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = default!;

    // Distance preference
    public int DistanceMiles { get; set; } = 25; // 15–100

    // Age preference
    public int AgeMin { get; set; } = 18;
    public int AgeMax { get; set; } = 99;

    // Stored as JSON string for MVP (["female","male"])
    public string InterestedInJson { get; set; } = "[]";

    // ✅ NEW: Relationship structure preference
    public WovenBackend.Data.Entities.RelationshipStructure RelationshipStructure { get; set; } 
        = WovenBackend.Data.Entities.RelationshipStructure.OPEN;

    // Phase 5A: accessibility preferences
    [System.ComponentModel.DataAnnotations.Schema.Column("reduce_motion")]
    public bool ReduceMotion { get; set; } = false;

    [System.ComponentModel.DataAnnotations.Schema.Column("high_contrast")]
    public bool HighContrast { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}