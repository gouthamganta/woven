using WovenBackend.Data;

namespace WovenBackend.Data;

public class UserPhoto
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = default!;

    public string Url { get; set; } = "";
    public string? Caption { get; set; }          // <= 40 chars

    public int SortOrder { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
