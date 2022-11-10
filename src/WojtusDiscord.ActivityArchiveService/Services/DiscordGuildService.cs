using WojtusDiscord.ActivityArchiveService.Database;

namespace WojtusDiscord.ActivityArchiveService.Services
{
    public class DiscordGuildService
    {
        private readonly ILogger<DiscordGuildService> _logger;
        private readonly ActivityArchiveContext _context;

        public DiscordGuildService(ILogger<DiscordGuildService> logger, ActivityArchiveContext context)
        {
            _logger = logger;
            _context = context;
        }
    }
}
