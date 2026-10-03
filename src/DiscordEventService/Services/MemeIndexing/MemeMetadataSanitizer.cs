using System.Globalization;
using System.Text;
using DiscordEventService.Data.Entities.Core;

namespace DiscordEventService.Services.MemeIndexing;

// The no-names-on-cut-outs rule (#368). Models guess an identity for a cut-out face or an emote
// of a server member; under max-aggregated search one such annotation is a false name hit.
// Called from the one annotation write seam, so every writer passes through it.
internal static class MemeMetadataSanitizer
{
    // "Jan" names a person, "z" in "Hed z TVGry" does not.
    private const int MinNameTokenLength = 3;

    // Polish inflects names: "Gonciarz" -> "Gonciarzem", and search matches those forms on purpose
    // (trigram side). A word that is a name plus a short ending names the person too. Short names
    // are matched whole: "jan" must not take "janusz" with it.
    private const int MinInflectedNameLength = 5;
    private const int MaxEndingLength = 4;

    // Returns the same instance when there is nothing to drop — the caller uses that to keep
    // the model's verbatim output.
    public static MemeMetadata Sanitize(MemeMetadata metadata)
    {
        if (metadata.ImageKind != MemeImageKind.CutoutFaceOrEmote || metadata.People.Length == 0)
            return metadata;

        // Before the people go: their names are what the other fields get matched against.
        var fullNames = metadata.People.Select(p => Compact(p.Name)).Where(n => n.Length > 0).ToHashSet(StringComparer.Ordinal);

        // The space-separated words too, glued: "Korwin-Mikke" is also written "korwinmikke".
        var nameTokens = metadata.People
            .SelectMany(p => Tokens(p.Name).Concat(p.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Compact)))
            .Where(t => t.Length >= MinNameTokenLength)
            .ToHashSet(StringComparer.Ordinal);

        bool IsNameToken(string token) =>
            nameTokens.Contains(token) || nameTokens.Any(name => IsInflectedFormOf(name, token));

        bool NamesAPerson(string value) =>
            fullNames.Contains(Compact(value)) || Tokens(value).Any(IsNameToken);

        // Every weight-A field a name can sit in. Descriptions are prose: the prompt forbids the
        // name there, code cannot cut it out reliably.
        return metadata with
        {
            People = [],
            Tags = [.. metadata.Tags.Where(t => !NamesAPerson(t))],
            Templates = [.. metadata.Templates.Where(t => !NamesAPerson(t))],
            SearchPhrases = [.. metadata.SearchPhrases.Where(p => !NamesAPerson(p))],
            Franchise = metadata.Franchise is { } franchise && NamesAPerson(franchise) ? null : metadata.Franchise,
        };
    }

    private static bool IsInflectedFormOf(string name, string token) =>
        name.Length >= MinInflectedNameLength
        && token.Length > name.Length
        && token.Length - name.Length <= MaxEndingLength
        && token.StartsWith(name, StringComparison.Ordinal);

    private static string Compact(string value) => string.Concat(Tokens(value));

    private static string[] Tokens(string value) =>
        new string([.. Fold(value).Select(c => char.IsLetterOrDigit(c) ? c : ' ')])
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

    // Lowercase and accent-free, like the search side (f_unaccent). Decompose first: lowercasing
    // first turns "İ" into "i" plus a mark that no longer strips cleanly. FormKD also turns
    // full-width and stylised letters into plain ones. The letters in the switch have no
    // decomposition, so they follow the unaccent rules by hand.
    private static string Fold(string value)
    {
        var folded = new StringBuilder(value.Length);
        foreach (var c in value.Normalize(NormalizationForm.FormKD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;

            folded.Append(char.ToLowerInvariant(c) switch
            {
                'ł' => "l",
                'ø' => "o",
                'đ' or 'ð' => "d",
                'ı' => "i",
                'ß' => "ss",
                'æ' => "ae",
                'œ' => "oe",
                'þ' => "th",
                var plain => plain.ToString(),
            });
        }

        return folded.ToString();
    }
}
