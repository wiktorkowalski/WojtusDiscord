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
        string? rawJson = null;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var userService = scope.ServiceProvider.GetRequiredService<UserService>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            rawJson = await rawEventService.SerializeAndLogAsync(
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
            using var failureScope = scopeFactory.CreateScope();
            var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
            await failedEventService.RecordFailureAsync(
                "GuildMemberAdded", nameof(MemberEventHandler), ex,
                e.Guild?.Id, null, e.Member.Id, rawJson);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildMemberRemovedEventArgs e)
    {
        string? rawJson = null;
        try
        {
            var receivedAt = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            rawJson = await rawEventService.SerializeAndLogAsync(
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
                EventTimestampUtc = receivedAt,
                ReceivedAtUtc = receivedAt,
                RawEventJson = rawJson
            };

            db.MemberEvents.Add(memberEvent);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling member removed for UserId={UserId} GuildId={GuildId}", e.Member.Id, e.Guild.Id);
            using var failureScope = scopeFactory.CreateScope();
            var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
            await failedEventService.RecordFailureAsync(
                "GuildMemberRemoved", nameof(MemberEventHandler), ex,
                e.Guild?.Id, null, e.Member.Id, rawJson);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildMemberUpdatedEventArgs e)
    {
        string? rawJson = null;
        try
        {
            var receivedAt = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var userService = scope.ServiceProvider.GetRequiredService<UserService>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            rawJson = await rawEventService.SerializeAndLogAsync(
                e, "GuildMemberUpdated", e.Guild.Id, null, e.Member.Id);

            await userService.UpsertMemberAsync(e.Member);

            var oldRoleIds = e.RolesBefore?.Select(r => r.Id).ToHashSet() ?? new HashSet<ulong>();
            var newRoleIds = e.RolesAfter?.Select(r => r.Id).ToHashSet() ?? new HashSet<ulong>();

            var rolesAdded = newRoleIds.Except(oldRoleIds).ToList();
            var rolesRemoved = oldRoleIds.Except(newRoleIds).ToList();

            var nicknameChanged = e.NicknameBefore != e.NicknameAfter;
            var avatarHashChanged = e.GuildAvatarHashBefore != e.GuildAvatarHashAfter;
            var pendingChanged = e.PendingBefore != e.PendingAfter;
            var timeoutChanged = e.CommunicationDisabledUntilBefore != e.CommunicationDisabledUntilAfter;

            var premiumBefore = e.MemberBefore?.PremiumSince;
            var premiumAfter = e.Member.PremiumSince;
            var premiumChanged = premiumBefore != premiumAfter;

            bool? mutedBefore = e.MemberBefore?.IsMuted;
            bool mutedAfter = e.Member.IsMuted;
            var mutedChanged = mutedBefore != mutedAfter;

            bool? deafenedBefore = e.MemberBefore?.IsDeafened;
            bool deafenedAfter = e.Member.IsDeafened;
            var deafenedChanged = deafenedBefore != deafenedAfter;

            if (nicknameChanged || rolesAdded.Any() || rolesRemoved.Any() || timeoutChanged
                || avatarHashChanged || pendingChanged || premiumChanged || mutedChanged || deafenedChanged)
            {
                var memberEvent = new MemberEventEntity
                {
                    UserDiscordId = e.Member.Id,
                    GuildDiscordId = e.Guild.Id,
                    EventType = MemberEventType.Updated,
                    NicknameBefore = e.NicknameBefore,
                    NicknameAfter = e.NicknameAfter,
                    RolesAddedJson = rolesAdded.Any()
                        ? JsonSerializer.Serialize(rolesAdded)
                        : null,
                    RolesRemovedJson = rolesRemoved.Any()
                        ? JsonSerializer.Serialize(rolesRemoved)
                        : null,
                    TimeoutUntilUtc = e.CommunicationDisabledUntilAfter?.UtcDateTime,
                    PremiumSinceBefore = premiumBefore?.UtcDateTime,
                    PremiumSinceAfter = premiumAfter?.UtcDateTime,
                    GuildAvatarHashBefore = e.GuildAvatarHashBefore,
                    GuildAvatarHashAfter = e.GuildAvatarHashAfter,
                    IsPendingBefore = e.PendingBefore,
                    IsPendingAfter = e.PendingAfter,
                    IsMutedBefore = mutedBefore,
                    IsMutedAfter = mutedAfter,
                    IsDeafenedBefore = deafenedBefore,
                    IsDeafenedAfter = deafenedAfter,
                    EventTimestampUtc = receivedAt,
                    ReceivedAtUtc = receivedAt,
                    RawEventJson = rawJson
                };

                db.MemberEvents.Add(memberEvent);
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling member updated for UserId={UserId} GuildId={GuildId}", e.Member.Id, e.Guild.Id);
            using var failureScope = scopeFactory.CreateScope();
            var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
            await failedEventService.RecordFailureAsync(
                "GuildMemberUpdated", nameof(MemberEventHandler), ex,
                e.Guild?.Id, null, e.Member.Id, rawJson);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildBanAddedEventArgs e)
    {
        string? rawJson = null;
        try
        {
            var receivedAt = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var userService = scope.ServiceProvider.GetRequiredService<UserService>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            rawJson = await rawEventService.SerializeAndLogAsync(
                e, "GuildBanAddedMember", e.Guild.Id, null, e.Member.Id);

            await userService.UpsertUserAsync(e.Member);

            var memberEvent = new MemberEventEntity
            {
                UserDiscordId = e.Member.Id,
                GuildDiscordId = e.Guild.Id,
                EventType = MemberEventType.Banned,
                EventTimestampUtc = receivedAt,
                ReceivedAtUtc = receivedAt,
                RawEventJson = rawJson
            };

            db.MemberEvents.Add(memberEvent);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling ban added for UserId={UserId} GuildId={GuildId}", e.Member.Id, e.Guild.Id);
            using var failureScope = scopeFactory.CreateScope();
            var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
            await failedEventService.RecordFailureAsync(
                "GuildBanAddedMember", nameof(MemberEventHandler), ex,
                e.Guild?.Id, null, e.Member.Id, rawJson);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildBanRemovedEventArgs e)
    {
        string? rawJson = null;
        try
        {
            var receivedAt = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var userService = scope.ServiceProvider.GetRequiredService<UserService>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            rawJson = await rawEventService.SerializeAndLogAsync(
                e, "GuildBanRemovedMember", e.Guild.Id, null, e.Member.Id);

            await userService.UpsertUserAsync(e.Member);

            var memberEvent = new MemberEventEntity
            {
                UserDiscordId = e.Member.Id,
                GuildDiscordId = e.Guild.Id,
                EventType = MemberEventType.Unbanned,
                EventTimestampUtc = receivedAt,
                ReceivedAtUtc = receivedAt,
                RawEventJson = rawJson
            };

            db.MemberEvents.Add(memberEvent);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling ban removed for UserId={UserId} GuildId={GuildId}", e.Member.Id, e.Guild.Id);
            using var failureScope = scopeFactory.CreateScope();
            var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
            await failedEventService.RecordFailureAsync(
                "GuildBanRemovedMember", nameof(MemberEventHandler), ex,
                e.Guild?.Id, null, e.Member.Id, rawJson);
        }
    }
}
