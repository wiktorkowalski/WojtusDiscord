namespace DiscordEventService.Services.Pipeline;

public sealed record ResolvedFks(bool Success, Guid GuildId, Guid ChannelId, Guid UserId)
{
    public static ResolvedFks Resolved(Guid guildId, Guid channelId, Guid userId) =>
        new ResolvedFks(true, guildId, channelId, userId);

    public static readonly ResolvedFks Failed = new ResolvedFks(false, default, default, default);
}

public sealed record ResolvedUserFks(bool Success, Guid GuildId, Guid UserId)
{
    public static ResolvedUserFks Resolved(Guid guildId, Guid userId) => new ResolvedUserFks(true, guildId, userId);

    public static readonly ResolvedUserFks Failed = new ResolvedUserFks(false, default, default);
}
