using Discord;
using Discord.WebSocket;

namespace WojtusDiscord.ArchiveService
{
    public class DiscordService : IHostedService
    {
        private readonly ILogger<DiscordService> _logger;
        private readonly DiscordEventsHandlers _discordEventsHandlers;
        private readonly DiscordSocketClient _discordClient;

        public DiscordService(ILogger<DiscordService> logger, DiscordEventsHandlers discordEventsHandlers)
        {
            _logger = logger;
            _discordEventsHandlers = discordEventsHandlers;
            _discordClient = new DiscordSocketClient(new DiscordSocketConfig
            {
                LogLevel = LogSeverity.Debug
            });
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting ArchiveService...");

            _discordEventsHandlers.AssignEventHandlers(_discordClient);
            _discordClient.Ready += Ready;
            
            await _discordClient.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("DiscordToken"));
            await _discordClient.StartAsync();
            

            _discordClient.SlashCommandExecuted += SlashCommandExecuted;
        }

        private async Task Ready()
        {
            var command = new SlashCommandBuilder().WithName("file").WithDescription("Get link to share files");
            await _discordClient.CreateGlobalApplicationCommandAsync(command.Build());
        }

        private Task SlashCommandExecuted(SocketSlashCommand arg)
        {
            return arg.RespondAsync(Environment.GetEnvironmentVariable("FileShareURL") ?? ".");
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping ArchiveService...");
            return _discordClient.StopAsync();
        }
    }
}
