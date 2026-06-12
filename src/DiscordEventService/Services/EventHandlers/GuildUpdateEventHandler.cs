using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

internal sealed class GuildUpdateEventHandler(EventPipeline pipeline) :
    IEventHandler<GuildUpdatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, GuildUpdatedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "GuildUpdatedEvent", nameof(GuildUpdateEventHandler),
            e.GuildAfter.Id, null, null, async ctx =>
            {
                var guildEvent = new GuildEventEntity
                {
                    GuildDiscordId = e.GuildAfter.Id,
                    EventType = GuildEventType.Updated,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson,
                };

                if (e.GuildBefore.Name != e.GuildAfter.Name)
                {
                    guildEvent.NameBefore = e.GuildBefore.Name;
                    guildEvent.NameAfter = e.GuildAfter.Name;
                }

                if (e.GuildBefore.IconHash != e.GuildAfter.IconHash)
                {
                    guildEvent.IconHashBefore = e.GuildBefore.IconHash;
                    guildEvent.IconHashAfter = e.GuildAfter.IconHash;
                }

                if (e.GuildBefore.OwnerId != e.GuildAfter.OwnerId)
                {
                    guildEvent.OwnerDiscordIdBefore = e.GuildBefore.OwnerId;
                    guildEvent.OwnerDiscordIdAfter = e.GuildAfter.OwnerId;
                }

                ctx.Db.GuildEvents.Add(guildEvent);
                await ctx.Db.SaveChangesAsync();
            });
    }
}
