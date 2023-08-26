using ActivityArchiveService.Configuration;
using ActivityArchiveService.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ActivityArchiveService.Database;

public class ActivityArchiveContext : DbContext
{
        private readonly DatabaseConfig _config;
        private readonly ILoggerFactory _loggerFactory;

        public ActivityArchiveContext(DbContextOptions<ActivityArchiveContext> options, IOptions<DatabaseConfig> config, ILoggerFactory loggerFactory) : base(options)
        {
            this._config = config.Value;
            this._loggerFactory = loggerFactory;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder
                .UseLoggerFactory(_loggerFactory)
                .UseNpgsql(_config.ToConnectionString())
                .UseSnakeCaseNamingConvention()
                .EnableSensitiveDataLogging()
                .EnableDetailedErrors();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            //enum conversions
            modelBuilder.Entity<DiscordPresenceStatusDetails>(p =>
            {
                p.Property(p => p.DesktopStatus).HasConversion<string>();
                p.Property(p => p.MobileStatus).HasConversion<string>();
                p.Property(p => p.WebStatus).HasConversion<string>();
            });

            modelBuilder.Entity<DiscordActivity>()
                .Property(p => p.ActivityType)
                .HasConversion<string>();

            modelBuilder.Entity<DiscordChannel>()
                .Property(p => p.Type)
                .HasConversion<string>();
            
            // soft delete filter
            //modelBuilder.Entity<BaseEntity>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            modelBuilder.Entity<DiscordActivity>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            modelBuilder.Entity<DiscordChannel>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            modelBuilder.Entity<DiscordEmote>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            modelBuilder.Entity<DiscordGuild>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            modelBuilder.Entity<DiscordGuildMember>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            modelBuilder.Entity<DiscordMessage>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            modelBuilder.Entity<DiscordMessageContentEdit>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            modelBuilder.Entity<DiscordPresenceStatus>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            modelBuilder.Entity<DiscordPresenceStatusDetails>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            modelBuilder.Entity<DiscordReaction>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            modelBuilder.Entity<DiscordTypingStatus>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            modelBuilder.Entity<DiscordUser>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            modelBuilder.Entity<DiscordVoiceStatus>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            modelBuilder.Entity<DiscordVoiceStatusDetails>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            
            
            base.OnModelCreating(modelBuilder);
        }

        public override int SaveChanges()
        {
            var items = ChangeTracker.Entries<BaseEntity>();
            
            foreach (var item in items)
            {
                if(item.State == EntityState.Deleted)
                {
                    item.State = EntityState.Modified;
                    item.Entity.DeletedAt = DateTime.UtcNow;
                    continue;
                }
                
                if (item.State == EntityState.Added)
                {
                    item.Entity.CreatedAt = DateTime.UtcNow;
                    item.Entity.UpdatedAt = DateTime.UtcNow;
                    continue;
                }
                
                if (item.State == EntityState.Modified)
                {
                    item.Entity.UpdatedAt = DateTime.UtcNow;
                }
            }

            return base.SaveChanges();
        }

        #region DbSets

        public DbSet<DiscordActivity> DiscordActivities { get; set; }
        public DbSet<DiscordChannel> DiscordChannels { get; set; }
        public DbSet<DiscordEmote> DiscordEmotes { get; set; }
        public DbSet<DiscordGuild> DiscordGuilds { get; set; }
        public DbSet<DiscordGuildMember> DiscordGuildMembers { get; set; }
        public DbSet<DiscordMessage> DiscordMessages { get; set; }
        public DbSet<DiscordMessageContentEdit> DiscordMessageContentEdits { get; set; }
        public DbSet<DiscordPresenceStatus> DiscordPresenceStatuses { get; set; }
        public DbSet<DiscordPresenceStatusDetails> DiscordPresenceStatusDetails { get; set; }
        public DbSet<DiscordReaction> DiscordReactions { get; set; }
        public DbSet<DiscordTypingStatus> DiscordTypingStatuses { get; set; }
        public DbSet<DiscordUser> DiscordUsers { get; set; }
        public DbSet<DiscordVoiceStatus> DiscordVoiceStatuses { get; set; }
        public DbSet<DiscordVoiceStatusDetails> DiscordVoiceStatusDetails { get; set; }

        #endregion
}