using DiscordEventService.Configuration;
using DiscordEventService.Services.Conversation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiscordEventService.Tests;

// The confirm-button primitives the §6 click handler leans on: parsing only our own custom ids,
// and the claim-once (double-click) guard. These are pure, so no Discord client is needed — the
// store operations never touch it.
public sealed class ConfirmationServiceTests
{
    [Theory]
    // expectedKind: "confirm" / "cancel" / "" (don't care when the id isn't ours)
    [InlineData("conv6:confirm:abc123", true, "confirm", "abc123")]
    [InlineData("conv6:cancel:def456", true, "cancel", "def456")]
    [InlineData("conv6:confirm:", false, "", "")]
    [InlineData("someoneelses:button", false, "", "")]
    [InlineData("", false, "", "")]
    public void TryParseCustomId_ClassifiesOnlyOurButtons(
        string customId, bool expectedOk, string expectedKind, string expectedToken)
    {
        var ok = ConfirmationService.TryParseCustomId(customId, out var kind, out var token);

        Assert.Equal(expectedOk, ok);
        if (expectedOk)
        {
            Assert.Equal(expectedKind, kind == ConfirmKind.Confirm ? "confirm" : "cancel");
            Assert.Equal(expectedToken, token);
        }
    }

    [Fact]
    public void TryClaim_FirstCallWins_SecondReturnsFalse()
    {
        var service = NewService();
        var pending = service.Register(requesterId: 99UL, "Ban someone", _ => Task.FromResult("banned"));

        Assert.True(service.TryClaim(pending.Token, out var claimed));
        Assert.Equal("Ban someone", claimed.Description);
        Assert.Equal(99UL, claimed.RequesterId);

        // A double-click finds nothing — the action can never run twice.
        Assert.False(service.TryClaim(pending.Token, out _));
    }

    [Fact]
    public void TryClaim_UnknownToken_ReturnsFalse()
    {
        var service = NewService();

        Assert.False(service.TryClaim("never-registered", out _));
    }

    // A failed post (here: no Discord client yet) drops the action, so nothing stays confirmable
    // behind a card the model said it couldn't post (#455).
    [Fact]
    public async Task StageAsync_PostFails_ReturnsCouldNotPost_AndLeavesNothingPending()
    {
        var service = new ConfirmationService(
            new DiscordClientAccessor(), Options.Create(new ConversationOptions()), NullLogger<ConfirmationService>.Instance);

        var result = await service.StageAsync(
            channelId: 1UL, requesterId: 2UL, "tester", "Kick someone", _ => Task.FromResult("kicked"),
            CancellationToken.None);

        Assert.Equal("I couldn't post the confirmation prompt, so I didn't do anything.", result);
        Assert.Equal(0, service.PendingCount);
    }

    // ExpireAsync's store half: an expired action can't be claimed, and it reads as unknown —
    // not handled — so a late click retires the card.
    [Fact]
    public void TryExpire_DropsAnUnclaimedAction_AsUnknown()
    {
        var service = NewService();
        var pending = service.Register(requesterId: 99UL, "Ban someone", _ => Task.FromResult("banned"));

        Assert.True(service.TryExpire(pending.Token));
        Assert.False(service.TryClaim(pending.Token, out _));
        Assert.False(service.WasHandled(pending.Token));
        Assert.False(service.TryExpire(pending.Token));
    }

    [Fact]
    public void TryExpire_AfterAClaim_FindsNothing_AndTheTokenReadsAsHandled()
    {
        var service = NewService();
        var pending = service.Register(requesterId: 99UL, "Ban someone", _ => Task.FromResult("banned"));

        Assert.True(service.TryClaim(pending.Token, out _));

        Assert.False(service.TryExpire(pending.Token));
        Assert.True(service.WasHandled(pending.Token));
        Assert.False(service.WasHandled("never-registered"));
    }

    private static ConfirmationService NewService() =>
        new(clientAccessor: null!, Options.Create(new ConversationOptions()), NullLogger<ConfirmationService>.Instance);
}

public sealed class ConversationOptionsAdminTests
{
    [Fact]
    public void IsAdmin_TrueOnlyForListedUsers()
    {
        var options = new ConversationOptions { AdminUserIds = [1UL, 2UL, 3UL] };

        Assert.True(options.IsAdmin(2UL));
        Assert.False(options.IsAdmin(4UL));
    }

    [Fact]
    public void IsAdmin_EmptyAllowList_RefusesEveryone()
    {
        var options = new ConversationOptions { AdminUserIds = [] };

        Assert.False(options.IsAdmin(1UL));
    }
}
