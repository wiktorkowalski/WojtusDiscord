using System.Text.Json;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

public sealed class ThreadEventHandler(EventPipeline pipeline) :
    IEventHandler<ThreadCreatedEventArgs>,
    IEventHandler<ThreadUpdatedEventArgs>,
    IEventHandler<ThreadDeletedEventArgs>,
    IEventHandler<ThreadMembersUpdatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, ThreadCreatedEventArgs e)
    {
        await pipeline.Execute(e, "ThreadCreated", nameof(ThreadEventHandler),
            e.Guild.Id, e.Thread.Id, e.Thread.CreatorId, async ctx =>
            {
                var guildUpsert = ctx.Services.GetRequiredService<GuildUpsertService>();
                var channelUpsert = ctx.Services.GetRequiredService<ChannelUpsertService>();

                // Threads are channels (ADR-0003): upsert the thread into `channels` so messages
                // sent into it have a non-null channel_id from the first event onward.
                var guildId = await guildUpsert.UpsertGuildAsync(e.Guild);
                await channelUpsert.UpsertChannelAsync(e.Thread, guildId);

                // For message threads (parent is text/news), thread ID == starter message ID
                var isMessageThread = e.Parent?.Type is DiscordChannelType.Text or DiscordChannelType.News;

                ctx.Db.ThreadEvents.Add(new ThreadEventEntity
                {
                    ThreadDiscordId = e.Thread.Id,
                    ParentChannelDiscordId = e.Thread.ParentId ?? 0,
                    GuildDiscordId = e.Guild.Id,
                    EventType = ThreadEventType.Created,
                    Name = e.Thread.Name,
                    OwnerDiscordId = e.Thread.CreatorId,
                    StarterMessageDiscordId = isMessageThread ? e.Thread.Id : null,
                    IsArchived = e.Thread.ThreadMetadata?.IsArchived ?? false,
                    IsLocked = e.Thread.ThreadMetadata?.IsLocked ?? false,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, ThreadUpdatedEventArgs e)
    {
        await pipeline.Execute(e, "ThreadUpdated", nameof(ThreadEventHandler),
            e.Guild.Id, e.ThreadAfter.Id, e.ThreadAfter.CreatorId, async ctx =>
            {
                var guildUpsert = ctx.Services.GetRequiredService<GuildUpsertService>();
                var channelUpsert = ctx.Services.GetRequiredService<ChannelUpsertService>();

                var guildId = await guildUpsert.UpsertGuildAsync(e.Guild);
                await channelUpsert.UpsertChannelAsync(e.ThreadAfter, guildId);

                ctx.Db.ThreadEvents.Add(new ThreadEventEntity
                {
                    ThreadDiscordId = e.ThreadAfter.Id,
                    ParentChannelDiscordId = e.ThreadAfter.ParentId ?? 0,
                    GuildDiscordId = e.Guild.Id,
                    EventType = ThreadEventType.Updated,
                    Name = e.ThreadAfter.Name,
                    OwnerDiscordId = e.ThreadAfter.CreatorId,
                    IsArchived = e.ThreadAfter.ThreadMetadata?.IsArchived ?? false,
                    IsLocked = e.ThreadAfter.ThreadMetadata?.IsLocked ?? false,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, ThreadDeletedEventArgs e)
    {
        await pipeline.Execute(e, "ThreadDeleted", nameof(ThreadEventHandler),
            e.Guild.Id, e.Thread.Id, e.Thread.CreatorId, async ctx =>
            {
                var channelUpsert = ctx.Services.GetRequiredService<ChannelUpsertService>();
                await channelUpsert.MarkDeletedAsync(e.Thread.Id, ctx.ReceivedAtUtc);

                ctx.Db.ThreadEvents.Add(new ThreadEventEntity
                {
                    ThreadDiscordId = e.Thread.Id,
                    ParentChannelDiscordId = e.Thread.ParentId ?? 0,
                    GuildDiscordId = e.Guild.Id,
                    EventType = ThreadEventType.Deleted,
                    Name = e.Thread.Name,
                    OwnerDiscordId = e.Thread.CreatorId,
                    IsArchived = e.Thread.ThreadMetadata?.IsArchived ?? false,
                    IsLocked = e.Thread.ThreadMetadata?.IsLocked ?? false,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, ThreadMembersUpdatedEventArgs e)
    {
        await pipeline.Execute(e, "ThreadMembersUpdated", nameof(ThreadEventHandler),
            e.Guild.Id, e.Thread.Id, null, async ctx =>
            {
                string? membersAddedJson = null;
                string? membersRemovedJson = null;

                if (e.AddedMembers.Count > 0)
                {
                    var addedMemberIds = e.AddedMembers.Select(m => m.Id).ToList();
                    membersAddedJson = JsonSerializer.Serialize(addedMemberIds);
                }

                if (e.RemovedMembers.Count > 0)
                {
                    var removedMemberIds = e.RemovedMembers.Select(m => m.Id).ToList();
                    membersRemovedJson = JsonSerializer.Serialize(removedMemberIds);
                }

                ctx.Db.ThreadEvents.Add(new ThreadEventEntity
                {
                    ThreadDiscordId = e.Thread.Id,
                    ParentChannelDiscordId = e.Thread.ParentId ?? 0,
                    GuildDiscordId = e.Guild.Id,
                    EventType = ThreadEventType.MembersUpdated,
                    Name = e.Thread.Name,
                    OwnerDiscordId = e.Thread.CreatorId,
                    IsArchived = e.Thread.ThreadMetadata?.IsArchived ?? false,
                    IsLocked = e.Thread.ThreadMetadata?.IsLocked ?? false,
                    MembersAddedJson = membersAddedJson,
                    MembersRemovedJson = membersRemovedJson,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();
            });
    }
}
