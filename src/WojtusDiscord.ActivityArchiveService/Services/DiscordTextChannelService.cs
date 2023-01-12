using WojtusDiscord.ActivityArchiveService.Database;
using WojtusDiscord.ActivityArchiveService.Models;

namespace WojtusDiscord.ActivityArchiveService.Services;

public class DiscordTextChannelService
{
    private readonly ILogger<DiscordTextChannelService> _logger;
    private readonly ActivityArchiveContext _context;

    public DiscordTextChannelService(ILogger<DiscordTextChannelService> logger, ActivityArchiveContext context)
    {
        _logger = logger;
        _context = context;
    }

    public DiscordTextChannel[] CreateMany(DiscordTextChannel[] channels)
    {
        foreach (var channel in channels)
        {
            if (!_context.DiscordTextChannels.Any(x => x.DiscordId == channel.DiscordId))
            {
                _context.DiscordTextChannels.Add(channel);
            }
        }
        _context.SaveChanges();
        return channels;
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

    public DiscordTextChannel Create(DiscordTextChannel channel)
    {
        if (!_context.DiscordTextChannels.Any(x => x.DiscordId == channel.DiscordId))
        {
            _context.DiscordTextChannels.Add(channel);
        }
        _context.SaveChanges();
        return channel;
    }

    public DiscordTextChannel? GetByDiscordId(ulong id)
    {
        return _context.DiscordTextChannels.FirstOrDefault(x => x.DiscordId == id);
    }

    public DiscordTypingStatus CreateTypingStatus(DiscordTypingStatus status)
    {
        _context.DiscordTypingStatuses.Add(status);
        _context.SaveChanges();
        return status;
    }
}