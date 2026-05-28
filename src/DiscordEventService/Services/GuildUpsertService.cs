using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DSharpPlus.Entities;

namespace DiscordEventService.Services;

public class GuildUpsertService(DiscordDbContext db, ILogger<GuildUpsertService> logger)
{
    public async Task<Guid> UpsertGuildAsync(DiscordGuild guild)
    {
        var id = await db.Guilds.UpsertAsync(
            g => g.DiscordId == guild.Id,
            s => s
                .SetProperty(g => g.Name, guild.Name)
                .SetProperty(g => g.IconHash, guild.IconHash)
                .SetProperty(g => g.OwnerId, guild.OwnerId),
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
            logger.LogError("GuildUpsert lost the row for DiscordId={DiscordId} after upsert", guild.Id);
        }

        return id;
    }
}
