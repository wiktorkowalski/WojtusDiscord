using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DSharpPlus;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Jobs;

public class RolesBackfillJob(
    IServiceScopeFactory scopeFactory,
    DiscordClient discordClient,
    ILogger<RolesBackfillJob> logger) : BackfillJobBase, IBackfillJob
{
    protected override BackfillType BackfillType => BackfillType.Roles;

    public async Task ExecuteAsync(ulong guildId, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();

        var checkpoint = await GetOrCreateCheckpointAsync(db, guildId);
        checkpoint.Status = BackfillStatus.InProgress;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var guild = await discordClient.GetGuildAsync(guildId);
            var guildEntity = await db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == guildId, cancellationToken);

            if (guildEntity is null)
            {
                logger.LogWarning("Guild {GuildId} not found in database, cannot backfill roles", guildId);
                await MarkFailedAsync(db, checkpoint, new InvalidOperationException($"Guild {guildId} not found in database"));
                return;
            }

            checkpoint.TotalCount = guild.Roles.Count;
            await db.SaveChangesAsync(cancellationToken);

            foreach (var role in guild.Roles.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var permissions = long.TryParse(role.Permissions.ToString(), out var perm) ? perm : 0;

                var rowsAffected = await db.Roles
                    .Where(r => r.DiscordId == role.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.Name, role.Name)
                        .SetProperty(r => r.Color, role.Color.Value)
                        .SetProperty(r => r.IsHoisted, role.IsHoisted)
                        .SetProperty(r => r.Position, role.Position)
                        .SetProperty(r => r.Permissions, permissions)
                        .SetProperty(r => r.IsManaged, role.IsManaged)
                        .SetProperty(r => r.IsMentionable, role.IsMentionable)
                        .SetProperty(r => r.IsDeleted, false),
                    cancellationToken);

                if (rowsAffected == 0)
                {
                    try
                    {
                        db.Roles.Add(new RoleEntity
                        {
                            DiscordId = role.Id,
                            GuildId = guildEntity.Id,
                            Name = role.Name,
                            Color = role.Color.Value,
                            IsHoisted = role.IsHoisted,
                            Position = role.Position,
                            Permissions = permissions,
                            IsManaged = role.IsManaged,
                            IsMentionable = role.IsMentionable
                        });
                        await db.SaveChangesAsync(cancellationToken);
                    }
                    catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
                    {
                        logger.LogDebug("Role {RoleId} already exists (race condition), skipping insert", role.Id);
                        db.ChangeTracker.Clear();
                    }
                }

                checkpoint.ProcessedCount++;

                if (checkpoint.ProcessedCount % 50 == 0)
                {
                    await SaveProgressAsync(db, checkpoint);
                }
            }

            await MarkCompletedAsync(db, checkpoint);
            logger.LogInformation("Roles backfill completed for guild {GuildId}: {Count} roles", guildId, checkpoint.ProcessedCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Roles backfill failed for guild {GuildId}", guildId);
            await MarkFailedAsync(db, checkpoint, ex);
            throw;
        }
    }
}
