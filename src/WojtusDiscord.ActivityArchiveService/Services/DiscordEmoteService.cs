using WojtusDiscord.ActivityArchiveService.Database;
using WojtusDiscord.ActivityArchiveService.Models;

namespace WojtusDiscord.ActivityArchiveService.Services
{
    public class DiscordEmoteService
    {
        private readonly ILogger<DiscordEmoteService> _logger;
        private readonly ActivityArchiveContext _context;

        public DiscordEmoteService(ILogger<DiscordEmoteService> logger, ActivityArchiveContext context)
        {
            _logger = logger;
            _context = context;
        }

        public DiscordEmote Create(DiscordEmote emote)
        {
            if (!_context.DiscordEmotes.Any(x => x.DiscordId == emote.DiscordId))
            {
                _context.DiscordEmotes.Add(emote);
                _context.SaveChanges();
                return emote;
            }

            return _context.DiscordEmotes.First(e => e.DiscordId == emote.DiscordId);
        }

        public DiscordEmote[] CreateMany(DiscordEmote[] emotes)
        {
            foreach (var emote in emotes)
            {
                if (!_context.DiscordEmotes.Any(x => x.DiscordId == emote.DiscordId))
                {
                    _context.DiscordEmotes.Add(emote);
                }
            }
            _context.SaveChanges();
            return emotes;
        }
    }
}
