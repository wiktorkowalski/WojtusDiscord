using WojtusDiscord.ActivityArchiveService.Database;
using WojtusDiscord.ActivityArchiveService.Models;

namespace WojtusDiscord.ActivityArchiveService.Services
{
    public class DiscordUserService
    {
        private readonly ILogger<DiscordUserService> _logger;
        private readonly ActivityArchiveContext _context;

        public DiscordUserService(ILogger<DiscordUserService> logger, ActivityArchiveContext context)
        {
            _logger = logger;
            _context = context;
        }
    }
}