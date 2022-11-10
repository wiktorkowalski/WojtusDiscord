using WojtusDiscord.ActivityArchiveService.Models;

namespace WojtusDiscord.ActivityArchiveService.Services
{
    public class GuildInitializerService
    {
        private readonly ILogger<GuildInitializerService> _logger;
        private readonly DiscordUserService _discordUserService;
        private readonly DiscordGuildService _discordGuildService;
        private readonly DiscordChannelService _discordTextChannelService;

        public GuildInitializerService(ILogger<GuildInitializerService> logger, DiscordUserService discordUserService, DiscordGuildService discordGuildService, DiscordChannelService discordTextChannelService)
        {
            _logger = logger;
            _discordUserService = discordUserService;
            _discordGuildService = discordGuildService;
            _discordTextChannelService = discordTextChannelService;
        }

        public async Task CreateUsers(DiscordUser[] users)
        {
            
        }

        public async Task CreateGuild(DiscordGuild guild)
        {
            
        }
    }
}
