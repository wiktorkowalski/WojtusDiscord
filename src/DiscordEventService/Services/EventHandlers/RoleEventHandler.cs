using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.EventHandlers;

public class RoleEventHandler(IServiceScopeFactory scopeFactory, ILogger<RoleEventHandler> logger) :
    IEventHandler<GuildRoleCreatedEventArgs>,
    IEventHandler<GuildRoleUpdatedEventArgs>,
    IEventHandler<GuildRoleDeletedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, GuildRoleCreatedEventArgs e)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "GuildRoleCreated", e.Guild.Id, null, null);

            // Look up Guild Guid
            var guild = await db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == e.Guild.Id);

            var roleEntity = await db.Roles
                .FirstOrDefaultAsync(r => r.DiscordId == e.Role.Id);

            if (roleEntity == null)
            {
                roleEntity = new RoleEntity
                {
                    DiscordId = e.Role.Id,
                    GuildId = guild?.Id ?? Guid.Empty
                };
                db.Roles.Add(roleEntity);
            }

            UpdateRoleEntity(roleEntity, e.Role);

            var roleEvent = new RoleEventEntity
            {
                RoleDiscordId = e.Role.Id,
                GuildDiscordId = e.Guild.Id,
                EventType = RoleEventType.Created,
                NameAfter = e.Role.Name,
                ColorAfter = e.Role.Color.Value,
                EventTimestampUtc = DateTime.UtcNow,
                ReceivedAtUtc = DateTime.UtcNow,
                RawEventJson = rawJson
            };

            db.RoleEvents.Add(roleEvent);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling role created for RoleId={RoleId}", e.Role.Id);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildRoleUpdatedEventArgs e)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "GuildRoleUpdated", e.Guild.Id, null, null);

            var roleEntity = await db.Roles
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
                EventTimestampUtc = DateTime.UtcNow,
                ReceivedAtUtc = DateTime.UtcNow,
                RawEventJson = rawJson
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

            db.RoleEvents.Add(roleEvent);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling role updated for RoleId={RoleId}", e.RoleAfter.Id);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildRoleDeletedEventArgs e)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "GuildRoleDeleted", e.Guild.Id, null, null);

            var roleEntity = await db.Roles
                .FirstOrDefaultAsync(r => r.DiscordId == e.Role.Id);

            if (roleEntity != null)
            {
                roleEntity.IsDeleted = true;
            }

            var roleEvent = new RoleEventEntity
            {
                RoleDiscordId = e.Role.Id,
                GuildDiscordId = e.Guild.Id,
                EventType = RoleEventType.Deleted,
                NameBefore = e.Role.Name,
                ColorBefore = e.Role.Color.Value,
                EventTimestampUtc = DateTime.UtcNow,
                ReceivedAtUtc = DateTime.UtcNow,
                RawEventJson = rawJson
            };

            db.RoleEvents.Add(roleEvent);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling role deleted for RoleId={RoleId}", e.Role.Id);
        }
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
    }
}
