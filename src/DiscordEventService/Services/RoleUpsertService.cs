using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DSharpPlus.Entities;

namespace DiscordEventService.Services;

// The single write seam for role rows (#290): live events, guild cold-sync, and backfill
// all go through UpsertRoleAsync so the column map exists exactly once.
internal sealed class RoleUpsertService(DiscordDbContext db, ILogger<RoleUpsertService> logger)
{
    public async Task<UpsertResult<Guid>> UpsertRoleAsync(
        DiscordRole role, Guid guildId, CancellationToken cancellationToken = default)
    {
        var permissions = long.TryParse(role.Permissions.ToString(), out var parsed) ? parsed : 0;

        // A live sighting means the role exists — always clear soft-deletion.
        var id = await db.Roles.UpsertAsync(
            r => r.DiscordId == role.Id,
            s => s
                .SetProperty(r => r.Name, role.Name)
                .SetProperty(r => r.Color, role.Color.Value)
                .SetProperty(r => r.IsHoisted, role.IsHoisted)
                .SetProperty(r => r.Position, role.Position)
                .SetProperty(r => r.Permissions, permissions)
                .SetProperty(r => r.IsManaged, role.IsManaged)
                .SetProperty(r => r.IsMentionable, role.IsMentionable)
                .SetProperty(r => r.IsDeleted, false)
                .SetProperty(r => r.DeletedAtUtc, (DateTime?)null),
            () => new RoleEntity
            {
                DiscordId = role.Id,
                GuildId = guildId,
                Name = role.Name,
                Color = role.Color.Value,
                IsHoisted = role.IsHoisted,
                Position = role.Position,
                Permissions = permissions,
                IsManaged = role.IsManaged,
                IsMentionable = role.IsMentionable,
                IsDeleted = false
            },
            r => r.Id,
            cancellationToken);

        if (id == Guid.Empty)
        {
            logger.LogError("Role upsert lost the row for role {RoleId} after upsert", role.Id);
            return UpsertResult<Guid>.Failure($"Role upsert lost the row for DiscordId={role.Id}");
        }

        return UpsertResult<Guid>.Success(id);
    }
}
