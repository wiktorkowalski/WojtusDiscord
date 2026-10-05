using System.Globalization;

namespace DiscordEventService.Services.MemeIndexing;

// The state of a /meme page button (#391): meme-page:<offset>:<query>. It travels in the custom
// id, not in memory, so a button on an old message still works after a restart.
internal static class MemePageCustomId
{
    public const string Prefix = "meme-page:";

    // Discord's limit for a custom id. Counted in UTF-16 units, never fewer than the characters
    // Discord counts, so an id that passes here always passes there.
    public const int MaxLength = 100;

    // The most digits of an int offset (2147483647).
    private const int MaxOffsetDigits = 10;

    // One decision per query, not per page: the id grows with the digits of the offset, so a rule
    // on the real id would give a long query buttons on page 1 and none on page 2. Room for the
    // longest offset is reserved up front, which leaves 79 characters for the query.
    public static bool CanPage(string query) =>
        Prefix.Length + MaxOffsetDigits + 1 + query.Length <= MaxLength;

    // Fits Discord's limit at every offset when CanPage(query) holds: the caller asks that first.
    public static string Build(int offset, string query) =>
        $"{Prefix}{offset.ToString(CultureInfo.InvariantCulture)}:{query}";

    // Offset first, query last: only the first two colons separate, so a query may hold colons.
    public static bool TryParse(string? customId, out int offset, out string query)
    {
        offset = 0;
        query = string.Empty;
        if (customId is null || !customId.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var parts = customId.Split(':', 3);
        if (parts.Length != 3
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out offset))
        {
            offset = 0;
            return false;
        }

        query = parts[2];
        return true;
    }
}
