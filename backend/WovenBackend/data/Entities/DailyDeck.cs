namespace WovenBackend.Data.Entities;
using WovenBackend.Data; 
public class DailyDeck
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = default!;

    // UTC date key (2026-01-03)
    public DateOnly DateUtc { get; set; }

    // When this deck was generated
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    // Array of 5 deck items as JSON:
    // [
    //   {
    //     "candidateId": 456,
    //     "score": 87.5,
    //     "bucket": "CORE_FIT",
    //     "explanationId": 123
    //   },
    //   ...
    // ]
    public string ItemsJson { get; set; } = "[]";
}