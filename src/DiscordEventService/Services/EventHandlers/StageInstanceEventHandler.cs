using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.EventHandlers;

internal sealed class StageInstanceEventHandler(EventPipeline pipeline) :
    IEventHandler<StageInstanceCreatedEventArgs>,
    IEventHandler<StageInstanceUpdatedEventArgs>,
    IEventHandler<StageInstanceDeletedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, StageInstanceCreatedEventArgs e)
    {
        await pipeline.Execute(e, "StageInstanceCreated", nameof(StageInstanceEventHandler),
            e.StageInstance.GuildId, e.StageInstance.ChannelId, null, async ctx =>
            {
                var guildGuid = await ctx.Db.Guilds
                    .Where(g => g.DiscordId == e.StageInstance.GuildId)
                    .Select(g => g.Id)
                    .FirstOrDefaultAsync();
                var channelGuid = await ctx.Db.Channels
                    .Where(c => c.DiscordId == e.StageInstance.ChannelId)
                    .Select(c => c.Id)
                    .FirstOrDefaultAsync();

                ctx.Db.StageInstances.Add(new StageInstanceEntity
                {
                    DiscordId = e.StageInstance.Id,
                    GuildId = guildGuid,
                    ChannelId = channelGuid,
                    Topic = e.StageInstance.Topic,
                    PrivacyLevel = (int)e.StageInstance.PrivacyLevel
                });

                ctx.Db.StageInstanceEvents.Add(new StageInstanceEventEntity
                {
                    StageInstanceDiscordId = e.StageInstance.Id,
                    GuildDiscordId = e.StageInstance.GuildId,
                    ChannelDiscordId = e.StageInstance.ChannelId,
                    EventType = StageInstanceEventType.Created,
                    TopicAfter = e.StageInstance.Topic,
                    PrivacyLevelAfter = (int)e.StageInstance.PrivacyLevel,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, StageInstanceUpdatedEventArgs e)
    {
        await pipeline.Execute(e, "StageInstanceUpdated", nameof(StageInstanceEventHandler),
            e.StageInstanceAfter.GuildId, e.StageInstanceAfter.ChannelId, null, async ctx =>
            {
                await ctx.Db.StageInstances
                    .Where(s => s.DiscordId == e.StageInstanceAfter.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(st => st.Topic, e.StageInstanceAfter.Topic)
                        .SetProperty(st => st.PrivacyLevel, (int)e.StageInstanceAfter.PrivacyLevel));

                ctx.Db.StageInstanceEvents.Add(new StageInstanceEventEntity
                {
                    StageInstanceDiscordId = e.StageInstanceAfter.Id,
                    GuildDiscordId = e.StageInstanceAfter.GuildId,
                    ChannelDiscordId = e.StageInstanceAfter.ChannelId,
                    EventType = StageInstanceEventType.Updated,
                    TopicBefore = e.StageInstanceBefore?.Topic,
                    TopicAfter = e.StageInstanceAfter.Topic,
                    PrivacyLevelBefore = e.StageInstanceBefore != null ? (int)e.StageInstanceBefore.PrivacyLevel : null,
                    PrivacyLevelAfter = (int)e.StageInstanceAfter.PrivacyLevel,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, StageInstanceDeletedEventArgs e)
    {
        await pipeline.Execute(e, "StageInstanceDeleted", nameof(StageInstanceEventHandler),
            e.StageInstance.GuildId, e.StageInstance.ChannelId, null, async ctx =>
            {
                await ctx.Db.StageInstances
                    .Where(s => s.DiscordId == e.StageInstance.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(st => st.IsDeleted, true)
                        .SetProperty(st => st.DeletedAtUtc, (DateTime?)ctx.ReceivedAtUtc));

                ctx.Db.StageInstanceEvents.Add(new StageInstanceEventEntity
                {
                    StageInstanceDiscordId = e.StageInstance.Id,
                    GuildDiscordId = e.StageInstance.GuildId,
                    ChannelDiscordId = e.StageInstance.ChannelId,
                    EventType = StageInstanceEventType.Deleted,
                    TopicBefore = e.StageInstance.Topic,
                    PrivacyLevelBefore = (int)e.StageInstance.PrivacyLevel,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();
            });
    }
}
