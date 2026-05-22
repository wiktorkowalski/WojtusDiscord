using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services;

public class GuildUpsertService(DiscordDbContext db, ILogger<GuildUpsertService> logger)
{
    public async Task<Guid> UpsertGuildAsync(DiscordGuild guild)
    {
        var rowsAffected = await db.Guilds
            .Where(g => g.DiscordId == guild.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(g => g.Name, guild.Name)
                .SetProperty(g => g.IconHash, guild.IconHash)
                .SetProperty(g => g.OwnerId, guild.OwnerId));

        if (rowsAffected == 0)
        {
            try
            {
                db.Guilds.Add(new GuildEntity
                {
                    DiscordId = guild.Id,
                    Name = guild.Name,
                    IconHash = guild.IconHash,
                    OwnerId = guild.OwnerId,
                    LeftAtUtc = null
                });
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            {
                db.ChangeTracker.Clear();
                await db.Guilds
                    .Where(g => g.DiscordId == guild.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(g => g.Name, guild.Name)
                        .SetProperty(g => g.IconHash, guild.IconHash)
                        .SetProperty(g => g.OwnerId, guild.OwnerId));
            }
        }

        var id = await db.Guilds
            .Where(g => g.DiscordId == guild.Id)
            .Select(g => g.Id)
            .FirstOrDefaultAsync();

        if (id == Guid.Empty)
        {
            logger.LogError("GuildUpsert lost the row for DiscordId={DiscordId} after upsert", guild.Id);
        }

        return id;
    }
}
