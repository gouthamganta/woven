namespace WovenBackend.Data.Entities;

public class UserBehavioralFingerprint
{
    // PK — one row per user, upserted nightly
    public int UserId { get; set; }

    // float[16] stored as JSON — see BehavioralFingerprintService for dimension definitions
    public string VectorJson { get; set; } = "[]";

    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
}
