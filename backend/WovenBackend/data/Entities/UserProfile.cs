using WovenBackend.Data;

namespace WovenBackend.Data;

public class UserProfile
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = default!;

    public int Age { get; set; }                 // >= 18
    public string Gender { get; set; } = "";

    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public double? Lat { get; set; }
    public double? Lng { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
