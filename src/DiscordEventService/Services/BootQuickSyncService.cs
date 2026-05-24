using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DiscordEventService.Services;

public class BootQuickSyncService(
    IServiceScopeFactory scopeFactory,
    DiscordClient discordClient,
    ILogger<BootQuickSyncService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
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
            "Boot quick-sync completed for guild {GuildId}: {Messages} messages, {Presences} presence snapshots, {Activities} new activities",
            guildId, messagesInserted, presenceSnapshots, activitiesInserted);
    }

    private async Task<int> SyncRecentMessagesAsync(DiscordGuild guild)
    {
        var channels = await guild.GetChannelsAsync();
        var textChannels = channels
            .Where(c => c.Type is DiscordChannelType.Text or DiscordChannelType.News)
            .ToList();

        int totalInserted = 0;

        foreach (var channel in textChannels)
        {
            try
            {
                var messages = new List<DiscordMessage>();
                await foreach (var msg in channel.GetMessagesAsync(50))
                {
                    messages.Add(msg);
                }

                if (messages.Count == 0) continue;

                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
                var userService = scope.ServiceProvider.GetRequiredService<UserService>();
                var guildUpsert = scope.ServiceProvider.GetRequiredService<GuildUpsertService>();
                var channelUpsert = scope.ServiceProvider.GetRequiredService<ChannelUpsertService>();

                var guildId = await guildUpsert.UpsertGuildAsync(guild);
                var channelId = await channelUpsert.UpsertChannelAsync(channel, guildId);

                var existingDiscordIds = await db.Messages
                    .Where(m => messages.Select(msg => msg.Id).Contains(m.DiscordId))
                    .Select(m => m.DiscordId)
                    .ToListAsync();
                var existingSet = existingDiscordIds.ToHashSet();

                var newMessages = messages.Where(m => !existingSet.Contains(m.Id) && m.Author is not null).ToList();
                if (newMessages.Count == 0) continue;

                var uniqueAuthors = newMessages.Select(m => m.Author!).DistinctBy(a => a.Id).ToList();
                foreach (var author in uniqueAuthors)
                    await userService.UpsertUserAsync(author);

                var authorDiscordIds = newMessages.Select(m => m.Author!.Id).Distinct().ToList();
                var authorLookup = await db.Users
                    .Where(u => authorDiscordIds.Contains(u.DiscordId))
                    .ToDictionaryAsync(u => u.DiscordId, u => u.Id);

                foreach (var message in newMessages)
                {
                    if (!authorLookup.TryGetValue(message.Author!.Id, out var authorId))
                        continue;

                    var attachmentsJson = message.Attachments.Count > 0
                        ? JsonSerializer.Serialize(message.Attachments.Select(a => new { a.Id, a.Url, a.FileName, a.FileSize }))
                        : null;
                    var embedsJson = message.Embeds.Count > 0
                        ? JsonSerializer.Serialize(message.Embeds)
                        : null;

                    db.Messages.Add(new MessageEntity
                    {
                        DiscordId = message.Id,
                        ChannelId = channelId,
                        GuildId = guildId,
                        AuthorId = authorId,
                        Content = message.Content,
                        ReplyToDiscordId = message.ReferencedMessage?.Id,
                        HasAttachments = message.Attachments.Count > 0,
                        HasEmbeds = message.Embeds.Count > 0,
                        AttachmentsJson = attachmentsJson,
                        EmbedsJson = embedsJson,
                        Flags = (int)(message.Flags ?? 0),
                        CreatedAtUtc = message.Timestamp.UtcDateTime,
                        EditedAtUtc = message.EditedTimestamp?.UtcDateTime
                    });

                    db.MessageEvents.Add(new MessageEventEntity
                    {
                        MessageDiscordId = message.Id,
                        ChannelDiscordId = channel.Id,
                        AuthorDiscordId = message.Author.Id,
                        GuildDiscordId = guild.Id,
                        EventType = MessageEventType.Backfilled,
                        Content = message.Content,
                        HasAttachments = message.Attachments.Count > 0,
                        HasEmbeds = message.Embeds.Count > 0,
                        ReplyToMessageDiscordId = message.ReferencedMessage?.Id,
                        AttachmentsJson = attachmentsJson,
                        EmbedsJson = embedsJson,
                        EventTimestampUtc = message.Timestamp.UtcDateTime,
                        ReceivedAtUtc = DateTime.UtcNow
                    });

                    totalInserted++;
                }

                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
                {
                    db.ChangeTracker.Clear();
                }
            }
            catch (DSharpPlus.Exceptions.UnauthorizedException)
            {
                logger.LogDebug("Quick-sync: no permission to read channel {ChannelId}, skipping", channel.Id);
            }
            catch (DSharpPlus.Exceptions.NotFoundException)
            {
                logger.LogDebug("Quick-sync: channel {ChannelId} not found, skipping", channel.Id);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Quick-sync: failed to sync messages for channel {ChannelId}, continuing", channel.Id);
            }
        }

        return totalInserted;
    }

    private async Task<(int presenceSnapshots, int activitiesInserted)> SyncPresencesAsync(DiscordGuild guild)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
        var userService = scope.ServiceProvider.GetRequiredService<UserService>();

        int presenceSnapshots = 0;
        int activitiesInserted = 0;
        var now = DateTime.UtcNow;

        var guildGuid = await db.Guilds
            .Where(g => g.DiscordId == guild.Id)
            .Select(g => g.Id)
            .FirstOrDefaultAsync();

        if (guildGuid == Guid.Empty) return (0, 0);

        var members = new List<DiscordMember>();
        await foreach (var member in guild.GetAllMembersAsync())
        {
            members.Add(member);
        }

        foreach (var member in members)
        {
            try
            {
                var presence = member.Presence;
                if (presence is null) continue;

                await userService.UpsertUserAsync(member);
                var userGuid = await db.Users
                    .Where(u => u.DiscordId == member.Id)
                    .Select(u => u.Id)
                    .FirstOrDefaultAsync();

                if (userGuid == Guid.Empty) continue;

                var activitiesJson = SerializeActivities(presence.Activities);

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
                    ActivitiesAfterJson = activitiesJson,
                    EventTimestampUtc = now,
                    ReceivedAtUtc = now
                });
                presenceSnapshots++;

                if (presence.Activities is { Count: > 0 })
                {
                    foreach (var activity in presence.Activities)
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
                            activitiesInserted++;
                        }
                    }
                }

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Quick-sync: failed to snapshot presence for member {MemberId}, continuing", member.Id);
            }
        }

        return (presenceSnapshots, activitiesInserted);
    }

    private static int GetStatusValue(Optional<DiscordUserStatus>? status)
    {
        if (status?.HasValue == true)
            return (int)status.Value.Value;
        return (int)DiscordUserStatus.Offline;
    }

    private static string? SerializeActivities(IReadOnlyList<DiscordActivity>? activities)
    {
        if (activities == null || activities.Count == 0)
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
