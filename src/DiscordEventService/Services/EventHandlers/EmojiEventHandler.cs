using System.Text.Json;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.EventHandlers;

internal sealed class EmojiEventHandler(EventPipeline pipeline) :
    IEventHandler<GuildEmojisUpdatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, GuildEmojisUpdatedEventArgs e)
    {
        await pipeline.Execute(e, "GuildEmojisUpdated", nameof(EmojiEventHandler),
            e.Guild.Id, null, null, async ctx =>
            {
                var guildUpsert = ctx.Services.GetRequiredService<GuildUpsertService>();
                var guildResult = await guildUpsert.UpsertGuildAsync(e.Guild);
                var guildGuid = guildResult.IsSuccess ? guildResult.Value : (Guid?)null;

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
                    if (existing is null)
                    {
                        ctx.Db.Emotes.Add(new EmoteEntity
                        {
                            DiscordId = emoji.Id,
                            GuildId = guildGuid,
                            Name = emoji.Name,
                            IsAnimated = emoji.IsAnimated,
                            IsAvailable = emoji.IsAvailable,
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

                // Soft-delete stale rows. EmojiEventHandler is now the single canonical writer of
                // Emotes for GuildEmojisUpdated (GuildEventHandler no longer touches the table), so
                // reconcile the full stored set: any of this guild's active emotes absent from
                // EmojisAfter is stale. This is broader than the before/after diff — it also reaps
                // rows the event's "before" snapshot omitted — and preserves the first deletion time.
                // When the guild FK didn't resolve, fall back to the before/after diff (by DiscordId)
                // so a transient guild-upsert miss still records the removals it can see.
                var staleQuery = ctx.Db.Emotes.Where(em => !em.IsDeleted);
                staleQuery = guildGuid is { } gid
                    ? staleQuery.Where(em => em.GuildId == gid && !afterIds.Contains(em.DiscordId))
                    : staleQuery.Where(em => removedIds.Contains(em.DiscordId));

                foreach (var stale in await staleQuery.ToListAsync())
                {
                    stale.IsDeleted = true;
                    stale.DeletedAtUtc ??= ctx.ReceivedAtUtc;
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
                    RawEventJson = ctx.RawJson,
                });

                await ctx.Db.SaveChangesAsync();
            });
    }
}
