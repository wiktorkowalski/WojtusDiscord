using Microsoft.EntityFrameworkCore;
using WojtusDiscord.ActivityArchiveService.Models;

namespace WojtusDiscord.ActivityArchiveService.Database
{
    public class ActivityArchiveContext : DbContext
    {
        public ActivityArchiveContext(DbContextOptions<ActivityArchiveContext> options) : base(options)
        {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Composite Keys can only be created via modelBuilder
            modelBuilder.Entity<DiscordGuildMember>()
                .HasKey(m => new {m.DiscordGuildId, m.DiscordUserId});
        }

        public DbSet<DiscordUser> DiscordUsers { get; set; }
        public DbSet<DiscordGuild> DiscordGuilds { get; set; }
        public DbSet<DiscordGuildMember> DiscordGuildMembers { get; set; }
        public DbSet<DiscordTextChannel> DiscordTextChannels { get; set; }
        public DbSet<DiscordEmote> DiscordEmotes { get; set; }
        public DbSet<DiscordMessage> DiscordMessages { get; set; }
        public DbSet<DiscordMessageContentEdit> DiscordMessageContentEdit { get; set; }
        public DbSet<DiscordReaction> DiscordReactions { get; set; }
        public DbSet<DiscordTypingStatus> DiscordTypingStatuses { get; set; }
        public DbSet<DiscordVoiceChannel> DiscordVoiceChannels { get; set; }
        public DbSet<DiscordVoiceStatus> DiscordVoiceStatuses { get; set; }
        public DbSet<DiscordPresenceStatus> DiscordPresenceStatuses { get; set; }
    }
}
