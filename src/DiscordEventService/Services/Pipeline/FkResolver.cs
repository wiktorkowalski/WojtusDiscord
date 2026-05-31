using DSharpPlus.Entities;

namespace DiscordEventService.Services.Pipeline;

/// <summary>
/// Resolves a Discord guild/channel/user to their internal Guid FKs via the upsert services and
/// validates that all three resolved. Concentrates the snowflake→Guid resolve + all-or-fail
/// validation + failure-recording pattern that the required-FK handlers (e.g. MessageCreated)
/// would otherwise repeat inline. Soft nullable-FK handlers keep consuming
/// <see cref="UpsertResult{T}"/> directly and should NOT route through this resolver.
/// </summary>
public sealed class FkResolver(
    GuildUpsertService guildUpsert,
    ChannelUpsertService channelUpsert,
    UserService userService)
{
    /// <summary>
    /// Upserts the guild, channel (under that guild), and user, then validates all resolved. On any
    /// failure the resolver logs an aggregate line, records a <c>FailedEvent</c> via
    /// <paramref name="ctx"/>, and returns <see cref="ResolvedFks.Failed"/> — the caller should
    /// simply <c>return</c>. <paramref name="logContext"/> is appended to the failure log for
    /// traceability (e.g. <c>"MessageId=123"</c>).
    /// </summary>
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

    /// <summary>
    /// Pure validation seam: all three success → <see cref="ResolvedFks.Resolved"/>; otherwise log +
    /// record failure + <see cref="ResolvedFks.Failed"/>. Takes already-resolved results so it is
    /// unit-testable without constructing DSharpPlus entities.
    /// </summary>
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
            "Could not resolve required FKs ({LogContext}): guildResolved={GuildResolved} channelResolved={ChannelResolved} userResolved={UserResolved}; skipping insert",
            logContext ?? "no context", guild.IsSuccess, channel.IsSuccess, user.IsSuccess);
        await ctx.RecordFailureAsync(new InvalidOperationException(
            $"Required FK not resolved ({logContext ?? "no context"}): guildResolved={guild.IsSuccess} channelResolved={channel.IsSuccess} userResolved={user.IsSuccess}"));
        return ResolvedFks.Failed;
    }

    /// <summary>
    /// Guild+user variant of <see cref="ResolveAsync(EventContext, DiscordGuild, DiscordChannel, DiscordUser, string?)"/>
    /// for required-FK handlers whose core row has no channel (e.g. <c>bans</c>). Upserts the guild
    /// and user, then validates both resolved. On any failure the resolver logs, records a
    /// <c>FailedEvent</c> via <paramref name="ctx"/>, and returns <see cref="ResolvedUserFks.Failed"/> —
    /// the caller skips the core-row write (and, for a faithful timeline, may still log the event row).
    /// </summary>
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

    /// <summary>
    /// Pure guild+user validation seam, mirroring the three-FK <see cref="ValidateAsync(EventContext, UpsertResult{Guid}, UpsertResult{Guid}, UpsertResult{Guid}, string?)"/>:
    /// both success → <see cref="ResolvedUserFks.Resolved"/>; otherwise log + record failure +
    /// <see cref="ResolvedUserFks.Failed"/>. Takes already-resolved results so it is unit-testable
    /// without constructing DSharpPlus entities.
    /// </summary>
    internal static async Task<ResolvedUserFks> ValidateAsync(
        EventContext ctx,
        UpsertResult<Guid> guild,
        UpsertResult<Guid> user,
        string? logContext = null)
    {
        if (guild.IsSuccess && user.IsSuccess)
            return ResolvedUserFks.Resolved(guild.Value, user.Value);

        ctx.Logger.LogError(
            "Could not resolve required FKs ({LogContext}): guildResolved={GuildResolved} userResolved={UserResolved}; skipping insert",
            logContext ?? "no context", guild.IsSuccess, user.IsSuccess);
        await ctx.RecordFailureAsync(new InvalidOperationException(
            $"Required FK not resolved ({logContext ?? "no context"}): guildResolved={guild.IsSuccess} userResolved={user.IsSuccess}"));
        return ResolvedUserFks.Failed;
    }
}
