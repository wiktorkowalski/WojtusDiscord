using Microsoft.EntityFrameworkCore;
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

        public DiscordUser Create(DiscordUser model)
        {
            if (!_context.DiscordUsers.Any(x => x.DiscordId == model.DiscordId))
            {
                _context.DiscordUsers.Add(model);
                _context.SaveChanges();
                return model;
            }

            return _context.DiscordUsers.First(u => u.DiscordId == model.DiscordId);
        }

        public DiscordUser[] CreateMany(DiscordUser[] models)
        {
            foreach (var model in models)
            {
                if (!_context.DiscordUsers.Any(x => x.DiscordId == model.DiscordId))
                {
                    _context.DiscordUsers.Add(model);
                }
            }

            _context.SaveChanges();
            return models;
        }

        public void Delete(DiscordUser model)
        {
            _context.DiscordUsers.Remove(model);
            _context.SaveChanges();
        }

        public DiscordUser? Get(Guid id)
        {
            return _context.DiscordUsers.FirstOrDefault(x => x.Id == id);
        }

        public IEnumerable<DiscordUser> GetAll()
        {
            return _context.DiscordUsers;
        }

        public void Update(DiscordUser model)
        {
            _context.DiscordUsers.Update(model);
            _context.SaveChanges();
        }
    }
}