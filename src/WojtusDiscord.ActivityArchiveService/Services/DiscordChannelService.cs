using WojtusDiscord.ActivityArchiveService.Database;
using WojtusDiscord.ActivityArchiveService.Models;

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

    public DiscordChannel[] CreateMany(DiscordChannel[] channels)
    {
        foreach (var channel in channels)
        {
            if (!_context.DiscordChannels.Any(x => x.DiscordId == channel.DiscordId))
            {
                _context.DiscordChannels.Add(channel);
            }
        }
        _context.SaveChanges();
        return channels;
    }

    public DiscordChannel Create(DiscordChannel channel)
    {
        if (!_context.DiscordChannels.Any(x => x.DiscordId == channel.DiscordId))
        {
            _context.DiscordChannels.Add(channel);
        }
        _context.SaveChanges();
        return channel;
    }

    public DiscordChannel? GetByDiscordId(ulong id)
    {
        return _context.DiscordChannels.FirstOrDefault(x => x.DiscordId == id);
    }

    public DiscordTypingStatus CreateTypingStatus(DiscordTypingStatus status)
    {
        _context.DiscordTypingStatuses.Add(status);
        _context.SaveChanges();
        return status;
    }

    public DiscordVoiceStatus CreateVoiceState(DiscordVoiceStatus voiceStatus)
    {
        _context.DiscordVoiceStatuses.Add(voiceStatus);
        _context.SaveChanges();
        return voiceStatus;
    }
}