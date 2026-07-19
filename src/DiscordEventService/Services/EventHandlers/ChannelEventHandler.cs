using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.EventHandlers;

internal sealed class ChannelEventHandler(EventPipeline pipeline) :
    IEventHandler<ChannelCreatedEventArgs>,
    IEventHandler<ChannelUpdatedEventArgs>,
    IEventHandler<ChannelDeletedEventArgs>,
    IEventHandler<ChannelPinsUpdatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, ChannelCreatedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "ChannelCreated", nameof(ChannelEventHandler),
            e.Guild.Id, e.Channel.Id, null, async ctx =>
            {
                var channelUpsert = ctx.Services.GetRequiredService<ChannelUpsertService>();
                // Resolve the required guild FK via the shared resolver: on a miss it logs and
                // records a FailedEvent instead of flowing Guid.Empty into a fresh channels row
                // (#292). The ChannelEvent timeline row is FK-free and still lands below.
                var fks = await ctx.Services.GetRequiredService<FkResolver>()
                    .ResolveAsync(ctx, e.Guild, $"ChannelCreated ChannelId={e.Channel.Id}");

                // Upsert the channel (handles the 23505 race internally) before staging the event,
                // then commit channel + event together. ExecutionStrategy is required because
                // EnableRetryOnFailure is configured on the DbContext.
                var strategy = ctx.Db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    // Reset the tracker so an EnableRetryOnFailure retry doesn't re-stage entities
                    // left Added by a rolled-back attempt.
                    ctx.Db.ChangeTracker.Clear();
                    await using var tx = await ctx.Db.Database.BeginTransactionAsync();

                    if (fks.Success)
                        await channelUpsert.UpsertChannelAsync(e.Channel, fks.GuildId);

                    ctx.Db.ChannelEvents.Add(BuildChannelCreatedEvent(e, ctx));
                    await ctx.Db.SaveChangesAsync();
                    await tx.CommitAsync();
                });
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, ChannelUpdatedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "ChannelUpdated", nameof(ChannelEventHandler),
            e.ChannelAfter.Guild.Id, e.ChannelAfter.Id, null, async ctx =>
            {
                var channelUpsert = ctx.Services.GetRequiredService<ChannelUpsertService>();
                // Shared resolver: a guild miss must not flow Guid.Empty into a fresh channels row (#292).
                var fks = await ctx.Services.GetRequiredService<FkResolver>()
                    .ResolveAsync(ctx, e.ChannelAfter.Guild, $"ChannelUpdated ChannelId={e.ChannelAfter.Id}");

                var channelEvent = new ChannelEventEntity
                {
                    ChannelDiscordId = e.ChannelAfter.Id,
                    GuildDiscordId = e.ChannelAfter.Guild.Id,
                    ChannelType = (int)e.ChannelAfter.Type,
                    EventType = ChannelEventType.Updated,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson,
                };

                if (e.ChannelBefore.Name != e.ChannelAfter.Name)
                {
                    channelEvent.NameBefore = e.ChannelBefore.Name;
                    channelEvent.NameAfter = e.ChannelAfter.Name;
                }

                if (e.ChannelBefore.Topic != e.ChannelAfter.Topic)
                {
                    channelEvent.TopicBefore = e.ChannelBefore.Topic;
                    channelEvent.TopicAfter = e.ChannelAfter.Topic;
                }

                if (e.ChannelBefore.Position != e.ChannelAfter.Position)
                {
                    channelEvent.PositionBefore = e.ChannelBefore.Position;
                    channelEvent.PositionAfter = e.ChannelAfter.Position;
                }

                // Channel row + event row must commit together. ExecutionStrategy is
                // required because EnableRetryOnFailure is configured on the DbContext.
                var strategy = ctx.Db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    // Reset the tracker so an EnableRetryOnFailure retry doesn't re-stage entities
                    // left Added by a rolled-back attempt.
                    ctx.Db.ChangeTracker.Clear();
                    await using var tx = await ctx.Db.Database.BeginTransactionAsync();

                    // Through the shared seam so an update for a channel we never saw creates
                    // the row instead of being dropped.
                    if (fks.Success)
                        await channelUpsert.UpsertChannelAsync(e.ChannelAfter, fks.GuildId);

                    ctx.Db.ChannelEvents.Add(channelEvent);
                    await ctx.Db.SaveChangesAsync();
                    await tx.CommitAsync();
                });
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, ChannelDeletedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "ChannelDeleted", nameof(ChannelEventHandler),
            e.Guild.Id, e.Channel.Id, null, async ctx =>
            {
                var channelEntity = await ctx.Db.Channels
                    .FirstOrDefaultAsync(c => c.DiscordId == e.Channel.Id);

                if (channelEntity is not null)
                {
                    channelEntity.IsDeleted = true;
                    channelEntity.DeletedAtUtc = ctx.ReceivedAtUtc;
                }

                ctx.Db.ChannelEvents.Add(new ChannelEventEntity
                {
                    ChannelDiscordId = e.Channel.Id,
                    GuildDiscordId = e.Guild.Id,
                    ChannelType = (int)e.Channel.Type,
                    EventType = ChannelEventType.Deleted,
                    NameBefore = e.Channel.Name,
                    TopicBefore = e.Channel.Topic,
                    PositionBefore = e.Channel.Position,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson,
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, ChannelPinsUpdatedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "ChannelPinsUpdatedChannel", nameof(ChannelEventHandler),
            e.Guild?.Id ?? 0, e.Channel.Id, null, async ctx =>
            {
                ctx.Db.ChannelEvents.Add(new ChannelEventEntity
                {
                    ChannelDiscordId = e.Channel.Id,
                    GuildDiscordId = e.Guild?.Id ?? 0,
                    ChannelType = (int)e.Channel.Type,
                    EventType = ChannelEventType.PinsUpdated,
                    EventTimestampUtc = e.LastPinTimestamp?.UtcDateTime ?? ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson,
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    private static ChannelEventEntity BuildChannelCreatedEvent(ChannelCreatedEventArgs e, EventContext ctx) => new ChannelEventEntity
    {
        ChannelDiscordId = e.Channel.Id,
        GuildDiscordId = e.Guild.Id,
        ChannelType = (int)e.Channel.Type,
        EventType = ChannelEventType.Created,
        NameAfter = e.Channel.Name,
        TopicAfter = e.Channel.Topic,
        PositionAfter = e.Channel.Position,
        EventTimestampUtc = ctx.ReceivedAtUtc,
        ReceivedAtUtc = ctx.ReceivedAtUtc,
        RawEventJson = ctx.RawJson,
    };
}
