using System;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WojtusDiscord.Bot
{
    internal class DiscordBot : IHostedService
    {
        private readonly ILogger<DiscordBot> logger;
        private DiscordClient discordClient;

        public DiscordBot(ILogger<DiscordBot> logger)
        {
            this.logger = logger;
            discordClient = new DiscordClient(GetDiscordConfiguration());
        }

        private DiscordConfiguration GetDiscordConfiguration()
        {
            return new DiscordConfiguration
            {
                Token = Environment.GetEnvironmentVariable("DiscordToken"),
                TokenType = TokenType.Bot,
                Intents = DiscordIntents.All, // Full admin right as the bot is designed for a private server
                MinimumLogLevel = LogLevel.Debug
            };
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("Starting DiscordBot service...");

            return discordClient.ConnectAsync();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("Stopping DiscordBot service...");

            return discordClient.DisconnectAsync();
        }
    }
}
