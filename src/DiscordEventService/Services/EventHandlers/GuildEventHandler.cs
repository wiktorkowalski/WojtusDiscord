using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.EventHandlers;

public sealed class GuildEventHandler(EventPipeline pipeline) :
    IEventHandler<GuildCreatedEventArgs>,
    IEventHandler<GuildAvailableEventArgs>,
    IEventHandler<GuildUpdatedEventArgs>,
    IEventHandler<GuildDeletedEventArgs>,
    IEventHandler<GuildEmojisUpdatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, GuildCreatedEventArgs e)
    {
        await pipeline.Execute(e, "GuildCreated", nameof(GuildEventHandler),
            e.Guild.Id, null, null, async ctx =>
            {
                ctx.Logger.LogInformation("GuildCreated event received for GuildId={GuildId} Name={GuildName}", e.Guild.Id, e.Guild.Name);

                void ApplyGuildFields(GuildEntity g)
                {
                    g.Name = e.Guild.Name;
                    g.IconHash = e.Guild.IconHash;
                    g.OwnerId = e.Guild.OwnerId;
                    g.LeftAtUtc = null;
                }

                // Wrap the two-stage upsert (Guild flush for Guid, then channels/roles) in
                // one transaction so a partial failure (e.g. FK error on roles after Guild
                // already flushed) rolls back atomically. ExecutionStrategy is required
                // because EnableRetryOnFailure is configured on the DbContext.
                var strategy = ctx.Db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    ctx.Db.ChangeTracker.Clear();
                    await using var tx = await ctx.Db.Database.BeginTransactionAsync();

                    var existingGuild = await ctx.Db.Guilds
                        .Where(g => g.DiscordId == e.Guild.Id)
                        .FirstOrDefaultAsync();
                    if (existingGuild == null)
                    {
                        existingGuild = new GuildEntity
                        {
                            DiscordId = e.Guild.Id,
                            Name = e.Guild.Name,
                            IconHash = e.Guild.IconHash,
                            OwnerId = e.Guild.OwnerId,
                            LeftAtUtc = null
                        };
                        ctx.Db.Guilds.Add(existingGuild);
                        try
                        {
                            await ctx.Db.SaveChangesAsync(); // Flush to get the Guid Id (committed atomically with the rest at tx.CommitAsync).
                        }
                        catch (DbUpdateException dbEx) when (dbEx.InnerException is Npgsql.PostgresException { SqlState: "23505" })
                        {
                            // Concurrent insert won the race. Re-fetch and update.
                            ctx.Db.ChangeTracker.Clear();
                            existingGuild = await ctx.Db.Guilds
                                .Where(g => g.DiscordId == e.Guild.Id)
                                .FirstOrDefaultAsync()
                                ?? throw new InvalidOperationException($"Guild {e.Guild.Id} disappeared after 23505 conflict");
                            ApplyGuildFields(existingGuild);
                        }
                    }
                    else
                    {
                        ApplyGuildFields(existingGuild);
                    }

                    var guildGuid = existingGuild.Id;

                    await UpsertChannelsAndRolesAsync(ctx.Db, ctx.Logger, e.Guild, guildGuid);

                    try
                    {
                        await ctx.Db.SaveChangesAsync();
                    }
                    catch (DbUpdateException dbEx) when (dbEx.InnerException is Npgsql.PostgresException { SqlState: "23505" })
                    {
                        // Concurrent GuildCreated inserted a channel/role that wasn't in our batch lookup.
                        // Clear drops the Guild field updates set above, so re-fetch and re-apply them
                        // alongside the channel/role re-batch-load before saving.
                        ctx.Db.ChangeTracker.Clear();
                        var refreshedGuild = await ctx.Db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == e.Guild.Id);
                        if (refreshedGuild != null)
                        {
                            ApplyGuildFields(refreshedGuild);
                        }
                        await UpsertChannelsAndRolesAsync(ctx.Db, ctx.Logger, e.Guild, guildGuid);
                        await ctx.Db.SaveChangesAsync();
                    }

                    await tx.CommitAsync();
                });
            });
    }

    public Task HandleEventAsync(DiscordClient sender, GuildAvailableEventArgs e)
    {
        // GuildAvailable fires during initial connection - delegate to GuildCreated handler
        return HandleEventAsync(sender, (GuildCreatedEventArgs)e);
    }

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

    public async Task HandleEventAsync(DiscordClient sender, GuildEmojisUpdatedEventArgs e)
    {
        // Already raw-logged by EmojiEventHandler; skip the duplicate raw row here.
        await pipeline.Execute(e, "GuildEmojisUpdated", nameof(GuildEventHandler),
            e.Guild.Id, null, null, async ctx =>
            {
                var guild = await ctx.Db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == e.Guild.Id);
                var guildGuid = guild?.Id;

                var existingEmotes = await ctx.Db.Emotes
                    .Where(em => em.GuildId == guildGuid)
                    .ToDictionaryAsync(em => em.DiscordId);

                var currentEmoteIds = e.EmojisAfter.Select(em => em.Key).ToHashSet();

                foreach (var existingEmote in existingEmotes.Values)
                {
                    if (!currentEmoteIds.Contains(existingEmote.DiscordId))
                    {
                        existingEmote.IsDeleted = true;
                        existingEmote.DeletedAtUtc ??= ctx.ReceivedAtUtc;
                    }
                }

                foreach (var emoteKvp in e.EmojisAfter)
                {
                    var emote = emoteKvp.Value;
                    if (!existingEmotes.TryGetValue(emote.Id, out var existingEmote))
                    {
                        ctx.Db.Emotes.Add(new EmoteEntity
                        {
                            DiscordId = emote.Id,
                            GuildId = guildGuid,
                            Name = emote.Name,
                            IsAnimated = emote.IsAnimated,
                            IsAvailable = emote.IsAvailable,
                            IsDeleted = false
                        });
                    }
                    else
                    {
                        existingEmote.Name = emote.Name;
                        existingEmote.IsAnimated = emote.IsAnimated;
                        existingEmote.IsAvailable = emote.IsAvailable;
                        existingEmote.IsDeleted = false;
                        existingEmote.DeletedAtUtc = null;
                    }
                }

                await ctx.Db.SaveChangesAsync();
            }, logRawEvent: false);
    }

    private static async Task UpsertChannelsAndRolesAsync(DiscordDbContext db, ILogger logger, DiscordGuild guild, Guid guildGuid)
    {
        // Batch load existing channels
        var channelIds = guild.Channels.Keys.ToList();
        var existingChannels = await db.Channels
            .Where(c => channelIds.Contains(c.DiscordId))
            .ToDictionaryAsync(c => c.DiscordId);

        foreach (var channel in guild.Channels.Values)
        {
            if (!existingChannels.TryGetValue(channel.Id, out var existingChannel))
            {
                db.Channels.Add(new ChannelEntity
                {
                    DiscordId = channel.Id,
                    GuildId = guildGuid,
                    ParentDiscordId = channel.Parent?.Id,
                    Name = channel.Name,
                    Type = MapChannelType(channel.Type, logger),
                    Topic = channel.Topic,
                    Bitrate = channel.Bitrate,
                    UserLimit = channel.UserLimit,
                    RateLimitPerUser = channel.PerUserRateLimit,
                    IsNsfw = false,
                    Position = channel.Position,
                    IsDeleted = false
                });
            }
            else
            {
                existingChannel.Name = channel.Name;
                existingChannel.ParentDiscordId = channel.Parent?.Id;
                existingChannel.Type = MapChannelType(channel.Type, logger);
                existingChannel.Topic = channel.Topic;
                existingChannel.Bitrate = channel.Bitrate;
                existingChannel.UserLimit = channel.UserLimit;
                existingChannel.RateLimitPerUser = channel.PerUserRateLimit;
                existingChannel.IsNsfw = false;
                existingChannel.Position = channel.Position;
                existingChannel.IsDeleted = false;
                existingChannel.DeletedAtUtc = null;
            }
        }

        // Batch load existing roles
        var roleIds = guild.Roles.Keys.ToList();
        var existingRoles = await db.Roles
            .Where(r => roleIds.Contains(r.DiscordId))
            .ToDictionaryAsync(r => r.DiscordId);

        foreach (var role in guild.Roles.Values)
        {
            if (!existingRoles.TryGetValue(role.Id, out var existingRole))
            {
                db.Roles.Add(new RoleEntity
                {
                    DiscordId = role.Id,
                    GuildId = guildGuid,
                    Name = role.Name,
                    Color = role.Color.Value,
                    IsHoisted = role.IsHoisted,
                    Position = role.Position,
                    Permissions = long.TryParse(role.Permissions.ToString(), out var p) ? p : 0,
                    IsManaged = role.IsManaged,
                    IsMentionable = role.IsMentionable,
                    IsDeleted = false
                });
            }
            else
            {
                existingRole.Name = role.Name;
                existingRole.Color = role.Color.Value;
                existingRole.IsHoisted = role.IsHoisted;
                existingRole.Position = role.Position;
                existingRole.Permissions = long.TryParse(role.Permissions.ToString(), out var perm) ? perm : 0;
                existingRole.IsManaged = role.IsManaged;
                existingRole.IsMentionable = role.IsMentionable;
                existingRole.IsDeleted = false;
                existingRole.DeletedAtUtc = null;
            }
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
                "Unknown DiscordChannelType={DiscordType} (int={Int}); mapping to ChannelType.Unknown",
                discordType, (int)discordType);
        }

        return mapped;
    }
}
