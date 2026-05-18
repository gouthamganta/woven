using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.Data.Entities;

[Table("seasons")]
public class Season
{
    [Column("id")]
    public int Id { get; set; }

    [Column("season_number")]
    public int SeasonNumber { get; set; }

    [Column("start_date")]
    public DateOnly StartDate { get; set; }

    [Column("end_date")]
    public DateOnly EndDate { get; set; }

    [Column("prompt_text")]
    [MaxLength(200)]
    public string PromptText { get; set; } = default!;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<UserSeasonResponse> Responses { get; set; } = new List<UserSeasonResponse>();
}
