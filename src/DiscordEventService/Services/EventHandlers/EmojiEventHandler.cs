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
        await pipeline.ExecuteAsync(e, "GuildEmojisUpdated", nameof(EmojiEventHandler),
            e.Guild.Id, null, null, async ctx =>
            {
                var guildUpsert = ctx.Services.GetRequiredService<GuildUpsertService>();
                var emoteUpsert = ctx.Services.GetRequiredService<EmoteUpsertService>();
                var guildResult = await guildUpsert.UpsertGuildAsync(e.Guild);
                var guildGuid = guildResult.IsSuccess ? guildResult.Value : (Guid?)null;

                var beforeIds = e.EmojisBefore.Keys.ToHashSet();
                var afterIds = e.EmojisAfter.Keys.ToHashSet();

                var addedIds = afterIds.Except(beforeIds).ToList();
                var removedIds = beforeIds.Except(afterIds).ToList();
                var updatedIds = beforeIds.Intersect(afterIds)
                    .Where(id => e.EmojisBefore[id].Name != e.EmojisAfter[id].Name)
                    .ToList();

                // Emote rows + event row must commit together. ExecutionStrategy is required
                // because EnableRetryOnFailure is configured on the DbContext.
                var strategy = ctx.Db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    // Reset the tracker so an EnableRetryOnFailure retry doesn't re-stage entities
                    // left Added by a rolled-back attempt.
                    ctx.Db.ChangeTracker.Clear();
                    await using var tx = await ctx.Db.Database.BeginTransactionAsync();

                    foreach (var emoji in e.EmojisAfter.Values)
                        await emoteUpsert.UpsertEmoteAsync(emoji, guildGuid);

                    await SoftDeleteStaleEmotesAsync(ctx, guildGuid, afterIds, removedIds);

                    ctx.Db.EmojiEvents.Add(BuildEmojiEvent(e, ctx, addedIds, removedIds, updatedIds));
                    await ctx.Db.SaveChangesAsync();
                    await tx.CommitAsync();
                });
            });
    }

    // Soft-delete stale rows. EmojiEventHandler is now the single canonical writer of
    // Emotes for GuildEmojisUpdated (GuildEventHandler no longer touches the table), so
    // reconcile the full stored set: any of this guild's active emotes absent from
    // EmojisAfter is stale. This is broader than the before/after diff — it also reaps
    // rows the event's "before" snapshot omitted — and preserves the first deletion time.
    // When the guild FK didn't resolve, fall back to the before/after diff (by DiscordId)
    // so a transient guild-upsert miss still records the removals it can see.
    private static async Task SoftDeleteStaleEmotesAsync(
        EventContext ctx, Guid? guildGuid, HashSet<ulong> afterIds, List<ulong> removedIds)
    {
        var staleQuery = ctx.Db.Emotes.Where(em => !em.IsDeleted);
        staleQuery = guildGuid is { } gid
            ? staleQuery.Where(em => em.GuildId == gid && !afterIds.Contains(em.DiscordId))
            : staleQuery.Where(em => removedIds.Contains(em.DiscordId));

        foreach (var stale in await staleQuery.ToListAsync())
        {
            stale.IsDeleted = true;
            stale.DeletedAtUtc ??= ctx.ReceivedAtUtc;
        }
    }

    private static EmojiEventEntity BuildEmojiEvent(
        GuildEmojisUpdatedEventArgs e,
        EventContext ctx,
        List<ulong> addedIds,
        List<ulong> removedIds,
        List<ulong> updatedIds) => new EmojiEventEntity
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
        };
}
