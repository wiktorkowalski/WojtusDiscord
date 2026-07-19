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
        var channelResult = await UpsertChannelIfGuildResolvedAsync(guildResult, channel);
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

    public async Task<ResolvedChannelFks> ResolveAsync(
        EventContext ctx,
        DiscordGuild guild,
        DiscordChannel channel,
        string? logContext = null)
    {
        var guildResult = await guildUpsert.UpsertGuildAsync(guild);
        var channelResult = await UpsertChannelIfGuildResolvedAsync(guildResult, channel);

        return await ValidateGuildChannelAsync(ctx, guildResult, channelResult, logContext);
    }

    internal static async Task<ResolvedChannelFks> ValidateGuildChannelAsync(
        EventContext ctx,
        UpsertResult<Guid> guild,
        UpsertResult<Guid> channel,
        string? logContext = null)
    {
        if (guild.IsSuccess && channel.IsSuccess)
            return ResolvedChannelFks.Resolved(guild.Value, channel.Value);

        ctx.Logger.LogError(
            "Could not resolve required FKs for {LogContext}: guild resolved {GuildResolved}, channel resolved {ChannelResolved}; skipping insert",
            logContext ?? "no context", guild.IsSuccess, channel.IsSuccess);
        await ctx.RecordFailureAsync(new InvalidOperationException(
            $"Required FK not resolved ({logContext ?? "no context"}): guildResolved={guild.IsSuccess} channelResolved={channel.IsSuccess}"));
        return ResolvedChannelFks.Failed;
    }

    public async Task<ResolvedGuildFk> ResolveAsync(
        EventContext ctx,
        DiscordGuild guild,
        string? logContext = null)
    {
        var guildResult = await guildUpsert.UpsertGuildAsync(guild);

        return await ValidateAsync(ctx, guildResult, logContext);
    }

    internal static async Task<ResolvedGuildFk> ValidateAsync(
        EventContext ctx,
        UpsertResult<Guid> guild,
        string? logContext = null)
    {
        if (guild.IsSuccess)
            return ResolvedGuildFk.Resolved(guild.Value);

        ctx.Logger.LogError(
            "Could not resolve required FKs for {LogContext}: guild resolved {GuildResolved}; skipping insert",
            logContext ?? "no context", guild.IsSuccess);
        await ctx.RecordFailureAsync(new InvalidOperationException(
            $"Required FK not resolved ({logContext ?? "no context"}): guildResolved={guild.IsSuccess}"));
        return ResolvedGuildFk.Failed;
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

    // A failed guild must not flow Guid.Empty into a fresh channel row's GuildId (#292);
    // the skipped upsert surfaces as a channel failure in the validation pass.
    private async Task<UpsertResult<Guid>> UpsertChannelIfGuildResolvedAsync(
        UpsertResult<Guid> guildResult, DiscordChannel channel) =>
        guildResult.IsSuccess
            ? await channelUpsert.UpsertChannelAsync(channel, guildResult.Value)
            : UpsertResult<Guid>.Failure("guild FK unresolved; channel upsert skipped");
}
