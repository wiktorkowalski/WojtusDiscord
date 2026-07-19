using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.EventHandlers;

internal sealed class IntegrationEventHandler(EventPipeline pipeline) :
    IEventHandler<IntegrationCreatedEventArgs>,
    IEventHandler<IntegrationUpdatedEventArgs>,
    IEventHandler<IntegrationDeletedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, IntegrationCreatedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "IntegrationCreated", nameof(IntegrationEventHandler),
            e.Guild.Id, null, null, async ctx =>
            {
                // Resolve the required guild FK via the shared resolver: on a miss it logs and
                // records a FailedEvent instead of flowing Guid.Empty into the integrations row
                // (#292). The IntegrationEvent timeline row is FK-free and still lands below.
                var fks = await ctx.Services.GetRequiredService<FkResolver>()
                    .ResolveAsync(ctx, e.Guild, $"IntegrationCreated IntegrationId={e.Integration.Id}");

                if (fks.Success)
                {
                    ctx.Db.Integrations.Add(new IntegrationEntity
                    {
                        DiscordId = e.Integration.Id,
                        GuildId = fks.GuildId,
                        Name = e.Integration.Name,
                        Type = e.Integration.Type,
                        IsEnabled = e.Integration.IsEnabled,
                    });
                }

                ctx.Db.IntegrationEvents.Add(new IntegrationEventEntity
                {
                    IntegrationDiscordId = e.Integration.Id,
                    GuildDiscordId = e.Guild.Id,
                    EventType = IntegrationEventType.Created,
                    Name = e.Integration.Name,
                    Type = e.Integration.Type,
                    IsEnabled = e.Integration.IsEnabled,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson,
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, IntegrationUpdatedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "IntegrationUpdated", nameof(IntegrationEventHandler),
            e.Guild.Id, null, null, async ctx =>
            {
                await ctx.Db.Integrations
                    .Where(i => i.DiscordId == e.Integration.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(i => i.Name, e.Integration.Name)
                        .SetProperty(i => i.IsEnabled, e.Integration.IsEnabled));

                ctx.Db.IntegrationEvents.Add(new IntegrationEventEntity
                {
                    IntegrationDiscordId = e.Integration.Id,
                    GuildDiscordId = e.Guild.Id,
                    EventType = IntegrationEventType.Updated,
                    Name = e.Integration.Name,
                    Type = e.Integration.Type,
                    IsEnabled = e.Integration.IsEnabled,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson,
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, IntegrationDeletedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "IntegrationDeleted", nameof(IntegrationEventHandler),
            e.Guild.Id, null, null, async ctx =>
            {
                await ctx.Db.Integrations
                    .Where(i => i.DiscordId == e.IntegrationId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(i => i.IsDeleted, true)
                        .SetProperty(i => i.DeletedAtUtc, (DateTime?)ctx.ReceivedAtUtc));

                ctx.Db.IntegrationEvents.Add(new IntegrationEventEntity
                {
                    IntegrationDiscordId = e.IntegrationId,
                    GuildDiscordId = e.Guild.Id,
                    EventType = IntegrationEventType.Deleted,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson,
                });

                await ctx.Db.SaveChangesAsync();
            });
    }
}
