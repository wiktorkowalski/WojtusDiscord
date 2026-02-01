using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.EventHandlers;

public class VoiceEventHandler(IServiceScopeFactory scopeFactory, ILogger<VoiceEventHandler> logger) :
    IEventHandler<VoiceStateUpdatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, VoiceStateUpdatedEventArgs args)
    {
        try
        {
            var now = DateTime.UtcNow;
            var eventType = DetermineEventType(args);

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                args, "VoiceStateUpdated", args.Guild.Id, args.After?.Channel?.Id, args.User.Id);

            // Upsert VoiceStateEntity
            await UpsertVoiceStateAsync(db, args, now);

            var voiceEvent = new VoiceStateEventEntity
            {
                UserDiscordId = args.User.Id,
                GuildDiscordId = args.Guild.Id,
                ChannelDiscordIdBefore = args.Before?.Channel?.Id,
                ChannelDiscordIdAfter = args.After?.Channel?.Id,
                EventType = eventType,

                // Before state flags
                WasSelfMuted = args.Before?.IsSelfMuted ?? false,
                WasSelfDeafened = args.Before?.IsSelfDeafened ?? false,
                WasServerMuted = args.Before?.IsServerMuted ?? false,
                WasServerDeafened = args.Before?.IsServerDeafened ?? false,
                WasStreaming = args.Before?.IsSelfStream ?? false,
                WasVideo = args.Before?.IsSelfVideo ?? false,
                WasSuppressed = args.Before?.IsSuppressed ?? false,

                // After state flags
                IsSelfMuted = args.After?.IsSelfMuted ?? false,
                IsSelfDeafened = args.After?.IsSelfDeafened ?? false,
                IsServerMuted = args.After?.IsServerMuted ?? false,
                IsServerDeafened = args.After?.IsServerDeafened ?? false,
                IsStreaming = args.After?.IsSelfStream ?? false,
                IsVideo = args.After?.IsSelfVideo ?? false,
                IsSuppressed = args.After?.IsSuppressed ?? false,

                SessionId = args.After?.SessionId,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            };

            db.VoiceStateEvents.Add(voiceEvent);
            await db.SaveChangesAsync();

            logger.LogDebug("Recorded voice event: {EventType} for user {UserId} in guild {GuildId}",
                eventType, args.User.Id, args.Guild.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling voice state update for user {UserId} in guild {GuildId}",
                args.User.Id, args.Guild.Id);
            using var failureScope = scopeFactory.CreateScope();
            var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
            await failedEventService.RecordFailureAsync(
                "VoiceStateUpdated", nameof(VoiceEventHandler), ex,
                args.Guild.Id, args.After?.Channel?.Id, args.User.Id);
        }
    }

    private async Task UpsertVoiceStateAsync(DiscordDbContext db, VoiceStateUpdatedEventArgs args, DateTime now)
    {
        // Look up Guid FKs
        var user = await db.Users.FirstOrDefaultAsync(u => u.DiscordId == args.User.Id);
        var guild = await db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == args.Guild.Id);

        if (user == null || guild == null)
        {
            logger.LogWarning("Cannot upsert voice state: User={UserFound} Guild={GuildFound} for UserId={UserId} GuildId={GuildId}",
                user != null, guild != null, args.User.Id, args.Guild.Id);
            return;
        }

        var afterState = args.After;
        if (afterState is null || afterState.Channel is null)
        {
            // User left voice - remove their voice state
            await db.VoiceStates
                .Where(v => v.UserId == user.Id && v.GuildId == guild.Id)
                .ExecuteDeleteAsync();
            return;
        }

        var channel = await db.Channels.FirstOrDefaultAsync(c => c.DiscordId == afterState.Channel.Id);

        var rowsAffected = await db.VoiceStates
            .Where(v => v.UserId == user.Id && v.GuildId == guild.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(v => v.ChannelId, channel != null ? channel.Id : (Guid?)null)
                .SetProperty(v => v.SessionId, afterState.SessionId)
                .SetProperty(v => v.IsSelfMuted, afterState.IsSelfMuted)
                .SetProperty(v => v.IsSelfDeafened, afterState.IsSelfDeafened)
                .SetProperty(v => v.IsServerMuted, afterState.IsServerMuted)
                .SetProperty(v => v.IsServerDeafened, afterState.IsServerDeafened)
                .SetProperty(v => v.IsStreaming, afterState.IsSelfStream)
                .SetProperty(v => v.IsVideo, afterState.IsSelfVideo)
                .SetProperty(v => v.IsSuppressed, afterState.IsSuppressed)
                .SetProperty(v => v.LastUpdatedUtc, now));

        if (rowsAffected == 0)
        {
            try
            {
                db.VoiceStates.Add(new VoiceStateEntity
                {
                    UserId = user.Id,
                    GuildId = guild.Id,
                    ChannelId = channel?.Id,
                    SessionId = afterState.SessionId,
                    IsSelfMuted = afterState.IsSelfMuted,
                    IsSelfDeafened = afterState.IsSelfDeafened,
                    IsServerMuted = afterState.IsServerMuted,
                    IsServerDeafened = afterState.IsServerDeafened,
                    IsStreaming = afterState.IsSelfStream,
                    IsVideo = afterState.IsSelfVideo,
                    IsSuppressed = afterState.IsSuppressed,
                    JoinedChannelAtUtc = now
                });
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            {
                // Unique constraint violation - race condition, another request inserted first
                logger.LogDebug("Voice state race condition for User={UserId} Guild={GuildId}, retrying with update", user.Id, guild.Id);
                db.ChangeTracker.Clear();

                // Retry with update to ensure latest data is persisted
                await db.VoiceStates
                    .Where(v => v.UserId == user.Id && v.GuildId == guild.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(v => v.ChannelId, channel != null ? channel.Id : (Guid?)null)
                        .SetProperty(v => v.SessionId, afterState.SessionId)
                        .SetProperty(v => v.IsSelfMuted, afterState.IsSelfMuted)
                        .SetProperty(v => v.IsSelfDeafened, afterState.IsSelfDeafened)
                        .SetProperty(v => v.IsServerMuted, afterState.IsServerMuted)
                        .SetProperty(v => v.IsServerDeafened, afterState.IsServerDeafened)
                        .SetProperty(v => v.IsStreaming, afterState.IsSelfStream)
                        .SetProperty(v => v.IsVideo, afterState.IsSelfVideo)
                        .SetProperty(v => v.IsSuppressed, afterState.IsSuppressed)
                        .SetProperty(v => v.LastUpdatedUtc, now));
            }
        }
    }

    private static VoiceEventType DetermineEventType(VoiceStateUpdatedEventArgs args)
    {
        var beforeChannelId = args.Before?.Channel?.Id;
        var afterChannelId = args.After?.Channel?.Id;

        // Joined: before channel null, after not null
        if (beforeChannelId == null && afterChannelId != null)
            return VoiceEventType.Joined;

        // Left: before not null, after null
        if (beforeChannelId != null && afterChannelId == null)
            return VoiceEventType.Left;

        // Moved: both not null, different
        if (beforeChannelId != null && afterChannelId != null && beforeChannelId != afterChannelId)
            return VoiceEventType.Moved;

        // StateChanged: same channel, flags changed
        return VoiceEventType.StateChanged;
    }
}
