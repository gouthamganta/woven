using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.Data.Entities;

[Table("spark_wallets")]
public class SparkWallet
{
    [Column("user_id")]
    public int UserId { get; set; }

    // Stored as tenths to support 0.5 increments without floating point drift.
    // balance_tenths = 50 → 5.0 sparks. Max 100 → 10.0 sparks.
    [Column("balance_tenths")]
    public int BalanceTenths { get; set; } = 50;

    [Column("last_earned_date")]
    public DateOnly? LastEarnedDate { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
