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
        await pipeline.Execute(e, "GuildCreated", nameof(GuildEventHandler),
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

                    await UpsertChannelsAndRolesAsync(ctx.Db, ctx.Logger, e.Guild, guildGuid);

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
        await pipeline.Execute(e, "GuildUpdated", nameof(GuildEventHandler),
            e.GuildAfter.Id, null, null, async ctx =>
            {
                var existingGuild = await ctx.Db.Guilds
                    .Where(g => g.DiscordId == e.GuildAfter.Id)
                    .FirstOrDefaultAsync();
                if (existingGuild != null)
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
        await pipeline.Execute(e, "GuildDeleted", nameof(GuildEventHandler),
            e.Guild.Id, null, null, async ctx =>
            {
                var existingGuild = await ctx.Db.Guilds
                    .Where(g => g.DiscordId == e.Guild.Id)
                    .FirstOrDefaultAsync();
                if (existingGuild != null)
                {
                    existingGuild.LeftAtUtc = ctx.ReceivedAtUtc;
                    await ctx.Db.SaveChangesAsync();
                }
            });
    }

    private static async Task UpsertChannelsAndRolesAsync(DiscordDbContext db, ILogger logger, DiscordGuild guild, Guid guildGuid)
    {
        // Per-entity upsert: each channel/role goes through the shared primitive, which absorbs
        // the 23505 race a concurrent GuildCreate could otherwise cause in a batched insert.
        foreach (var channel in guild.Channels.Values)
        {
            var mappedType = MapChannelType(channel.Type, logger);
            await db.Channels.UpsertAsync(
                c => c.DiscordId == channel.Id,
                s => s
                    .SetProperty(c => c.Name, channel.Name)
                    .SetProperty(c => c.ParentDiscordId, channel.Parent?.Id)
                    .SetProperty(c => c.Type, mappedType)
                    .SetProperty(c => c.Topic, channel.Topic)
                    .SetProperty(c => c.Bitrate, channel.Bitrate)
                    .SetProperty(c => c.UserLimit, channel.UserLimit)
                    .SetProperty(c => c.RateLimitPerUser, channel.PerUserRateLimit)
                    .SetProperty(c => c.IsNsfw, false)
                    .SetProperty(c => c.Position, channel.Position)
                    .SetProperty(c => c.IsDeleted, false)
                    .SetProperty(c => c.DeletedAtUtc, (DateTime?)null),
                () => new ChannelEntity
                {
                    DiscordId = channel.Id,
                    GuildId = guildGuid,
                    ParentDiscordId = channel.Parent?.Id,
                    Name = channel.Name,
                    Type = mappedType,
                    Topic = channel.Topic,
                    Bitrate = channel.Bitrate,
                    UserLimit = channel.UserLimit,
                    RateLimitPerUser = channel.PerUserRateLimit,
                    IsNsfw = false,
                    Position = channel.Position,
                    IsDeleted = false,
                },
                c => c.Id);
        }

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

    private static ChannelType MapChannelType(DiscordChannelType discordType, ILogger logger)
    {
        // Add new DSharpPlus channel types here as they appear. Anything not
        // listed becomes Unknown (with a warning log) instead of silently
        // collapsing to Text. The int value is still preserved at the storage
        // layer via (ChannelType)channel.Type casts elsewhere — Unknown is
        // just the label.
        var mapped = discordType switch
        {
            DiscordChannelType.Text => ChannelType.Text,
            DiscordChannelType.Private => ChannelType.Private,
            DiscordChannelType.Voice => ChannelType.Voice,
            DiscordChannelType.Group => ChannelType.Group,
            DiscordChannelType.Category => ChannelType.Category,
            DiscordChannelType.News => ChannelType.News,
            DiscordChannelType.NewsThread => ChannelType.NewsThread,
            DiscordChannelType.PublicThread => ChannelType.PublicThread,
            DiscordChannelType.PrivateThread => ChannelType.PrivateThread,
            DiscordChannelType.Stage => ChannelType.Stage,
            DiscordChannelType.GuildForum => ChannelType.Forum,
            DiscordChannelType.GuildMedia => ChannelType.Media,
            _ => ChannelType.Unknown
        };

        if (mapped == ChannelType.Unknown)
        {
            logger.LogWarning(
                "Unknown Discord channel type {DiscordChannelType} (value {ChannelTypeValue}); mapping to ChannelType.Unknown",
                discordType, (int)discordType);
        }

        return mapped;
    }
}
