using WojtusDiscord.ActivityArchiveService.Database;
using WojtusDiscord.ActivityArchiveService.Models;

namespace WojtusDiscord.ActivityArchiveService.Services
{
    public class DiscordGuildMemberService
    {
        private readonly ILogger<DiscordGuildMemberService> _logger;
        private readonly ActivityArchiveContext _context;

        public DiscordGuildMemberService(ILogger<DiscordGuildMemberService> logger, ActivityArchiveContext context)
        {
            _logger = logger;
            _context = context;
        }

        public DiscordGuildMember[] CreateMany(DiscordGuild guild, DiscordUser[] users)
        {
            var members = new List<DiscordGuildMember>();
            foreach (var user in users)
            {
                members.Add(new DiscordGuildMember
                {
                    DiscordGuild = guild,
                    DiscordUser = user
                });
            }

            _context.DiscordGuildMembers.AddRange(members);
            _context.SaveChanges();
            return members.ToArray();
        }
    }
}
