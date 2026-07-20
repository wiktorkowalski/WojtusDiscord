using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DSharpPlus.Entities;

namespace DiscordEventService.Services;

internal sealed class GuildUpsertService(DiscordDbContext db, ILogger<GuildUpsertService> logger)
{
    public async Task<UpsertResult<Guid>> UpsertGuildAsync(DiscordGuild guild)
    {
        // Every caller upserts because the bot just observed the guild live (boot sync, a
        // gateway event from it, or FK resolution) — so any stale "left" marker is wrong here.
        var id = await db.Guilds.UpsertAsync(
            g => g.DiscordId == guild.Id,
            s => s
                .SetProperty(g => g.Name, guild.Name)
                .SetProperty(g => g.IconHash, guild.IconHash)
                .SetProperty(g => g.OwnerId, guild.OwnerId)
                .SetProperty(g => g.LeftAtUtc, (DateTime?)null),
            () => new GuildEntity
            {
                DiscordId = guild.Id,
                Name = guild.Name,
                IconHash = guild.IconHash,
                OwnerId = guild.OwnerId,
                LeftAtUtc = null
            },
            g => g.Id);

        if (id == Guid.Empty)
        {
            logger.LogError("Guild upsert lost the row for guild {GuildId} after upsert", guild.Id);
            return UpsertResult<Guid>.Failure($"Guild upsert lost the row for DiscordId={guild.Id}");
        }

        return UpsertResult<Guid>.Success(id);
    }
}
