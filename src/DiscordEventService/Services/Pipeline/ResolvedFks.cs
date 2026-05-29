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
