namespace DiscordEventService.Infrastructure;

internal static class StringExtensions
{
    // Cut to maxLength chars, the last one an ellipsis.
    public static string Truncate(this string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";
}
