using DiscordEventService.Configuration;
using DiscordEventService.Services.Conversation;
using DSharpPlus.Exceptions;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Infrastructure.Auth;

internal enum MembershipStatus
{
    Member,
    NotMember,
    // The bot cannot tell right now: guild not in the cache yet after a restart, or a REST failure.
    Unknown,
}

internal interface IGuildMembershipChecker
{
    Task<MembershipStatus> CheckAsync(ulong userId, CancellationToken cancellationToken);
}

// Asks the bot, not the DB: `members` has no "left" column (#328). Cache first, then one REST call.
internal sealed class DiscordGuildMembershipChecker(
    DiscordClientAccessor clientAccessor,
    IOptions<AuthOptions> options,
    ILogger<DiscordGuildMembershipChecker> logger) : IGuildMembershipChecker
{
    public async Task<MembershipStatus> CheckAsync(ulong userId, CancellationToken cancellationToken)
    {
        if (!clientAccessor.Client.Guilds.TryGetValue(options.Value.GuildId, out var guild))
            return MembershipStatus.Unknown;

        if (guild.Members.ContainsKey(userId))
            return MembershipStatus.Member;

        try
        {
            await guild.GetMemberAsync(userId, updateCache: true);
            return MembershipStatus.Member;
        }
        catch (NotFoundException)
        {
            return MembershipStatus.NotMember;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Membership check for user {UserId} failed", userId);
            return MembershipStatus.Unknown;
        }
    }
}
