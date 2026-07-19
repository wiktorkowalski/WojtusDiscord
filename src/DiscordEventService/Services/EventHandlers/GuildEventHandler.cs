using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.EventHandlers;

internal sealed class GuildEventHandler(EventPipeline pipeline) :
    IEventHandler<GuildCreatedEventArgs>,
    IEventHandler<GuildAvailableEventArgs>,
    IEventHandler<GuildUpdatedEventArgs>,
    IEventHandler<GuildDeletedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, GuildCreatedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "GuildCreated", nameof(GuildEventHandler),
            e.Guild.Id, null, null, async ctx =>
            {
                ctx.Logger.LogInformation("GuildCreated received for guild {GuildId} ({GuildName})", e.Guild.Id, e.Guild.Name);

                // Upsert the guild (for its Guid) then every channel/role, all in one transaction
                // so a partial failure rolls back atomically. Each upsert handles its own 23505
                // race internally. ExecutionStrategy is required because EnableRetryOnFailure is
                // configured on the DbContext.
                var strategy = ctx.Db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    // Reset the tracker so an EnableRetryOnFailure retry doesn't re-stage entities
                    // left Added by a rolled-back attempt.
                    ctx.Db.ChangeTracker.Clear();
                    await using var tx = await ctx.Db.Database.BeginTransactionAsync();

                    var guildGuid = await ctx.Db.Guilds.UpsertAsync(
                        g => g.DiscordId == e.Guild.Id,
                        s => s
                            .SetProperty(g => g.Name, e.Guild.Name)
                            .SetProperty(g => g.IconHash, e.Guild.IconHash)
                            .SetProperty(g => g.OwnerId, e.Guild.OwnerId)
                            .SetProperty(g => g.LeftAtUtc, (DateTime?)null),
                        () => new GuildEntity
                        {
                            DiscordId = e.Guild.Id,
                            Name = e.Guild.Name,
                            IconHash = e.Guild.IconHash,
                            OwnerId = e.Guild.OwnerId,
                            LeftAtUtc = null,
                        },
                        g => g.Id);

                    await UpsertChannelsAndRolesAsync(
                        ctx.Db, ctx.Services.GetRequiredService<ChannelUpsertService>(), e.Guild, guildGuid);

                    await tx.CommitAsync();
                });
            });
    }

    // GuildAvailable fires during initial connection - delegate to GuildCreated handler
    public Task HandleEventAsync(DiscordClient sender, GuildAvailableEventArgs e) =>
        HandleEventAsync(sender, (GuildCreatedEventArgs)e);

    public async Task HandleEventAsync(DiscordClient sender, GuildUpdatedEventArgs e)
    {
        // Already raw-logged by GuildUpdateEventHandler; skip the duplicate raw row here.
        await pipeline.ExecuteAsync(e, "GuildUpdated", nameof(GuildEventHandler),
            e.GuildAfter.Id, null, null, async ctx =>
            {
                var existingGuild = await ctx.Db.Guilds
                    .Where(g => g.DiscordId == e.GuildAfter.Id)
                    .FirstOrDefaultAsync();
                if (existingGuild is not null)
                {
                    existingGuild.Name = e.GuildAfter.Name;
                    existingGuild.IconHash = e.GuildAfter.IconHash;
                    existingGuild.OwnerId = e.GuildAfter.OwnerId;

                    await ctx.Db.SaveChangesAsync();
                }
            }, logRawEvent: false);
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildDeletedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "GuildDeleted", nameof(GuildEventHandler),
            e.Guild.Id, null, null, async ctx =>
            {
                var existingGuild = await ctx.Db.Guilds
                    .Where(g => g.DiscordId == e.Guild.Id)
                    .FirstOrDefaultAsync();
                if (existingGuild is not null)
                {
                    existingGuild.LeftAtUtc = ctx.ReceivedAtUtc;
                    await ctx.Db.SaveChangesAsync();
                }
            });
    }

    // Per-entity upsert: each channel/role goes through the shared primitive, which absorbs
    // the 23505 race a concurrent GuildCreate could otherwise cause in a batched insert.
    private static async Task UpsertChannelsAndRolesAsync(
        DiscordDbContext db, ChannelUpsertService channelUpsert, DiscordGuild guild, Guid guildGuid)
    {
        foreach (var channel in guild.Channels.Values)
            await channelUpsert.UpsertChannelAsync(channel, guildGuid);

        await UpsertGuildRolesAsync(db, guild, guildGuid);
    }

    private static async Task UpsertGuildRolesAsync(DiscordDbContext db, DiscordGuild guild, Guid guildGuid)
    {
        foreach (var role in guild.Roles.Values)
        {
            var permissions = long.TryParse(role.Permissions.ToString(), out var p) ? p : 0;
            await db.Roles.UpsertAsync(
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
                    GuildId = guildGuid,
                    Name = role.Name,
                    Color = role.Color.Value,
                    IsHoisted = role.IsHoisted,
                    Position = role.Position,
                    Permissions = permissions,
                    IsManaged = role.IsManaged,
                    IsMentionable = role.IsMentionable,
                    IsDeleted = false,
                },
                r => r.Id);
        }
    }

}
