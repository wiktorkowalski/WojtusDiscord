using DSharpPlus.Entities;
using Xunit;

namespace DiscordEventService.Tests;

// DiscordConfirmationSurface can't be constructed without a live interaction, so this pins
// the one thing about it that a refactor could silently break: the mention-suppression verb.
// `WithAllowedMentions` exists only on DiscordMessageBuilder — the interaction, followup and
// webhook builders expose AddMentions, and passing Mentions.None is what makes Discord treat
// the payload's allowed-mentions as an explicit empty set. A staged action's description is
// model-authored, so the cancel path re-emitting it unsuppressed would ping the server.
public sealed class InteractionMentionSuppressionTests
{
    [Fact]
    public void MentionsNone_IsAnExplicitEmptySet_NotAnAbsentOne()
    {
        Assert.Empty(Mentions.None);
    }

    [Fact]
    public void InteractionResponseBuilder_SuppressesMentionsViaAddMentions()
    {
        var builder = new DiscordInteractionResponseBuilder()
            .WithContent("@everyone")
            .AddMentions(Mentions.None);

        Assert.NotNull(builder.Mentions);
        Assert.Empty(builder.Mentions);
    }

    [Fact]
    public void FollowupBuilder_SuppressesMentionsViaAddMentions()
    {
        var builder = new DiscordFollowupMessageBuilder()
            .WithContent("@everyone")
            .AddMentions(Mentions.None);

        Assert.NotNull(builder.Mentions);
        Assert.Empty(builder.Mentions);
    }

    [Fact]
    public void WebhookBuilder_SuppressesMentionsViaAddMentions()
    {
        var builder = new DiscordWebhookBuilder()
            .WithContent("@everyone")
            .AddMentions(Mentions.None);

        Assert.NotNull(builder.Mentions);
        Assert.Empty(builder.Mentions);
    }
}
