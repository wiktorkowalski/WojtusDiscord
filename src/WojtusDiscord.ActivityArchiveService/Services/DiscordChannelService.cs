using WojtusDiscord.ActivityArchiveService.Database;

namespace WojtusDiscord.ActivityArchiveService.Services;

public class DiscordChannelService
{
    private readonly ILogger<DiscordChannelService> _logger;
    private readonly ActivityArchiveContext _context;

    public DiscordChannelService(ILogger<DiscordChannelService> logger, ActivityArchiveContext context)
    {
        _logger = logger;
        _context = context;
    }
}