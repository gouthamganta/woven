using Microsoft.Extensions.DependencyInjection;

namespace WovenBackend.Services.Games;

public interface IGameAgentFactory
{
    IGameAgent GetAgent(string gameType);
}

public class GameAgentFactory : IGameAgentFactory
{
    private readonly IServiceProvider _serviceProvider;

    public GameAgentFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IGameAgent GetAgent(string gameType)
    {
        return gameType.ToUpperInvariant() switch
        {
            "KNOW_ME" => _serviceProvider.GetRequiredService<KnowMeAgent>(),
            "RED_GREEN_FLAG" => _serviceProvider.GetRequiredService<RedGreenFlagAgent>(),
            // Add more game types here as you build them:
            // "TOP_10" => _serviceProvider.GetRequiredService<Top10Agent>(),
            // "RAPID_FIRE" => _serviceProvider.GetRequiredService<RapidFireAgent>(),
            _ => throw new ArgumentException($"Unknown game type: {gameType}")
        };
    }
}