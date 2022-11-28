using WojtusDiscord.ActivityArchiveService.Database;
using WojtusDiscord.ActivityArchiveService.Models;

namespace WojtusDiscord.ActivityArchiveService.Services;

public class DiscordMessageService
{
    private readonly ILogger<DiscordMessageService> _logger;
    private readonly ActivityArchiveContext _context;

    public DiscordMessageService(ILogger<DiscordMessageService> logger, ActivityArchiveContext context)
    {
        _logger = logger;
        _context = context;
    }

    public DiscordMessage Create(DiscordMessage message)
    {
        if (!_context.DiscordMessages.Any(x => x.DiscordId == message.DiscordId))
        {
            _context.DiscordMessages.Add(message);
            _context.SaveChanges();
        }
        else
        {
            message = _context.DiscordMessages.Single(m => m.DiscordId == message.DiscordId);
        }
        
        return message;
    }

    public DiscordMessage[] CreateMany(DiscordMessage[] messages)
    {
        foreach (var message in messages)
        {
            if (!_context.DiscordMessages.Any(x => x.DiscordId == message.DiscordId))
            {
                _context.DiscordMessages.Add(message);
            }
            else
            {
                message.Id = _context.DiscordMessages.Single(m => m.DiscordId == message.DiscordId).Id;
            }
        }
        _context.SaveChanges();

        return messages;
    }
}
