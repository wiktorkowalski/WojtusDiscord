namespace DiscordEventService.Infrastructure;

internal static class DateTimeQueryExtensions
{
    /// <summary>
    /// Normalises a <see cref="DateTime"/> bound from a query string to a UTC instant.
    /// Query-string binding yields <see cref="DateTimeKind.Unspecified"/> (no offset in
    /// the text) or <see cref="DateTimeKind.Local"/> (text ended in 'Z'); writing either
    /// to a <c>timestamptz</c> parameter throws in Npgsql 10. Local values are converted
    /// (preserving the instant); unspecified values are assumed UTC.
    /// </summary>
    public static DateTime ToUtcInstant(this DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
