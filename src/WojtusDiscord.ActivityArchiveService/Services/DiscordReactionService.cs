using Microsoft.AspNetCore.Identity;
using WojtusDiscord.ActivityArchiveService.Database;
using WojtusDiscord.ActivityArchiveService.Models;

namespace WojtusDiscord.ActivityArchiveService.Services
{
    public class DiscordReactionService
    {
        private readonly ILogger<DiscordReactionService> _logger;
        private readonly ActivityArchiveContext _context;

        public DiscordReactionService(ILogger<DiscordReactionService> logger, ActivityArchiveContext context)
        {
            _logger = logger;
            _context = context;
        }

        public DiscordReaction Create(DiscordReaction reaction)
        {
            _context.DiscordReactions.Add(reaction);
            _context.SaveChanges();

            return reaction;
        }

        public void SetAsRemoved(ulong userId, ulong messageId, ulong emoteId)
        {
            var reaction = _context.DiscordReactions.FirstOrDefault(r => r.User.DiscordId == userId && r.Message.DiscordId == messageId && r.Emote.DiscordId == emoteId);
            reaction.IsRemoved = true;
            _context.SaveChanges();
        }
    }
}
