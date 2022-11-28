using WojtusDiscord.ActivityArchiveService.Database;
using WojtusDiscord.ActivityArchiveService.Models;

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


        public DiscordGuild Create(DiscordGuild model)
        {
            if (!_context.DiscordGuilds.Any(x => x.DiscordId == model.DiscordId))
            {
                var owner = _context.DiscordUsers.FirstOrDefault(x => x.DiscordId == model.Owner.DiscordId);
                if (owner != null)
                {
                    model.Owner = owner;
                }
                _context.DiscordGuilds.Add(model);
            }
            _context.SaveChanges();
            return model;
        }
    }
}
