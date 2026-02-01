using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.EventHandlers;

public class BanEventHandler(IServiceScopeFactory scopeFactory, ILogger<BanEventHandler> logger) :
    IEventHandler<GuildBanAddedEventArgs>,
    IEventHandler<GuildBanRemovedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, GuildBanAddedEventArgs e)
    {
        try
        {
            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var userService = scope.ServiceProvider.GetRequiredService<UserService>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "GuildBanAdded", e.Guild.Id, null, e.Member.Id);

            await userService.UpsertUserAsync(e.Member);

            // Look up Guid FKs
            var guild = await db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == e.Guild.Id);
            var user = await db.Users.FirstOrDefaultAsync(u => u.DiscordId == e.Member.Id);

            if (guild != null && user != null)
            {
                // Add or update ban entity
                var existingBan = await db.Bans
                    .FirstOrDefaultAsync(b => b.GuildId == guild.Id && b.UserId == user.Id && b.IsActive);

                if (existingBan == null)
                {
                    db.Bans.Add(new BanEntity
                    {
                        GuildId = guild.Id,
                        UserId = user.Id,
                        IsActive = true,
                        BannedAtUtc = now
                    });
                }
            }

            db.BanEvents.Add(new BanEventEntity
            {
                GuildDiscordId = e.Guild.Id,
                UserDiscordId = e.Member.Id,
                EventType = BanEventType.Added,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            });

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling ban added for UserId={UserId}", e.Member.Id);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildBanRemovedEventArgs e)
    {
        try
        {
            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "GuildBanRemoved", e.Guild.Id, null, e.Member.Id);

            // Look up Guid FKs
            var guild = await db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == e.Guild.Id);
            var user = await db.Users.FirstOrDefaultAsync(u => u.DiscordId == e.Member.Id);

            if (guild != null && user != null)
            {
                // Mark ban as inactive
                await db.Bans
                    .Where(b => b.GuildId == guild.Id && b.UserId == user.Id && b.IsActive)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(b => b.IsActive, false)
                        .SetProperty(b => b.UnbannedAtUtc, now));
            }

            db.BanEvents.Add(new BanEventEntity
            {
                GuildDiscordId = e.Guild.Id,
                UserDiscordId = e.Member.Id,
                EventType = BanEventType.Removed,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            });

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling ban removed for UserId={UserId}", e.Member.Id);
        }
    }
}
