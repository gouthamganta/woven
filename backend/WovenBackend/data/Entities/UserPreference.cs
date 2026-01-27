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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}