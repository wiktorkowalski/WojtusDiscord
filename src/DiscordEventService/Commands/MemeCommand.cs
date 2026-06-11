using System.ComponentModel;
using DiscordEventService.Services.MemeIndexing;
using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;

namespace DiscordEventService.Commands;

// #224: the bot's first user-facing command. /meme searches the Indexed meme
// metadata (MemeSearchService) and responds publicly: top hit as an embed with
// the image re-fetched for a fresh signed CDN URL (stored URLs expire,
// ADR-0004), runners-up as compact jump links. Every failure path answers the
// interaction — a swallowed exception here would leave a dangling "thinking…"
// state, but can never touch the gateway event pipeline.
public sealed class MemeCommand(MemeSearchService searchService, ILogger<MemeCommand> logger)
{
    private const int RunnerUpCount = 4;

    [Command("meme")]
    [Description("Szuka mema po opisie, tekście z obrazka, tagach lub szablonie")]
    public async ValueTask ExecuteAsync(
        SlashCommandContext ctx,
        [Description("Co znaleźć — np. \"kot lodówka\" albo tekst z mema")] string query)
    {
        // Defer immediately: the search plus the fresh-URL message fetch can
        // exceed Discord's 3s interaction window.
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

        // The raw query never goes into plain message content — pinging
        // mentions could be smuggled in it (embeds are immune, content isn't).
        if (hits.Count == 0)
        {
            await ctx.EditResponseAsync("Nic nie znalazłem dla tego zapytania.");
            return;
        }

        // Top-1 needs a live message for the fresh image URL; an index row can
        // outlive its message (just deleted, attachment edited away) — fall
        // back to the next hit instead of rendering a dead embed.
        MemeSearchHit? top = null;
        string? freshUrl = null;
        var runnersUp = new List<MemeSearchHit>();
        foreach (var hit in hits)
        {
            if (top is not null)
            {
                runnersUp.Add(hit);
                continue;
            }

            freshUrl = await TryGetFreshUrlAsync(ctx.Client, hit);
            if (freshUrl is null)
            {
                logger.LogWarning(
                    "/meme top hit {AttachmentId} in guild {GuildId} has no live message/attachment, falling back",
                    hit.AttachmentDiscordId, ctx.Guild.Id);
                continue;
            }
            top = hit;
        }

        if (top is null)
        {
            await ctx.EditResponseAsync("Znalazłem trafienia, ale ich wiadomości już nie istnieją.");
            return;
        }

        var embed = new DiscordEmbedBuilder()
            .WithDescription($"{Snippet(top, 300)}\n[Skocz do wiadomości]({JumpLink(ctx.Guild.Id, top)})")
            .WithImageUrl(freshUrl!)
            .WithFooter(top.FileName)
            .WithTimestamp(top.MessageCreatedAtUtc)
            .WithColor(new DiscordColor(0x5865F2));

        if (runnersUp.Count > 0)
        {
            var lines = runnersUp
                .Take(RunnerUpCount)
                .Select(h => $"[{Snippet(h, 80)}]({JumpLink(ctx.Guild.Id, h)})");
            embed.AddField("Inne trafienia", string.Join("\n", lines));
        }

        await ctx.EditResponseAsync(new DiscordMessageBuilder().AddEmbed(embed));
    }

    // Fresh signed CDN URL via a live message fetch; null when the message,
    // channel, or the specific attachment is gone (or the bot lost access).
    private static async Task<string?> TryGetFreshUrlAsync(DiscordClient client, MemeSearchHit hit)
    {
        try
        {
            var channel = await client.GetChannelAsync(hit.ChannelDiscordId);
            var message = await channel.GetMessageAsync(hit.MessageDiscordId);
            return message.Attachments.FirstOrDefault(a => a.Id == hit.AttachmentDiscordId)?.Url;
        }
        catch (NotFoundException)
        {
            return null;
        }
        catch (UnauthorizedException)
        {
            return null;
        }
    }

    private static string JumpLink(ulong guildId, MemeSearchHit hit) =>
        $"https://discord.com/channels/{guildId}/{hit.ChannelDiscordId}/{hit.MessageDiscordId}";

    // The "why it matched" line: Polish description first (the corpus is a
    // Polish guild), English fallback, file name as a last resort. Square
    // brackets would break the surrounding markdown link labels.
    private static string Snippet(MemeSearchHit hit, int maxLength)
    {
        var text = hit.DescriptionPl ?? hit.DescriptionEn ?? hit.FileName;
        text = text.Replace('[', '(').Replace(']', ')');
        return Truncate(text, maxLength);
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";
}
