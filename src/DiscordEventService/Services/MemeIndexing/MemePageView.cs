using DSharpPlus.Entities;

namespace DiscordEventService.Services.MemeIndexing;

// One /meme page as Discord shows it: the text and the paging buttons (#391). The command and
// the button handler both build their message from this, so a page looks the same from either.
internal sealed record MemePageView(string Content, IReadOnlyList<DiscordButtonComponent> Buttons)
{
    public const int PageSize = MemeSearchService.DefaultLimit;

    // A line is a jump link of at most 91 characters, a space and the label: 5 lines and the
    // header stay under 1,000 characters, well inside Discord's 2,000 for a message.
    internal const int MaxLabelLength = 100;

    // The shortest text that may be a whole label, the full stop included.
    internal const int MinSentenceLength = 25;

    public const string PreviousLabel = "Poprzednie";
    public const string NextLabel = "Następne";

    public static MemePageView Render(ulong guildId, string query, int offset, MemeSearchPage page)
    {
        if (page.Hits.Count == 0)
        {
            // A page past the end: memes went away after the button was drawn. The way back stays.
            return offset == 0
                ? new MemePageView("Nic nie znalazłem dla tego zapytania.", [])
                : new MemePageView("Tu nie ma już wyników.", PagingButtons(query, offset, hasPrevious: true, hasNext: false));
        }

        // Bare URLs render as pills (markdown links never do); message links
        // produce pills, not unfurled preview embeds.
        var lines = page.Hits.Select(h => $"{JumpLink(guildId, h.ChannelDiscordId, h.MessageDiscordId)} {HitLabel(h)}");
        var header = $"{offset + 1}–{offset + page.Hits.Count} z {page.Total}";

        return new MemePageView(
            string.Join("\n", lines.Prepend(header)),
            PagingButtons(query, offset, hasPrevious: offset > 0, hasNext: offset + PageSize < page.Total));
    }

    // Descriptions are model-generated text landing in plain content — meme OCR could
    // contain @everyone — so all mentions are explicitly disarmed.
    public T ApplyTo<T>(T builder)
        where T : BaseDiscordMessageBuilder<T>
    {
        builder.WithContent(Content).AddMentions(Mentions.None);
        return Buttons.Count > 0 ? builder.AddComponents(Buttons) : builder;
    }

    private static List<DiscordButtonComponent> PagingButtons(string query, int offset, bool hasPrevious, bool hasNext)
    {
        // A query too long for a custom id has no buttons on any page: a way forward that ends
        // on a later page would be a dead end.
        if (!MemePageCustomId.CanPage(query))
            return [];

        List<DiscordButtonComponent> buttons = [];
        if (hasPrevious)
            buttons.Add(PageButton(Math.Max(0, offset - PageSize), query, PreviousLabel));
        if (hasNext)
            buttons.Add(PageButton(offset + PageSize, query, NextLabel));

        return buttons;
    }

    private static DiscordButtonComponent PageButton(int offset, string query, string label) =>
        new(DiscordButtonStyle.Secondary, MemePageCustomId.Build(offset, query), label);

    // Also the jump link of the dashboard's meme page (#395).
    internal static string JumpLink(ulong guildId, ulong channelId, ulong messageId) =>
        $"https://discord.com/channels/{guildId}/{channelId}/{messageId}";

    // What tells one hit from the next: the description's first sentence. Tags are not the
    // label: they repeat the query on every line (#391).
    internal static string HitLabel(MemeSearchHit hit)
    {
        var text = !string.IsNullOrWhiteSpace(hit.DescriptionPl) ? hit.DescriptionPl
            : !string.IsNullOrWhiteSpace(hit.DescriptionEn) ? hit.DescriptionEn
            : hit.FileName;
        return Truncate(FirstSentence(text.ReplaceLineEndings(" ").Trim()), MaxLabelLength);
    }

    // A sentence end counts only from MinSentenceLength: an abbreviation early in the text
    // ("Mem z np. kotem") must not cut the label to a fragment.
    private static string FirstSentence(string text)
    {
        if (text.Length < MinSentenceLength)
            return text;

        var end = text.IndexOf(". ", MinSentenceLength - 1, StringComparison.Ordinal);
        return end < 0 ? text : text[..(end + 1)];
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";
}
