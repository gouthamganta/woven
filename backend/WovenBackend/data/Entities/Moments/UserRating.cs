using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.data.Entities.Moments;

[Table("user_ratings")]
public class UserRating
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("rated_user_id")]
    public int RatedUserId { get; set; }

    [Column("rater_user_id")]
    public int RaterUserId { get; set; }

    [Column("match_id")]
    public Guid? MatchId { get; set; }

    [Column("rating_value")]
    public int RatingValue { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
