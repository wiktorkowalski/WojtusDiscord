using System.ComponentModel;
using DiscordEventService.Services.MemeIndexing;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;

namespace DiscordEventService.Commands;

public sealed class MemeCommand(MemeSearchService searchService, ILogger<MemeCommand> logger)
{
    [Command("meme")]
    [Description("Szuka mema po opisie, tekście z obrazka, tagach lub szablonie")]
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
            logger.LogError(ex, "/meme failed for query {Query} in guild {GuildId}", query, ctx.Guild?.Id);
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

        var hits = await searchService.SearchAsync(
            ctx.Guild.Id, query, MemeSearchService.DefaultLimit, CancellationToken.None);

        logger.LogInformation(
            "/meme by {UserId} in guild {GuildId}: query {Query}, {HitCount} hits",
            ctx.User.Id, ctx.Guild.Id, query, hits.Count);

        if (hits.Count == 0)
        {
            await ctx.EditResponseAsync("Nic nie znalazłem dla tego zapytania.");
            return;
        }

        // Bare URLs render as pills (markdown links never do); message links
        // produce pills, not unfurled preview embeds. Descriptions are
        // model-generated text landing in plain content — meme OCR could
        // contain @everyone — so all mentions are explicitly disarmed.
        var lines = hits.Select(h => $"{JumpLink(ctx.Guild.Id, h)} {HitLabel(h)}");

        await ctx.EditResponseAsync(new DiscordMessageBuilder()
            .WithContent(string.Join("\n", lines))
            .WithAllowedMentions(Mentions.None));
    }

    private static string JumpLink(ulong guildId, MemeSearchHit hit) =>
        $"https://discord.com/channels/{guildId}/{hit.ChannelDiscordId}/{hit.MessageDiscordId}";

    // The "why it matched" label: tags (short, keyword-y, and the A-weighted
    // search field — they usually contain the matched term). Rows without tags
    // fall back to the description's first sentence, then the file name.
    private static string HitLabel(MemeSearchHit hit)
    {
        if (hit.Tags.Length > 0)
            return Truncate(string.Join(" · ", hit.Tags), 120);

        var text = (hit.DescriptionPl ?? hit.DescriptionEn ?? hit.FileName).ReplaceLineEndings(" ");
        return Truncate(FirstSentence(text), 80);
    }

    private static string FirstSentence(string text)
    {
        var end = text.IndexOf(". ", StringComparison.Ordinal);
        return end < 0 ? text : text[..(end + 1)];
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";
}
