using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.EventHandlers;

internal sealed class ScheduledEventHandler(EventPipeline pipeline) :
    IEventHandler<ScheduledGuildEventCreatedEventArgs>,
    IEventHandler<ScheduledGuildEventUpdatedEventArgs>,
    IEventHandler<ScheduledGuildEventDeletedEventArgs>,
    IEventHandler<ScheduledGuildEventCompletedEventArgs>,
    IEventHandler<ScheduledGuildEventUserAddedEventArgs>,
    IEventHandler<ScheduledGuildEventUserRemovedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, ScheduledGuildEventCreatedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "ScheduledGuildEventCreated", nameof(ScheduledEventHandler),
            e.Guild.Id, e.Channel?.Id, e.Creator?.Id, async ctx =>
            {
                await UpsertScheduledEventAsync(ctx.Db, e.Event, e.Creator?.Id);

                ctx.Db.ScheduledEvents.Add(new ScheduledEventEntity
                {
                    EventDiscordId = e.Event.Id,
                    GuildDiscordId = e.Guild.Id,
                    ChannelDiscordId = e.Channel?.Id,
                    CreatorDiscordId = e.Creator?.Id,
                    EventType = ScheduledEventEventType.Created,
                    Name = e.Event.Name,
                    Description = e.Event.Description,
                    Status = (int)e.Event.Status,
                    EntityType = (int)e.Event.Type,
                    ScheduledStartTimeUtc = e.Event.StartTime.UtcDateTime,
                    ScheduledEndTimeUtc = e.Event.EndTime?.UtcDateTime,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, ScheduledGuildEventUpdatedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "ScheduledGuildEventUpdated", nameof(ScheduledEventHandler),
            e.EventAfter.GuildId, e.EventAfter.ChannelId, e.EventAfter.Creator?.Id, async ctx =>
            {
                await UpsertScheduledEventAsync(ctx.Db, e.EventAfter, e.EventAfter.Creator?.Id);

                ctx.Db.ScheduledEvents.Add(new ScheduledEventEntity
                {
                    EventDiscordId = e.EventAfter.Id,
                    GuildDiscordId = e.EventAfter.GuildId,
                    ChannelDiscordId = e.EventAfter.ChannelId,
                    CreatorDiscordId = e.EventAfter.Creator?.Id,
                    EventType = ScheduledEventEventType.Updated,
                    Name = e.EventAfter.Name,
                    Description = e.EventAfter.Description,
                    Status = (int)e.EventAfter.Status,
                    EntityType = (int)e.EventAfter.Type,
                    ScheduledStartTimeUtc = e.EventAfter.StartTime.UtcDateTime,
                    ScheduledEndTimeUtc = e.EventAfter.EndTime?.UtcDateTime,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, ScheduledGuildEventDeletedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "ScheduledGuildEventDeleted", nameof(ScheduledEventHandler),
            e.Event.GuildId, e.Event.ChannelId, null, async ctx =>
            {
                await ctx.Db.GuildScheduledEvents
                    .Where(s => s.DiscordId == e.Event.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.IsDeleted, true)
                        .SetProperty(x => x.DeletedAtUtc, (DateTime?)ctx.ReceivedAtUtc));

                ctx.Db.ScheduledEvents.Add(new ScheduledEventEntity
                {
                    EventDiscordId = e.Event.Id,
                    GuildDiscordId = e.Event.GuildId,
                    ChannelDiscordId = e.Event.ChannelId,
                    EventType = ScheduledEventEventType.Deleted,
                    Name = e.Event.Name,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, ScheduledGuildEventCompletedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "ScheduledGuildEventCompleted", nameof(ScheduledEventHandler),
            e.Event.GuildId, e.Event.ChannelId, null, async ctx =>
            {
                await UpsertScheduledEventAsync(ctx.Db, e.Event, e.Event.Creator?.Id);

                ctx.Db.ScheduledEvents.Add(new ScheduledEventEntity
                {
                    EventDiscordId = e.Event.Id,
                    GuildDiscordId = e.Event.GuildId,
                    ChannelDiscordId = e.Event.ChannelId,
                    EventType = ScheduledEventEventType.Completed,
                    Name = e.Event.Name,
                    Status = (int)e.Event.Status,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, ScheduledGuildEventUserAddedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "ScheduledGuildEventUserAdded", nameof(ScheduledEventHandler),
            e.Guild.Id, null, e.User.Id, async ctx =>
            {
                await ctx.Db.GuildScheduledEvents
                    .Where(s => s.DiscordId == e.Event.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.UserCount, x => x.UserCount + 1));

                ctx.Db.ScheduledEvents.Add(new ScheduledEventEntity
                {
                    EventDiscordId = e.Event.Id,
                    GuildDiscordId = e.Guild.Id,
                    EventType = ScheduledEventEventType.UserAdded,
                    UserDiscordId = e.User.Id,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, ScheduledGuildEventUserRemovedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "ScheduledGuildEventUserRemoved", nameof(ScheduledEventHandler),
            e.Guild.Id, null, e.User.Id, async ctx =>
            {
                await ctx.Db.GuildScheduledEvents
                    .Where(s => s.DiscordId == e.Event.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.UserCount, x => x.UserCount - 1));

                ctx.Db.ScheduledEvents.Add(new ScheduledEventEntity
                {
                    EventDiscordId = e.Event.Id,
                    GuildDiscordId = e.Guild.Id,
                    EventType = ScheduledEventEventType.UserRemoved,
                    UserDiscordId = e.User.Id,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    private static async Task UpsertScheduledEventAsync(DiscordDbContext db, DiscordScheduledGuildEvent evt, ulong? creatorDiscordId)
    {
        var guildGuid = await db.Guilds
            .Where(g => g.DiscordId == evt.GuildId)
            .Select(g => g.Id)
            .FirstOrDefaultAsync();
        var channelGuid = evt.ChannelId.HasValue
            ? await db.Channels
                .Where(c => c.DiscordId == evt.ChannelId.Value)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync()
            : null;
        var creatorGuid = creatorDiscordId.HasValue
            ? await db.Users
                .Where(u => u.DiscordId == creatorDiscordId.Value)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefaultAsync()
            : null;

        var existing = await db.GuildScheduledEvents.FirstOrDefaultAsync(s => s.DiscordId == evt.Id);
        if (existing is null)
        {
            existing = new GuildScheduledEventEntity { DiscordId = evt.Id };
            db.GuildScheduledEvents.Add(existing);
        }

        existing.GuildId = guildGuid;
        existing.ChannelId = channelGuid;
        existing.CreatorId = creatorGuid;
        existing.Name = evt.Name;
        existing.Description = evt.Description;
        existing.Status = (int)evt.Status;
        existing.EntityType = (int)evt.Type;
        existing.ScheduledStartTimeUtc = evt.StartTime.UtcDateTime;
        existing.ScheduledEndTimeUtc = evt.EndTime?.UtcDateTime;
        existing.EntityMetadataLocation = evt.Metadata?.Location;
        existing.IsDeleted = false;
        existing.DeletedAtUtc = null;
    }
}
