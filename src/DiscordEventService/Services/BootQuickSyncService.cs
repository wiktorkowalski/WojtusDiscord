using System.Text.Json;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services;

internal sealed class BootQuickSyncService(
    IServiceScopeFactory scopeFactory,
    DiscordClient discordClient,
    ILogger<BootQuickSyncService> logger)
{
    // Per-channel recent-message fetch depth for the boot quick-sync.
    private const int RecentMessagesPerChannel = 50;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task SyncAsync(ulong guildId)
    {
        logger.LogInformation("Boot quick-sync starting for guild {GuildId}", guildId);

        var guild = await discordClient.GetGuildAsync(guildId);
        var messagesInserted = await SyncRecentMessagesAsync(guild);
        var (presenceSnapshots, activitiesInserted) = await SyncPresencesAsync(guild);

        logger.LogInformation(
            "Boot quick-sync completed for guild {GuildId}: {MessageCount} messages, {PresenceSnapshotCount} presence snapshots, {NewActivityCount} new activities",
            guildId, messagesInserted, presenceSnapshots, activitiesInserted);
    }

    private async Task<int> SyncRecentMessagesAsync(DiscordGuild guild)
    {
        var channels = await guild.GetChannelsAsync();
        var textChannels = channels
            .Where(c => c.Type is DiscordChannelType.Text or DiscordChannelType.News)
            .ToList();

        var totalInserted = 0;

        foreach (var channel in textChannels)
        {
            try
            {
                totalInserted += await SyncChannelMessagesAsync(guild, channel);
            }
            catch (DSharpPlus.Exceptions.UnauthorizedException)
            {
                logger.LogWarning("Quick-sync: no permission to read channel {ChannelId}, skipping", channel.Id);
            }
            catch (DSharpPlus.Exceptions.NotFoundException)
            {
                logger.LogWarning("Quick-sync: channel {ChannelId} not found, skipping", channel.Id);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Quick-sync: failed to sync messages for channel {ChannelId}, continuing", channel.Id);
            }
        }

        return totalInserted;
    }

    private async Task<int> SyncChannelMessagesAsync(DiscordGuild guild, DiscordChannel channel)
    {
        List<DiscordMessage> messages = [];
        await foreach (var msg in channel.GetMessagesAsync(RecentMessagesPerChannel))
            messages.Add(msg);

        if (messages.Count == 0) return 0;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
        var userService = scope.ServiceProvider.GetRequiredService<UserService>();
        var guildUpsert = scope.ServiceProvider.GetRequiredService<GuildUpsertService>();
        var channelUpsert = scope.ServiceProvider.GetRequiredService<ChannelUpsertService>();

        var guildId = (await guildUpsert.UpsertGuildAsync(guild)).Value;
        var channelId = (await channelUpsert.UpsertChannelAsync(channel, guildId)).Value;

        var existingDiscordIds = await db.Messages
            .Where(m => messages.Select(msg => msg.Id).Contains(m.DiscordId))
            .Select(m => m.DiscordId)
            .ToListAsync();
        var existingSet = existingDiscordIds.ToHashSet();

        var newMessages = messages.Where(m => !existingSet.Contains(m.Id) && m.Author is not null).ToList();
        if (newMessages.Count == 0) return 0;

        var uniqueAuthors = newMessages.Select(m => m.Author!).DistinctBy(a => a.Id).ToList();
        foreach (var author in uniqueAuthors)
            await userService.UpsertUserAsync(author);

        var authorDiscordIds = newMessages.Select(m => m.Author!.Id).Distinct().ToList();
        var authorLookup = await db.Users
            .Where(u => authorDiscordIds.Contains(u.DiscordId))
            .ToDictionaryAsync(u => u.DiscordId, u => u.Id);

        var inserted = 0;
        foreach (var message in newMessages)
        {
            if (!authorLookup.TryGetValue(message.Author!.Id, out var authorId))
                continue;

            if (await InsertBackfilledMessageAsync(db, guild, channel, message, guildId, channelId, authorId))
                inserted++;
        }

        return inserted;
    }

    // Insert-or-ignore per message: the caller's existingSet pre-check filters known rows;
    // this guards the race where a live MessageCreated inserts the same message during boot.
    // On conflict we skip the backfilled event (the live path logs its own Created event)
    // and don't count it.
    private static async Task<bool> InsertBackfilledMessageAsync(
        DiscordDbContext db,
        DiscordGuild guild,
        DiscordChannel channel,
        DiscordMessage message,
        Guid guildId,
        Guid channelId,
        Guid authorId)
    {
        var attachmentsJson = message.Attachments.Count > 0
            ? JsonSerializer.Serialize(message.Attachments.Select(a => new { a.Id, a.Url, a.FileName, a.FileSize }))
            : null;
        var embedsJson = message.Embeds.Count > 0
            ? JsonSerializer.Serialize(message.Embeds)
            : null;

        var (_, inserted) = await db.Messages.GetOrInsertAsync(
            m => m.DiscordId == message.Id,
            () => new MessageEntity
            {
                DiscordId = message.Id,
                ChannelId = channelId,
                GuildId = guildId,
                AuthorId = authorId,
                Content = string.IsNullOrEmpty(message.Content) ? null : message.Content,
                ReplyToDiscordId = message.ReferencedMessage?.Id,
                HasAttachments = message.Attachments.Count > 0,
                HasEmbeds = message.Embeds.Count > 0,
                AttachmentsJson = attachmentsJson,
                EmbedsJson = embedsJson,
                Flags = (int)(message.Flags ?? 0),
                CreatedAtUtc = message.Timestamp.UtcDateTime,
                EditedAtUtc = message.EditedTimestamp?.UtcDateTime
            });

        if (!inserted) return false;

        db.MessageEvents.Add(new MessageEventEntity
        {
            MessageDiscordId = message.Id,
            ChannelDiscordId = channel.Id,
            AuthorDiscordId = message.Author!.Id,
            GuildDiscordId = guild.Id,
            EventType = MessageEventType.Backfilled,
            Content = string.IsNullOrEmpty(message.Content) ? null : message.Content,
            HasAttachments = message.Attachments.Count > 0,
            HasEmbeds = message.Embeds.Count > 0,
            ReplyToMessageDiscordId = message.ReferencedMessage?.Id,
            AttachmentsJson = attachmentsJson,
            EmbedsJson = embedsJson,
            EventTimestampUtc = message.Timestamp.UtcDateTime,
            ReceivedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        return true;
    }

    private async Task<(int presenceSnapshots, int activitiesInserted)> SyncPresencesAsync(DiscordGuild guild)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
        var userService = scope.ServiceProvider.GetRequiredService<UserService>();

        var presenceSnapshots = 0;
        var activitiesInserted = 0;
        var now = DateTime.UtcNow;

        var guildGuid = await db.Guilds
            .Where(g => g.DiscordId == guild.Id)
            .Select(g => (Guid?)g.Id)
            .FirstOrDefaultAsync();

        if (guildGuid is null) return (0, 0);

        List<DiscordMember> members = [];
        await foreach (var member in guild.GetAllMembersAsync())
            members.Add(member);

        foreach (var member in members)
        {
            try
            {
                var newActivities = await SnapshotMemberPresenceAsync(db, userService, guild, member, guildGuid.Value, now);
                if (newActivities is null) continue;

                presenceSnapshots++;
                activitiesInserted += newActivities.Value;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Quick-sync: failed to snapshot presence for member {MemberId}, continuing", member.Id);
            }
        }

        return (presenceSnapshots, activitiesInserted);
    }

    // Returns the number of newly-started activities, or null when the member has no
    // presence (or their user row couldn't be upserted) and no snapshot was taken.
    private static async Task<int?> SnapshotMemberPresenceAsync(
        DiscordDbContext db,
        UserService userService,
        DiscordGuild guild,
        DiscordMember member,
        Guid guildGuid,
        DateTime now)
    {
        var presence = member.Presence;
        if (presence is null) return null;

        var userResult = await userService.UpsertUserAsync(member);
        if (!userResult.IsSuccess) return null;
        var userGuid = userResult.Value;

        db.PresenceEvents.Add(new PresenceEventEntity
        {
            UserDiscordId = member.Id,
            GuildDiscordId = guild.Id,
            EventType = PresenceEventType.BootSnapshot,
            DesktopStatusBefore = (int)DiscordUserStatus.Offline,
            MobileStatusBefore = (int)DiscordUserStatus.Offline,
            WebStatusBefore = (int)DiscordUserStatus.Offline,
            DesktopStatusAfter = GetStatusValue(presence.ClientStatus?.Desktop),
            MobileStatusAfter = GetStatusValue(presence.ClientStatus?.Mobile),
            WebStatusAfter = GetStatusValue(presence.ClientStatus?.Web),
            ActivitiesAfterJson = SerializeActivities(presence.Activities),
            EventTimestampUtc = now,
            ReceivedAtUtc = now
        });

        var activitiesInserted = await StartMissingActivitiesAsync(db, presence.Activities, userGuid, guildGuid, now);

        await db.SaveChangesAsync();
        return activitiesInserted;
    }

    private static async Task<int> StartMissingActivitiesAsync(
        DiscordDbContext db,
        IReadOnlyList<DiscordActivity>? activities,
        Guid userGuid,
        Guid guildGuid,
        DateTime now)
    {
        if (activities is not { Count: > 0 }) return 0;

        var inserted = 0;
        foreach (var activity in activities)
        {
            var hasActive = await db.Activities
                .AnyAsync(a => a.UserId == userGuid && a.IsActive
                    && a.Name == activity.Name && a.ActivityType == (int)activity.ActivityType);

            if (!hasActive)
            {
                db.Activities.Add(new ActivityEntity
                {
                    UserId = userGuid,
                    GuildId = guildGuid,
                    ActivityType = (int)activity.ActivityType,
                    Name = activity.Name,
                    StreamUrl = activity.StreamUrl,
                    IsActive = true,
                    FirstSeenAtUtc = now,
                    LastSeenAtUtc = now
                });
                inserted++;
            }
        }

        return inserted;
    }

    private static int GetStatusValue(Optional<DiscordUserStatus>? status)
    {
        if (status?.HasValue == true)
            return (int)status.Value.Value;
        return (int)DiscordUserStatus.Offline;
    }

    private static string? SerializeActivities(IReadOnlyList<DiscordActivity>? activities)
    {
        if (activities is null || activities.Count == 0)
            return null;

        var activityData = activities.Select(a => new
        {
            a.Name,
            Type = (int)a.ActivityType,
            a.StreamUrl
        }).ToList();

        return JsonSerializer.Serialize(activityData, JsonOptions);
    }
}
