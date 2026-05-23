using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DiscordEventService.Data;

public class DiscordDbContext(DbContextOptions<DiscordDbContext> options) : DbContext(options)
{
    // Core entities
    public DbSet<GuildEntity> Guilds => Set<GuildEntity>();
    public DbSet<ChannelEntity> Channels => Set<ChannelEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<MemberEntity> Members => Set<MemberEntity>();
    public DbSet<RoleEntity> Roles => Set<RoleEntity>();
    public DbSet<EmoteEntity> Emotes => Set<EmoteEntity>();
    public DbSet<StickerEntity> Stickers => Set<StickerEntity>();
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();
    public DbSet<MessageEditHistoryEntity> MessageEditHistory => Set<MessageEditHistoryEntity>();
    public DbSet<InviteEntity> Invites => Set<InviteEntity>();
    public DbSet<BanEntity> Bans => Set<BanEntity>();
    public DbSet<GuildScheduledEventEntity> GuildScheduledEvents => Set<GuildScheduledEventEntity>();
    public DbSet<StageInstanceEntity> StageInstances => Set<StageInstanceEntity>();
    public DbSet<WebhookEntity> Webhooks => Set<WebhookEntity>();
    public DbSet<IntegrationEntity> Integrations => Set<IntegrationEntity>();
    public DbSet<AutoModRuleEntity> AutoModRules => Set<AutoModRuleEntity>();
    public DbSet<ActivityEntity> Activities => Set<ActivityEntity>();

    // Event entities - Messages & Reactions
    public DbSet<MessageEventEntity> MessageEvents => Set<MessageEventEntity>();
    public DbSet<ReactionEventEntity> ReactionEvents => Set<ReactionEventEntity>();
    public DbSet<PollEventEntity> PollEvents => Set<PollEventEntity>();
    public DbSet<PinEventEntity> PinEvents => Set<PinEventEntity>();

    // Event entities - Voice & Presence
    public DbSet<VoiceStateEventEntity> VoiceStateEvents => Set<VoiceStateEventEntity>();
    public DbSet<VoiceServerEventEntity> VoiceServerEvents => Set<VoiceServerEventEntity>();
    public DbSet<PresenceEventEntity> PresenceEvents => Set<PresenceEventEntity>();

    // Event entities - Members
    public DbSet<MemberEventEntity> MemberEvents => Set<MemberEventEntity>();
    public DbSet<BanEventEntity> BanEvents => Set<BanEventEntity>();
    public DbSet<GuildMembersChunkEventEntity> GuildMembersChunkEvents => Set<GuildMembersChunkEventEntity>();

    // Event entities - Channels & Threads
    public DbSet<ChannelEventEntity> ChannelEvents => Set<ChannelEventEntity>();
    public DbSet<RoleEventEntity> RoleEvents => Set<RoleEventEntity>();
    public DbSet<ThreadEventEntity> ThreadEvents => Set<ThreadEventEntity>();
    public DbSet<ThreadSyncEventEntity> ThreadSyncEvents => Set<ThreadSyncEventEntity>();
    public DbSet<StageInstanceEventEntity> StageInstanceEvents => Set<StageInstanceEventEntity>();

    // Event entities - Guild
    public DbSet<GuildEventEntity> GuildEvents => Set<GuildEventEntity>();
    public DbSet<EmojiEventEntity> EmojiEvents => Set<EmojiEventEntity>();
    public DbSet<StickerEventEntity> StickerEvents => Set<StickerEventEntity>();
    public DbSet<WebhookEventEntity> WebhookEvents => Set<WebhookEventEntity>();
    public DbSet<IntegrationEventEntity> IntegrationEvents => Set<IntegrationEventEntity>();
    public DbSet<AuditLogEventEntity> AuditLogEvents => Set<AuditLogEventEntity>();

    // Event entities - Scheduled & AutoMod
    public DbSet<ScheduledEventEntity> ScheduledEvents => Set<ScheduledEventEntity>();
    public DbSet<AutoModEventEntity> AutoModEvents => Set<AutoModEventEntity>();
    public DbSet<AutoModRuleEventEntity> AutoModRuleEvents => Set<AutoModRuleEventEntity>();
    public DbSet<InviteEventEntity> InviteEvents => Set<InviteEventEntity>();
    public DbSet<TypingEventEntity> TypingEvents => Set<TypingEventEntity>();

    // Raw event logging for debugging
    public DbSet<RawEventLogEntity> RawEventLogs => Set<RawEventLogEntity>();

    // Dead-letter queue for failed events
    public DbSet<FailedEventEntity> FailedEvents => Set<FailedEventEntity>();

    // Backfill checkpoints
    public DbSet<BackfillCheckpointEntity> BackfillCheckpoints => Set<BackfillCheckpointEntity>();

    // Bot downtime intervals
    public DbSet<BotDowntimeIntervalEntity> BotDowntimeIntervals => Set<BotDowntimeIntervalEntity>();

    // Bot heartbeats (single-row liveness signal for power-loss detection)
    public DbSet<BotHeartbeatEntity> BotHeartbeats => Set<BotHeartbeatEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply snowflake converter to all ulong properties
        var snowflakeConverter = new ValueConverter<ulong, long>(
            v => unchecked((long)v),
            v => unchecked((ulong)v));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(ulong))
                {
                    property.SetValueConverter(snowflakeConverter);
                }
                else if (property.ClrType == typeof(ulong?))
                {
                    property.SetValueConverter(new ValueConverter<ulong?, long?>(
                        v => v.HasValue ? unchecked((long)v.Value) : null,
                        v => v.HasValue ? unchecked((ulong)v.Value) : null));
                }
            }
        }

        // =====================================================
        // UUID v7 generation - let PostgreSQL generate UUIDs
        // =====================================================
        modelBuilder.ConfigureUuidGeneration();

        // =====================================================
        // DiscordId indexes for lookups
        // =====================================================
        modelBuilder.Entity<GuildEntity>()
            .HasIndex(g => g.DiscordId).IsUnique();
        modelBuilder.Entity<UserEntity>()
            .HasIndex(u => u.DiscordId).IsUnique();
        modelBuilder.Entity<ChannelEntity>()
            .HasIndex(c => c.DiscordId).IsUnique();
        modelBuilder.Entity<MessageEntity>()
            .HasIndex(m => m.DiscordId).IsUnique();
        modelBuilder.Entity<RoleEntity>()
            .HasIndex(r => r.DiscordId).IsUnique();
        modelBuilder.Entity<EmoteEntity>()
            .HasIndex(e => e.DiscordId).IsUnique();
        modelBuilder.Entity<StickerEntity>()
            .HasIndex(s => s.DiscordId).IsUnique();
        modelBuilder.Entity<GuildScheduledEventEntity>()
            .HasIndex(e => e.DiscordId).IsUnique();
        modelBuilder.Entity<StageInstanceEntity>()
            .HasIndex(s => s.DiscordId).IsUnique();
        modelBuilder.Entity<WebhookEntity>()
            .HasIndex(w => w.DiscordId).IsUnique();
        modelBuilder.Entity<IntegrationEntity>()
            .HasIndex(i => i.DiscordId).IsUnique();
        modelBuilder.Entity<AutoModRuleEntity>()
            .HasIndex(a => a.DiscordId).IsUnique();

        // Member unique constraint (user + guild combination)
        modelBuilder.Entity<MemberEntity>()
            .HasIndex(m => new { m.UserId, m.GuildId }).IsUnique();

        // Invite Code index
        modelBuilder.Entity<InviteEntity>()
            .HasIndex(i => i.Code).IsUnique();

        // Indexes for common queries
        modelBuilder.Entity<ChannelEntity>()
            .HasIndex(c => c.GuildId);

        modelBuilder.Entity<MemberEntity>()
            .HasIndex(m => m.GuildId);

        modelBuilder.Entity<RoleEntity>()
            .HasIndex(r => r.GuildId);

        modelBuilder.Entity<EmoteEntity>()
            .HasIndex(e => e.GuildId);

        // Message indexes
        modelBuilder.Entity<MessageEntity>()
            .HasIndex(m => new { m.GuildId, m.CreatedAtUtc });

        modelBuilder.Entity<MessageEntity>()
            .HasIndex(m => m.ChannelId);

        modelBuilder.Entity<MessageEntity>()
            .HasIndex(m => m.AuthorId);

        modelBuilder.Entity<MessageEntity>()
            .HasIndex(m => new { m.ChannelId, m.IsDeleted });

        modelBuilder.Entity<MessageEntity>()
            .Property(m => m.AttachmentsJson)
            .HasColumnType("jsonb");

        modelBuilder.Entity<MessageEntity>()
            .Property(m => m.EmbedsJson)
            .HasColumnType("jsonb");

        // §P2.6 / #74: messages.guild_id/channel_id/author_id are NOT NULL FKs,
        // but we never hard-delete guilds/channels/users — they soft-delete with
        // is_deleted+deleted_at_utc. Restrict (vs the EF default of Cascade)
        // protects message history if a delete ever does happen (would surface
        // as a constraint violation rather than silently nuking messages).
        modelBuilder.Entity<MessageEntity>()
            .HasOne(m => m.Guild)
            .WithMany()
            .HasForeignKey(m => m.GuildId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MessageEntity>()
            .HasOne(m => m.Channel)
            .WithMany()
            .HasForeignKey(m => m.ChannelId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MessageEntity>()
            .HasOne(m => m.Author)
            .WithMany()
            .HasForeignKey(m => m.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);

        // Message event indexes
        modelBuilder.Entity<MessageEventEntity>()
            .HasIndex(m => new { m.GuildDiscordId, m.EventTimestampUtc });

        modelBuilder.Entity<MessageEventEntity>()
            .HasIndex(m => m.ChannelDiscordId);

        modelBuilder.Entity<MessageEventEntity>()
            .HasIndex(m => m.AuthorDiscordId);

        modelBuilder.Entity<MessageEventEntity>()
            .HasIndex(m => m.MessageDiscordId);

        // Configure JSONB columns for PostgreSQL
        modelBuilder.Entity<MessageEventEntity>()
            .Property(m => m.AttachmentsJson)
            .HasColumnType("jsonb");

        modelBuilder.Entity<MessageEventEntity>()
            .Property(m => m.EmbedsJson)
            .HasColumnType("jsonb");

        // Reaction event indexes
        modelBuilder.Entity<ReactionEventEntity>()
            .HasIndex(r => new { r.GuildDiscordId, r.EventTimestampUtc });

        modelBuilder.Entity<ReactionEventEntity>()
            .HasIndex(r => r.MessageDiscordId);

        // Voice state event indexes
        modelBuilder.Entity<VoiceStateEventEntity>()
            .HasIndex(v => new { v.GuildDiscordId, v.EventTimestampUtc });

        modelBuilder.Entity<VoiceStateEventEntity>()
            .HasIndex(v => v.UserDiscordId);

        // Presence event indexes
        modelBuilder.Entity<PresenceEventEntity>()
            .HasIndex(p => new { p.GuildDiscordId, p.EventTimestampUtc });

        modelBuilder.Entity<PresenceEventEntity>()
            .HasIndex(p => p.UserDiscordId);

        // Configure JSONB columns for PostgreSQL (Presence events)
        modelBuilder.Entity<PresenceEventEntity>()
            .Property(p => p.ActivitiesBeforeJson)
            .HasColumnType("jsonb");

        modelBuilder.Entity<PresenceEventEntity>()
            .Property(p => p.ActivitiesAfterJson)
            .HasColumnType("jsonb");

        // Member event indexes
        modelBuilder.Entity<MemberEventEntity>()
            .HasIndex(m => new { m.GuildDiscordId, m.EventTimestampUtc });

        modelBuilder.Entity<MemberEventEntity>()
            .HasIndex(m => m.UserDiscordId);

        // Configure JSONB columns for PostgreSQL (Member events)
        modelBuilder.Entity<MemberEventEntity>()
            .Property(m => m.RolesAddedJson)
            .HasColumnType("jsonb");

        modelBuilder.Entity<MemberEventEntity>()
            .Property(m => m.RolesRemovedJson)
            .HasColumnType("jsonb");

        // Channel event indexes
        modelBuilder.Entity<ChannelEventEntity>()
            .HasIndex(c => new { c.GuildDiscordId, c.EventTimestampUtc });

        modelBuilder.Entity<ChannelEventEntity>()
            .HasIndex(c => c.ChannelDiscordId);

        // Role event indexes
        modelBuilder.Entity<RoleEventEntity>()
            .HasIndex(r => new { r.GuildDiscordId, r.EventTimestampUtc });

        // Thread event indexes
        modelBuilder.Entity<ThreadEventEntity>()
            .HasIndex(t => new { t.GuildDiscordId, t.EventTimestampUtc });

        modelBuilder.Entity<ThreadEventEntity>()
            .HasIndex(t => t.ParentChannelDiscordId);

        // Configure JSONB columns for PostgreSQL (Thread events)
        modelBuilder.Entity<ThreadEventEntity>()
            .Property(t => t.MembersAddedJson)
            .HasColumnType("jsonb");

        modelBuilder.Entity<ThreadEventEntity>()
            .Property(t => t.MembersRemovedJson)
            .HasColumnType("jsonb");

        // Scheduled event indexes
        modelBuilder.Entity<ScheduledEventEntity>()
            .HasIndex(s => new { s.GuildDiscordId, s.EventTimestampUtc });

        modelBuilder.Entity<ScheduledEventEntity>()
            .HasIndex(s => s.EventDiscordId);

        // AutoMod event indexes
        modelBuilder.Entity<AutoModEventEntity>()
            .HasIndex(a => new { a.GuildDiscordId, a.EventTimestampUtc });

        modelBuilder.Entity<AutoModEventEntity>()
            .HasIndex(a => a.RuleDiscordId);

        // Invite event indexes
        modelBuilder.Entity<InviteEventEntity>()
            .HasIndex(i => new { i.GuildDiscordId, i.EventTimestampUtc });

        // Typing event indexes
        modelBuilder.Entity<TypingEventEntity>()
            .HasIndex(t => new { t.GuildDiscordId, t.ReceivedAtUtc });

        modelBuilder.Entity<TypingEventEntity>()
            .HasIndex(t => t.UserDiscordId);

        // Invite indexes
        modelBuilder.Entity<InviteEntity>()
            .HasIndex(i => i.GuildId);

        modelBuilder.Entity<InviteEntity>()
            .HasIndex(i => new { i.GuildId, i.IsDeleted });

        // GuildScheduledEvent indexes
        modelBuilder.Entity<GuildScheduledEventEntity>()
            .HasIndex(e => e.GuildId);

        modelBuilder.Entity<GuildScheduledEventEntity>()
            .HasIndex(e => new { e.GuildId, e.IsDeleted });

        // Sticker indexes
        modelBuilder.Entity<StickerEntity>()
            .HasIndex(s => s.GuildId);

        // Ban indexes
        modelBuilder.Entity<BanEntity>()
            .HasIndex(b => b.GuildId);

        modelBuilder.Entity<BanEntity>()
            .HasIndex(b => new { b.GuildId, b.UserId });

        modelBuilder.Entity<BanEntity>()
            .HasIndex(b => new { b.GuildId, b.IsActive });

        // StageInstance indexes
        modelBuilder.Entity<StageInstanceEntity>()
            .HasIndex(s => s.GuildId);

        modelBuilder.Entity<StageInstanceEntity>()
            .HasIndex(s => s.ChannelId);

        // Webhook indexes
        modelBuilder.Entity<WebhookEntity>()
            .HasIndex(w => w.GuildId);

        modelBuilder.Entity<WebhookEntity>()
            .HasIndex(w => w.ChannelId);

        // Integration indexes
        modelBuilder.Entity<IntegrationEntity>()
            .HasIndex(i => i.GuildId);

        // AutoModRule indexes and JSONB
        modelBuilder.Entity<AutoModRuleEntity>()
            .HasIndex(a => a.GuildId);

        modelBuilder.Entity<AutoModRuleEntity>()
            .Property(a => a.TriggerMetadataJson)
            .HasColumnType("jsonb");

        modelBuilder.Entity<AutoModRuleEntity>()
            .Property(a => a.ActionsJson)
            .HasColumnType("jsonb");

        modelBuilder.Entity<AutoModRuleEntity>()
            .Property(a => a.ExemptRolesJson)
            .HasColumnType("jsonb");

        modelBuilder.Entity<AutoModRuleEntity>()
            .Property(a => a.ExemptChannelsJson)
            .HasColumnType("jsonb");

        // Guild event indexes
        modelBuilder.Entity<GuildEventEntity>()
            .HasIndex(g => new { g.GuildDiscordId, g.EventTimestampUtc });

        // Ban event indexes
        modelBuilder.Entity<BanEventEntity>()
            .HasIndex(b => new { b.GuildDiscordId, b.EventTimestampUtc });

        modelBuilder.Entity<BanEventEntity>()
            .HasIndex(b => b.UserDiscordId);

        // Emoji event indexes and JSONB
        modelBuilder.Entity<EmojiEventEntity>()
            .HasIndex(e => new { e.GuildDiscordId, e.EventTimestampUtc });

        modelBuilder.Entity<EmojiEventEntity>()
            .Property(e => e.EmojisAddedJson)
            .HasColumnType("jsonb");

        modelBuilder.Entity<EmojiEventEntity>()
            .Property(e => e.EmojisRemovedJson)
            .HasColumnType("jsonb");

        modelBuilder.Entity<EmojiEventEntity>()
            .Property(e => e.EmojisUpdatedJson)
            .HasColumnType("jsonb");

        // Sticker event indexes and JSONB
        modelBuilder.Entity<StickerEventEntity>()
            .HasIndex(s => new { s.GuildDiscordId, s.EventTimestampUtc });

        modelBuilder.Entity<StickerEventEntity>()
            .Property(s => s.StickersAddedJson)
            .HasColumnType("jsonb");

        modelBuilder.Entity<StickerEventEntity>()
            .Property(s => s.StickersRemovedJson)
            .HasColumnType("jsonb");

        modelBuilder.Entity<StickerEventEntity>()
            .Property(s => s.StickersUpdatedJson)
            .HasColumnType("jsonb");

        // StageInstance event indexes
        modelBuilder.Entity<StageInstanceEventEntity>()
            .HasIndex(s => new { s.GuildDiscordId, s.EventTimestampUtc });

        // Poll event indexes
        modelBuilder.Entity<PollEventEntity>()
            .HasIndex(p => new { p.GuildDiscordId, p.EventTimestampUtc });

        modelBuilder.Entity<PollEventEntity>()
            .HasIndex(p => p.MessageDiscordId);

        // Pin event indexes
        modelBuilder.Entity<PinEventEntity>()
            .HasIndex(p => new { p.GuildDiscordId, p.EventTimestampUtc });

        modelBuilder.Entity<PinEventEntity>()
            .HasIndex(p => p.ChannelDiscordId);

        // Webhook event indexes
        modelBuilder.Entity<WebhookEventEntity>()
            .HasIndex(w => new { w.GuildDiscordId, w.EventTimestampUtc });

        // Integration event indexes
        modelBuilder.Entity<IntegrationEventEntity>()
            .HasIndex(i => new { i.GuildDiscordId, i.EventTimestampUtc });

        // AutoModRule event indexes and JSONB
        modelBuilder.Entity<AutoModRuleEventEntity>()
            .HasIndex(a => new { a.GuildDiscordId, a.EventTimestampUtc });

        modelBuilder.Entity<AutoModRuleEventEntity>()
            .Property(a => a.ActionsJson)
            .HasColumnType("jsonb");

        // AuditLog event indexes and JSONB
        modelBuilder.Entity<AuditLogEventEntity>()
            .HasIndex(a => new { a.GuildDiscordId, a.EventTimestampUtc });

        modelBuilder.Entity<AuditLogEventEntity>()
            .HasIndex(a => a.UserDiscordId);

        modelBuilder.Entity<AuditLogEventEntity>()
            .HasIndex(a => a.ActionType);

        modelBuilder.Entity<AuditLogEventEntity>()
            .Property(a => a.ChangesJson)
            .HasColumnType("jsonb");

        modelBuilder.Entity<AuditLogEventEntity>()
            .Property(a => a.OptionsJson)
            .HasColumnType("jsonb");

        // MessageEditHistory indexes
        modelBuilder.Entity<MessageEditHistoryEntity>()
            .HasIndex(m => m.MessageDiscordId);

        modelBuilder.Entity<MessageEditHistoryEntity>()
            .HasIndex(m => m.MessageId);

        modelBuilder.Entity<MessageEditHistoryEntity>()
            .HasIndex(m => m.EditedAtUtc);

        // MessageEditHistory soft relation
        modelBuilder.Entity<MessageEditHistoryEntity>()
            .HasOne(e => e.Message).WithMany(m => m.EditHistory)
            .HasForeignKey(e => e.MessageId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        // Match the jsonb column type used on messages.attachments_json /
        // messages.embeds_json so the history columns are queryable with the
        // same operators (and no ::jsonb casts needed).
        modelBuilder.Entity<MessageEditHistoryEntity>()
            .Property(m => m.AttachmentsBeforeJson).HasColumnType("jsonb");
        modelBuilder.Entity<MessageEditHistoryEntity>()
            .Property(m => m.AttachmentsAfterJson).HasColumnType("jsonb");
        modelBuilder.Entity<MessageEditHistoryEntity>()
            .Property(m => m.EmbedsBeforeJson).HasColumnType("jsonb");
        modelBuilder.Entity<MessageEditHistoryEntity>()
            .Property(m => m.EmbedsAfterJson).HasColumnType("jsonb");

        // Activity indexes
        modelBuilder.Entity<ActivityEntity>()
            .HasIndex(a => a.UserId);

        modelBuilder.Entity<ActivityEntity>()
            .HasIndex(a => new { a.UserId, a.IsActive });

        modelBuilder.Entity<ActivityEntity>()
            .HasIndex(a => a.ActivityType);

        modelBuilder.Entity<ActivityEntity>()
            .HasIndex(a => a.FirstSeenAtUtc);

        modelBuilder.Entity<ActivityEntity>()
            .Property(a => a.SpotifyArtistsJson)
            .HasColumnType("jsonb");

        modelBuilder.Entity<ActivityEntity>()
            .Property(a => a.ButtonsJson)
            .HasColumnType("jsonb");

        // Activity soft relations
        modelBuilder.Entity<ActivityEntity>()
            .HasOne(a => a.User).WithMany(u => u.Activities)
            .HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ActivityEntity>()
            .HasOne(a => a.Guild).WithMany(g => g.Activities)
            .HasForeignKey(a => a.GuildId).OnDelete(DeleteBehavior.NoAction);

        // VoiceServerEvent indexes
        modelBuilder.Entity<VoiceServerEventEntity>()
            .HasIndex(v => new { v.GuildDiscordId, v.EventTimestampUtc });

        // GuildMembersChunkEvent indexes and JSONB
        modelBuilder.Entity<GuildMembersChunkEventEntity>()
            .HasIndex(m => new { m.GuildDiscordId, m.EventTimestampUtc });

        modelBuilder.Entity<GuildMembersChunkEventEntity>()
            .Property(m => m.MemberIdsJson)
            .HasColumnType("jsonb");

        modelBuilder.Entity<GuildMembersChunkEventEntity>()
            .Property(m => m.PresencesJson)
            .HasColumnType("jsonb");

        modelBuilder.Entity<GuildMembersChunkEventEntity>()
            .Property(m => m.NotFoundIdsJson)
            .HasColumnType("jsonb");

        // ThreadSyncEvent indexes and JSONB
        modelBuilder.Entity<ThreadSyncEventEntity>()
            .HasIndex(t => new { t.GuildDiscordId, t.EventTimestampUtc });

        modelBuilder.Entity<ThreadSyncEventEntity>()
            .Property(t => t.ThreadIdsJson)
            .HasColumnType("jsonb");

        modelBuilder.Entity<ThreadSyncEventEntity>()
            .Property(t => t.ChannelIdsJson)
            .HasColumnType("jsonb");

        modelBuilder.Entity<ThreadSyncEventEntity>()
            .Property(t => t.MembersJson)
            .HasColumnType("jsonb");

        // =====================================================
        // RawEventLog configuration (dedicated event JSON storage)
        // =====================================================
        modelBuilder.Entity<RawEventLogEntity>()
            .HasIndex(r => new { r.GuildDiscordId, r.ReceivedAtUtc });

        modelBuilder.Entity<RawEventLogEntity>()
            .HasIndex(r => r.EventType);

        modelBuilder.Entity<RawEventLogEntity>()
            .HasIndex(r => r.ReceivedAtUtc);

        modelBuilder.Entity<RawEventLogEntity>()
            .HasIndex(r => r.UserDiscordId);

        modelBuilder.Entity<RawEventLogEntity>()
            .Property(r => r.EventJson)
            .HasColumnType("jsonb");

        // =====================================================
        // FailedEventEntity configuration (dead-letter queue)
        // =====================================================
        modelBuilder.Entity<FailedEventEntity>()
            .HasIndex(f => f.IsResolved);

        modelBuilder.Entity<FailedEventEntity>()
            .HasIndex(f => f.FailedAtUtc);

        modelBuilder.Entity<FailedEventEntity>()
            .HasIndex(f => new { f.IsResolved, f.FailedAtUtc });

        modelBuilder.Entity<FailedEventEntity>()
            .HasIndex(f => f.EventType);

        modelBuilder.Entity<FailedEventEntity>()
            .HasIndex(f => f.GuildDiscordId);

        // =====================================================
        // BackfillCheckpoint configuration
        // =====================================================
        modelBuilder.Entity<BackfillCheckpointEntity>()
            .HasIndex(b => new { b.GuildDiscordId, b.Type }).IsUnique();

        modelBuilder.Entity<BackfillCheckpointEntity>()
            .HasIndex(b => b.Status);

        modelBuilder.Entity<BackfillCheckpointEntity>()
            .HasIndex(b => b.HangfireJobId);

        // =====================================================
        // BotDowntimeInterval configuration
        // =====================================================
        modelBuilder.Entity<BotDowntimeIntervalEntity>(b =>
        {
            b.HasIndex(x => new { x.StartedAtUtc, x.EndedAtUtc });
            b.HasIndex(x => x.EndedAtUtc)
                .HasFilter("ended_at_utc IS NULL");
        });

        // =====================================================
        // BotHeartbeat configuration
        // =====================================================
        // Index on LastHeartbeatUtc — append-only table, "most recent tick"
        // lookups via OrderByDescending(LastHeartbeatUtc).First() use this.
        modelBuilder.Entity<BotHeartbeatEntity>()
            .HasIndex(h => h.LastHeartbeatUtc);

        // =====================================================
        // Soft-delete CHECK constraints (§P2.5)
        // is_deleted ↔ deleted_at_utc IS NOT NULL
        // =====================================================
        const string softDeleteCheck =
            "(is_deleted = false AND deleted_at_utc IS NULL) OR " +
            "(is_deleted = true AND deleted_at_utc IS NOT NULL)";

        modelBuilder.Entity<MessageEntity>()
            .ToTable(t => t.HasCheckConstraint("ck_messages_soft_delete", softDeleteCheck));
        modelBuilder.Entity<ChannelEntity>()
            .ToTable(t => t.HasCheckConstraint("ck_channels_soft_delete", softDeleteCheck));
        modelBuilder.Entity<RoleEntity>()
            .ToTable(t => t.HasCheckConstraint("ck_roles_soft_delete", softDeleteCheck));
        modelBuilder.Entity<WebhookEntity>()
            .ToTable(t => t.HasCheckConstraint("ck_webhooks_soft_delete", softDeleteCheck));
        modelBuilder.Entity<EmoteEntity>()
            .ToTable(t => t.HasCheckConstraint("ck_emotes_soft_delete", softDeleteCheck));
        modelBuilder.Entity<StickerEntity>()
            .ToTable(t => t.HasCheckConstraint("ck_stickers_soft_delete", softDeleteCheck));
        modelBuilder.Entity<IntegrationEntity>()
            .ToTable(t => t.HasCheckConstraint("ck_integrations_soft_delete", softDeleteCheck));
        modelBuilder.Entity<AutoModRuleEntity>()
            .ToTable(t => t.HasCheckConstraint("ck_auto_mod_rules_soft_delete", softDeleteCheck));
        modelBuilder.Entity<GuildScheduledEventEntity>()
            .ToTable(t => t.HasCheckConstraint("ck_guild_scheduled_events_soft_delete", softDeleteCheck));
        modelBuilder.Entity<StageInstanceEntity>()
            .ToTable(t => t.HasCheckConstraint("ck_stage_instances_soft_delete", softDeleteCheck));
        modelBuilder.Entity<InviteEntity>()
            .ToTable(t => t.HasCheckConstraint("ck_invites_soft_delete", softDeleteCheck));
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
        // Configure all Guid Id properties to use PostgreSQL's uuidv7() for generation
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var idProperty = entityType.FindProperty("Id");
            if (idProperty != null && idProperty.ClrType == typeof(Guid))
            {
                idProperty.SetDefaultValueSql("uuidv7()");
            }
        }
    }
}
