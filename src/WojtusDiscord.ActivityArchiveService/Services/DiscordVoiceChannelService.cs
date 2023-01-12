using WojtusDiscord.ActivityArchiveService.Database;
using WojtusDiscord.ActivityArchiveService.Models;

namespace WojtusDiscord.ActivityArchiveService.Services;

public class DiscordVoiceChannelService
{
    private readonly ILogger<DiscordVoiceChannelService> _logger;
    private readonly ActivityArchiveContext _context;

    public DiscordVoiceChannelService(ILogger<DiscordVoiceChannelService> logger, ActivityArchiveContext context)
    {
        _logger = logger;
        _context = context;
    }

    public DiscordVoiceChannel[] CreateMany(DiscordVoiceChannel[] channels)
    {
        foreach (var channel in channels)
        {
            if (!_context.DiscordVoiceChannels.Any(x => x.DiscordId == channel.DiscordId))
            {
                _context.DiscordVoiceChannels.Add(channel);
            }
        }
        _context.SaveChanges();
        return channels;
    }

    public DiscordVoiceChannel? GetByDiscordId(ulong id)
    {
        return _context.DiscordVoiceChannels.FirstOrDefault(x => x.DiscordId == id);
    }

    public DiscordVoiceStatus CreateVoiceState(DiscordVoiceStatus voiceStatus)
    {
        _context.DiscordVoiceStatuses.Add(voiceStatus);
        _context.SaveChanges();
        return voiceStatus;
    }
}