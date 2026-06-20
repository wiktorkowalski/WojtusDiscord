using System.Collections.Concurrent;
using DiscordEventService.Configuration;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Services.Conversation;

// An irreversible admin action staged behind a Discord confirm button (#238 §6). The execute
// delegate is the actual Discord write, run only when an admin clicks Confirm — it closes over
// the singleton GuildActionService + the resolved ids, so it can outlive the turn that staged it.
internal sealed record PendingGuildAction(
    string Token,
    ulong RequesterId,
    string Description,
    Func<CancellationToken, Task<string>> ExecuteAsync);

internal enum ConfirmKind
{
    Confirm,
    Cancel,
}

internal interface IConfirmationService
{
    // Register a pending action, post a confirm/cancel button to the channel, and return the
    // line the model relays. The action does NOT run here — it runs when an admin clicks Confirm.
    Task<string> StageAsync(
        ulong channelId, ulong requesterId, string requesterName, string description,
        Func<CancellationToken, Task<string>> execute, CancellationToken cancellationToken);

    // Atomically take ownership of a staged action (TryRemove): the first caller wins, so a
    // double-click can never run it twice and a stale/expired token returns false.
    bool TryClaim(string token, out PendingGuildAction action);
}

// Holds staged actions in memory (a ConcurrentDictionary) keyed by an opaque token carried in
// the button's CustomId. Singleton, and deliberately NOT persisted — staged actions are dropped
// on restart (acceptable per the design). The admin gate is NOT here: staging is only reached
// after the tool's ConversationContext.IsAdmin check, and the *clicker* is re-checked against
// AdminUserIds by the component handler at click time.
internal sealed class ConfirmationService(
    DiscordClientAccessor clientAccessor,
    IOptions<ConversationOptions> options,
    ILogger<ConfirmationService> logger) : IConfirmationService
{
    private const string ConfirmPrefix = "conv6:confirm:";
    private const string CancelPrefix = "conv6:cancel:";

    private readonly ConcurrentDictionary<string, PendingGuildAction> _pending = new(StringComparer.Ordinal);

    // Split a button CustomId into its kind + token, and reject anything that isn't one of ours
    // (other components in the guild fire the same event). Pure, so the parsing is unit-tested.
    public static bool TryParseCustomId(string customId, out ConfirmKind kind, out string token)
    {
        kind = default;
        token = string.Empty;
        if (string.IsNullOrEmpty(customId))
            return false;

        if (customId.StartsWith(ConfirmPrefix, StringComparison.Ordinal))
        {
            kind = ConfirmKind.Confirm;
            token = customId[ConfirmPrefix.Length..];
        }
        else if (customId.StartsWith(CancelPrefix, StringComparison.Ordinal))
        {
            kind = ConfirmKind.Cancel;
            token = customId[CancelPrefix.Length..];
        }
        else
        {
            return false;
        }

        return token.Length > 0;
    }

    // Register first so the token exists before the button does — a click can never arrive for
    // an unregistered action.
    public PendingGuildAction Register(
        ulong requesterId, string description, Func<CancellationToken, Task<string>> execute)
    {
        var token = Guid.NewGuid().ToString("N");
        var action = new PendingGuildAction(token, requesterId, description, execute);
        _pending[token] = action;
        return action;
    }

    public bool TryClaim(string token, out PendingGuildAction action) =>
        _pending.TryRemove(token, out action!);

    public async Task<string> StageAsync(
        ulong channelId, ulong requesterId, string requesterName, string description,
        Func<CancellationToken, Task<string>> execute, CancellationToken cancellationToken)
    {
        var expiry = TimeSpan.FromSeconds(Math.Max(1, options.Value.ConfirmExpirySeconds));
        var pending = Register(requesterId, description, execute);

        DiscordMessage message;
        try
        {
            message = await PostConfirmAsync(channelId, pending.Token, requesterName, description, expiry);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Couldn't post the prompt — drop the orphaned action so it can't be confirmed blind.
            TryClaim(pending.Token, out _);
            logger.LogWarning(ex, "Failed to post the confirmation prompt to channel {ChannelId}", channelId);
            return "I couldn't post the confirmation prompt, so I didn't do anything.";
        }

        _ = ExpireAsync(pending.Token, message, expiry);

        logger.LogInformation(
            "Staged action {Token} requested by {RequesterId}: {Description}",
            pending.Token, requesterId, description);
        return $"I've posted a confirmation here — a server admin has to click **Confirm** to apply this: "
            + $"{description}. It expires in {(int)expiry.TotalMinutes} minute(s) if no admin confirms.";
    }

    private async Task<DiscordMessage> PostConfirmAsync(
        ulong channelId, string token, string requesterName, string description, TimeSpan expiry)
    {
        var embed = new DiscordEmbedBuilder()
            .WithTitle("⚠️ Confirm action")
            .WithDescription(description)
            .WithColor(DiscordColor.Orange)
            .WithFooter(
                $"Requested by {requesterName} · only an admin can confirm · expires in {(int)expiry.TotalMinutes} min");

        var builder = new DiscordMessageBuilder()
            .AddEmbed(embed)
            .AddComponents(
                new DiscordButtonComponent(DiscordButtonStyle.Success, ConfirmPrefix + token, "Confirm"),
                new DiscordButtonComponent(DiscordButtonStyle.Secondary, CancelPrefix + token, "Cancel"))
            .WithAllowedMentions(Mentions.None);

        var channel = await clientAccessor.Client.GetChannelAsync(channelId);
        return await channel.SendMessageAsync(builder);
    }

    // Fire-and-forget cleanup: if no one has claimed the token by the deadline, remove it and
    // mark the prompt expired. If it was already confirmed/cancelled, TryRemove finds nothing.
    private async Task ExpireAsync(string token, DiscordMessage message, TimeSpan expiry)
    {
        await Task.Delay(expiry);
        if (!_pending.TryRemove(token, out _))
            return;

        try
        {
            await message.ModifyAsync(new DiscordMessageBuilder()
                .WithContent("⏱️ This confirmation expired before an admin confirmed it.")
                .WithAllowedMentions(Mentions.None));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not edit expired confirmation {Token}", token);
        }
    }
}
