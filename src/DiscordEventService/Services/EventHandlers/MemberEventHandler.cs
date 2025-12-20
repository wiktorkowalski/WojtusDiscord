using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.EventArgs;
using System.Text.Json;

namespace DiscordEventService.Services.EventHandlers;

public class MemberEventHandler(IServiceScopeFactory scopeFactory, ILogger<MemberEventHandler> logger) :
    IEventHandler<GuildMemberAddedEventArgs>,
    IEventHandler<GuildMemberRemovedEventArgs>,
    IEventHandler<GuildMemberUpdatedEventArgs>,
    IEventHandler<GuildBanAddedEventArgs>,
    IEventHandler<GuildBanRemovedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, GuildMemberAddedEventArgs e)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var userService = scope.ServiceProvider.GetRequiredService<UserService>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "GuildMemberAdded", e.Guild.Id, null, e.Member.Id);

            await userService.UpsertMemberAsync(e.Member);

            var memberEvent = new MemberEventEntity
            {
                UserDiscordId = e.Member.Id,
                GuildDiscordId = e.Guild.Id,
                EventType = MemberEventType.Joined,
                NicknameAfter = e.Member.Nickname,
                RolesAddedJson = e.Member.Roles.Any()
                    ? JsonSerializer.Serialize(e.Member.Roles.Select(r => r.Id))
                    : null,
                EventTimestampUtc = e.Member.JoinedAt.UtcDateTime,
                ReceivedAtUtc = DateTime.UtcNow,
                RawEventJson = rawJson
            };

            db.MemberEvents.Add(memberEvent);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling member added for UserId={UserId} GuildId={GuildId}", e.Member.Id, e.Guild.Id);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildMemberRemovedEventArgs e)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "GuildMemberRemoved", e.Guild.Id, null, e.Member.Id);

            var memberEvent = new MemberEventEntity
            {
                UserDiscordId = e.Member.Id,
                GuildDiscordId = e.Guild.Id,
                EventType = MemberEventType.Left,
                NicknameBefore = e.Member.Nickname,
                RolesRemovedJson = e.Member.Roles.Any()
                    ? JsonSerializer.Serialize(e.Member.Roles.Select(r => r.Id))
                    : null,
                EventTimestampUtc = DateTime.UtcNow,
                ReceivedAtUtc = DateTime.UtcNow,
                RawEventJson = rawJson
            };

            db.MemberEvents.Add(memberEvent);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling member removed for UserId={UserId} GuildId={GuildId}", e.Member.Id, e.Guild.Id);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildMemberUpdatedEventArgs e)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var userService = scope.ServiceProvider.GetRequiredService<UserService>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "GuildMemberUpdated", e.Guild.Id, null, e.Member.Id);

            await userService.UpsertMemberAsync(e.Member);

            var oldRoleIds = e.RolesBefore?.Select(r => r.Id).ToHashSet() ?? new HashSet<ulong>();
            var newRoleIds = e.RolesAfter?.Select(r => r.Id).ToHashSet() ?? new HashSet<ulong>();

            var rolesAdded = newRoleIds.Except(oldRoleIds).ToList();
            var rolesRemoved = oldRoleIds.Except(newRoleIds).ToList();

            var nicknameBefore = e.NicknameBefore;
            var nicknameAfter = e.NicknameAfter;
            var nicknameChanged = nicknameBefore != nicknameAfter;

            var timeoutChanged = e.PendingBefore != e.PendingAfter || e.Member.CommunicationDisabledUntil != null;

            if (nicknameChanged || rolesAdded.Any() || rolesRemoved.Any() || timeoutChanged)
            {
                var memberEvent = new MemberEventEntity
                {
                    UserDiscordId = e.Member.Id,
                    GuildDiscordId = e.Guild.Id,
                    EventType = MemberEventType.Updated,
                    NicknameBefore = nicknameBefore,
                    NicknameAfter = nicknameAfter,
                    RolesAddedJson = rolesAdded.Any()
                        ? JsonSerializer.Serialize(rolesAdded)
                        : null,
                    RolesRemovedJson = rolesRemoved.Any()
                        ? JsonSerializer.Serialize(rolesRemoved)
                        : null,
                    TimeoutUntilUtc = e.Member.CommunicationDisabledUntil?.UtcDateTime,
                    EventTimestampUtc = DateTime.UtcNow,
                    ReceivedAtUtc = DateTime.UtcNow,
                    RawEventJson = rawJson
                };

                db.MemberEvents.Add(memberEvent);
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling member updated for UserId={UserId} GuildId={GuildId}", e.Member.Id, e.Guild.Id);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildBanAddedEventArgs e)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var userService = scope.ServiceProvider.GetRequiredService<UserService>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "GuildBanAddedMember", e.Guild.Id, null, e.Member.Id);

            await userService.UpsertUserAsync(e.Member);

            var memberEvent = new MemberEventEntity
            {
                UserDiscordId = e.Member.Id,
                GuildDiscordId = e.Guild.Id,
                EventType = MemberEventType.Banned,
                EventTimestampUtc = DateTime.UtcNow,
                ReceivedAtUtc = DateTime.UtcNow,
                RawEventJson = rawJson
            };

            db.MemberEvents.Add(memberEvent);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling ban added for UserId={UserId} GuildId={GuildId}", e.Member.Id, e.Guild.Id);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildBanRemovedEventArgs e)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var userService = scope.ServiceProvider.GetRequiredService<UserService>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "GuildBanRemovedMember", e.Guild.Id, null, e.Member.Id);

            await userService.UpsertUserAsync(e.Member);

            var memberEvent = new MemberEventEntity
            {
                UserDiscordId = e.Member.Id,
                GuildDiscordId = e.Guild.Id,
                EventType = MemberEventType.Unbanned,
                EventTimestampUtc = DateTime.UtcNow,
                ReceivedAtUtc = DateTime.UtcNow,
                RawEventJson = rawJson
            };

            db.MemberEvents.Add(memberEvent);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling ban removed for UserId={UserId} GuildId={GuildId}", e.Member.Id, e.Guild.Id);
        }
    }
}
