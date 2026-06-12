using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.Entities;
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
        await pipeline.Execute(e, "GuildRoleCreated", nameof(RoleEventHandler),
            e.Guild.Id, null, null, async ctx =>
            {
                var guildUpsert = ctx.Services.GetRequiredService<GuildUpsertService>();
                var guildGuid = (await guildUpsert.UpsertGuildAsync(e.Guild)).Value;

                var permissions = long.TryParse(e.Role.Permissions.ToString(), out var perm) ? perm : 0;

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

                    await ctx.Db.Roles.UpsertAsync(
                        r => r.DiscordId == e.Role.Id,
                        s => s
                            .SetProperty(r => r.Name, e.Role.Name)
                            .SetProperty(r => r.Color, e.Role.Color.Value)
                            .SetProperty(r => r.IsHoisted, e.Role.IsHoisted)
                            .SetProperty(r => r.Position, e.Role.Position)
                            .SetProperty(r => r.Permissions, permissions)
                            .SetProperty(r => r.IsManaged, e.Role.IsManaged)
                            .SetProperty(r => r.IsMentionable, e.Role.IsMentionable)
                            .SetProperty(r => r.IsDeleted, false)
                            .SetProperty(r => r.DeletedAtUtc, (DateTime?)null),
                        () => new RoleEntity
                        {
                            DiscordId = e.Role.Id,
                            GuildId = guildGuid,
                            Name = e.Role.Name,
                            Color = e.Role.Color.Value,
                            IsHoisted = e.Role.IsHoisted,
                            Position = e.Role.Position,
                            Permissions = permissions,
                            IsManaged = e.Role.IsManaged,
                            IsMentionable = e.Role.IsMentionable,
                            IsDeleted = false
                        },
                        r => r.Id);

                    ctx.Db.RoleEvents.Add(new RoleEventEntity
                    {
                        RoleDiscordId = e.Role.Id,
                        GuildDiscordId = e.Guild.Id,
                        EventType = RoleEventType.Created,
                        NameAfter = e.Role.Name,
                        ColorAfter = e.Role.Color.Value,
                        EventTimestampUtc = ctx.ReceivedAtUtc,
                        ReceivedAtUtc = ctx.ReceivedAtUtc,
                        RawEventJson = ctx.RawJson,
                    });
                    await ctx.Db.SaveChangesAsync();
                    await tx.CommitAsync();
                });
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildRoleUpdatedEventArgs e)
    {
        await pipeline.Execute(e, "GuildRoleUpdated", nameof(RoleEventHandler),
            e.Guild.Id, null, null, async ctx =>
            {
                var roleEntity = await ctx.Db.Roles
                    .FirstOrDefaultAsync(r => r.DiscordId == e.RoleAfter.Id);

                if (roleEntity is not null)
                    UpdateRoleEntity(roleEntity, e.RoleAfter);

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

                ctx.Db.RoleEvents.Add(roleEvent);
                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildRoleDeletedEventArgs e)
    {
        await pipeline.Execute(e, "GuildRoleDeleted", nameof(RoleEventHandler),
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

    private static void UpdateRoleEntity(RoleEntity entity, DiscordRole role)
    {
        entity.Name = role.Name;
        entity.Color = role.Color.Value;
        entity.IsHoisted = role.IsHoisted;
        entity.Position = role.Position;
        entity.Permissions = long.TryParse(role.Permissions.ToString(), out var perm) ? perm : 0;
        entity.IsManaged = role.IsManaged;
        entity.IsMentionable = role.IsMentionable;
        entity.IsDeleted = false;
        entity.DeletedAtUtc = null;
    }
}
