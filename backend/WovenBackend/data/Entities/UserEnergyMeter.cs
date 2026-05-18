using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.Data.Entities;

[Table("user_energy_meter")]
public class UserEnergyMeter
{
    [Column("user_id")]
    public int UserId { get; set; }

    [Column("date_utc")]
    public DateOnly DateUtc { get; set; }

    [Column("tiles_viewed")]
    public int TilesViewed { get; set; }
}
