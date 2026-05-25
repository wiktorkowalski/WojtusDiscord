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
        var correlationId = Guid.NewGuid();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            string? rawJson = null;
            try
            {
                var now = DateTime.UtcNow;
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
                var userService = scope.ServiceProvider.GetRequiredService<UserService>();
                var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

                rawJson = await rawEventService.SerializeAndLogAsync(
                    e, "GuildBanAdded", e.Guild.Id, null, e.Member.Id, correlationId: correlationId);

                await db.SaveChangesAsync();

                var guildUpsert = scope.ServiceProvider.GetRequiredService<GuildUpsertService>();
                var guildGuid = await guildUpsert.UpsertGuildAsync(e.Guild);
                await userService.UpsertUserAsync(e.Member);
                var userGuid = await db.Users
                    .Where(u => u.DiscordId == e.Member.Id)
                    .Select(u => u.Id)
                    .FirstOrDefaultAsync();

                if (guildGuid != Guid.Empty && userGuid != Guid.Empty)
                {
                    // Add or update ban entity
                    var existingBan = await db.Bans
                        .FirstOrDefaultAsync(b => b.GuildId == guildGuid && b.UserId == userGuid && b.IsActive);

                    if (existingBan == null)
                    {
                        db.Bans.Add(new BanEntity
                        {
                            GuildId = guildGuid,
                            UserId = userGuid,
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
                using var failureScope = scopeFactory.CreateScope();
                var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
                await failedEventService.RecordFailureAsync(
                    "GuildBanAdded", nameof(BanEventHandler), ex,
                    e.Guild?.Id, null, e.Member?.Id, rawJson, correlationId: correlationId);
            }
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildBanRemovedEventArgs e)
    {
        var correlationId = Guid.NewGuid();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            string? rawJson = null;
            try
            {
                var now = DateTime.UtcNow;
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
                var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();
                var guildUpsert = scope.ServiceProvider.GetRequiredService<GuildUpsertService>();
                var userService = scope.ServiceProvider.GetRequiredService<UserService>();

                rawJson = await rawEventService.SerializeAndLogAsync(
                    e, "GuildBanRemoved", e.Guild.Id, null, e.Member.Id, correlationId: correlationId);

                await db.SaveChangesAsync();

                var guildGuid = await guildUpsert.UpsertGuildAsync(e.Guild);
                await userService.UpsertUserAsync(e.Member);
                var userGuid = await db.Users
                    .Where(u => u.DiscordId == e.Member.Id)
                    .Select(u => u.Id)
                    .FirstOrDefaultAsync();

                if (guildGuid != Guid.Empty && userGuid != Guid.Empty)
                {
                    // Mark ban as inactive
                    await db.Bans
                        .Where(b => b.GuildId == guildGuid && b.UserId == userGuid && b.IsActive)
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
                using var failureScope = scopeFactory.CreateScope();
                var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
                await failedEventService.RecordFailureAsync(
                    "GuildBanRemoved", nameof(BanEventHandler), ex,
                    e.Guild?.Id, null, e.Member?.Id, rawJson, correlationId: correlationId);
            }
        }
    }
}
