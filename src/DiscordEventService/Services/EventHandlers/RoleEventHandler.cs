using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.EventHandlers;

internal sealed class RoleEventHandler(EventPipeline pipeline) :
    IEventHandler<GuildRoleCreatedEventArgs>,
    IEventHandler<GuildRoleUpdatedEventArgs>,
    IEventHandler<GuildRoleDeletedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, GuildRoleCreatedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "GuildRoleCreated", nameof(RoleEventHandler),
            e.Guild.Id, null, null, async ctx =>
            {
                var roleUpsert = ctx.Services.GetRequiredService<RoleUpsertService>();
                // Resolve the required guild FK via the shared resolver: on a miss it logs and
                // records a FailedEvent (replayable trace) instead of flowing Guid.Empty into the
                // roles row (#292). The RoleEvent timeline row is FK-free and still lands below.
                var fks = await ctx.Services.GetRequiredService<FkResolver>()
                    .ResolveAsync(ctx, e.Guild, $"GuildRoleCreated RoleId={e.Role.Id}");

                // Upsert the role (handles the 23505 race internally) before staging the event,
                // then commit role + event together. ExecutionStrategy is required because
                // EnableRetryOnFailure is configured on the DbContext.
                var strategy = ctx.Db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    // Reset the tracker so an EnableRetryOnFailure retry doesn't re-stage entities
                    // left Added by a rolled-back attempt.
                    ctx.Db.ChangeTracker.Clear();
                    await using var tx = await ctx.Db.Database.BeginTransactionAsync();

                    if (fks.Success)
                        await roleUpsert.UpsertRoleAsync(e.Role, fks.GuildId);

                    ctx.Db.RoleEvents.Add(BuildRoleCreatedEvent(e, ctx));
                    await ctx.Db.SaveChangesAsync();
                    await tx.CommitAsync();
                });
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildRoleUpdatedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "GuildRoleUpdated", nameof(RoleEventHandler),
            e.Guild.Id, null, null, async ctx =>
            {
                var roleUpsert = ctx.Services.GetRequiredService<RoleUpsertService>();
                // Shared resolver: a guild miss must not flow Guid.Empty into a fresh roles row (#292).
                var fks = await ctx.Services.GetRequiredService<FkResolver>()
                    .ResolveAsync(ctx, e.Guild, $"GuildRoleUpdated RoleId={e.RoleAfter.Id}");

                var roleEvent = new RoleEventEntity
                {
                    RoleDiscordId = e.RoleAfter.Id,
                    GuildDiscordId = e.Guild.Id,
                    EventType = RoleEventType.Updated,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                };

                if (e.RoleBefore.Name != e.RoleAfter.Name)
                {
                    roleEvent.NameBefore = e.RoleBefore.Name;
                    roleEvent.NameAfter = e.RoleAfter.Name;
                }

                if (e.RoleBefore.Color.Value != e.RoleAfter.Color.Value)
                {
                    roleEvent.ColorBefore = e.RoleBefore.Color.Value;
                    roleEvent.ColorAfter = e.RoleAfter.Color.Value;
                }

                // Role row + event row must commit together. ExecutionStrategy is required
                // because EnableRetryOnFailure is configured on the DbContext.
                var strategy = ctx.Db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    // Reset the tracker so an EnableRetryOnFailure retry doesn't re-stage entities
                    // left Added by a rolled-back attempt.
                    ctx.Db.ChangeTracker.Clear();
                    await using var tx = await ctx.Db.Database.BeginTransactionAsync();

                    // Through the shared seam so an update for a role we never saw creates
                    // the row instead of being dropped.
                    if (fks.Success)
                        await roleUpsert.UpsertRoleAsync(e.RoleAfter, fks.GuildId);

                    ctx.Db.RoleEvents.Add(roleEvent);
                    await ctx.Db.SaveChangesAsync();
                    await tx.CommitAsync();
                });
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildRoleDeletedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "GuildRoleDeleted", nameof(RoleEventHandler),
            e.Guild.Id, null, null, async ctx =>
            {
                var roleEntity = await ctx.Db.Roles
                    .FirstOrDefaultAsync(r => r.DiscordId == e.Role.Id);

                if (roleEntity is not null)
                {
                    roleEntity.IsDeleted = true;
                    roleEntity.DeletedAtUtc = ctx.ReceivedAtUtc;
                }

                ctx.Db.RoleEvents.Add(new RoleEventEntity
                {
                    RoleDiscordId = e.Role.Id,
                    GuildDiscordId = e.Guild.Id,
                    EventType = RoleEventType.Deleted,
                    NameBefore = e.Role.Name,
                    ColorBefore = e.Role.Color.Value,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    private static RoleEventEntity BuildRoleCreatedEvent(GuildRoleCreatedEventArgs e, EventContext ctx) => new RoleEventEntity
    {
        RoleDiscordId = e.Role.Id,
        GuildDiscordId = e.Guild.Id,
        EventType = RoleEventType.Created,
        NameAfter = e.Role.Name,
        ColorAfter = e.Role.Color.Value,
        EventTimestampUtc = ctx.ReceivedAtUtc,
        ReceivedAtUtc = ctx.ReceivedAtUtc,
        RawEventJson = ctx.RawJson,
    };
}
