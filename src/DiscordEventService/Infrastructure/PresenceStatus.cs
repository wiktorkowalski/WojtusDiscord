using DSharpPlus.Entities;

namespace DiscordEventService.Infrastructure;

/// <summary>
/// Derives a user's presence status from per-device DSharpPlus statuses. The
/// <see cref="DiscordUserStatus"/> enum is NOT ordered by online-ness (Offline=0,
/// Online=1, Idle=2, DoNotDisturb=4, Invisible=5; Invisible reads as offline), so the
/// overall status is a client-status priority pick (online &gt; idle &gt; dnd &gt;
/// offline), never a numeric max. Status names are the lowercase strings the dashboard
/// and DTOs expect.
/// </summary>
internal static class PresenceStatus
{
    public const string Online = "online";
    public const string Idle = "idle";
    public const string Dnd = "dnd";
    public const string Offline = "offline";

    /// <summary>
    /// Aggregates a user's three device statuses into one by client-status priority —
    /// not a numeric max, since the enum values are not ordered by activity.
    /// </summary>
    public static string Overall(int desktop, int mobile, int web)
    {
        int[] devices = [desktop, mobile, web];
        if (devices.Contains((int)DiscordUserStatus.Online)) return Online;
        if (devices.Contains((int)DiscordUserStatus.Idle)) return Idle;
        if (devices.Contains((int)DiscordUserStatus.DoNotDisturb)) return Dnd;
        return Offline;
    }

    /// <summary>Sort key (lower = more active) for ordering a "who's online" list.</summary>
    public static int Rank(string status) => status switch
    {
        Online => 0,
        Idle => 1,
        Dnd => 2,
        _ => 3,
    };
}
