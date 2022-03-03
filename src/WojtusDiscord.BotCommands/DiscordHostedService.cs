using dotenv.net.Utilities;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Serilog;

namespace WojtusDiscord.BotCommands
{
    internal class DiscordHostedService : IHostedService
    {
        private readonly ILogger<DiscordHostedService> _logger;
        private readonly DiscordClient _discordClient;

        public DiscordHostedService(ILogger<DiscordHostedService> logger)
        {
            _logger = logger;
            _discordClient = new DiscordClient(GetDiscordConfiguration());
        }

        private DiscordConfiguration GetDiscordConfiguration()
        {
            return new DiscordConfiguration
            {
                Token = "ODU5NDc4NTY3MTI5MzE3Mzg2.YNtRyg.NuKdxRbojzZcwaBQSLg1Wpjca5U",//EnvReader.GetStringValue("discord-token"),
                TokenType = TokenType.Bot,
                Intents = DiscordIntents.All,
                LoggerFactory = new LoggerFactory().AddSerilog()
            };
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting DiscordService");

            _discordClient.MessageCreated += MessageCreated;

            return _discordClient.ConnectAsync();
        }

        private Task MessageCreated(DiscordClient sender, MessageCreateEventArgs e)
        {
            _logger.LogInformation($"Message received: [{e.Author.Username}]: {e.Message.Content}");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping DiscordService");
            return _discordClient.DisconnectAsync();
        }
    }
}
