namespace DiscordEventService.Infrastructure;

// When this process started, as one UTC instant, read by anything that has to tell
// "this boot" from the one before it (#350).
//
// Deliberately NOT Process.StartTime: that is a local-time value needing conversion,
// while every timestamp it gets compared against — BotDowntimeIntervalEntity.EndedAtUtc
// above all — is written from DateTime.UtcNow. Two clocks with a conversion between them
// can order a row a few milliseconds on the wrong side of the boundary, which would drop a
// legitimate shutdown row and silently restore the 33-day fallback this exists to prevent.
// One clock, no conversion, no skew. HealthResponseWriter keeps its own Process.StartTime
// reading, because there the value is displayed rather than compared.
//
// The static initializer runs on first access, so Program.cs touches it before anything
// else. Left implicit, the first read could happen well after boot — at which point a
// shutdown row closed during StartAsync would sort BEFORE "start" and be discarded.
internal static class BootClock
{
    public static readonly DateTime StartedAtUtc = DateTime.UtcNow;

    // Called for its side effect: forcing the static initializer to run now.
    public static void Touch() => _ = StartedAtUtc;
}
