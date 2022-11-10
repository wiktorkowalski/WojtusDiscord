using Microsoft.EntityFrameworkCore;
using WojtusDiscord.ActivityArchiveService.Mappers;
using WojtusDiscord.ActivityArchiveService.Models;

namespace WojtusDiscord.ActivityArchiveService.Database
{
    public class ActivityArchiveContext : DbContext
    {
        public ActivityArchiveContext(DbContextOptions<ActivityArchiveContext> options) : base(options)
        {
            DiscordApiObjectsToModelsMapper.InitializeMappings();
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder
                .EnableSensitiveDataLogging()
                .EnableDetailedErrors();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Composite Keys can only be created via FluentAPI
            modelBuilder.Entity<DiscordGuildMember>()
                .HasKey(m => new {m.DiscordGuildId, m.DiscordUserId});

            //enum conversions
            modelBuilder.Entity<DiscordPresenceStatus>()
                .Property(p => p.Status)
                .HasConversion<string>();
            modelBuilder.Entity<DiscordPresenceStatus>()
                .Property(p => p.ActivityType)
                .HasConversion<string>();
        }

        //methods for GetAndCreate, EnsureExists and so on


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
