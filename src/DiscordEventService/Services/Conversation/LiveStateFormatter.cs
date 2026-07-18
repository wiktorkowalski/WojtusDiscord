using System.Globalization;
using System.Text;

namespace DiscordEventService.Services.Conversation;

// Model-facing rendering for the §4 live-state tools (#270): plain records in, one
// compact string out. Kept pure (no Discord, no clock) so the output shapes are
// unit-tested — the live projection layer is verified on the dev bot instead.
internal static class LiveStateFormatter
{
    public static string FormatVoiceChannels(IReadOnlyList<LiveVoiceChannel> channels)
    {
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"{channels.Count} voice channel(s) with people in them:");
        foreach (var channel in channels)
        {
            builder.Append(CultureInfo.InvariantCulture,
                $"\n**{channel.ChannelName}** ({channel.Occupants.Count}) — "
                + $"https://discord.com/channels/{channel.GuildId}/{channel.ChannelId}");
            foreach (var occupant in channel.Occupants)
                builder.Append(CultureInfo.InvariantCulture,
                    $"\n- {occupant.DisplayName}{DescribeFlags(occupant.IsMuted, occupant.IsDeafened, occupant.IsStreaming, occupant.HasVideo)}");
        }
        return builder.ToString();
    }

    public static string FormatCandidates(string query, IReadOnlyList<LiveMemberCandidate> candidates)
    {
        var lines = candidates.Select(candidate =>
        {
            var alias = candidate.GlobalName is { } global && global != candidate.DisplayName
                ? $" / {global}"
                : string.Empty;
            return $"- {candidate.DisplayName}{alias} (username {candidate.Username}, id {candidate.Id})";
        });
        return $"Several members match \"{query}\" — ask which one they mean:\n{string.Join("\n", lines)}";
    }

    public static string FormatMember(LiveMemberInfo member)
    {
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture,
            $"**{member.DisplayName}** (username {member.Username}, id {member.Id})");
        if (member.GlobalName is { } global && global != member.DisplayName)
            builder.Append(CultureInfo.InvariantCulture, $", global name {global}");
        if (member.IsBot)
            builder.Append(" — BOT");

        builder.Append(CultureInfo.InvariantCulture, $"\nStatus: {member.PresenceStatus}");
        foreach (var activity in member.Activities)
            builder.Append(CultureInfo.InvariantCulture, $"\n- {activity}");

        builder.Append(member.Voice is { } voice
            ? $"\nVoice: in **{voice.ChannelName}**{DescribeFlags(voice.IsMuted, voice.IsDeafened, voice.IsStreaming, voice.HasVideo)}"
            : "\nVoice: not in a voice channel");

        builder.Append(CultureInfo.InvariantCulture,
            $"\nRoles: {(member.Roles.Count > 0 ? string.Join(", ", member.Roles) : "(none)")}");
        builder.Append(CultureInfo.InvariantCulture,
            $"\nJoined this server: {FormatDate(member.JoinedAt)} | Account created: {FormatDate(member.AccountCreatedAt)}");
        if (member.BoostingSince is { } boosting)
            builder.Append(CultureInfo.InvariantCulture, $"\nBoosting the server since {FormatDate(boosting)}");
        if (member.TimedOutUntil is { } timedOut)
            builder.Append(CultureInfo.InvariantCulture,
                $"\nTimed out until {timedOut.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)}");
        return builder.ToString();
    }

    public static string FormatServerInfo(LiveServerInfo server)
    {
        var owner = server.OwnerName is { } name ? $"\nOwner: {name}" : string.Empty;
        return $"""
            **{server.Name}** (id {server.Id}), created {FormatDate(server.CreatedAt)}{owner}
            Members: {server.MemberCount}
            Boosts: level {server.BoostTier}, {server.BoostCount} boost(s)
            Channels: {server.TextChannelCount} text, {server.VoiceChannelCount} voice, {server.CategoryCount} categories
            Roles: {server.RoleCount} | Emojis: {server.EmojiCount}
            """;
    }

    private static string DescribeFlags(bool isMuted, bool isDeafened, bool isStreaming, bool hasVideo)
    {
        List<string> flags = [];
        // Deafening implies muting on Discord, so "deafened" alone tells the whole story.
        if (isDeafened)
            flags.Add("deafened");
        else if (isMuted)
            flags.Add("muted");
        if (isStreaming)
            flags.Add("streaming");
        if (hasVideo)
            flags.Add("camera on");
        return flags.Count > 0 ? $" ({string.Join(", ", flags)})" : string.Empty;
    }

    private static string FormatDate(DateTimeOffset timestamp) =>
        timestamp.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
