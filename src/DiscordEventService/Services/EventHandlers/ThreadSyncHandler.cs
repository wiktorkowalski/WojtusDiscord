using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.EventArgs;
using System.Text.Json;

namespace DiscordEventService.Services.EventHandlers;

public class ThreadSyncHandler(IServiceScopeFactory scopeFactory, ILogger<ThreadSyncHandler> logger) :
    IEventHandler<ThreadListSyncedEventArgs>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task HandleEventAsync(DiscordClient sender, ThreadListSyncedEventArgs args)
    {
        try
        {
            var now = DateTime.UtcNow;

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                args, "ThreadListSynced", args.Guild.Id, null, null);

            var syncEvent = new ThreadSyncEventEntity
            {
                GuildDiscordId = args.Guild.Id,
                ThreadCount = args.Threads.Count,
                ThreadIdsJson = JsonSerializer.Serialize(args.Threads.Select(t => t.Id.ToString()), JsonOptions),
                ChannelIdsJson = args.Channels?.Count > 0
                    ? JsonSerializer.Serialize(args.Channels.Select(c => c.Id.ToString()), JsonOptions)
                    : null,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            };

            await db.ThreadSyncEvents.AddAsync(syncEvent);
            await db.SaveChangesAsync();

            logger.LogDebug("Recorded thread list sync for guild {GuildId} with {ThreadCount} threads",
                args.Guild.Id, args.Threads.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling thread list sync for guild {GuildId}", args.Guild.Id);
        }
    }
}
