﻿using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WojtusDiscord.ActivityArchiveService.Config;
using WojtusDiscord.ActivityArchiveService.Models;

namespace WojtusDiscord.ActivityArchiveService.Database
{
    public class ActivityArchiveContext : DbContext
    {
        private readonly DatabaseConfig config;
        private readonly ILoggerFactory loggerFactory;

        public ActivityArchiveContext(DbContextOptions<ActivityArchiveContext> options, IOptions<DatabaseConfig> config, ILoggerFactory loggerFactory) : base(options)
        {
            this.config = config.Value;
            this.loggerFactory = loggerFactory;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder
                .UseLoggerFactory(loggerFactory)
                .UseNpgsql(config.ToConnectionString())
                .UseSnakeCaseNamingConvention()
                .EnableSensitiveDataLogging()
                .EnableDetailedErrors();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Composite Keys can only be created via FluentAPI
            modelBuilder.Entity<DiscordGuildMember>()
                .HasKey(m => new { m.DiscordGuildId, m.DiscordUserId });

            //One-to-one relations
            //modelBuilder.Entity<DiscordVoiceStatusDetails>()
            modelBuilder.Entity<DiscordVoiceStatus>()
                .HasOne(s => s.Details)
                .WithOne(e => e.Status)
                .HasForeignKey<DiscordVoiceStatusDetails>(e => e.StatusId);
            //modelBuilder.Entity<DiscordVoiceStatus>()
            //    .HasOne(s => s.VoiceStatusAfter)
            //    .WithOne(e => e.VoiceStatusAfter)
            //    .HasForeignKey<DiscordVoiceStatusDetails>(e => e.VoiceStatusAfterId);

            //modelBuilder.Entity<DiscordPresenceStatus>()
            //    .HasOne(s => s.PresenceStatusBefore)
            //    .WithOne(e => e.PresenceStatusBefore)
            //    .HasForeignKey<DiscordPresenceStatusDetails>(e => e.PresenceStatusBeforeId);
            //modelBuilder.Entity<DiscordPresenceStatus>()
            //    .HasOne(s => s.PresenceStatusAfter)
            //    .WithOne(e => e.PresenceStatusAfter)
            //    .HasForeignKey<DiscordPresenceStatusDetails>(e => e.PresenceStatusAfterId);

            //enum conversions
            modelBuilder.Entity<DiscordPresenceStatusDetails>()
                .Property(p => p.Status)
                .HasConversion<string>();

            modelBuilder.Entity<DiscordPresenceStatusDetails>()
                .Property(p => p.ActivityType)
                .HasConversion<string>();
        }

        public override int SaveChanges()
        {
            var items = ChangeTracker.Entries<BaseModel>().Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

            foreach (var item in items)
            {
                if (item.State == EntityState.Added)
                {
                    item.Entity.CreatedAt = DateTime.UtcNow;
                }
                item.Entity.UpdatedAt = DateTime.UtcNow;
            }

            return base.SaveChanges();
        }

        #region DbSets

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
        public DbSet<DiscordVoiceStatusDetails> DiscordVoiceStatusEntries { get; set; }
        public DbSet<DiscordPresenceStatus> DiscordPresenceStatuses { get; set; }
        public DbSet<DiscordPresenceStatusDetails> DiscordPresenceStatusEntries { get; set; }

        #endregion
    }
}