namespace DiscordEventService.Services.Pipeline;

internal sealed record ResolvedFks(bool Success, Guid GuildId, Guid ChannelId, Guid UserId)
{
    public static readonly ResolvedFks Failed = new ResolvedFks(false, default, default, default);

    public static ResolvedFks Resolved(Guid guildId, Guid channelId, Guid userId) =>
        new ResolvedFks(true, guildId, channelId, userId);
}

internal sealed record ResolvedUserFks(bool Success, Guid GuildId, Guid UserId)
{
    public static readonly ResolvedUserFks Failed = new ResolvedUserFks(false, default, default);

    public static ResolvedUserFks Resolved(Guid guildId, Guid userId) => new ResolvedUserFks(true, guildId, userId);
}

internal sealed record ResolvedChannelFks(bool Success, Guid GuildId, Guid ChannelId)
{
    public static readonly ResolvedChannelFks Failed = new ResolvedChannelFks(false, default, default);

    public static ResolvedChannelFks Resolved(Guid guildId, Guid channelId) =>
        new ResolvedChannelFks(true, guildId, channelId);
}

internal sealed record ResolvedGuildFk(bool Success, Guid GuildId)
{
    public static readonly ResolvedGuildFk Failed = new ResolvedGuildFk(false, default);

    public static ResolvedGuildFk Resolved(Guid guildId) => new ResolvedGuildFk(true, guildId);
}
