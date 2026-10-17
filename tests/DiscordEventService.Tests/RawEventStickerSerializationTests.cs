using DiscordEventService.Services;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Xunit;

namespace DiscordEventService.Tests;

// #466: MessageUpdated with a sticker failed to serialize — DiscordMessageSticker.Guild is a
// cache lookup that throws when the sticker's guild isn't cached (a sticker from another server),
// so the raw log kept only the failure stub and the payload was lost.
public sealed class RawEventStickerSerializationTests
{
    [Fact]
    public void SerializeEvent_StickerFromUncachedGuild_KeepsPayload()
    {
        var sticker = Hydrate<DiscordMessageSticker>(new
        {
            id = "1234",
            name = "wojak",
            guild_id = "999",
            type = 2,
            format_type = 1,
            available = true,
        });

        var result = NewService().SerializeEvent(new { Sticker = sticker });

        Assert.False(result.Failed, result.Error?.ToString());
        Assert.Contains("\"name\":\"wojak\"", result.Json);
        // The guild id is plain data and still lands; only the throwing navigation is dropped.
        Assert.Contains("\"guild_id\":999", result.Json);
        Assert.DoesNotContain("\"guild\":", result.Json);
    }

    [Fact]
    public void SerializeEvent_MessageWithStickers_KeepsPayload()
    {
        var message = Hydrate<DiscordMessage>(new
        {
            id = "42",
            channel_id = "7",
            guild_id = "1",
            content = "edited",
            author = new { id = "5", username = "wojtus" },
            sticker_items = new[] { new { id = "1234", name = "wojak", format_type = 1 } },
            stickers = new[] { new { id = "1234", name = "wojak", guild_id = "999", type = 2, format_type = 1 } },
        });

        var result = NewService().SerializeEvent(new { Message = message });

        Assert.False(result.Failed, result.Error?.ToString());
        Assert.Contains("\"content\":\"edited\"", result.Json);
        Assert.Contains("\"name\":\"wojak\"", result.Json);
    }

    private static T Hydrate<T>(object fields)
        => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(fields))
           ?? throw new InvalidOperationException($"{typeof(T).Name} deserialization returned null");

    // dbContext/logger are unused by SerializeEvent, so no DB needed here.
    private static RawEventLogService NewService()
        => new RawEventLogService(null!, NullLogger<RawEventLogService>.Instance);
}
