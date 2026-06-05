namespace DiscordEventService.Dtos;

// Typed, hand-crafted projections for the rich entity browser. Snowflakes are
// ulong and serialize as strings via the global converter; enums are pre-decoded
// to their names. Joins are core-to-core by Guid FK.

public sealed record UserListDto(
    Guid Id,
    ulong DiscordId,
    string Username,
    string? GlobalName,
    bool IsBot,
    bool IsSystem,
    DateTime FirstSeenUtc,
    DateTime LastUpdatedUtc,
    string? AvatarHash);

public sealed record NameChangeDto(
    string? UsernameBefore,
    string? UsernameAfter,
    string? GlobalNameBefore,
    string? GlobalNameAfter,
    DateTime ChangedAtUtc);

public sealed record UserDetailDto(
    Guid Id,
    ulong DiscordId,
    string Username,
    string? GlobalName,
    string? Discriminator,
    bool IsBot,
    bool IsSystem,
    DateTime FirstSeenUtc,
    DateTime LastUpdatedUtc,
    int MembershipCount,
    IReadOnlyList<NameChangeDto> NameHistory,
    string? AvatarHash);

public sealed record ChannelListDto(
    Guid Id,
    ulong DiscordId,
    string Name,
    string Type,
    ulong? ParentDiscordId,
    bool IsNsfw,
    int Position,
    bool IsDeleted);

public sealed record ChannelDetailDto(
    Guid Id,
    ulong DiscordId,
    string Name,
    string Type,
    string? Topic,
    ulong? ParentDiscordId,
    bool IsNsfw,
    int Position,
    bool IsDeleted,
    long MessageCount);

public sealed record MemberListDto(
    Guid Id,
    Guid UserId,
    ulong UserDiscordId,
    string Username,
    string? Nickname,
    DateTime? JoinedAtUtc,
    bool IsPending,
    DateTime? TimeoutUntilUtc,
    string? AvatarHash,
    string? GuildAvatarHash);

public sealed record MessageListDto(
    Guid Id,
    ulong DiscordId,
    string? Content,
    ulong AuthorDiscordId,
    string AuthorName,
    ulong ChannelDiscordId,
    string ChannelName,
    bool HasAttachments,
    bool HasEmbeds,
    bool IsDeleted,
    DateTime CreatedAtUtc,
    DateTime? EditedAtUtc,
    string? AuthorAvatarHash);

public sealed record MessageEditDto(
    string? ContentBefore,
    string? ContentAfter,
    DateTime EditedAtUtc,
    DateTime RecordedAtUtc);

public sealed record MessageDetailDto(
    Guid Id,
    ulong DiscordId,
    string? Content,
    ulong AuthorDiscordId,
    string AuthorName,
    ulong ChannelDiscordId,
    string ChannelName,
    bool HasAttachments,
    bool HasEmbeds,
    string? AttachmentsJson,
    string? EmbedsJson,
    bool IsDeleted,
    DateTime? DeletedAtUtc,
    DateTime CreatedAtUtc,
    DateTime? EditedAtUtc,
    IReadOnlyList<MessageEditDto> EditHistory);
