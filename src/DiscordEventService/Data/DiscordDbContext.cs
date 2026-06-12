using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DiscordEventService.Data;

public class DiscordDbContext(DbContextOptions<DiscordDbContext> options) : DbContext(options)
{
    public DbSet<GuildEntity> Guilds => Set<GuildEntity>();
    public DbSet<ChannelEntity> Channels => Set<ChannelEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<MemberEntity> Members => Set<MemberEntity>();
    public DbSet<RoleEntity> Roles => Set<RoleEntity>();
    public DbSet<EmoteEntity> Emotes => Set<EmoteEntity>();
    public DbSet<StickerEntity> Stickers => Set<StickerEntity>();
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();
    public DbSet<MessageEditHistoryEntity> MessageEditHistory => Set<MessageEditHistoryEntity>();
    public DbSet<MessageMentionEntity> MessageMentions => Set<MessageMentionEntity>();
    public DbSet<InviteEntity> Invites => Set<InviteEntity>();
    public DbSet<BanEntity> Bans => Set<BanEntity>();
    public DbSet<GuildScheduledEventEntity> GuildScheduledEvents => Set<GuildScheduledEventEntity>();
    public DbSet<StageInstanceEntity> StageInstances => Set<StageInstanceEntity>();
    public DbSet<WebhookEntity> Webhooks => Set<WebhookEntity>();
    public DbSet<IntegrationEntity> Integrations => Set<IntegrationEntity>();
    public DbSet<AutoModRuleEntity> AutoModRules => Set<AutoModRuleEntity>();
    public DbSet<ActivityEntity> Activities => Set<ActivityEntity>();

    // Member role snapshots (SCD for historical role membership)
    public DbSet<MemberRoleSnapshotEntity> MemberRoleSnapshots => Set<MemberRoleSnapshotEntity>();

    public DbSet<UserNameHistoryEntity> UserNameHistory => Set<UserNameHistoryEntity>();

    public DbSet<MessageEventEntity> MessageEvents => Set<MessageEventEntity>();
    public DbSet<ReactionEventEntity> ReactionEvents => Set<ReactionEventEntity>();
    public DbSet<PollEventEntity> PollEvents => Set<PollEventEntity>();
    public DbSet<PinEventEntity> PinEvents => Set<PinEventEntity>();

    public DbSet<VoiceStateEventEntity> VoiceStateEvents => Set<VoiceStateEventEntity>();
    public DbSet<VoiceServerEventEntity> VoiceServerEvents => Set<VoiceServerEventEntity>();
    public DbSet<PresenceEventEntity> PresenceEvents => Set<PresenceEventEntity>();

    public DbSet<MemberEventEntity> MemberEvents => Set<MemberEventEntity>();
    public DbSet<BanEventEntity> BanEvents => Set<BanEventEntity>();
    public DbSet<GuildMembersChunkEventEntity> GuildMembersChunkEvents => Set<GuildMembersChunkEventEntity>();

    public DbSet<ChannelEventEntity> ChannelEvents => Set<ChannelEventEntity>();
    public DbSet<RoleEventEntity> RoleEvents => Set<RoleEventEntity>();
    public DbSet<ThreadEventEntity> ThreadEvents => Set<ThreadEventEntity>();
    public DbSet<ThreadSyncEventEntity> ThreadSyncEvents => Set<ThreadSyncEventEntity>();
    public DbSet<StageInstanceEventEntity> StageInstanceEvents => Set<StageInstanceEventEntity>();

    public DbSet<GuildEventEntity> GuildEvents => Set<GuildEventEntity>();
    public DbSet<EmojiEventEntity> EmojiEvents => Set<EmojiEventEntity>();
    public DbSet<StickerEventEntity> StickerEvents => Set<StickerEventEntity>();
    public DbSet<WebhookEventEntity> WebhookEvents => Set<WebhookEventEntity>();
    public DbSet<IntegrationEventEntity> IntegrationEvents => Set<IntegrationEventEntity>();
    public DbSet<AuditLogEventEntity> AuditLogEvents => Set<AuditLogEventEntity>();

    public DbSet<ScheduledEventEntity> ScheduledEvents => Set<ScheduledEventEntity>();
    public DbSet<AutoModEventEntity> AutoModEvents => Set<AutoModEventEntity>();
    public DbSet<AutoModRuleEventEntity> AutoModRuleEvents => Set<AutoModRuleEventEntity>();
    public DbSet<InviteEventEntity> InviteEvents => Set<InviteEventEntity>();
    public DbSet<TypingEventEntity> TypingEvents => Set<TypingEventEntity>();

    public DbSet<RawEventLogEntity> RawEventLogs => Set<RawEventLogEntity>();

    public DbSet<FailedEventEntity> FailedEvents => Set<FailedEventEntity>();

    public DbSet<BackfillCheckpointEntity> BackfillCheckpoints => Set<BackfillCheckpointEntity>();

    // Bot downtime intervals
    public DbSet<BotDowntimeIntervalEntity> BotDowntimeIntervals => Set<BotDowntimeIntervalEntity>();

    // Bot heartbeats (single-row liveness signal for power-loss detection)
    public DbSet<BotHeartbeatEntity> BotHeartbeats => Set<BotHeartbeatEntity>();

    // Indexed memes (#218): vision metadata per meme-channel image attachment
    public DbSet<MemeIndexEntity> MemeIndex => Set<MemeIndexEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Meme search (#220, ADR-0005): unaccent feeds f_unaccent inside the
        // meme_index generated columns; pg_trgm backs the trigram GIN index.
        modelBuilder.HasPostgresExtension("unaccent");
        modelBuilder.HasPostgresExtension("pg_trgm");

        var snowflakeConverter = new ValueConverter<ulong, long>(
            v => unchecked((long)v),
            v => unchecked((ulong)v));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(ulong))
                    property.SetValueConverter(snowflakeConverter);
                else if (property.ClrType == typeof(ulong?))
                {
                    property.SetValueConverter(new ValueConverter<ulong?, long?>(
                        v => v.HasValue ? unchecked((long)v.Value) : null,
                        v => v.HasValue ? unchecked((ulong)v.Value) : null));
                }
            }
        }

        // Let PostgreSQL generate UUID v7 for all Guid Id columns.
        modelBuilder.ConfigureUuidGeneration();

        // Per-entity indexes, FKs, JSONB columns, and CHECK constraints live in
        // Data/Configurations/*EntityConfiguration.cs (one IEntityTypeConfiguration<T>
        // per entity). Applied after the global converters so they compose on top.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DiscordDbContext).Assembly);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateTimestamps()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<ITimestamped>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.FirstSeenUtc = now;
                entry.Entity.LastUpdatedUtc = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.LastUpdatedUtc = now;
            }
        }
    }
}

public interface ITimestamped
{
    DateTime FirstSeenUtc { get; set; }
    DateTime LastUpdatedUtc { get; set; }
}

public static class UuidV7Extensions
{
    public static void ConfigureUuidGeneration(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var idProperty = entityType.FindProperty("Id");
            if (idProperty != null && idProperty.ClrType == typeof(Guid))
                idProperty.SetDefaultValueSql("uuidv7()");
        }
    }
}
