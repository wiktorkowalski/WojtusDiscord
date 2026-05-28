using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DiscordEventService.Services.EventHandlers;

public sealed class EmojiEventHandler(EventPipeline pipeline) :
    IEventHandler<GuildEmojisUpdatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, GuildEmojisUpdatedEventArgs e)
    {
        await pipeline.Execute(e, "GuildEmojisUpdated", nameof(EmojiEventHandler),
            e.Guild.Id, null, null, async ctx =>
            {
                var guildUpsert = ctx.Services.GetRequiredService<GuildUpsertService>();
                var guildGuid = await guildUpsert.UpsertGuildAsync(e.Guild);

                var beforeIds = e.EmojisBefore.Keys.ToHashSet();
                var afterIds = e.EmojisAfter.Keys.ToHashSet();

                var addedIds = afterIds.Except(beforeIds).ToList();
                var removedIds = beforeIds.Except(afterIds).ToList();
                var updatedIds = beforeIds.Intersect(afterIds)
                    .Where(id => e.EmojisBefore[id].Name != e.EmojisAfter[id].Name)
                    .ToList();

                foreach (var emoji in e.EmojisAfter.Values)
                {
                    var existing = await ctx.Db.Emotes
                        .Where(em => em.DiscordId == emoji.Id)
                        .FirstOrDefaultAsync();
                    if (existing == null)
                    {
                        ctx.Db.Emotes.Add(new EmoteEntity
                        {
                            DiscordId = emoji.Id,
                            GuildId = guildGuid != Guid.Empty ? guildGuid : null,
                            Name = emoji.Name,
                            IsAnimated = emoji.IsAnimated,
                            IsAvailable = emoji.IsAvailable
                        });
                    }
                    else
                    {
                        existing.Name = emoji.Name;
                        existing.IsAnimated = emoji.IsAnimated;
                        existing.IsAvailable = emoji.IsAvailable;
                        existing.IsDeleted = false;
                        existing.DeletedAtUtc = null;
                    }
                }

                foreach (var id in removedIds)
                {
                    await ctx.Db.Emotes.Where(em => em.DiscordId == id)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(em => em.IsDeleted, true)
                            .SetProperty(em => em.DeletedAtUtc, (DateTime?)ctx.ReceivedAtUtc));
                }

                ctx.Db.EmojiEvents.Add(new EmojiEventEntity
                {
                    GuildDiscordId = e.Guild.Id,
                    EmojisAddedJson = addedIds.Count > 0
                        ? JsonSerializer.Serialize(addedIds.Select(id => new { Id = id, e.EmojisAfter[id].Name }))
                        : null,
                    EmojisRemovedJson = removedIds.Count > 0
                        ? JsonSerializer.Serialize(removedIds.Select(id => new { Id = id, e.EmojisBefore[id].Name }))
                        : null,
                    EmojisUpdatedJson = updatedIds.Count > 0
                        ? JsonSerializer.Serialize(updatedIds.Select(id => new { Id = id, NameBefore = e.EmojisBefore[id].Name, NameAfter = e.EmojisAfter[id].Name }))
                        : null,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();
            });
    }
}
