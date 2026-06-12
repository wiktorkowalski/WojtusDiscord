using System.Text.Json;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services;

internal sealed class MemberRoleSnapshotBackfillService(
    DiscordDbContext db,
    DiscordClient discordClient,
    ILogger<MemberRoleSnapshotBackfillService> logger)
{
    public sealed record Result(
        int EventsProcessed,
        int SnapshotsCreated,
        int SnapshotsClosed,
        int CurrentRolesSeeded);

    public async Task<Result> BackfillAsync(CancellationToken ct)
    {
        var eventsProcessed = 0;
        var created = 0;
        var closed = 0;

        var memberLookup = await db.Members
            .Include(m => m.User)
            .Include(m => m.Guild)
            .ToDictionaryAsync(
                m => (m.User.DiscordId, m.Guild.DiscordId),
                m => m.Id,
                ct);

        var events = await db.MemberEvents
            .Where(e => e.EventType == MemberEventType.Updated
                && (e.RolesAddedJson != null || e.RolesRemovedJson != null))
            .OrderBy(e => e.EventTimestampUtc)
            .Select(e => new
            {
                e.Id,
                e.UserDiscordId,
                e.GuildDiscordId,
                e.RolesAddedJson,
                e.RolesRemovedJson,
                e.EventTimestampUtc
            })
            .ToListAsync(ct);

        logger.LogInformation("Backfilling role snapshots from {EventCount} member events with role changes", events.Count);

        foreach (var evt in events)
        {
            ct.ThrowIfCancellationRequested();

            if (!memberLookup.TryGetValue((evt.UserDiscordId, evt.GuildDiscordId), out var memberId))
            {
                logger.LogDebug("Skipping event {EventId}: member not found for user {UserId} in guild {GuildId}",
                    evt.Id, evt.UserDiscordId, evt.GuildDiscordId);
                continue;
            }

            var (eventCreated, eventClosed) = await ReplayRoleChangeEventAsync(
                memberId, evt.Id, evt.RolesAddedJson, evt.RolesRemovedJson, evt.EventTimestampUtc, ct);

            created += eventCreated;
            closed += eventClosed;
            eventsProcessed++;
        }

        logger.LogInformation("Event replay done: {EventCount} events, {CreatedCount} snapshots created, {ClosedCount} closed",
            eventsProcessed, created, closed);

        var seeded = await SeedCurrentRolesAsync(memberLookup, ct);

        return new Result(eventsProcessed, created, closed, seeded);
    }

    private async Task<(int Created, int Closed)> ReplayRoleChangeEventAsync(
        Guid memberId,
        Guid eventId,
        string? rolesAddedJson,
        string? rolesRemovedJson,
        DateTime eventTimestampUtc,
        CancellationToken ct)
    {
        var created = 0;
        var closed = 0;

        var rolesAdded = rolesAddedJson is not null
            ? JsonSerializer.Deserialize<List<ulong>>(rolesAddedJson) ?? []
            : [];
        var rolesRemoved = rolesRemovedJson is not null
            ? JsonSerializer.Deserialize<List<ulong>>(rolesRemovedJson) ?? []
            : [];

        var openRoles = (await db.MemberRoleSnapshots
            .Where(s => s.MemberId == memberId && s.RevokedAtUtc == null)
            .Select(s => s.RoleDiscordId)
            .ToListAsync(ct))
            .ToHashSet();

        foreach (var roleId in rolesAdded)
        {
            if (openRoles.Contains(roleId))
                continue;

            // Insert-or-ignore: the openRoles pre-check skips known duplicates; this guards
            // the rare race where a live event inserts the same open snapshot concurrently.
            var (_, inserted) = await db.MemberRoleSnapshots.GetOrInsertAsync(
                s => s.MemberId == memberId && s.RoleDiscordId == roleId && s.RevokedAtUtc == null,
                () => new MemberRoleSnapshotEntity
                {
                    MemberId = memberId,
                    RoleDiscordId = roleId,
                    GrantedAtUtc = eventTimestampUtc,
                    SourceEventId = eventId
                },
                ct);
            if (inserted) created++;
        }

        foreach (var roleId in rolesRemoved)
        {
            closed += await db.MemberRoleSnapshots
                .Where(s => s.MemberId == memberId && s.RoleDiscordId == roleId && s.RevokedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, eventTimestampUtc), ct);
        }

        return (created, closed);
    }

    private async Task<int> SeedCurrentRolesAsync(
        Dictionary<(ulong UserDiscordId, ulong GuildDiscordId), Guid> memberLookup,
        CancellationToken ct)
    {
        var seeded = 0;
        var guildIds = memberLookup.Keys.Select(k => k.GuildDiscordId).Distinct();

        foreach (var guildDiscordId in guildIds)
        {
            ct.ThrowIfCancellationRequested();
            seeded += await SeedGuildRolesAsync(guildDiscordId, memberLookup, ct);
        }

        logger.LogInformation("Current role seeding done: {SeededCount} new snapshots", seeded);
        return seeded;
    }

    private async Task<int> SeedGuildRolesAsync(
        ulong guildDiscordId,
        Dictionary<(ulong UserDiscordId, ulong GuildDiscordId), Guid> memberLookup,
        CancellationToken ct)
    {
        DSharpPlus.Entities.DiscordGuild guild;
        try
        {
            guild = await discordClient.GetGuildAsync(guildDiscordId);
        }
        catch (Exception ex) when (ex is NotFoundException or UnauthorizedException)
        {
            logger.LogWarning(ex, "Cannot fetch guild {GuildId} for role seeding — skipping", guildDiscordId);
            return 0;
        }

        var members = new List<DSharpPlus.Entities.DiscordMember>();
        await foreach (var member in guild.GetAllMembersAsync())
            members.Add(member);

        logger.LogInformation("Seeding current roles for {MemberCount} members in guild {GuildId}",
            members.Count, guildDiscordId);

        var seeded = 0;
        foreach (var member in members)
        {
            ct.ThrowIfCancellationRequested();

            if (!memberLookup.TryGetValue((member.Id, guildDiscordId), out var memberId))
                continue;

            var openRolesForMember = (await db.MemberRoleSnapshots
                .Where(s => s.MemberId == memberId && s.RevokedAtUtc == null)
                .Select(s => s.RoleDiscordId)
                .ToListAsync(ct))
                .ToHashSet();

            foreach (var role in member.Roles)
            {
                if (openRolesForMember.Contains(role.Id))
                    continue;

                // Insert-or-ignore: openRolesForMember pre-check skips known duplicates; this
                // guards the rare race where a live event inserts the same snapshot concurrently.
                var (_, inserted) = await db.MemberRoleSnapshots.GetOrInsertAsync(
                    s => s.MemberId == memberId && s.RoleDiscordId == role.Id && s.RevokedAtUtc == null,
                    () => new MemberRoleSnapshotEntity
                    {
                        MemberId = memberId,
                        RoleDiscordId = role.Id,
                        GrantedAtUtc = DateTime.UtcNow
                    },
                    ct);
                if (inserted) seeded++;
            }
        }

        return seeded;
    }
}
