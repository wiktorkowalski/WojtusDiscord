using DSharpPlus.Entities;

namespace DiscordEventService.Infrastructure;

// DiscordUserStatus is NOT ordered by online-ness (Offline=0, Online=1, Idle=2, DoNotDisturb=4,
// Invisible=5), so overall status is a priority pick (online > idle > dnd > offline), never a max.
internal static class PresenceStatus
{
    public const string Online = "online";
    public const string Idle = "idle";
    public const string Dnd = "dnd";
    public const string Offline = "offline";

    public static string Overall(int desktop, int mobile, int web)
    {
        int[] devices = [desktop, mobile, web];
        if (devices.Contains((int)DiscordUserStatus.Online)) return Online;
        if (devices.Contains((int)DiscordUserStatus.Idle)) return Idle;
        if (devices.Contains((int)DiscordUserStatus.DoNotDisturb)) return Dnd;
        return Offline;
    }

    // Sort key (lower = more active) for ordering a "who's online" list.
    public static int Rank(string status) => status switch
    {
        Online => 0,
        Idle => 1,
        Dnd => 2,
        _ => 3,
    };
}
