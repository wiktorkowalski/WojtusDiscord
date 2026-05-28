using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.EventHandlers;

public sealed class RoleEventHandler(EventPipeline pipeline) :
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
                var guildGuid = await guildUpsert.UpsertGuildAsync(e.Guild);

                RoleEventEntity NewRoleEvent() => new()
                {
                    RoleDiscordId = e.Role.Id,
                    GuildDiscordId = e.Guild.Id,
                    EventType = RoleEventType.Created,
                    NameAfter = e.Role.Name,
                    ColorAfter = e.Role.Color.Value,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                };

                var roleEntity = await ctx.Db.Roles
                    .FirstOrDefaultAsync(r => r.DiscordId == e.Role.Id);

                if (roleEntity == null)
                {
                    roleEntity = new RoleEntity
                    {
                        DiscordId = e.Role.Id,
                        GuildId = guildGuid
                    };
                    ctx.Db.Roles.Add(roleEntity);
                }

                UpdateRoleEntity(roleEntity, e.Role);
                ctx.Db.RoleEvents.Add(NewRoleEvent());

                try
                {
                    await ctx.Db.SaveChangesAsync();
                }
                catch (DbUpdateException dbEx) when (dbEx.InnerException is Npgsql.PostgresException { SqlState: "23505" })
                {
                    // Concurrent insert won the race. Re-fetch, update, re-add the event.
                    ctx.Db.ChangeTracker.Clear();
                    var existing = await ctx.Db.Roles.FirstOrDefaultAsync(r => r.DiscordId == e.Role.Id);
                    if (existing != null)
                    {
                        UpdateRoleEntity(existing, e.Role);
                    }
                    ctx.Db.RoleEvents.Add(NewRoleEvent());
                    await ctx.Db.SaveChangesAsync();
                }
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildRoleUpdatedEventArgs e)
    {
        await pipeline.Execute(e, "GuildRoleUpdated", nameof(RoleEventHandler),
            e.Guild.Id, null, null, async ctx =>
            {
                var roleEntity = await ctx.Db.Roles
                    .FirstOrDefaultAsync(r => r.DiscordId == e.RoleAfter.Id);

                if (roleEntity != null)
                {
                    UpdateRoleEntity(roleEntity, e.RoleAfter);
                }

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

                if (roleEntity != null)
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
