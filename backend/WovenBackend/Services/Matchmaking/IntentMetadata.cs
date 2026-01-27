namespace WovenBackend.Services.Matchmaking;

public class IntentMetadata
{
    public double Seriousness { get; set; } = 0.5;      // 0 = casual, 1 = very serious
    public double Flexibility { get; set; } = 0.5;      // 0 = rigid, 1 = very flexible
    public double CommitmentReadiness { get; set; } = 0.5;  // 0 = exploring, 1 = ready now

    public List<string> Tags { get; set; } = new();
}