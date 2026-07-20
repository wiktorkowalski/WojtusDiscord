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

                    var guildResult = await ctx.Services.GetRequiredService<GuildUpsertService>()
                        .UpsertGuildAsync(e.Guild);
                    if (!guildResult.IsSuccess)
                        return;

                    await UpsertChannelsAndRolesAsync(
                        ctx.Services.GetRequiredService<ChannelUpsertService>(),
                        ctx.Services.GetRequiredService<RoleUpsertService>(),
                        e.Guild, guildResult.Value);

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

    // Per-entity upsert: each channel/role goes through the shared seam, which owns the
    // column map and absorbs the 23505 race a concurrent GuildCreate could otherwise cause
    // in a batched insert.
    private static async Task UpsertChannelsAndRolesAsync(
        ChannelUpsertService channelUpsert, RoleUpsertService roleUpsert, DiscordGuild guild, Guid guildGuid)
    {
        foreach (var channel in guild.Channels.Values)
            await channelUpsert.UpsertChannelAsync(channel, guildGuid);

        foreach (var role in guild.Roles.Values)
            await roleUpsert.UpsertRoleAsync(role, guildGuid);
    }
}
