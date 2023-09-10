using Discord;
using Discord.WebSocket;

namespace ActivityListenerService.Services;

public class DiscordNetService : IHostedService
{
    private readonly ILogger<DiscordNetService> _logger;
    private readonly DiscordSocketClient _client;
    public DiscordNetService(ILogger<DiscordNetService> logger)
    {
        _logger = logger;
        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.All,
        };
        
        _client = new DiscordSocketClient(config);
        _client.Ready += ReadyAsync;
        _client.PresenceUpdated += PresenceUpdatedAsync;
    }

    private Task PresenceUpdatedAsync(SocketUser arg1, SocketPresence presenceBefore, SocketPresence presenceAfter)
    {
        _logger.LogInformation("Presence updated");
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _client.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("DiscordToken"));
        return _client.StartAsync();
        
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return _client.StopAsync();
    }
    
    private Task ReadyAsync()
    {
        _logger.LogInformation("Discord.Net is ready");
        return Task.CompletedTask;
    }
}