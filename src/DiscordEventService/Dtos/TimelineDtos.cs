using System.Text.Json;

namespace DiscordEventService.Dtos;

public sealed record TimelineEventDto(
    Guid Id,
    string EventType,
    ulong GuildDiscordId,
    ulong? ChannelDiscordId,
    ulong? UserDiscordId,
    DateTime ReceivedAtUtc,
    int JsonSizeBytes,
    bool SerializationFailed,
    JsonElement Payload);

public sealed record TimelinePage(
    IReadOnlyList<TimelineEventDto> Events,
    string? NextCursor,
    bool HasMore);
