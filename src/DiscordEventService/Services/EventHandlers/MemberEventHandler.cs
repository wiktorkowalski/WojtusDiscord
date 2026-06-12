using System.Text.Json;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.EventHandlers;

internal sealed class MemberEventHandler(EventPipeline pipeline) :
    IEventHandler<GuildMemberAddedEventArgs>,
    IEventHandler<GuildMemberRemovedEventArgs>,
    IEventHandler<GuildMemberUpdatedEventArgs>,
    IEventHandler<GuildBanAddedEventArgs>,
    IEventHandler<GuildBanRemovedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, GuildMemberAddedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "GuildMemberAdded", nameof(MemberEventHandler),
            e.Guild.Id, null, e.Member.Id, async ctx =>
            {
                var userService = ctx.Services.GetRequiredService<UserService>();
                await userService.UpsertMemberAsync(e.Member);

                ctx.Db.MemberEvents.Add(new MemberEventEntity
                {
                    UserDiscordId = e.Member.Id,
                    GuildDiscordId = e.Guild.Id,
                    EventType = MemberEventType.Joined,
                    NicknameAfter = e.Member.Nickname,
                    RolesAddedJson = e.Member.Roles.Any()
                        ? JsonSerializer.Serialize(e.Member.Roles.Select(r => r.Id))
                        : null,
                    EventTimestampUtc = e.Member.JoinedAt.UtcDateTime,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson,
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildMemberRemovedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "GuildMemberRemoved", nameof(MemberEventHandler),
            e.Guild.Id, null, e.Member.Id, async ctx =>
            {
                ctx.Db.MemberEvents.Add(new MemberEventEntity
                {
                    UserDiscordId = e.Member.Id,
                    GuildDiscordId = e.Guild.Id,
                    EventType = MemberEventType.Left,
                    NicknameBefore = e.Member.Nickname,
                    RolesRemovedJson = e.Member.Roles.Any()
                        ? JsonSerializer.Serialize(e.Member.Roles.Select(r => r.Id))
                        : null,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson,
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildMemberUpdatedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "GuildMemberUpdated", nameof(MemberEventHandler),
            e.Guild.Id, null, e.Member.Id, async ctx =>
            {
                var userService = ctx.Services.GetRequiredService<UserService>();
                await userService.UpsertMemberAsync(e.Member);

                var oldRoleIds = e.RolesBefore?.Select(r => r.Id).ToHashSet() ?? [];
                var newRoleIds = e.RolesAfter?.Select(r => r.Id).ToHashSet() ?? [];

                var rolesAdded = newRoleIds.Except(oldRoleIds).ToList();
                var rolesRemoved = oldRoleIds.Except(newRoleIds).ToList();

                var nicknameChanged = e.NicknameBefore != e.NicknameAfter;
                var avatarHashChanged = e.GuildAvatarHashBefore != e.GuildAvatarHashAfter;
                var pendingChanged = e.PendingBefore != e.PendingAfter;
                var timeoutChanged = e.CommunicationDisabledUntilBefore != e.CommunicationDisabledUntilAfter;

                // Member-object deltas are only meaningful when MemberBefore is cached.
                // Without it we can't distinguish "field changed" from "field newly known",
                // so treat unknown-before as unchanged to avoid spurious filter triggers.
                // (DSharpPlus annotates MemberBefore as non-nullable, but defensively check.)
                var memberBefore = e.MemberBefore;
                var hasBefore = memberBefore is not null;

                var premiumBefore = memberBefore?.PremiumSince;
                var premiumAfter = e.Member.PremiumSince;
                var premiumChanged = hasBefore && premiumBefore != premiumAfter;

                var mutedBefore = memberBefore?.IsMuted;
                var mutedAfter = e.Member.IsMuted;
                var mutedChanged = mutedBefore.HasValue && mutedBefore.Value != mutedAfter;

                var deafenedBefore = memberBefore?.IsDeafened;
                var deafenedAfter = e.Member.IsDeafened;
                var deafenedChanged = deafenedBefore.HasValue && deafenedBefore.Value != deafenedAfter;

                if (nicknameChanged || rolesAdded.Any() || rolesRemoved.Any() || timeoutChanged
                    || avatarHashChanged || pendingChanged || premiumChanged || mutedChanged || deafenedChanged)
                {
                    var strategy = ctx.Db.Database.CreateExecutionStrategy();
                    await strategy.ExecuteAsync(async () =>
                    {
                        ctx.Db.ChangeTracker.Clear();
                        await using var tx = await ctx.Db.Database.BeginTransactionAsync();

                        var memberEvent = BuildMemberUpdatedEvent(
                            e, ctx, rolesAdded, rolesRemoved,
                            premiumBefore, premiumAfter, mutedBefore, mutedAfter, deafenedBefore, deafenedAfter);

                        ctx.Db.MemberEvents.Add(memberEvent);
                        await ctx.Db.SaveChangesAsync();

                        if ((rolesAdded.Any() || rolesRemoved.Any())
                            && e.RolesBefore is not null && e.RolesAfter is not null)
                        {
                            await MaintainRoleSnapshotsAsync(ctx.Db, ctx.Logger, e.Member.Id, e.Guild.Id,
                                rolesAdded, rolesRemoved, ctx.ReceivedAtUtc, memberEvent.Id);
                        }

                        await tx.CommitAsync();
                    });
                }
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildBanAddedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "GuildBanAddedMember", nameof(MemberEventHandler),
            e.Guild.Id, null, e.Member.Id, async ctx =>
            {
                var userService = ctx.Services.GetRequiredService<UserService>();
                await userService.UpsertUserAsync(e.Member);

                ctx.Db.MemberEvents.Add(new MemberEventEntity
                {
                    UserDiscordId = e.Member.Id,
                    GuildDiscordId = e.Guild.Id,
                    EventType = MemberEventType.Banned,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson,
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildBanRemovedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "GuildBanRemovedMember", nameof(MemberEventHandler),
            e.Guild.Id, null, e.Member.Id, async ctx =>
            {
                var userService = ctx.Services.GetRequiredService<UserService>();
                await userService.UpsertUserAsync(e.Member);

                ctx.Db.MemberEvents.Add(new MemberEventEntity
                {
                    UserDiscordId = e.Member.Id,
                    GuildDiscordId = e.Guild.Id,
                    EventType = MemberEventType.Unbanned,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson,
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    private static async Task MaintainRoleSnapshotsAsync(
        DiscordDbContext db, ILogger logger, ulong userDiscordId, ulong guildDiscordId,
        List<ulong> rolesAdded, List<ulong> rolesRemoved,
        DateTime eventTime, Guid sourceEventId)
    {
        var member = await db.Members
            .Where(m => m.User.DiscordId == userDiscordId && m.Guild.DiscordId == guildDiscordId)
            .Select(m => new { m.Id })
            .FirstOrDefaultAsync();

        if (member is null)
        {
            logger.LogWarning("Cannot maintain role snapshots: member not found for user {UserDiscordId} in guild {GuildDiscordId}",
                userDiscordId, guildDiscordId);
            return;
        }

        foreach (var roleId in rolesAdded)
        {
            // Insert-or-ignore: an open snapshot for this (member, role) may already exist
            // (filtered unique index). On conflict the existing snapshot is left untouched.
            await db.MemberRoleSnapshots.GetOrInsertAsync(
                s => s.MemberId == member.Id && s.RoleDiscordId == roleId && s.RevokedAtUtc == null,
                () => new MemberRoleSnapshotEntity
                {
                    MemberId = member.Id,
                    RoleDiscordId = roleId,
                    GrantedAtUtc = eventTime,
                    SourceEventId = sourceEventId,
                });
        }

        foreach (var roleId in rolesRemoved)
        {
            var closed = await db.MemberRoleSnapshots
                .Where(s => s.MemberId == member.Id && s.RoleDiscordId == roleId && s.RevokedAtUtc == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.RevokedAtUtc, eventTime));

            if (closed == 0)
            {
                logger.LogDebug("No open role snapshot to close for member {MemberGuid} and role {RoleDiscordId}",
                    member.Id, roleId);
            }
        }
    }

    private static MemberEventEntity BuildMemberUpdatedEvent(
        GuildMemberUpdatedEventArgs e,
        EventContext ctx,
        List<ulong> rolesAdded,
        List<ulong> rolesRemoved,
        DateTimeOffset? premiumBefore,
        DateTimeOffset? premiumAfter,
        bool? mutedBefore,
        bool mutedAfter,
        bool? deafenedBefore,
        bool deafenedAfter) => new MemberEventEntity
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
            PremiumSinceBeforeUtc = premiumBefore?.UtcDateTime,
            PremiumSinceAfterUtc = premiumAfter?.UtcDateTime,
            GuildAvatarHashBefore = e.GuildAvatarHashBefore,
            GuildAvatarHashAfter = e.GuildAvatarHashAfter,
            IsPendingBefore = e.PendingBefore,
            IsPendingAfter = e.PendingAfter,
            IsMutedBefore = mutedBefore,
            IsMutedAfter = mutedAfter,
            IsDeafenedBefore = deafenedBefore,
            IsDeafenedAfter = deafenedAfter,
            EventTimestampUtc = ctx.ReceivedAtUtc,
            ReceivedAtUtc = ctx.ReceivedAtUtc,
            RawEventJson = ctx.RawJson,
        };
}
