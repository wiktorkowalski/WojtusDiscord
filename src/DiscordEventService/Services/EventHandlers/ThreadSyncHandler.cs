using System.Text.Json;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

internal sealed class ThreadSyncHandler(EventPipeline pipeline) :
    IEventHandler<ThreadListSyncedEventArgs>
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task HandleEventAsync(DiscordClient sender, ThreadListSyncedEventArgs args)
    {
        await pipeline.Execute(args, "ThreadListSynced", nameof(ThreadSyncHandler),
            args.Guild.Id, null, null, async ctx =>
            {
                ctx.Db.ThreadSyncEvents.Add(new ThreadSyncEventEntity
                {
                    GuildDiscordId = args.Guild.Id,
                    ThreadCount = args.Threads.Count,
                    ThreadIdsJson = JsonSerializer.Serialize(args.Threads.Select(t => t.Id.ToString()), JsonOptions),
                    ChannelIdsJson = args.Channels?.Count > 0
                        ? JsonSerializer.Serialize(args.Channels.Select(c => c.Id.ToString()), JsonOptions)
                        : null,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();

                ctx.Logger.LogDebug("Recorded thread list sync for guild {GuildId} with {ThreadCount} threads",
                    args.Guild.Id, args.Threads.Count);
            });
    }
}
