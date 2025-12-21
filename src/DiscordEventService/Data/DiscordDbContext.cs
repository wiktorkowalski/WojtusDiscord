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
    public DbSet<VoiceStateEntity> VoiceStates => Set<VoiceStateEntity>();
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

        // VoiceState unique constraint (user + guild combination)
        modelBuilder.Entity<VoiceStateEntity>()
            .HasIndex(v => new { v.UserId, v.GuildId }).IsUnique();

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

        // VoiceState indexes
        modelBuilder.Entity<VoiceStateEntity>()
            .HasIndex(v => v.GuildId);

        modelBuilder.Entity<VoiceStateEntity>()
            .HasIndex(v => v.ChannelId);

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
            .HasForeignKey(e => e.MessageId).OnDelete(DeleteBehavior.NoAction);

        // Activity indexes
        modelBuilder.Entity<ActivityEntity>()
            .HasIndex(a => a.UserDiscordId);

        modelBuilder.Entity<ActivityEntity>()
            .HasIndex(a => new { a.UserDiscordId, a.IsActive });

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

        // VoiceServerEvent soft relation
        modelBuilder.Entity<VoiceServerEventEntity>()
            .HasOne(e => e.Guild).WithMany(g => g.VoiceServerEvents)
            .HasForeignKey(e => e.GuildId).OnDelete(DeleteBehavior.NoAction);

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

        // GuildMembersChunkEvent soft relation
        modelBuilder.Entity<GuildMembersChunkEventEntity>()
            .HasOne(e => e.Guild).WithMany(g => g.GuildMembersChunkEvents)
            .HasForeignKey(e => e.GuildId).OnDelete(DeleteBehavior.NoAction);

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

        // ThreadSyncEvent soft relation
        modelBuilder.Entity<ThreadSyncEventEntity>()
            .HasOne(e => e.Guild).WithMany(g => g.ThreadSyncEvents)
            .HasForeignKey(e => e.GuildId).OnDelete(DeleteBehavior.NoAction);

        // =====================================================
        // SOFT RELATIONS - Navigation properties without FK constraints
        // These allow .Include() queries but don't create DB constraints
        // =====================================================

        // MessageEventEntity soft relations
        modelBuilder.Entity<MessageEventEntity>()
            .HasOne(e => e.Guild).WithMany(g => g.MessageEvents)
            .HasForeignKey(e => e.GuildId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<MessageEventEntity>()
            .HasOne(e => e.Channel).WithMany(c => c.MessageEvents)
            .HasForeignKey(e => e.ChannelId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<MessageEventEntity>()
            .HasOne(e => e.Author).WithMany(u => u.MessageEvents)
            .HasForeignKey(e => e.AuthorId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<MessageEventEntity>()
            .HasOne(e => e.Message).WithMany(m => m.MessageEvents)
            .HasForeignKey(e => e.MessageId).OnDelete(DeleteBehavior.NoAction);

        // ReactionEventEntity soft relations
        modelBuilder.Entity<ReactionEventEntity>()
            .HasOne(e => e.Guild).WithMany(g => g.ReactionEvents)
            .HasForeignKey(e => e.GuildId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<ReactionEventEntity>()
            .HasOne(e => e.Channel).WithMany(c => c.ReactionEvents)
            .HasForeignKey(e => e.ChannelId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<ReactionEventEntity>()
            .HasOne(e => e.User).WithMany(u => u.ReactionEvents)
            .HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<ReactionEventEntity>()
            .HasOne(e => e.Message).WithMany(m => m.ReactionEvents)
            .HasForeignKey(e => e.MessageId).OnDelete(DeleteBehavior.NoAction);

        // VoiceStateEventEntity soft relations
        modelBuilder.Entity<VoiceStateEventEntity>()
            .HasOne(e => e.Guild).WithMany(g => g.VoiceStateEvents)
            .HasForeignKey(e => e.GuildId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<VoiceStateEventEntity>()
            .HasOne(e => e.User).WithMany(u => u.VoiceStateEvents)
            .HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<VoiceStateEventEntity>()
            .HasOne(e => e.ChannelBefore).WithMany()
            .HasForeignKey(e => e.ChannelIdBefore).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<VoiceStateEventEntity>()
            .HasOne(e => e.ChannelAfter).WithMany()
            .HasForeignKey(e => e.ChannelIdAfter).OnDelete(DeleteBehavior.NoAction);

        // PresenceEventEntity soft relations
        modelBuilder.Entity<PresenceEventEntity>()
            .HasOne(e => e.Guild).WithMany(g => g.PresenceEvents)
            .HasForeignKey(e => e.GuildId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<PresenceEventEntity>()
            .HasOne(e => e.User).WithMany(u => u.PresenceEvents)
            .HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.NoAction);

        // MemberEventEntity soft relations
        modelBuilder.Entity<MemberEventEntity>()
            .HasOne(e => e.Guild).WithMany(g => g.MemberEvents)
            .HasForeignKey(e => e.GuildId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<MemberEventEntity>()
            .HasOne(e => e.User).WithMany(u => u.MemberEvents)
            .HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.NoAction);

        // BanEventEntity soft relations
        modelBuilder.Entity<BanEventEntity>()
            .HasOne(e => e.Guild).WithMany(g => g.BanEvents)
            .HasForeignKey(e => e.GuildId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<BanEventEntity>()
            .HasOne(e => e.User).WithMany(u => u.BanEvents)
            .HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.NoAction);

        // ChannelEventEntity soft relations
        modelBuilder.Entity<ChannelEventEntity>()
            .HasOne(e => e.Guild).WithMany(g => g.ChannelEvents)
            .HasForeignKey(e => e.GuildId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<ChannelEventEntity>()
            .HasOne(e => e.Channel).WithMany(c => c.ChannelEvents)
            .HasForeignKey(e => e.ChannelId).OnDelete(DeleteBehavior.NoAction);

        // RoleEventEntity soft relations
        modelBuilder.Entity<RoleEventEntity>()
            .HasOne(e => e.Guild).WithMany(g => g.RoleEvents)
            .HasForeignKey(e => e.GuildId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<RoleEventEntity>()
            .HasOne(e => e.Role).WithMany(r => r.RoleEvents)
            .HasForeignKey(e => e.RoleId).OnDelete(DeleteBehavior.NoAction);

        // ThreadEventEntity soft relations
        modelBuilder.Entity<ThreadEventEntity>()
            .HasOne(e => e.Guild).WithMany(g => g.ThreadEvents)
            .HasForeignKey(e => e.GuildId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<ThreadEventEntity>()
            .HasOne(e => e.Thread).WithMany()
            .HasForeignKey(e => e.ThreadId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<ThreadEventEntity>()
            .HasOne(e => e.ParentChannel).WithMany()
            .HasForeignKey(e => e.ParentChannelId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<ThreadEventEntity>()
            .HasOne(e => e.Owner).WithMany()
            .HasForeignKey(e => e.OwnerId).OnDelete(DeleteBehavior.NoAction);

        // GuildEventEntity soft relations
        modelBuilder.Entity<GuildEventEntity>()
            .HasOne(e => e.Guild).WithMany(g => g.GuildEvents)
            .HasForeignKey(e => e.GuildId).OnDelete(DeleteBehavior.NoAction);

        // EmojiEventEntity soft relations
        modelBuilder.Entity<EmojiEventEntity>()
            .HasOne(e => e.Guild).WithMany(g => g.EmojiEvents)
            .HasForeignKey(e => e.GuildId).OnDelete(DeleteBehavior.NoAction);

        // StickerEventEntity soft relations
        modelBuilder.Entity<StickerEventEntity>()
            .HasOne(e => e.Guild).WithMany(g => g.StickerEvents)
            .HasForeignKey(e => e.GuildId).OnDelete(DeleteBehavior.NoAction);

        // StageInstanceEventEntity soft relations
        modelBuilder.Entity<StageInstanceEventEntity>()
            .HasOne(e => e.Guild).WithMany(g => g.StageInstanceEvents)
            .HasForeignKey(e => e.GuildId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<StageInstanceEventEntity>()
            .HasOne(e => e.Channel).WithMany(c => c.StageInstanceEvents)
            .HasForeignKey(e => e.ChannelId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<StageInstanceEventEntity>()
            .HasOne(e => e.StageInstance).WithMany(s => s.StageInstanceEvents)
            .HasForeignKey(e => e.StageInstanceId).OnDelete(DeleteBehavior.NoAction);

        // PollEventEntity soft relations
        modelBuilder.Entity<PollEventEntity>()
            .HasOne(e => e.Guild).WithMany(g => g.PollEvents)
            .HasForeignKey(e => e.GuildId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<PollEventEntity>()
            .HasOne(e => e.Channel).WithMany(c => c.PollEvents)
            .HasForeignKey(e => e.ChannelId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<PollEventEntity>()
            .HasOne(e => e.User).WithMany(u => u.PollEvents)
            .HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<PollEventEntity>()
            .HasOne(e => e.Message).WithMany(m => m.PollEvents)
            .HasForeignKey(e => e.MessageId).OnDelete(DeleteBehavior.NoAction);

        // PinEventEntity soft relations
        modelBuilder.Entity<PinEventEntity>()
            .HasOne(e => e.Guild).WithMany(g => g.PinEvents)
            .HasForeignKey(e => e.GuildId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<PinEventEntity>()
            .HasOne(e => e.Channel).WithMany(c => c.PinEvents)
            .HasForeignKey(e => e.ChannelId).OnDelete(DeleteBehavior.NoAction);

        // WebhookEventEntity soft relations
        modelBuilder.Entity<WebhookEventEntity>()
            .HasOne(e => e.Guild).WithMany(g => g.WebhookEvents)
            .HasForeignKey(e => e.GuildId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<WebhookEventEntity>()
            .HasOne(e => e.Channel).WithMany(c => c.WebhookEvents)
            .HasForeignKey(e => e.ChannelId).OnDelete(DeleteBehavior.NoAction);

        // IntegrationEventEntity soft relations
        modelBuilder.Entity<IntegrationEventEntity>()
            .HasOne(e => e.Guild).WithMany(g => g.IntegrationEvents)
            .HasForeignKey(e => e.GuildId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<IntegrationEventEntity>()
            .HasOne(e => e.Integration).WithMany(i => i.IntegrationEvents)
            .HasForeignKey(e => e.IntegrationId).OnDelete(DeleteBehavior.NoAction);

        // AutoModEventEntity soft relations
        modelBuilder.Entity<AutoModEventEntity>()
            .HasOne(e => e.Guild).WithMany(g => g.AutoModEvents)
            .HasForeignKey(e => e.GuildId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<AutoModEventEntity>()
            .HasOne(e => e.Channel).WithMany(c => c.AutoModEvents)
            .HasForeignKey(e => e.ChannelId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<AutoModEventEntity>()
            .HasOne(e => e.User).WithMany(u => u.AutoModEvents)
            .HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<AutoModEventEntity>()
            .HasOne(e => e.Rule).WithMany(r => r.AutoModEvents)
            .HasForeignKey(e => e.RuleId).OnDelete(DeleteBehavior.NoAction);

        // AutoModRuleEventEntity soft relations
        modelBuilder.Entity<AutoModRuleEventEntity>()
            .HasOne(e => e.Guild).WithMany(g => g.AutoModRuleEvents)
            .HasForeignKey(e => e.GuildId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<AutoModRuleEventEntity>()
            .HasOne(e => e.Creator).WithMany()
            .HasForeignKey(e => e.CreatorId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<AutoModRuleEventEntity>()
            .HasOne(e => e.Rule).WithMany(r => r.AutoModRuleEvents)
            .HasForeignKey(e => e.RuleId).OnDelete(DeleteBehavior.NoAction);

        // ScheduledEventEntity soft relations
        modelBuilder.Entity<ScheduledEventEntity>()
            .HasOne(e => e.Guild).WithMany(g => g.ScheduledEvents)
            .HasForeignKey(e => e.GuildId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<ScheduledEventEntity>()
            .HasOne(e => e.Channel).WithMany(c => c.ScheduledEvents)
            .HasForeignKey(e => e.ChannelId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<ScheduledEventEntity>()
            .HasOne(e => e.Creator).WithMany()
            .HasForeignKey(e => e.CreatorId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<ScheduledEventEntity>()
            .HasOne(e => e.User).WithMany()
            .HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<ScheduledEventEntity>()
            .HasOne(e => e.ScheduledEvent).WithMany(s => s.ScheduledEvents)
            .HasForeignKey(e => e.EventId).OnDelete(DeleteBehavior.NoAction);

        // InviteEventEntity soft relations
        modelBuilder.Entity<InviteEventEntity>()
            .HasOne(e => e.Guild).WithMany(g => g.InviteEvents)
            .HasForeignKey(e => e.GuildId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<InviteEventEntity>()
            .HasOne(e => e.Channel).WithMany(c => c.InviteEvents)
            .HasForeignKey(e => e.ChannelId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<InviteEventEntity>()
            .HasOne(e => e.Inviter).WithMany(u => u.InviteEvents)
            .HasForeignKey(e => e.InviterId).OnDelete(DeleteBehavior.NoAction);

        // TypingEventEntity soft relations
        modelBuilder.Entity<TypingEventEntity>()
            .HasOne(e => e.Guild).WithMany(g => g.TypingEvents)
            .HasForeignKey(e => e.GuildId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<TypingEventEntity>()
            .HasOne(e => e.Channel).WithMany(c => c.TypingEvents)
            .HasForeignKey(e => e.ChannelId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<TypingEventEntity>()
            .HasOne(e => e.User).WithMany(u => u.TypingEvents)
            .HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.NoAction);

        // AuditLogEventEntity soft relations
        modelBuilder.Entity<AuditLogEventEntity>()
            .HasOne(e => e.Guild).WithMany(g => g.AuditLogEvents)
            .HasForeignKey(e => e.GuildId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<AuditLogEventEntity>()
            .HasOne(e => e.User).WithMany(u => u.AuditLogEvents)
            .HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.NoAction);

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
