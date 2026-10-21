using DiscordEventService.Jobs;
using DSharpPlus.Entities;
using Newtonsoft.Json;
using Xunit;

namespace DiscordEventService.Tests;

// #363: a reconnect backfill fetched reactions for the whole 100-message batch of every channel,
// whatever the window. With a window set, only messages inside it get reaction calls.
public sealed class ReactionsBackfillWindowTests
{
    private static readonly DateTime After = new(2026, 10, 2, 15, 22, 54, DateTimeKind.Utc);

    [Fact]
    public void MessagesNeedingReactions_WithWindow_KeepsOnlyMessagesInsideIt()
    {
        var batch = new[]
        {
            Message(1, After.AddMinutes(2), withReactions: true),
            Message(2, After, withReactions: true),
            Message(3, After.AddMinutes(1), withReactions: false),
            Message(4, After.AddSeconds(-1), withReactions: true),
            Message(5, After.AddDays(-3), withReactions: true),
        };

        var ids = ReactionsBackfillJob.MessagesNeedingReactions(batch, After).Select(m => m.Id);

        Assert.Equal([1UL, 2UL], ids);
    }

    [Fact]
    public void MessagesNeedingReactions_FullRun_KeepsEveryMessageWithReactions()
    {
        var batch = new[]
        {
            Message(1, After, withReactions: true),
            Message(2, After.AddDays(-3), withReactions: true),
            Message(3, After.AddDays(-3), withReactions: false),
        };

        var ids = ReactionsBackfillJob.MessagesNeedingReactions(batch, afterTimestampUtc: null).Select(m => m.Id);

        Assert.Equal([1UL, 2UL], ids);
    }

    private static DiscordMessage Message(ulong id, DateTime timestampUtc, bool withReactions)
        => JsonConvert.DeserializeObject<DiscordMessage>(JsonConvert.SerializeObject(new
        {
            id = id.ToString(),
            channel_id = "7",
            timestamp = timestampUtc.ToString("O"),
            author = new { id = "5", username = "wojtus" },
            reactions = withReactions
                ? new[] { new { count = 1, me = false, emoji = new { name = "👍" } } }
                : [],
        }))!;
}
