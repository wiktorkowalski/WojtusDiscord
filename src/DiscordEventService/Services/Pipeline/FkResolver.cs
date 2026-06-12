using DSharpPlus.Entities;

namespace DiscordEventService.Services.Pipeline;

internal sealed class FkResolver(
    GuildUpsertService guildUpsert,
    ChannelUpsertService channelUpsert,
    UserService userService)
{
    public async Task<ResolvedFks> ResolveAsync(
        EventContext ctx,
        DiscordGuild guild,
        DiscordChannel channel,
        DiscordUser user,
        string? logContext = null)
    {
        var guildResult = await guildUpsert.UpsertGuildAsync(guild);
        // Unconditional, matching the prior inline behavior: channel upsert runs even when the
        // guild failed (passing Guid.Empty), so a single validation pass reports every failed FK.
        var channelResult = await channelUpsert.UpsertChannelAsync(channel, guildResult.Value);
        var userResult = await userService.UpsertUserAsync(user);

        return await ValidateAsync(ctx, guildResult, channelResult, userResult, logContext);
    }

    internal static async Task<ResolvedFks> ValidateAsync(
        EventContext ctx,
        UpsertResult<Guid> guild,
        UpsertResult<Guid> channel,
        UpsertResult<Guid> user,
        string? logContext = null)
    {
        if (guild.IsSuccess && channel.IsSuccess && user.IsSuccess)
            return ResolvedFks.Resolved(guild.Value, channel.Value, user.Value);

        ctx.Logger.LogError(
            "Could not resolve required FKs for {LogContext}: guild resolved {GuildResolved}, channel resolved {ChannelResolved}, user resolved {UserResolved}; skipping insert",
            logContext ?? "no context", guild.IsSuccess, channel.IsSuccess, user.IsSuccess);
        await ctx.RecordFailureAsync(new InvalidOperationException(
            $"Required FK not resolved ({logContext ?? "no context"}): guildResolved={guild.IsSuccess} channelResolved={channel.IsSuccess} userResolved={user.IsSuccess}"));
        return ResolvedFks.Failed;
    }

    public async Task<ResolvedUserFks> ResolveAsync(
        EventContext ctx,
        DiscordGuild guild,
        DiscordUser user,
        string? logContext = null)
    {
        var guildResult = await guildUpsert.UpsertGuildAsync(guild);
        var userResult = await userService.UpsertUserAsync(user);

        return await ValidateAsync(ctx, guildResult, userResult, logContext);
    }

    internal static async Task<ResolvedUserFks> ValidateAsync(
        EventContext ctx,
        UpsertResult<Guid> guild,
        UpsertResult<Guid> user,
        string? logContext = null)
    {
        if (guild.IsSuccess && user.IsSuccess)
            return ResolvedUserFks.Resolved(guild.Value, user.Value);

        ctx.Logger.LogError(
            "Could not resolve required FKs for {LogContext}: guild resolved {GuildResolved}, user resolved {UserResolved}; skipping insert",
            logContext ?? "no context", guild.IsSuccess, user.IsSuccess);
        await ctx.RecordFailureAsync(new InvalidOperationException(
            $"Required FK not resolved ({logContext ?? "no context"}): guildResolved={guild.IsSuccess} userResolved={user.IsSuccess}"));
        return ResolvedUserFks.Failed;
    }
}
