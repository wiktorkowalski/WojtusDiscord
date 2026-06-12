namespace DiscordEventService.Infrastructure;

internal static class DateTimeQueryExtensions
{
    // Npgsql 10 throws when a non-UTC-kind DateTime is bound to a timestamptz param; normalise query-string bounds.
    public static DateTime ToUtcInstant(this DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
