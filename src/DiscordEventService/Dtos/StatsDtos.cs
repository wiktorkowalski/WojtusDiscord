namespace DiscordEventService.Dtos;

// Stats aggregation results. Time buckets are computed in guild-local time
// (Europe/Warsaw) server-side. Snowflakes are ulong (serialize as strings).

// Volume & trends
public sealed record VolumeDailyDto(string Day, long Count);
public sealed record VolumeByTypeDto(string EventType, long Count);
public sealed record VolumeHourlyDto(int Hour, long Count);

// People
public sealed record UserStatDto(string? Username, ulong UserDiscordId, long Count, string? AvatarHash);
public sealed record VoiceStatDto(string? Username, ulong UserDiscordId, long Minutes, string? AvatarHash);

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

// ---- Guild (server header) ----

public sealed record GuildOnlineDto(ulong UserDiscordId, string? Username, string? AvatarHash, string Status);

public sealed record GuildDto(
    ulong DiscordId,
    string Name,
    string? IconHash,
    long MemberCount,
    long ChannelCount,
    long UserCount,
    DateTime? EventSpanStartUtc,
    IReadOnlyList<GuildOnlineDto> Online);

// ---- Community (windowed stats + leaderboards) ----

// A single metric: value in the current window, value in the immediately
// preceding equal-length window (null for range=all), and a dense daily series
// spanning the current window (oldest -> newest).
public sealed record CommunityMetricDto(long Value, long? Prev, IReadOnlyList<long> Spark);

public sealed record CommunityLeaderEntryDto(string? Username, ulong UserDiscordId, string? AvatarHash, long Value);

public sealed record CommunityMetricsDto(
    CommunityMetricDto Messages,
    CommunityMetricDto Memes,
    CommunityMetricDto ReactionsReceived,
    CommunityMetricDto VoiceMinutes,
    CommunityMetricDto OnlineMinutes,
    CommunityMetricDto ActiveMembers);

public sealed record CommunityLeaderboardsDto(
    IReadOnlyList<CommunityLeaderEntryDto> TopChatters,
    IReadOnlyList<CommunityLeaderEntryDto> MemeLords,
    IReadOnlyList<CommunityLeaderEntryDto> ReactionsReceived,
    IReadOnlyList<CommunityLeaderEntryDto> Voice);

public sealed record CommunityDto(
    string Range,
    string Label,
    string PrevLabel,
    CommunityMetricsDto Metrics,
    CommunityLeaderboardsDto Leaderboards);

// ---- Spotify ----

public sealed record SpotifyNowPlayingDto(
    ulong UserDiscordId,
    string? Username,
    string? AvatarHash,
    string? Track,
    string? Artist,
    string? Album,
    string? AlbumArtUrl,
    DateTime? StartedAtUtc,
    DateTime? EndsAtUtc);

public sealed record SpotifyTrackDto(string Track, string? Artist, string? AlbumArtUrl, long Plays);

public sealed record SpotifyDto(
    IReadOnlyList<SpotifyNowPlayingDto> NowPlaying,
    IReadOnlyList<SpotifyTrackDto> TopTracks);

// ---- People profile ----

public sealed record ProfileFavoriteEmoteDto(string EmoteName, ulong? EmoteDiscordId, bool IsCustom);

public sealed record ProfileBusiestChannelDto(string ChannelName, ulong ChannelDiscordId);

public sealed record ProfileDailyPointDto(string Day, long Count);

public sealed record ProfileDto(
    ulong UserDiscordId,
    string Username,
    string? GlobalName,
    string? AvatarHash,
    bool IsBot,
    string Status,
    DateTime? FirstSeenUtc,
    long MessageCount,
    long MemeCount,
    long ReactionsReceivedCount,
    long VoiceMinutes,
    long OnlineMinutes,
    ProfileFavoriteEmoteDto? FavoriteEmote,
    ProfileBusiestChannelDto? BusiestChannel,
    IReadOnlyList<ProfileDailyPointDto> MessagesDaily14,
    IReadOnlyList<NameChangeDto> NameHistory);
