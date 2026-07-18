using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;

namespace DiscordEventService.Services.Conversation;

// The Discord-read seam for the §4 live-state tools (#270): projects the gateway caches
// (kept warm by DiscordIntents.All) into plain records the tool layer formats and tests
// without DSharpPlus entities. Cache-first by design — the ONLY REST call is the
// member-by-id cache miss, per the ticket's now-correctness principle. A null return
// means the guild isn't in the client's cache (not connected / not a member); the tool
// layer turns that into a clean string. Stateless and singleton over the accessor, same
// as GuildActionService.
internal interface IGuildLiveStateService
{
    IReadOnlyList<LiveVoiceChannel>? GetVoiceOccupants(ulong guildId);
    Task<MemberLookupResult?> FindMemberAsync(ulong guildId, string query, CancellationToken cancellationToken);
    LiveServerInfo? GetServerInfo(ulong guildId);
}

internal sealed record LiveVoiceOccupant(
    string DisplayName, bool IsMuted, bool IsDeafened, bool IsStreaming, bool HasVideo);

internal sealed record LiveVoiceChannel(
    ulong GuildId, ulong ChannelId, string ChannelName, IReadOnlyList<LiveVoiceOccupant> Occupants);

internal sealed record LiveMemberCandidate(ulong Id, string Username, string? GlobalName, string DisplayName);

internal sealed record LiveMemberVoice(
    string ChannelName, bool IsMuted, bool IsDeafened, bool IsStreaming, bool HasVideo);

internal sealed record LiveMemberInfo(
    ulong Id,
    string Username,
    string? GlobalName,
    string DisplayName,
    bool IsBot,
    DateTimeOffset AccountCreatedAt,
    DateTimeOffset JoinedAt,
    DateTimeOffset? BoostingSince,
    DateTimeOffset? TimedOutUntil,
    IReadOnlyList<string> Roles,
    string PresenceStatus,
    IReadOnlyList<string> Activities,
    LiveMemberVoice? Voice);

internal sealed record LiveServerInfo(
    ulong Id,
    string Name,
    DateTimeOffset CreatedAt,
    string? OwnerName,
    int MemberCount,
    int BoostTier,
    int BoostCount,
    int TextChannelCount,
    int VoiceChannelCount,
    int CategoryCount,
    int RoleCount,
    int EmojiCount);

internal enum MemberLookupOutcome
{
    Found,
    Ambiguous,
    NotFound,
}

internal sealed record MemberLookupResult(
    MemberLookupOutcome Outcome,
    LiveMemberInfo? Member,
    IReadOnlyList<LiveMemberCandidate> Candidates)
{
    public static MemberLookupResult Found(LiveMemberInfo member) => new(MemberLookupOutcome.Found, member, []);
    public static MemberLookupResult Ambiguous(IReadOnlyList<LiveMemberCandidate> candidates) =>
        new(MemberLookupOutcome.Ambiguous, null, candidates);
    public static MemberLookupResult NotFound() => new(MemberLookupOutcome.NotFound, null, []);
}

internal sealed class GuildLiveStateService(DiscordClientAccessor clientAccessor) : IGuildLiveStateService
{
    // Resolved from the accessor (never injected directly) so DiscordClient stays out of the
    // child container's DI graph — see DiscordClientAccessor for why.
    private DiscordClient Client => clientAccessor.Client;

    public IReadOnlyList<LiveVoiceChannel>? GetVoiceOccupants(ulong guildId)
    {
        if (!Client.Guilds.TryGetValue(guildId, out var guild))
            return null;

        return guild.VoiceStates.Values
            .Where(state => state.Channel is not null)
            .GroupBy(state => state.Channel!)
            .Select(channel => new LiveVoiceChannel(
                guildId,
                channel.Key.Id,
                channel.Key.Name ?? channel.Key.Id.ToString(),
                [.. channel.Select(ProjectOccupant).OrderBy(o => o.DisplayName, StringComparer.OrdinalIgnoreCase)]))
            .OrderBy(channel => channel.ChannelName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<MemberLookupResult?> FindMemberAsync(
        ulong guildId, string query, CancellationToken cancellationToken)
    {
        if (!Client.Guilds.TryGetValue(guildId, out var guild))
            return null;

        var trimmed = query.Trim();
        if (ulong.TryParse(trimmed, out var id))
        {
            if (guild.Members.TryGetValue(id, out var cached))
                return MemberLookupResult.Found(ProjectMember(cached));

            // The single sanctioned REST fallback: an id the cache doesn't have (member left,
            // or the cache is still filling after a reconnect).
            try
            {
                var fetched = await guild.GetMemberAsync(id, updateCache: true);
                return MemberLookupResult.Found(ProjectMember(fetched));
            }
            catch (NotFoundException)
            {
                return MemberLookupResult.NotFound();
            }
            catch (BadRequestException)
            {
                // A numeric string Discord rejects as a snowflake — same answer as unknown.
                return MemberLookupResult.NotFound();
            }
        }

        var match = MemberMatcher.Match(
            guild.Members.Values.Select(member => new LiveMemberCandidate(
                member.Id, member.Username, member.GlobalName, member.DisplayName)),
            trimmed);
        return match.Outcome == MemberLookupOutcome.Found
            ? MemberLookupResult.Found(ProjectMember(guild.Members[match.Member!.Value]))
            : new MemberLookupResult(match.Outcome, null, match.Candidates);
    }

    public LiveServerInfo? GetServerInfo(ulong guildId)
    {
        if (!Client.Guilds.TryGetValue(guildId, out var guild))
            return null;

        var channels = guild.Channels.Values;
        return new LiveServerInfo(
            guild.Id,
            guild.Name,
            guild.CreationTimestamp,
            guild.Members.TryGetValue(guild.OwnerId, out var owner) ? owner.DisplayName : null,
            guild.MemberCount,
            guild.PremiumTier switch
            {
                DiscordPremiumTier.Tier_1 => 1,
                DiscordPremiumTier.Tier_2 => 2,
                DiscordPremiumTier.Tier_3 => 3,
                _ => 0,
            },
            guild.PremiumSubscriptionCount ?? 0,
            channels.Count(c => c.Type is DiscordChannelType.Text or DiscordChannelType.News),
            channels.Count(c => c.Type is DiscordChannelType.Voice or DiscordChannelType.Stage),
            channels.Count(c => c.Type is DiscordChannelType.Category),
            guild.Roles.Count,
            guild.Emojis.Count);
    }

    private static LiveVoiceOccupant ProjectOccupant(DiscordVoiceState state) =>
        new(
            state.Member?.DisplayName ?? state.User?.Username ?? "(unknown)",
            state.IsSelfMuted || state.IsServerMuted,
            state.IsSelfDeafened || state.IsServerDeafened,
            state.IsSelfStream,
            state.IsSelfVideo);

    private static LiveMemberInfo ProjectMember(DiscordMember member)
    {
        var presence = member.Presence;
        var voiceChannel = member.VoiceState?.Channel;
        return new LiveMemberInfo(
            member.Id,
            member.Username,
            member.GlobalName,
            member.DisplayName,
            member.IsBot,
            member.CreationTimestamp,
            member.JoinedAt,
            member.PremiumSince,
            member.IsTimedOut ? member.CommunicationDisabledUntil : null,
            [.. member.Roles.OrderByDescending(role => role.Position).Select(role => role.Name)],
            DescribeStatus(presence),
            presence is null ? [] : [.. presence.Activities.Select(DescribeActivity)],
            voiceChannel is null
                ? null
                : new LiveMemberVoice(
                    voiceChannel.Name ?? voiceChannel.Id.ToString(),
                    member.VoiceState!.IsSelfMuted || member.VoiceState.IsServerMuted,
                    member.VoiceState.IsSelfDeafened || member.VoiceState.IsServerDeafened,
                    member.VoiceState.IsSelfStream,
                    member.VoiceState.IsSelfVideo));
    }

    private static string DescribeStatus(DiscordPresence? presence) =>
        presence?.Status switch
        {
            DiscordUserStatus.Online => "online",
            DiscordUserStatus.Idle => "idle",
            DiscordUserStatus.DoNotDisturb => "do not disturb",
            // Invisible is indistinguishable from offline on the receiving side; null presence
            // means the gateway never sent one (offline since boot).
            _ => "offline",
        };

    private static string DescribeActivity(DiscordActivity activity) =>
        activity.ActivityType switch
        {
            DiscordActivityType.Custom when activity.CustomStatus is { } custom =>
                $"Custom status: {custom.Name}",
            DiscordActivityType.Playing => $"Playing {activity.Name}",
            DiscordActivityType.Streaming => $"Streaming {activity.Name}",
            DiscordActivityType.ListeningTo => $"Listening to {activity.Name}",
            DiscordActivityType.Watching => $"Watching {activity.Name}",
            DiscordActivityType.Competing => $"Competing in {activity.Name}",
            _ => activity.Name,
        };
}

// Pure name-fragment resolution over the live member cache, kept Discord-free so the
// ambiguity contract (#270 acceptance) is unit-testable: an exact username/global/display
// name hit wins outright; otherwise a unique substring hit wins; several hits return the
// candidates for the model to relay; none is a clean not-found.
internal static class MemberMatcher
{
    // Enough for the model to relay "which one did you mean?" without flooding the context.
    private const int MaxCandidates = 10;

    public sealed record Result(
        MemberLookupOutcome Outcome, ulong? Member, IReadOnlyList<LiveMemberCandidate> Candidates);

    public static Result Match(IEnumerable<LiveMemberCandidate> members, string fragment)
    {
        List<LiveMemberCandidate> exact = [];
        List<LiveMemberCandidate> partial = [];
        foreach (var member in members)
        {
            if (MatchesExactly(member, fragment))
                exact.Add(member);
            else if (Contains(member, fragment))
                partial.Add(member);
        }

        if (exact.Count == 1)
            return new Result(MemberLookupOutcome.Found, exact[0].Id, []);

        var hits = exact.Count > 1 ? exact : partial;
        return hits.Count switch
        {
            0 => new Result(MemberLookupOutcome.NotFound, null, []),
            1 => new Result(MemberLookupOutcome.Found, hits[0].Id, []),
            _ => new Result(
                MemberLookupOutcome.Ambiguous, null,
                [.. hits.OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase).Take(MaxCandidates)]),
        };
    }

    private static bool MatchesExactly(LiveMemberCandidate member, string fragment) =>
        string.Equals(member.Username, fragment, StringComparison.OrdinalIgnoreCase)
        || string.Equals(member.GlobalName, fragment, StringComparison.OrdinalIgnoreCase)
        || string.Equals(member.DisplayName, fragment, StringComparison.OrdinalIgnoreCase);

    private static bool Contains(LiveMemberCandidate member, string fragment) =>
        member.Username.Contains(fragment, StringComparison.OrdinalIgnoreCase)
        || (member.GlobalName?.Contains(fragment, StringComparison.OrdinalIgnoreCase) ?? false)
        || member.DisplayName.Contains(fragment, StringComparison.OrdinalIgnoreCase);
}
