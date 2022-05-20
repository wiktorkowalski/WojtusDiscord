using Discord;
using Discord.WebSocket;

namespace WojtusDiscord.ArchiveService
{
    public class DiscordService : IHostedService
    {
        private readonly ILogger<DiscordService> _logger;
        private readonly DiscordSocketClient _discordClient;

        public DiscordService(ILogger<DiscordService> logger)
        {
            _logger = logger;
            _discordClient = new DiscordSocketClient();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting ArchiveService...");

            _discordClient.MessageReceived += OnMessageReceived;

            return Task.WhenAll
            (
                _discordClient.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("DiscordToken")),
                _discordClient.StartAsync()
            );
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping ArchiveService...");
            return _discordClient.StopAsync();
        }

        private Task OnMessageReceived(SocketMessage arg)
        {
            _logger.LogInformation($"[Message][{arg.Channel.Name}]{arg.Author.Username}:{arg.Content}");
            return Task.CompletedTask;
        }
    }
}
