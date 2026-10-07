using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Infrastructure;
using DiscordEventService.Services.EventHandlers;
using Xunit;
using VoiceFlags = DiscordEventService.Services.EventHandlers.VoiceEventHandler.VoiceFlags;

namespace DiscordEventService.Tests;

// The label values of the message and voice counters. Both come from the event alone, so the
// rules are pure functions and need no gateway event.
public sealed class VoiceAndMessageSignalTests
{
    private static readonly VoiceFlags Quiet = default;

    [Theory]
    [InlineData(12, 1, 1, 1, "attachment")]
    [InlineData(12, 0, 1, 1, "sticker")]
    [InlineData(12, 0, 0, 1, "embed")]
    [InlineData(12, 0, 0, 0, "text")]
    [InlineData(0, 0, 0, 0, "empty")]
    public void MessageKindOf_NamesTheFirstThingTheMessageCarries(
        int length, int attachments, int stickers, int embeds, string expected) =>
        Assert.Equal(expected, MessageEventHandler.MessageKindOf(length, attachments, stickers, embeds));

    // The message counters have no label a test can own, so the length is the marker.
    [Fact]
    public void MessageCreated_CountsOneMessageWithItsLabelsLengthAndAttachments()
    {
        const int length = 3917;
        using var metrics = new MetricsCapture();

        BotMetrics.MessageCreated("attachment", fromBot: true, isReply: true, length, attachmentCount: 7);

        Assert.Contains(metrics.Of("wojtus.messages", "kind", "attachment"),
            m => Equals(m.Tags["author"], "bot") && Equals(m.Tags["reply"], "true") && m.Value == 1);
        Assert.Contains(metrics.Of("wojtus.message.length"), m => m.Value == length);
        Assert.Contains(metrics.Of("wojtus.message.attachments"), m => m.Value == 7);
    }

    [Fact]
    public void ChangesOf_JoinAndLeave_AreOneChangeWhateverTheFlags()
    {
        var loud = new VoiceFlags(Muted: true, Deafened: true, Streaming: true, Video: true);

        Assert.Equal(["join"], VoiceEventHandler.ChangesOf(VoiceEventType.Joined, Quiet, loud));
        Assert.Equal(["leave"], VoiceEventHandler.ChangesOf(VoiceEventType.Left, loud, Quiet));
    }

    [Fact]
    public void ChangesOf_StateChange_NamesEveryFlagThatFlipped()
    {
        var on = new VoiceFlags(Muted: true, Deafened: true, Streaming: true, Video: true);

        Assert.Equal(["mute", "deafen", "stream_start", "video_start"],
            VoiceEventHandler.ChangesOf(VoiceEventType.StateChanged, Quiet, on));
        Assert.Equal(["unmute", "undeafen", "stream_stop", "video_stop"],
            VoiceEventHandler.ChangesOf(VoiceEventType.StateChanged, on, Quiet));
    }

    [Fact]
    public void ChangesOf_Move_IsCountedWithTheFlagsThatFlippedInIt()
    {
        var muted = Quiet with { Muted = true };

        Assert.Equal(["move"], VoiceEventHandler.ChangesOf(VoiceEventType.Moved, muted, muted));
        Assert.Equal(["move", "unmute"], VoiceEventHandler.ChangesOf(VoiceEventType.Moved, muted, Quiet));
    }

    [Fact]
    public void ChangesOf_StateChangeWithNoFlagFlipped_IsOther() =>
        Assert.Equal(["other"], VoiceEventHandler.ChangesOf(VoiceEventType.StateChanged, Quiet, Quiet));

    [Fact]
    public void VoiceStateChanged_CountsTheChangeAsItsLabel()
    {
        using var metrics = new MetricsCapture();

        BotMetrics.VoiceStateChanged("stream_start");

        Assert.NotEmpty(metrics.Of("wojtus.voice.state.changes", "change", "stream_start"));
    }
}
