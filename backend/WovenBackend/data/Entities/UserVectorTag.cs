namespace WovenBackend.Data.Entities;
using WovenBackend.Data; 
public class UserVectorTag
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = default!;

    // Vector version this tag belongs to
    public int Version { get; set; }

    // Tag category: "values", "lifestyle", "hobbies", "communication", etc.
    public string TagType { get; set; } = "";

    // Tag value: "growth", "active", "family-oriented", "direct", etc.
    public string Tag { get; set; } = "";

    // Tag weight/importance (0.0-1.0)
    public double Weight { get; set; } = 1.0;
}