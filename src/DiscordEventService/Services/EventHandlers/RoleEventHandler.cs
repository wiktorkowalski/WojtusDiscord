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
        string? rawJson = null;
        try
        {
            var receivedAt = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            rawJson = await rawEventService.SerializeAndLogAsync(
                e, "GuildRoleCreated", e.Guild.Id, null, null);

            // Look up Guild Guid
            var guild = await db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == e.Guild.Id);

            RoleEventEntity NewRoleEvent() => new()
            {
                RoleDiscordId = e.Role.Id,
                GuildDiscordId = e.Guild.Id,
                EventType = RoleEventType.Created,
                NameAfter = e.Role.Name,
                ColorAfter = e.Role.Color.Value,
                EventTimestampUtc = receivedAt,
                ReceivedAtUtc = receivedAt,
                RawEventJson = rawJson
            };

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
            db.RoleEvents.Add(NewRoleEvent());

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException dbEx) when (dbEx.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            {
                // Concurrent insert won the race. Re-fetch, update, re-add the event.
                db.ChangeTracker.Clear();
                var existing = await db.Roles.FirstOrDefaultAsync(r => r.DiscordId == e.Role.Id);
                if (existing != null)
                {
                    UpdateRoleEntity(existing, e.Role);
                }
                db.RoleEvents.Add(NewRoleEvent());
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling role created for RoleId={RoleId}", e.Role.Id);
            using var failureScope = scopeFactory.CreateScope();
            var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
            await failedEventService.RecordFailureAsync(
                "GuildRoleCreated", nameof(RoleEventHandler), ex,
                e.Guild?.Id, null, null, rawJson);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildRoleUpdatedEventArgs e)
    {
        string? rawJson = null;
        try
        {
            var receivedAt = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            rawJson = await rawEventService.SerializeAndLogAsync(
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
                EventTimestampUtc = receivedAt,
                ReceivedAtUtc = receivedAt,
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
            using var failureScope = scopeFactory.CreateScope();
            var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
            await failedEventService.RecordFailureAsync(
                "GuildRoleUpdated", nameof(RoleEventHandler), ex,
                e.Guild?.Id, null, null, rawJson);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildRoleDeletedEventArgs e)
    {
        string? rawJson = null;
        try
        {
            var receivedAt = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            rawJson = await rawEventService.SerializeAndLogAsync(
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
                EventTimestampUtc = receivedAt,
                ReceivedAtUtc = receivedAt,
                RawEventJson = rawJson
            };

            db.RoleEvents.Add(roleEvent);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling role deleted for RoleId={RoleId}", e.Role.Id);
            using var failureScope = scopeFactory.CreateScope();
            var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
            await failedEventService.RecordFailureAsync(
                "GuildRoleDeleted", nameof(RoleEventHandler), ex,
                e.Guild?.Id, null, null, rawJson);
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
