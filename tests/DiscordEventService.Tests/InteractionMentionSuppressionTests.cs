using DSharpPlus.Entities;
using Xunit;

namespace DiscordEventService.Tests;

// DiscordConfirmationSurface can't be constructed without a live interaction, so this pins
// the library invariant its safety actually rests on. A staged action's description is
// model-authored and the cancel path re-emits it, so the payload must never let Discord
// parse mentions out of it.
//
// The mechanism is not the AddMentions call — it's the DEFAULT. DSharpPlus 5 starts every
// builder with an empty (never null) mention set; DiscordMentions maps an empty set to
// parse: [] ("no parsing"), and DiscordRestApiClient emits it because the collection is
// non-null. If a future version defaulted the collection to null or to Mentions.All, the
// interaction paths would start parsing @everyone out of model text — these tests go red.
public sealed class InteractionMentionSuppressionTests
{
    [Fact]
    public void MentionsNone_IsAnExplicitEmptySet_NotAnAbsentOne()
    {
        Assert.NotNull(Mentions.None);
        Assert.Empty(Mentions.None);
    }

    [Fact]
    public void InteractionResponseBuilder_SuppressesMentionsByDefault()
    {
        var untouched = new DiscordInteractionResponseBuilder().WithContent("@everyone");

        // Non-null is what makes the payload carry allowed_mentions at all; empty is what
        // makes it suppress-all. Null here would mean Mentions.All at the REST layer.
        Assert.NotNull(untouched.Mentions);
        Assert.Empty(untouched.Mentions);

        Assert.Empty(untouched.AddMentions(Mentions.None).Mentions);
    }

    [Fact]
    public void FollowupBuilder_SuppressesMentionsByDefault()
    {
        var untouched = new DiscordFollowupMessageBuilder().WithContent("@everyone");

        Assert.NotNull(untouched.Mentions);
        Assert.Empty(untouched.Mentions);

        Assert.Empty(untouched.AddMentions(Mentions.None).Mentions);
    }

    [Fact]
    public void WebhookBuilder_SuppressesMentionsByDefault()
    {
        var untouched = new DiscordWebhookBuilder().WithContent("@everyone");

        Assert.NotNull(untouched.Mentions);
        Assert.Empty(untouched.Mentions);

        Assert.Empty(untouched.AddMentions(Mentions.None).Mentions);
    }

    [Fact]
    public void MessageBuilder_SuppressesMentionsByDefault()
    {
        // The same default is what makes the repo's existing WithAllowedMentions(Mentions.None)
        // calls (DiscordTurnSurface, MemeCommand, ConfirmationService) declarations rather than
        // mechanism — worth pinning here so a library change surfaces in one place.
        var untouched = new DiscordMessageBuilder().WithContent("@everyone");

        Assert.NotNull(untouched.Mentions);
        Assert.Empty(untouched.Mentions);
    }
}
