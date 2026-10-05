using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services.MemeIndexing;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

// The Następne / Poprzednie buttons under a /meme answer (#391). The click carries the query and
// the offset in its custom id, so nothing is kept between clicks. Anyone may turn the page.
internal sealed class MemePageComponentHandler(MemeSearchService searchService, ILogger<MemePageComponentHandler> logger)
    : IEventHandler<ComponentInteractionCreatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, ComponentInteractionCreatedEventArgs e)
    {
        // Every component interaction in the guild reaches this — only act on our own buttons.
        // No acknowledgement for the others: that slot belongs to their own handler.
        if (!MemePageCustomId.TryParse(e.Id, out var offset, out var query))
            return;

        // /meme answers only in a guild, so its buttons exist only there.
        if (e.Interaction.GuildId is not { } guildId)
            return;

        try
        {
            // Acknowledge first: the search can exceed Discord's 3s window.
            await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);

            var caller = new MemeSearchCaller(MemeSearchSource.PageButton, e.Channel.Id, e.User.Id);
            var page = await searchService.SearchPageAsync(
                guildId, query, offset, MemePageView.PageSize, caller, CancellationToken.None);

            // No query text here: meme_search_log holds it, with the ranked hits (#384).
            logger.LogInformation(
                "/meme page by {UserId} in guild {GuildId}: offset {Offset}, {HitCount} hits of {TotalCount}",
                e.User.Id, guildId, offset, page.Hits.Count, page.Total);

            await e.Interaction.EditOriginalResponseAsync(
                MemePageView.Render(guildId, query, offset, page).ApplyTo(new DiscordWebhookBuilder()));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "/meme page failed at offset {Offset} in guild {GuildId}", offset, guildId);
            await TellClickerAsync(e);
        }
    }

    // Only the clicker sees it, and the page under the button stays as it was.
    private async Task TellClickerAsync(ComponentInteractionCreatedEventArgs e)
    {
        try
        {
            await e.Interaction.CreateFollowupMessageAsync(
                new DiscordFollowupMessageBuilder()
                    .AsEphemeral()
                    .WithContent("Coś poszło nie tak przy zmianie strony — spróbuj jeszcze raz.")
                    .AddMentions(Mentions.None));
        }
        catch (Exception ex)
        {
            // The acknowledgement itself failed: there is no interaction left to answer.
            logger.LogWarning(ex, "/meme page failure message was not delivered");
        }
    }
}
