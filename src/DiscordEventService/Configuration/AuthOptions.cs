namespace DiscordEventService.Configuration;

// Login gate of the dashboard, /api and /hangfire (#328, #339). Deliberately no fallback to
// Conversation:PrimaryGuildId / Conversation:AdminUserIds: the bot's admin list must not
// silently decide who enters /hangfire.
internal sealed class AuthOptions
{
    public const string SectionName = "Auth";

    // Disabled injects a fake Admin identity; the app refuses to start with it outside Development.
    public AuthMode Mode { get; set; } = AuthMode.Disabled;

    // Members of this guild see the dashboard and the read API.
    public ulong GuildId { get; set; }

    // These users reach the ops endpoints and /hangfire, member of the guild or not.
    public ulong[] AdminUserIds { get; set; } = [];

    // Static Bearer key for scripted access. Accepted only on Admin endpoints, never on /hangfire.
    // Empty => no key matches.
    public string? OpsApiKey { get; set; }

    public DiscordAppOptions Discord { get; set; } = new();

    public bool IsAdmin(ulong userId) => Array.IndexOf(AdminUserIds, userId) >= 0;

    internal sealed class DiscordAppOptions
    {
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
    }
}

internal enum AuthMode
{
    Disabled,
    Discord,
}
