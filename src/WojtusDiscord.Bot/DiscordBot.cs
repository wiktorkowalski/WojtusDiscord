using System;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.SlashCommands;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WojtusDiscord.Bot.Modules;

namespace WojtusDiscord.Bot
{
    internal class DiscordBot : IHostedService
    {
        private readonly ILogger<DiscordBot> _logger;
        private readonly DiscordClient _discordClient;

        public DiscordBot(ILogger<DiscordBot> logger)
        {
            _logger = logger;
            _discordClient = new DiscordClient(GetDiscordConfiguration());
        }

        private DiscordConfiguration GetDiscordConfiguration()
        {
            return new DiscordConfiguration
            {
                Token = Environment.GetEnvironmentVariable("DiscordToken"),
                TokenType = TokenType.Bot,
                Intents = DiscordIntents.All,
                MinimumLogLevel = LogLevel.Debug
            };
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting DiscordBot service...");
            
            var commands = _discordClient.UseSlashCommands();
            commands.RegisterCommands<InfoCommandsModule>();

            return _discordClient.ConnectAsync();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping DiscordBot service...");
            return _discordClient.DisconnectAsync();
        }
    }
}
