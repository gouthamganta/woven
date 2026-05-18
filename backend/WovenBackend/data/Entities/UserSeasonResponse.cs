using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.Data.Entities;

[Table("user_season_responses")]
public class UserSeasonResponse
{
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("season_id")]
    public int SeasonId { get; set; }

    [Column("pillar_id")]
    [MaxLength(50)]
    public string PillarId { get; set; } = default!;

    [Column("question_id")]
    [MaxLength(100)]
    public string QuestionId { get; set; } = default!;

    [Column("response")]
    public string Response { get; set; } = default!;

    [Column("responded_at")]
    public DateTimeOffset RespondedAt { get; set; } = DateTimeOffset.UtcNow;

    public Season Season { get; set; } = default!;
}
