using System.ComponentModel;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services.MemeIndexing;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;

namespace DiscordEventService.Commands;

public sealed class MemeCommand(MemeSearchService searchService, ILogger<MemeCommand> logger)
{
    [Command("meme")]
    [Description("Szuka mema po opisie, tekście z obrazka, tagach, szablonie, osobie albo tytule gry, filmu, serialu")]
    public async ValueTask ExecuteAsync(
        SlashCommandContext ctx,
        [Description("Co znaleźć — np. \"kot lodówka\" albo tekst z mema")] string query)
    {
        // Defer immediately: the search can exceed Discord's 3s window.
        await ctx.DeferResponseAsync();

        try
        {
            await RespondWithSearchAsync(ctx, query);
        }
        catch (Exception ex)
        {
            // No query text here either: what a person typed stays out of the application log.
            logger.LogError(ex, "/meme failed in guild {GuildId}", ctx.Guild?.Id);
            await ctx.EditResponseAsync("Coś poszło nie tak przy szukaniu — spróbuj jeszcze raz.");
        }
    }

    private async Task RespondWithSearchAsync(SlashCommandContext ctx, string query)
    {
        if (ctx.Guild is null)
        {
            await ctx.EditResponseAsync("Ta komenda działa tylko na serwerze.");
            return;
        }

        var caller = new MemeSearchCaller(MemeSearchSource.SlashCommand, ctx.Channel.Id, ctx.User.Id);
        var page = await searchService.SearchPageAsync(
            ctx.Guild.Id, query, offset: 0, MemePageView.PageSize, caller, CancellationToken.None);

        // No query text here: meme_search_log holds it, with the ranked hits (#384).
        logger.LogInformation(
            "/meme by {UserId} in guild {GuildId}: {HitCount} hits of {TotalCount}",
            ctx.User.Id, ctx.Guild.Id, page.Hits.Count, page.Total);

        // The text and the paging buttons are MemePageView's, shared with the button handler (#391).
        await ctx.EditResponseAsync(
            MemePageView.Render(ctx.Guild.Id, query, offset: 0, page).ApplyTo(new DiscordMessageBuilder()));
    }
}
