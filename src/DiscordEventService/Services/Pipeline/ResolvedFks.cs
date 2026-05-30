namespace DiscordEventService.Services.Pipeline;

/// <summary>
/// Outcome of <see cref="FkResolver.ResolveAsync"/>: on success the three Guids are guaranteed
/// resolved; on failure the resolver has already logged and recorded the failure, so the caller
/// just returns. Mirrors <see cref="UpsertResult{T}"/>'s success/failure shape.
/// </summary>
public sealed record ResolvedFks(bool Success, Guid GuildId, Guid ChannelId, Guid UserId)
{
    public static ResolvedFks Resolved(Guid guildId, Guid channelId, Guid userId) =>
        new(true, guildId, channelId, userId);

    public static readonly ResolvedFks Failed = new(false, default, default, default);
}

/// <summary>
/// Outcome of the guild+user <see cref="FkResolver.ResolveAsync(EventContext, DSharpPlus.Entities.DiscordGuild, DSharpPlus.Entities.DiscordUser, string?)"/>
/// overload, for required-FK handlers whose core row carries only a guild and a user FK (e.g.
/// <c>bans</c>) and so cannot use the channel-bearing <see cref="ResolvedFks"/>. Same success/failure
/// contract, minus the channel.
/// </summary>
public sealed record ResolvedUserFks(bool Success, Guid GuildId, Guid UserId)
{
    public static ResolvedUserFks Resolved(Guid guildId, Guid userId) => new(true, guildId, userId);

    public static readonly ResolvedUserFks Failed = new(false, default, default);
}
