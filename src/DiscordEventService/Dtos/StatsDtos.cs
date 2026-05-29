namespace DiscordEventService.Dtos;

// Stats aggregation results. Time buckets are computed in guild-local time
// (Europe/Warsaw) server-side. Snowflakes are ulong (serialize as strings).

// Volume & trends
public sealed record VolumeDailyDto(string Day, long Count);
public sealed record VolumeByTypeDto(string EventType, long Count);
public sealed record VolumeHourlyDto(int Hour, long Count);

// People
public sealed record UserStatDto(string? Username, ulong UserDiscordId, long Count);
public sealed record VoiceStatDto(string? Username, ulong UserDiscordId, long Minutes);

// Places
public sealed record ChannelActivityDto(
    string ChannelName,
    ulong ChannelDiscordId,
    long MessageCount,
    long ReactionCount);

// Behavior
public sealed record EmojiStatDto(string EmoteName, ulong? EmoteDiscordId, bool IsCustom, long Count);
public sealed record ActivityStatDto(string Name, long Count);
public sealed record HeatmapCellDto(int DayOfWeek, int Hour, long Count);

// Overview (home dashboard)
public sealed record WindowCountsDto(long Today, long Week, long Month, long Total);
public sealed record DailyPointDto(string Day, long Count);

public sealed record OverviewDto(
    long TotalMessages,
    long TotalReactions,
    long TotalEvents,
    long VoiceMinutes,
    long TotalUsers,
    long TotalChannels,
    WindowCountsDto Messages,
    WindowCountsDto Reactions,
    UserStatDto? TopChatter,
    ChannelActivityDto? TopChannel,
    IReadOnlyList<DailyPointDto> MessagesDaily,
    IReadOnlyList<EmojiStatDto> TopEmojis);
