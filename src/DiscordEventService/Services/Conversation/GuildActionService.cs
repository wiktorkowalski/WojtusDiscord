using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;

namespace DiscordEventService.Services.Conversation;

// The Discord-write seam for the conversational assistant's action tools (#238 §6). Every
// method ATTEMPTS the Discord call and turns the failure (a 403 from a missing permission
// or a role-hierarchy violation, a 404 for a vanished target, a 400 the API rejected) into a
// ToolSoftFailureException with a clean line the model can relay — we never preflight permission bits (the bot may lack a
// permission, or sit below the target role; Discord is the authority on that). The admin gate
// itself lives a layer up, in the tool closures (ConversationContext.IsAdmin) and the confirm
// button — this service performs, it does not authorize.
//
// Stateless and singleton: it reads the singleton DiscordClient from the accessor, so the
// deferred execute delegate a staged action captures can safely outlive the turn that staged it.
internal interface IGuildActionService
{
    // Reversible — run immediately after the admin gate.
    Task<string> AddReactionAsync(ulong channelId, ulong messageId, string emoji, CancellationToken cancellationToken);
    Task<string> PinMessageAsync(ulong channelId, ulong messageId, CancellationToken cancellationToken);

    // Irreversible — run only from a staged action's confirm-button delegate.
    Task<string> GrantRoleAsync(ulong guildId, ulong userId, ulong roleId, string reason, CancellationToken cancellationToken);
    Task<string> RemoveRoleAsync(ulong guildId, ulong userId, ulong roleId, string reason, CancellationToken cancellationToken);
    Task<string> TimeoutMemberAsync(ulong guildId, ulong userId, int minutes, string reason, CancellationToken cancellationToken);
    Task<string> KickMemberAsync(ulong guildId, ulong userId, string reason, CancellationToken cancellationToken);
    Task<string> BanMemberAsync(ulong guildId, ulong userId, string reason, CancellationToken cancellationToken);
    Task<string> DeleteMessageAsync(ulong channelId, ulong messageId, string reason, CancellationToken cancellationToken);

    // Best-effort, human-readable previews for the confirm embed (resolve names, fall back to ids).
    Task<string> DescribeUserAsync(ulong guildId, ulong userId, CancellationToken cancellationToken);
    Task<string> DescribeRoleAsync(ulong guildId, ulong roleId, CancellationToken cancellationToken);
}

internal sealed class GuildActionService(DiscordClientAccessor clientAccessor, ILogger<GuildActionService> logger)
    : IGuildActionService
{
    // Discord caps a timeout at 28 days; clamp so the API never rejects an over-long request.
    private const int MaxTimeoutMinutes = 28 * 24 * 60;

    // Resolved from the accessor (never injected directly) so DiscordClient stays out of the
    // child container's DI graph — see DiscordClientAccessor for why.
    private DiscordClient Client => clientAccessor.Client;

    public async Task<string> AddReactionAsync(
        ulong channelId, ulong messageId, string emoji, CancellationToken cancellationToken)
    {
        DiscordEmoji parsed;
        try
        {
            parsed = DiscordEmoji.FromUnicode(emoji);
        }
        catch (ArgumentException)
        {
            throw new ToolSoftFailureException($"\"{emoji}\" isn't a standard emoji I can react with.");
        }

        return await RunAsync("message", async () =>
        {
            var message = await FetchMessageAsync(channelId, messageId);
            await message.CreateReactionAsync(parsed);
            return $"Reacted with {emoji}.";
        }, cancellationToken);
    }

    public Task<string> PinMessageAsync(ulong channelId, ulong messageId, CancellationToken cancellationToken) =>
        RunAsync("message", async () =>
        {
            var message = await FetchMessageAsync(channelId, messageId);
            await message.PinAsync();
            return "Pinned the message.";
        }, cancellationToken);

    public Task<string> GrantRoleAsync(
        ulong guildId, ulong userId, ulong roleId, string reason, CancellationToken cancellationToken) =>
        RunAsync("user or role", async () =>
        {
            var guild = await Client.GetGuildAsync(guildId);
            var member = await guild.GetMemberAsync(userId);
            var role = await guild.GetRoleAsync(roleId);
            await member.GrantRoleAsync(role, reason);
            return $"Gave {member.DisplayName} the {role.Name} role.";
        }, cancellationToken);

    public Task<string> RemoveRoleAsync(
        ulong guildId, ulong userId, ulong roleId, string reason, CancellationToken cancellationToken) =>
        RunAsync("user or role", async () =>
        {
            var guild = await Client.GetGuildAsync(guildId);
            var member = await guild.GetMemberAsync(userId);
            var role = await guild.GetRoleAsync(roleId);
            await member.RevokeRoleAsync(role, reason);
            return $"Removed the {role.Name} role from {member.DisplayName}.";
        }, cancellationToken);

    public Task<string> TimeoutMemberAsync(
        ulong guildId, ulong userId, int minutes, string reason, CancellationToken cancellationToken) =>
        RunAsync("user", async () =>
        {
            var clamped = Math.Clamp(minutes, 1, MaxTimeoutMinutes);
            var guild = await Client.GetGuildAsync(guildId);
            var member = await guild.GetMemberAsync(userId);
            await member.TimeoutAsync(DateTimeOffset.UtcNow.AddMinutes(clamped), reason);
            return $"Timed out {member.DisplayName} for {clamped} minute(s).";
        }, cancellationToken);

    public Task<string> KickMemberAsync(ulong guildId, ulong userId, string reason, CancellationToken cancellationToken) =>
        RunAsync("user", async () =>
        {
            var guild = await Client.GetGuildAsync(guildId);
            var member = await guild.GetMemberAsync(userId);
            await member.RemoveAsync(reason);
            return $"Kicked {member.DisplayName}.";
        }, cancellationToken);

    public Task<string> BanMemberAsync(ulong guildId, ulong userId, string reason, CancellationToken cancellationToken) =>
        RunAsync("user", async () =>
        {
            var guild = await Client.GetGuildAsync(guildId);
            // Ban by id so it works even when the target isn't a resolvable member (already gone).
            await guild.BanMemberAsync(userId, TimeSpan.Zero, reason);
            return $"Banned user {userId}.";
        }, cancellationToken);

    public Task<string> DeleteMessageAsync(ulong channelId, ulong messageId, string reason, CancellationToken cancellationToken) =>
        RunAsync("message", async () =>
        {
            var message = await FetchMessageAsync(channelId, messageId);
            await message.DeleteAsync(reason);
            return "Deleted the message.";
        }, cancellationToken);

    public async Task<string> DescribeUserAsync(ulong guildId, ulong userId, CancellationToken cancellationToken)
    {
        try
        {
            var guild = await Client.GetGuildAsync(guildId);
            var member = await guild.GetMemberAsync(userId);
            return $"{member.DisplayName} ({userId})";
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Best-effort preview: a lookup failure must never block staging — the confirm
            // embed just shows the raw id, and execution reports the real error if it fails.
            return $"user {userId}";
        }
    }

    public async Task<string> DescribeRoleAsync(ulong guildId, ulong roleId, CancellationToken cancellationToken)
    {
        try
        {
            var guild = await Client.GetGuildAsync(guildId);
            var role = await guild.GetRoleAsync(roleId);
            return $"{role.Name} ({roleId})";
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return $"role {roleId}";
        }
    }

    private async Task<DiscordMessage> FetchMessageAsync(ulong channelId, ulong messageId)
    {
        var channel = await Client.GetChannelAsync(channelId);
        return await channel.GetMessageAsync(messageId);
    }

    // One funnel for the attempt-and-catch contract so every action reports failure the same
    // way. A failure is thrown as ToolSoftFailureException, never returned: the toolset counts it
    // as an error and the confirm card marks it failed (#455). Only the turn's own cancellation
    // propagates; DSharpPlus takes no token here, so any other OCE is a REST timeout — a failure.
    private async Task<string> RunAsync(string target, Func<Task<string>> action, CancellationToken cancellationToken)
    {
        string failure;
        try
        {
            return await action();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UnauthorizedException)
        {
            failure = "I don't have permission to do that — check my role is above the target and has the needed permission.";
        }
        catch (NotFoundException)
        {
            failure = $"I couldn't find that {target}.";
        }
        catch (BadRequestException ex)
        {
            logger.LogInformation("Discord rejected an action: {Message}", ex.Message);
            failure = "Discord rejected that request.";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Action against {Target} failed", target);
            failure = "Something went wrong while performing that action.";
        }

        throw new ToolSoftFailureException(failure);
    }
}
