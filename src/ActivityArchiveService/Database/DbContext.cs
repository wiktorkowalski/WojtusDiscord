using ActivityArchiveService.Configuration;
using ActivityArchiveService.Database.Entities;
using ActivityArchiveService.Database.Entities.Base;
using ActivityArchiveService.Database.Entities.Enums;
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
            #region enums

            modelBuilder.HasPostgresEnum<ActivityType>();
            modelBuilder.HasPostgresEnum<ChannelType>();
            modelBuilder.HasPostgresEnum<UserStatus>();
            
            #endregion
            
            #region enum conversions
            
            // modelBuilder.Entity<PresenceStatusDetails>(p =>
            // {
            //     p.Property(p => p.DesktopStatus).HasConversion<string>();
            //     p.Property(p => p.MobileStatus).HasConversion<string>();
            //     p.Property(p => p.WebStatus).HasConversion<string>();
            // });
            //
            // modelBuilder.Entity<Activity>()
            //     .Property(p => p.ActivityType)
            //     .HasConversion<string>();
            //
            // modelBuilder.Entity<Channel>()
            //     .Property(p => p.Type)
            //     .HasConversion<string>();

            #endregion
            
            #region soft delete

            modelBuilder.Entity<Activity>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            modelBuilder.Entity<Channel>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            modelBuilder.Entity<Emote>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            modelBuilder.Entity<Guild>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            modelBuilder.Entity<GuildMember>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            modelBuilder.Entity<Message>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            modelBuilder.Entity<MessageContentEdit>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            modelBuilder.Entity<PresenceStatus>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            modelBuilder.Entity<PresenceStatusDetails>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            modelBuilder.Entity<Reaction>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            modelBuilder.Entity<TypingStatus>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            modelBuilder.Entity<User>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            modelBuilder.Entity<VoiceStatus>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            modelBuilder.Entity<VoiceStatusDetails>().HasQueryFilter(p => !p.DeletedAt.HasValue);
            
            #endregion
            
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

        public DbSet<Activity> Activities { get; set; }
        public DbSet<Channel> Channels { get; set; }
        public DbSet<Emote> Emotes { get; set; }
        public DbSet<Guild> Guilds { get; set; }
        public DbSet<GuildMember> GuildMembers { get; set; }
        public DbSet<Message> Messages { get; set; }
        public DbSet<MessageContentEdit> MessageContentEdits { get; set; }
        public DbSet<PresenceStatus> PresenceStatuses { get; set; }
        public DbSet<PresenceStatusDetails> PresenceStatusDetails { get; set; }
        public DbSet<Reaction> Reactions { get; set; }
        public DbSet<TypingStatus> TypingStatuses { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<VoiceStatus> VoiceStatuses { get; set; }
        public DbSet<VoiceStatusDetails> VoiceStatusDetails { get; set; }

        #endregion
}