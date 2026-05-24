using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.Exceptions;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DiscordEventService.Services;

public class MemberRoleSnapshotBackfillService(
    DiscordDbContext db,
    DiscordClient discordClient,
    ILogger<MemberRoleSnapshotBackfillService> logger)
{
    public record Result(
        int EventsProcessed,
        int SnapshotsCreated,
        int SnapshotsClosed,
        int CurrentRolesSeeded);

    public async Task<Result> BackfillAsync(CancellationToken ct)
    {
        int eventsProcessed = 0, created = 0, closed = 0;

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

        logger.LogInformation("Backfilling role snapshots from {Count} member events with role changes", events.Count);

        foreach (var evt in events)
        {
            ct.ThrowIfCancellationRequested();

            if (!memberLookup.TryGetValue((evt.UserDiscordId, evt.GuildDiscordId), out var memberId))
            {
                logger.LogDebug("Skipping event {EventId}: member not found for User={User} Guild={Guild}",
                    evt.Id, evt.UserDiscordId, evt.GuildDiscordId);
                continue;
            }

            var rolesAdded = evt.RolesAddedJson != null
                ? JsonSerializer.Deserialize<List<ulong>>(evt.RolesAddedJson) ?? []
                : [];
            var rolesRemoved = evt.RolesRemovedJson != null
                ? JsonSerializer.Deserialize<List<ulong>>(evt.RolesRemovedJson) ?? []
                : [];

            foreach (var roleId in rolesAdded)
            {
                try
                {
                    db.MemberRoleSnapshots.Add(new MemberRoleSnapshotEntity
                    {
                        MemberId = memberId,
                        RoleDiscordId = roleId,
                        GrantedAtUtc = evt.EventTimestampUtc,
                        SourceEventId = evt.Id
                    });
                    await db.SaveChangesAsync(ct);
                    created++;
                }
                catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
                {
                    db.ChangeTracker.Clear();
                }
            }

            foreach (var roleId in rolesRemoved)
            {
                var closedCount = await db.MemberRoleSnapshots
                    .Where(s => s.MemberId == memberId && s.RoleDiscordId == roleId && s.RevokedAtUtc == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, evt.EventTimestampUtc), ct);
                closed += closedCount;
            }

            eventsProcessed++;
        }

        logger.LogInformation("Event replay done: {Events} events, {Created} snapshots created, {Closed} closed",
            eventsProcessed, created, closed);

        var seeded = await SeedCurrentRolesAsync(memberLookup, ct);

        return new Result(eventsProcessed, created, closed, seeded);
    }

    private async Task<int> SeedCurrentRolesAsync(
        Dictionary<(ulong UserDiscordId, ulong GuildDiscordId), Guid> memberLookup,
        CancellationToken ct)
    {
        int seeded = 0;
        var guildIds = memberLookup.Keys.Select(k => k.GuildDiscordId).Distinct();

        foreach (var guildDiscordId in guildIds)
        {
            ct.ThrowIfCancellationRequested();

            DSharpPlus.Entities.DiscordGuild guild;
            try
            {
                guild = await discordClient.GetGuildAsync(guildDiscordId);
            }
            catch (Exception ex) when (ex is NotFoundException or UnauthorizedException)
            {
                logger.LogWarning(ex, "Cannot fetch guild {Guild} for role seeding — skipping", guildDiscordId);
                continue;
            }

            var members = new List<DSharpPlus.Entities.DiscordMember>();
            await foreach (var member in guild.GetAllMembersAsync())
            {
                members.Add(member);
            }

            logger.LogInformation("Seeding current roles for {Count} members in guild {Guild}",
                members.Count, guildDiscordId);

            foreach (var member in members)
            {
                ct.ThrowIfCancellationRequested();

                if (!memberLookup.TryGetValue((member.Id, guildDiscordId), out var memberId))
                    continue;

                foreach (var role in member.Roles)
                {
                    var hasOpen = await db.MemberRoleSnapshots
                        .AnyAsync(s => s.MemberId == memberId && s.RoleDiscordId == role.Id && s.RevokedAtUtc == null, ct);

                    if (hasOpen)
                        continue;

                    try
                    {
                        db.MemberRoleSnapshots.Add(new MemberRoleSnapshotEntity
                        {
                            MemberId = memberId,
                            RoleDiscordId = role.Id,
                            GrantedAtUtc = DateTime.UtcNow
                        });
                        await db.SaveChangesAsync(ct);
                        seeded++;
                    }
                    catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
                    {
                        db.ChangeTracker.Clear();
                    }
                }
            }
        }

        logger.LogInformation("Current role seeding done: {Seeded} new snapshots", seeded);
        return seeded;
    }
}
