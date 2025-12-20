using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.EventHandlers;

public class GuildEventHandler(IServiceScopeFactory scopeFactory, ILogger<GuildEventHandler> logger) :
    IEventHandler<GuildCreatedEventArgs>,
    IEventHandler<GuildUpdatedEventArgs>,
    IEventHandler<GuildDeletedEventArgs>,
    IEventHandler<GuildEmojisUpdatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, GuildCreatedEventArgs e)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();

            // Upsert guild
            var existingGuild = await db.Guilds
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
                db.Guilds.Add(existingGuild);
                await db.SaveChangesAsync(); // Save to get the Guid Id
            }
            else
            {
                existingGuild.Name = e.Guild.Name;
                existingGuild.IconHash = e.Guild.IconHash;
                existingGuild.OwnerId = e.Guild.OwnerId;
                existingGuild.LeftAtUtc = null;
            }

            var guildGuid = existingGuild.Id;

            // Batch load existing channels
            var channelIds = e.Guild.Channels.Keys.ToList();
            var existingChannels = await db.Channels
                .Where(c => channelIds.Contains(c.DiscordId))
                .ToDictionaryAsync(c => c.DiscordId);

            foreach (var channel in e.Guild.Channels.Values)
            {
                if (!existingChannels.TryGetValue(channel.Id, out var existingChannel))
                {
                    db.Channels.Add(new ChannelEntity
                    {
                        DiscordId = channel.Id,
                        GuildId = guildGuid,
                        ParentDiscordId = channel.Parent?.Id,
                        Name = channel.Name,
                        Type = MapChannelType(channel.Type),
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
                    existingChannel.Type = MapChannelType(channel.Type);
                    existingChannel.Topic = channel.Topic;
                    existingChannel.Bitrate = channel.Bitrate;
                    existingChannel.UserLimit = channel.UserLimit;
                    existingChannel.RateLimitPerUser = channel.PerUserRateLimit;
                    existingChannel.IsNsfw = false;
                    existingChannel.Position = channel.Position;
                    existingChannel.IsDeleted = false;
                }
            }

            // Batch load existing roles
            var roleIds = e.Guild.Roles.Keys.ToList();
            var existingRoles = await db.Roles
                .Where(r => roleIds.Contains(r.DiscordId))
                .ToDictionaryAsync(r => r.DiscordId);

            foreach (var role in e.Guild.Roles.Values)
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
                }
            }

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling guild created for GuildId={GuildId}", e.Guild.Id);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildUpdatedEventArgs e)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();

            var existingGuild = await db.Guilds
                .Where(g => g.DiscordId == e.GuildAfter.Id)
                .FirstOrDefaultAsync();
            if (existingGuild != null)
            {
                existingGuild.Name = e.GuildAfter.Name;
                existingGuild.IconHash = e.GuildAfter.IconHash;
                existingGuild.OwnerId = e.GuildAfter.OwnerId;

                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling guild updated for GuildId={GuildId}", e.GuildAfter.Id);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildDeletedEventArgs e)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();

            var existingGuild = await db.Guilds
                .Where(g => g.DiscordId == e.Guild.Id)
                .FirstOrDefaultAsync();
            if (existingGuild != null)
            {
                existingGuild.LeftAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling guild deleted for GuildId={GuildId}", e.Guild.Id);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildEmojisUpdatedEventArgs e)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();

            // Look up Guild Guid
            var guild = await db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == e.Guild.Id);
            var guildGuid = guild?.Id;

            // Batch load existing emotes for this guild
            var existingEmotes = await db.Emotes
                .Where(em => em.GuildId == guildGuid)
                .ToDictionaryAsync(em => em.DiscordId);

            var currentEmoteIds = e.EmojisAfter.Select(em => em.Key).ToHashSet();

            // Mark deleted emotes
            foreach (var existingEmote in existingEmotes.Values)
            {
                if (!currentEmoteIds.Contains(existingEmote.DiscordId))
                {
                    existingEmote.IsDeleted = true;
                }
            }

            // Upsert current emotes (use dictionary lookup instead of FindAsync)
            foreach (var emoteKvp in e.EmojisAfter)
            {
                var emote = emoteKvp.Value;
                if (!existingEmotes.TryGetValue(emote.Id, out var existingEmote))
                {
                    db.Emotes.Add(new EmoteEntity
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
                }
            }

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling guild emojis updated for GuildId={GuildId}", e.Guild.Id);
        }
    }

    private static ChannelType MapChannelType(DiscordChannelType discordType)
    {
        return discordType switch
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
            _ => ChannelType.Text
        };
    }
}
